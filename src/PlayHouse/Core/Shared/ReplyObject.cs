#nullable enable

using PlayHouse.Abstractions;

namespace PlayHouse.Core.Shared;

/// <summary>
/// Wraps either a TaskCompletionSource or a ReplyCallback for handling replies.
/// </summary>
public sealed class ReplyObject : IDisposable
{
    private readonly TaskCompletionSource<IPacket>? _tcs;
    private readonly ReplyCallback? _callback;
    private readonly Action<ReplyCallback, ushort, IPacket?>? _postToStage;
    private readonly DateTime _createdAt;
    private bool _completed;

    /// <summary>
    /// Gets the message sequence number for this reply.
    /// </summary>
    public ushort MsgSeq { get; }

    /// <summary>
    /// Gets the creation time of this reply object.
    /// </summary>
    public DateTime CreatedAt => _createdAt;

    private ReplyObject(
        ushort msgSeq,
        TaskCompletionSource<IPacket>? tcs,
        ReplyCallback? callback,
        Action<ReplyCallback, ushort, IPacket?>? postToStage = null)
    {
        MsgSeq = msgSeq;
        _tcs = tcs;
        _callback = callback;
        _postToStage = postToStage;
        _createdAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Creates a ReplyObject with a TaskCompletionSource for async/await pattern.
    /// </summary>
    /// <param name="msgSeq">Message sequence number.</param>
    /// <returns>A tuple of the ReplyObject and the Task to await.</returns>
    public static (ReplyObject Reply, Task<IPacket> Task) CreateAsync(ushort msgSeq)
    {
        var tcs = new TaskCompletionSource<IPacket>(TaskCreationOptions.RunContinuationsAsynchronously);
        var reply = new ReplyObject(msgSeq, tcs, null);
        return (reply, tcs.Task);
    }

    /// <summary>
    /// Creates a ReplyObject with a callback.
    /// </summary>
    /// <param name="msgSeq">Message sequence number.</param>
    /// <param name="callback">Callback to invoke on reply.</param>
    /// <param name="postToStage">Optional delegate for posting callback to Stage event loop.</param>
    /// <returns>A new ReplyObject.</returns>
    public static ReplyObject CreateCallback(
        ushort msgSeq,
        ReplyCallback callback,
        Action<ReplyCallback, ushort, IPacket?>? postToStage = null)
    {
        return new ReplyObject(msgSeq, null, callback, postToStage);
    }

    /// <summary>
    /// Completes the reply with a successful response.
    /// </summary>
    /// <param name="packet">The reply packet.</param>
    public void Complete(IPacket packet)
    {
        if (_completed) return;
        _completed = true;

        if (_tcs != null)
        {
            _tcs.TrySetResult(packet);
        }
        else if (_callback != null)
        {
            if (_postToStage != null)
            {
                // Post callback to Stage event loop for thread-safe execution and proper disposal
                _postToStage(_callback, (ushort)ErrorCode.Success, packet);
            }
            else
            {
                // Direct invocation for non-Stage contexts (e.g., API server)
                _callback((ushort)ErrorCode.Success, packet);
            }
        }
    }

    /// <summary>
    /// Completes the reply with an error.
    /// </summary>
    /// <param name="errorCode">Error code.</param>
    public void CompleteWithError(ushort errorCode)
    {
        if (_completed) return;
        _completed = true;

        if (_tcs != null)
        {
            // Create an error packet
            var errorPacket = CPacket.Empty($"Error:{errorCode}");
            _tcs.TrySetResult(errorPacket);
        }
        else if (_callback != null)
        {
            if (_postToStage != null)
            {
                // Post callback to Stage event loop for thread-safe execution
                _postToStage(_callback, errorCode, null);
            }
            else
            {
                // Direct invocation for non-Stage contexts (e.g., API server)
                _callback(errorCode, null);
            }
        }
    }

    /// <summary>
    /// Cancels the reply due to timeout.
    /// </summary>
    public void Timeout()
    {
        CompleteWithError((ushort)ErrorCode.RequestTimeout);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (!_completed)
        {
            Timeout();
        }
    }
}
