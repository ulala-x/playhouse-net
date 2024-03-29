﻿using Playhouse.Protocol;
using PlayHouse.Communicator.Message;
using PlayHouse.Service.Shared;
using PlayHouse.Utils;
using System.Collections.Specialized;
using System.Runtime.Caching;

namespace PlayHouse.Communicator;
internal class ReplyObject
{
    private readonly ReplyCallback? _replyCallback = null;
    private readonly TaskCompletionSource<RoutePacket>? _taskCompletionSource= null;
    
    public ReplyObject(ReplyCallback? callback = null, TaskCompletionSource<RoutePacket>? taskCompletionSource = null)  
    { 
        _replyCallback = callback;
        _taskCompletionSource = taskCompletionSource;
    }

    public void OnReceive(RoutePacket routePacket)
    {
        if(_replyCallback != null)
        {
            using (routePacket)
            {
                _replyCallback?.Invoke(routePacket.ErrorCode,CPacket.Of(routePacket));
            }
        }
        
        _taskCompletionSource?.SetResult(routePacket);
    }

    public void Throw(ushort errorCode)
    {
        _taskCompletionSource?.SetException(new PlayHouseException($"request has exception - errorCode:{errorCode}",errorCode));
        //_replyCallback?.Invoke(errorCode,new EmptyPacket());
        //_taskCompletionSource?.SetResult(RoutePacket.Of(errorCode));

    }
}
internal class RequestCache
{
    private readonly AtomicShort _sequence = new();
    private readonly CacheItemPolicy _policy;
    private MemoryCache _cache;
    private readonly LOG<RequestCache> _log = new ();

    public RequestCache(int timeout) 
    {
        _policy = new CacheItemPolicy();
        if (timeout > 0)
        {
            _policy.SlidingExpiration = TimeSpan.FromSeconds(timeout);
        }

        // Set a callback to be called when the cache item is removed
        _policy.RemovedCallback = new CacheEntryRemovedCallback((args) => {
            if (args.RemovedReason == CacheEntryRemovedReason.Expired)
            {
                var replyObject = (ReplyObject)args.CacheItem.Value;
                //int msgSeq = int.Parse(args.CacheItem.Key);
                replyObject.Throw((int)BaseErrorCode.RequestTimeout);
            }
        });

        var cacheSettings = new NameValueCollection();
        cacheSettings.Add("CacheMemoryLimitMegabytes", "10");
        cacheSettings.Add("PhysicalMemoryLimitPercentage", "1");
        _cache = new MemoryCache("RequestCache", cacheSettings);
    }

    public ushort GetSequence()
    {
        return _sequence.IncrementAndGet();
    }

    public void Put(int seq,ReplyObject replyObject)
    {
        var cacheItem = new CacheItem(seq.ToString(), replyObject);
        MemoryCache.Default.Add(cacheItem, _policy);
    }

    public ReplyObject? Get(int seq)
    {
        return (ReplyObject)MemoryCache.Default.Get(seq.ToString());
    }

    public void OnReply(RoutePacket routePacket)
    {
        try
        {
            int msgSeq = routePacket.Header.MsgSeq;
            string key = msgSeq.ToString();
            ReplyObject? replyObject = (ReplyObject?)MemoryCache.Default.Get(key);

            if (replyObject != null)
            {
                replyObject.OnReceive(routePacket);
                MemoryCache.Default.Remove(key);
            }
            else
            {
                _log.Error(()=>$"request is not exist - [packetInfo:{routePacket.RouteHeader}]");
            }
        }catch (Exception ex)
        {
            _log.Error(()=>$"{ex}");
        }
        
    }
}

