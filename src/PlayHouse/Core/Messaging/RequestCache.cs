#nullable enable

using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using PlayHouse.Abstractions;

namespace PlayHouse.Core.Messaging;

/// <summary>
/// Manages pending request-reply operations with timeout support.
/// </summary>
/// <remarks>
/// RequestCache tracks outgoing requests using their MsgSeq (message sequence number)
/// and completes them when the corresponding reply arrives. Each request has a timeout
/// to prevent indefinite waiting.
/// </remarks>
internal sealed class RequestCache
{
    private readonly ConcurrentDictionary<ushort, PendingRequest> _pending = new();
    private readonly ILogger<RequestCache>? _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="RequestCache"/> class.
    /// </summary>
    /// <param name="logger">The logger instance (optional).</param>
    public RequestCache(ILogger<RequestCache>? logger = null)
    {
        _logger = logger;
    }

    /// <summary>
    /// Registers a pending request with timeout handling.
    /// </summary>
    /// <param name="msgSeq">The message sequence number.</param>
    /// <param name="tcs">The task completion source to complete when reply arrives.</param>
    /// <param name="timeout">The request timeout duration.</param>
    public void Register(ushort msgSeq, TaskCompletionSource<IPacket> tcs, TimeSpan timeout)
    {
        var cts = new CancellationTokenSource();
        var request = new PendingRequest(tcs, cts, DateTime.UtcNow);

        if (_pending.TryAdd(msgSeq, request))
        {
            _logger?.LogDebug("Registered request {MsgSeq} with timeout {Timeout}ms",
                msgSeq, timeout.TotalMilliseconds);

            // Set up timeout cancellation
            cts.CancelAfter(timeout);
            cts.Token.Register(() => OnTimeout(msgSeq));
        }
        else
        {
            _logger?.LogWarning("Failed to register request {MsgSeq} - already exists", msgSeq);
            tcs.TrySetException(new InvalidOperationException($"Request {msgSeq} already registered"));
        }
    }

    /// <summary>
    /// Completes a pending request with the received response.
    /// </summary>
    /// <param name="msgSeq">The message sequence number.</param>
    /// <param name="response">The response packet.</param>
    /// <returns>True if the request was found and completed; otherwise, false.</returns>
    public bool TryComplete(ushort msgSeq, IPacket response)
    {
        if (_pending.TryRemove(msgSeq, out var request))
        {
            request.TimeoutCts.Dispose();

            var elapsed = DateTime.UtcNow - request.CreatedAt;
            _logger?.LogDebug("Completed request {MsgSeq} after {ElapsedMs}ms",
                msgSeq, elapsed.TotalMilliseconds);

            request.Tcs.TrySetResult(response);
            return true;
        }

        _logger?.LogWarning("Failed to complete request {MsgSeq} - not found or already completed", msgSeq);
        return false;
    }

    /// <summary>
    /// Cancels a pending request.
    /// </summary>
    /// <param name="msgSeq">The message sequence number.</param>
    public void Cancel(ushort msgSeq)
    {
        if (_pending.TryRemove(msgSeq, out var request))
        {
            request.TimeoutCts.Dispose();
            request.Tcs.TrySetCanceled();

            _logger?.LogDebug("Cancelled request {MsgSeq}", msgSeq);
        }
    }

    /// <summary>
    /// Handles request timeout.
    /// </summary>
    private void OnTimeout(ushort msgSeq)
    {
        if (_pending.TryRemove(msgSeq, out var request))
        {
            var elapsed = DateTime.UtcNow - request.CreatedAt;
            _logger?.LogWarning("Request {MsgSeq} timed out after {ElapsedMs}ms",
                msgSeq, elapsed.TotalMilliseconds);

            request.TimeoutCts.Dispose();
            request.Tcs.TrySetException(new TimeoutException($"Request {msgSeq} timed out"));
        }
    }

