using System.Collections.Concurrent;
using PlayHouse.Communicator.Message;
using Playhouse.Protocol;
using PlayHouse.Service.Shared;
using PlayHouse.Utils;

namespace PlayHouse.Communicator;

internal class ReplyObject(
    ReplyCallback? callback = null,
    TaskCompletionSource<RoutePacket>? taskCompletionSource = null)
{
    private readonly DateTime _requestTime = DateTime.UtcNow;

    public void OnReceive(RoutePacket routePacket)
    {
        if (callback != null)
        {
            using (routePacket)
            {
                callback?.Invoke(routePacket.ErrorCode, CPacket.Of(routePacket));
            }
        }


        if (routePacket.ErrorCode == 0)
        {
            taskCompletionSource?.TrySetResult(routePacket);
        }
        else
        {
            Throw(routePacket.ErrorCode);
        }
    }

    public void Throw(ushort errorCode)
    {
        taskCompletionSource?.TrySetException(new PlayHouseException($"request has exception - errorCode:{errorCode}",
            errorCode));
    }

    public bool IsExpired(int timeoutMs)
    {
        var difference = DateTime.UtcNow - _requestTime;
        return difference.TotalMilliseconds > timeoutMs;
    }
}

internal class RequestCache(int timeout)
{
    private readonly ConcurrentDictionary<int, ReplyObject> _cache = new();
    private readonly LOG<RequestCache> _log = new();
    private readonly AtomicShort _sequence = new();
    private bool _isRunning = true;

    private void CheckExpire()
    {
        if (timeout > 0)
        {
            List<int> keysToDelete = new();

            foreach (var item in _cache)
            {
                if (item.Value.IsExpired(timeout))
                {
                    var replyObject = item.Value;
                    replyObject.Throw((ushort)BaseErrorCode.RequestTimeout);
                    keysToDelete.Add(item.Key);
                }
            }

            foreach (var key in keysToDelete)
            {
                Remove(key);
            }
        }
    }

    public ushort GetSequence()
    {
        return _sequence.IncrementAndGet();
    }

    private void Remove(int seq)
    {
        _cache.TryRemove(seq, out var _);
    }

    public void Put(int seq, ReplyObject replyObject)
    {
        _cache[seq] = replyObject;
    }

    public ReplyObject? Get(int seq)
    {
        return _cache.GetValueOrDefault(seq);
    }

    public void OnReply(RoutePacket routePacket)
    {
        try
        {
            int msgSeq = routePacket.Header.MsgSeq;
            var replyObject = Get(msgSeq);

            if (replyObject != null)
            {
                replyObject.OnReceive(routePacket);
                Remove(msgSeq);
            }
            else
            {
                _log.Error(() => $"request is not exist - [packetInfo:{routePacket.RouteHeader}]");
            }
        }
        catch (Exception ex)
        {
            _log.Error(() => $"{ex}");
        }
    }

    public void Start()
    {
        var thread = new Thread(() =>
        {
            while (_isRunning)
            {
                try
                {
                    CheckExpire();
                    Thread.Sleep(1000);
                }
                catch (Exception ex)
                {
                    _log.Error(() => $"{ex}");
                    throw;
                }
            }
        });

        thread.Start();
    }

    public void Stop()
    {
        _isRunning = false;
    }
}