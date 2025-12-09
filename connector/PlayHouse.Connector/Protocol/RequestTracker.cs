namespace PlayHouse.Connector.Protocol;

using System.Collections.Concurrent;
using Google.Protobuf;
using Microsoft.Extensions.Logging;

/// <summary>
/// Tracks pending request/response operations using MsgSeq.
/// </summary>
internal sealed class RequestTracker : IDisposable
{
    private readonly ConcurrentDictionary<ushort, PendingRequest> _pending = new();
    private readonly ILogger<RequestTracker>? _logger;
    private int _msgSeqCounter;

    public RequestTracker(ILogger<RequestTracker>? logger = null)
    {
        _logger = logger;
    }

    /// <summary>
    /// Generates the next unique message sequence number.
    /// </summary>
    /// <returns>Next MsgSeq value (never returns 0, which is reserved for one-way messages)</returns>
    public ushort GetNextMsgSeq()
    {
        int msgSeq;
        do
        {
            msgSeq = Interlocked.Increment(ref _msgSeqCounter);
        }
        while ((msgSeq & 0xFFFF) == 0); // Skip 0 as it's reserved for one-way messages

        return (ushort)(msgSeq & 0xFFFF);
    }

    /// <summary>
    /// Registers a pending request and returns a task that completes when the response is received.
    /// </summary>
    /// <typeparam name="T">Response message type</typeparam>
    /// <param name="msgSeq">Message sequence number</param>
    /// <param name="timeout">Request timeout</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Task that completes with the response</returns>
    public Task<Response<T>> TrackRequestAsync<T>(
        ushort msgSeq,
        TimeSpan timeout,
        CancellationToken cancellationToken) where T : IMessage, new()
    {
        var tcs = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);
        var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        var request = new PendingRequest
        {
            Tcs = tcs,
            ResponseType = typeof(T),
            TimeoutCts = timeoutCts,
            CreatedAt = DateTime.UtcNow
        };

        if (!_pending.TryAdd(msgSeq, request))
        {
            timeoutCts.Dispose();
            throw new InvalidOperationException($"Request with MsgSeq {msgSeq} already pending.");
        }

        _logger?.LogTrace("Tracking request: MsgSeq={MsgSeq}, Type={Type}, Timeout={Timeout}",
            msgSeq, typeof(T).Name, timeout);

        // Set up timeout
        timeoutCts.Token.Register(() =>
        {
            if (_pending.TryRemove(msgSeq, out var req))
            {
                req.Tcs.TrySetException(new TimeoutException(
                    $"Request timed out after {timeout.TotalSeconds:F1}s (MsgSeq: {msgSeq})"));
                req.TimeoutCts.Dispose();

                _logger?.LogWarning("Request timed out: MsgSeq={MsgSeq}, Type={Type}", msgSeq, typeof(T).Name);
            }
        });

        timeoutCts.CancelAfter(timeout);

        // Return typed task
        return tcs.Task.ContinueWith(
            task =>
            {
                if (task.IsFaulted)
                {
                    return new Response<T>(false, 0, default, task.Exception?.GetBaseException().Message);
                }

                if (task.IsCanceled)
                {
                    return new Response<T>(false, 0, default, "Request was canceled");
                }

                var result = task.Result;
                if (result is Response<T> response)
                {
                    return response;
                }

                throw new InvalidOperationException($"Unexpected result type: {result?.GetType().Name}");
            },
            TaskScheduler.Default);
    }

    /// <summary>
    /// Completes a pending request with a successful response.
    /// </summary>
    /// <param name="msgSeq">Message sequence number</param>
    /// <param name="errorCode">Error code (0 = success)</param>
    /// <param name="payload">Response payload</param>
    public void CompleteRequest(ushort msgSeq, ushort errorCode, ReadOnlyMemory<byte> payload)
    {
        if (!_pending.TryRemove(msgSeq, out var request))
        {
            _logger?.LogWarning("Received response for unknown request: MsgSeq={MsgSeq}", msgSeq);
            return;
        }

        try
        {
            request.TimeoutCts.Dispose();

            // Deserialize response based on expected type
            var parser = typeof(MessageParser<>)
                .MakeGenericType(request.ResponseType)
                .GetProperty("Parser")
                ?.GetValue(null);

            if (parser == null)
            {
                request.Tcs.TrySetException(new InvalidOperationException(
                    $"Could not find parser for type {request.ResponseType.Name}"));
                return;
            }

            var parseMethod = parser.GetType().GetMethod("ParseFrom", new[] { typeof(byte[]) });
            if (parseMethod == null)
            {
                request.Tcs.TrySetException(new InvalidOperationException(
                    $"Could not find ParseFrom method for type {request.ResponseType.Name}"));
                return;
            }

            var message = parseMethod.Invoke(parser, new object[] { payload.ToArray() });

            // Create Response<T> using reflection
            var responseType = typeof(Response<>).MakeGenericType(request.ResponseType);
            var success = errorCode == 0;
            var response = Activator.CreateInstance(
                responseType,
                success,
                errorCode,
                message,
                success ? null : $"Server error: {errorCode}");

            request.Tcs.TrySetResult(response!);

            var elapsed = DateTime.UtcNow - request.CreatedAt;
            _logger?.LogTrace(
                "Request completed: MsgSeq={MsgSeq}, Type={Type}, ErrorCode={ErrorCode}, Elapsed={Elapsed}ms",
                msgSeq, request.ResponseType.Name, errorCode, elapsed.TotalMilliseconds);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to complete request: MsgSeq={MsgSeq}", msgSeq);
            request.Tcs.TrySetException(ex);
        }
    }

    /// <summary>
    /// Fails a pending request with an exception.
    /// </summary>
    /// <param name="msgSeq">Message sequence number</param>
    /// <param name="exception">Exception that caused the failure</param>
    public void FailRequest(ushort msgSeq, Exception exception)
    {
        if (_pending.TryRemove(msgSeq, out var request))
        {
            request.TimeoutCts.Dispose();
            request.Tcs.TrySetException(exception);

            _logger?.LogError(exception, "Request failed: MsgSeq={MsgSeq}", msgSeq);
        }
    }

    /// <summary>
    /// Cancels all pending requests.
    /// </summary>
    public void CancelAll()
    {
        var pendingRequests = _pending.ToArray();
        _pending.Clear();

        foreach (var kvp in pendingRequests)
        {
            kvp.Value.TimeoutCts.Dispose();
            kvp.Value.Tcs.TrySetCanceled();
        }

        _logger?.LogDebug("Canceled {Count} pending requests", pendingRequests.Length);
    }

    /// <summary>
    /// Gets the count of currently pending requests.
    /// </summary>
    public int PendingCount => _pending.Count;

    public void Dispose()
    {
        CancelAll();
    }
}

/// <summary>
/// Represents a pending request waiting for a response.
/// </summary>
internal sealed class PendingRequest
{
    /// <summary>
    /// Task completion source for the request.
    /// </summary>
    public required TaskCompletionSource<object> Tcs { get; init; }

    /// <summary>
    /// Expected response message type.
    /// </summary>
    public required Type ResponseType { get; init; }

    /// <summary>
    /// Cancellation token source for timeout handling.
    /// </summary>
    public required CancellationTokenSource TimeoutCts { get; init; }

    /// <summary>
    /// Timestamp when the request was created.
    /// </summary>
    public required DateTime CreatedAt { get; init; }
}
