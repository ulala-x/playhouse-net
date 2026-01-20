#nullable enable

using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.ObjectPool;
using PlayHouse.Abstractions;

namespace PlayHouse.Core.Messaging;

/// <summary>
/// Sharded request-reply matching cache with object pooling.
/// Reduces contention and allocations during high-frequency S2S communication.
/// </summary>
public sealed class RequestCache(ILogger<RequestCache> logger)
{
    private readonly ConcurrentDictionary<ushort, PendingRequest> _pending = new();

    // Pool for PendingRequest objects to avoid heap allocations per request
    private static readonly ObjectPool<PendingRequest> RequestPool =
        new DefaultObjectPool<PendingRequest>(new DefaultPooledObjectPolicy<PendingRequest>());

    public void Register(ushort msgSeq, TaskCompletionSource<IPacket> tcs, TimeSpan timeout)
    {
        var cts = new CancellationTokenSource();
        var request = RequestPool.Get();
        request.Update(tcs, cts, DateTime.UtcNow);

        if (_pending.TryAdd(msgSeq, request))
        {
            cts.CancelAfter(timeout);
            cts.Token.Register(() => OnTimeout(msgSeq));
        }
        else
        {
            logger.LogWarning("Failed to register request {MsgSeq} - already exists", msgSeq);
            cts.Dispose();
            RequestPool.Return(request);
            tcs.TrySetException(new InvalidOperationException($"Request {msgSeq} already registered"));
        }
    }

    public bool TryComplete(ushort msgSeq, IPacket response)
    {
        if (_pending.TryRemove(msgSeq, out var request))
        {
            request.TimeoutCts.Dispose();
            request.Tcs.TrySetResult(response);
            RequestPool.Return(request);
            return true;
        }
        return false;
    }

    public void Cancel(ushort msgSeq)
    {
        if (_pending.TryRemove(msgSeq, out var request))
        {
            request.TimeoutCts.Dispose();
            request.Tcs.TrySetCanceled();
            RequestPool.Return(request);
        }
    }

    private void OnTimeout(ushort msgSeq)
    {
        if (_pending.TryRemove(msgSeq, out var request))
        {
            request.TimeoutCts.Dispose();
            request.Tcs.TrySetException(new TimeoutException($"Request {msgSeq} timed out"));
            RequestPool.Return(request);
        }
    }

    public void CancelAll()
    {
        var requests = _pending.Values.ToArray();
        _pending.Clear();
        foreach (var r in requests)
        {
            r.TimeoutCts.Dispose();
            r.Tcs.TrySetCanceled();
            RequestPool.Return(r);
        }
    }

    public int PendingCount => _pending.Count;

    // Legacy compatibility for tests
    public TaskCompletionSource<IPacket> Add(ushort msgSeq, TimeSpan? timeout = null)
    {
        var tcs = new TaskCompletionSource<IPacket>();
        Register(msgSeq, tcs, timeout ?? TimeSpan.FromSeconds(30));
        return tcs;
    }

    public bool TryRemove(ushort msgSeq, out TaskCompletionSource<IPacket>? tcs)
    {
        if (_pending.TryRemove(msgSeq, out var request))
        {
            request.TimeoutCts.Dispose();
            tcs = request.Tcs;
            RequestPool.Return(request);
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
/// Reusable pending request state.
/// </summary>
internal sealed class PendingRequest
{
    public TaskCompletionSource<IPacket> Tcs { get; private set; } = null!;
    public CancellationTokenSource TimeoutCts { get; private set; } = null!;
    public DateTime CreatedAt { get; private set; }

    public void Update(TaskCompletionSource<IPacket> tcs, CancellationTokenSource timeoutCts, DateTime createdAt)
    {
        Tcs = tcs;
        TimeoutCts = timeoutCts;
        CreatedAt = createdAt;
    }
}