    /// <summary>
    /// Cancels all pending requests.
    /// </summary>
    public void CancelAll()
    {
        var requests = _pending.ToArray();
        _pending.Clear();

        foreach (var kvp in requests)
        {
            kvp.Value.TimeoutCts.Dispose();
            kvp.Value.Tcs.TrySetCanceled();
        }

        _logger?.LogInformation("Cancelled {Count} pending requests", requests.Length);
    }

    /// <summary>
    /// Gets the number of pending requests.
    /// </summary>
    public int PendingCount => _pending.Count;

    /// <summary>
    /// Gets statistics about pending requests for monitoring.
    /// </summary>
    public RequestCacheStatistics GetStatistics()
    {
        var now = DateTime.UtcNow;
        var requests = _pending.Values.ToArray();

        return new RequestCacheStatistics
        {
            TotalPending = requests.Length,
            AverageAge = requests.Length > 0
                ? TimeSpan.FromMilliseconds(requests.Average(r => (now - r.CreatedAt).TotalMilliseconds))
                : TimeSpan.Zero,
            OldestAge = requests.Length > 0
                ? requests.Max(r => now - r.CreatedAt)
                : TimeSpan.Zero
        };
    }

    // Legacy compatibility methods for existing tests

    /// <summary>
    /// Adds a new request to the cache (legacy API for tests).
    /// </summary>
    /// <param name="msgSeq">The message sequence number.</param>
    /// <param name="timeout">Optional timeout (default: 30 seconds).</param>
    /// <returns>A task completion source for the request.</returns>
    public TaskCompletionSource<IPacket> Add(ushort msgSeq, TimeSpan? timeout = null)
    {
        var tcs = new TaskCompletionSource<IPacket>();
        Register(msgSeq, tcs, timeout ?? TimeSpan.FromSeconds(30));
        return tcs;
    }

    /// <summary>
    /// Tries to remove a request from the cache (legacy API for tests).
    /// </summary>
    /// <param name="msgSeq">The message sequence number.</param>
    /// <param name="tcs">The task completion source if found.</param>
    /// <returns>True if the request was found and removed.</returns>
    public bool TryRemove(ushort msgSeq, out TaskCompletionSource<IPacket>? tcs)
    {
        if (_pending.TryRemove(msgSeq, out var request))
        {
            request.TimeoutCts.Dispose();
            tcs = request.Tcs;
            return true;
        }

        tcs = null;
        return false;
    }

    /// <summary>
    /// Clears all pending requests (legacy API for tests).
    /// </summary>
    public void Clear()
    {
        CancelAll();
    }
}

/// <summary>
/// Represents a pending request awaiting a reply.
/// </summary>
internal sealed class PendingRequest
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PendingRequest"/> class.
    /// </summary>
    /// <param name="tcs">The task completion source.</param>
    /// <param name="timeoutCts">The timeout cancellation token source.</param>
    /// <param name="createdAt">The creation timestamp.</param>
    public PendingRequest(
        TaskCompletionSource<IPacket> tcs,
        CancellationTokenSource timeoutCts,
        DateTime createdAt)
    {
        Tcs = tcs;
        TimeoutCts = timeoutCts;
        CreatedAt = createdAt;
    }

    /// <summary>
    /// Gets the task completion source.
    /// </summary>
    public TaskCompletionSource<IPacket> Tcs { get; }

    /// <summary>
    /// Gets the timeout cancellation token source.
    /// </summary>
    public CancellationTokenSource TimeoutCts { get; }

    /// <summary>
    /// Gets the creation timestamp.
    /// </summary>
    public DateTime CreatedAt { get; }
}

/// <summary>
/// Statistics about pending requests.
/// </summary>
public sealed class RequestCacheStatistics
{
    /// <summary>
    /// Gets the total number of pending requests.
    /// </summary>
    public int TotalPending { get; init; }

    /// <summary>
    /// Gets the average age of pending requests.
    /// </summary>
    public TimeSpan AverageAge { get; init; }

    /// <summary>
    /// Gets the age of the oldest pending request.
    /// </summary>
    public TimeSpan OldestAge { get; init; }
}
