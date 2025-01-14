using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using PlayHouse.Communicator;
using PlayHouse.Communicator.Message;
using PlayHouse.Production.Api;
using Playhouse.Protocol;
using PlayHouse.Service.Api.Reflection;
using PlayHouse.Service.Shared;
using PlayHouse.Utils;

namespace PlayHouse.Service.Api;

internal class ApiDispatcher
{
    private readonly ApiReflection _apiReflection;
    private readonly ApiReflectionCallback _apiReflectionCallback;
    private readonly ConcurrentDictionary<long, ApiActor> _cache = new();
    private readonly IClientCommunicator _clientCommunicator;
    private readonly LOG<ApiService> _log = new();
    private readonly RequestCache _requestCache;
    private readonly ushort _serviceId;
    private bool _isRunning = true;

    public ApiDispatcher(
        ushort serviceId,
        RequestCache requestCache,
        IClientCommunicator clientCommunicator,
        IServiceProvider serviceProvider,
        ApiOption apiOption
    )
    {
        _serviceId = serviceId;
        _requestCache = requestCache;
        _clientCommunicator = clientCommunicator;
        _apiReflection = new ApiReflection(serviceProvider, apiOption.AspectifyManager);
        _apiReflectionCallback = new ApiReflectionCallback(serviceProvider);


        var controllerTester = serviceProvider.GetService<ControllerTester>();
        controllerTester?.Init(_apiReflection, _apiReflectionCallback);
    }

    public void Start()
    {
        var thread = new Thread(() =>
        {
            while (_isRunning)
            {
                CheckExpire();
                Thread.Sleep(1000);
            }
        });
        thread.Start();
    }

    private void CheckExpire()
    {
        List<long> keysToDelete = new();

        foreach (var item in _cache)
        {
            if (item.Value.IsExpired())
            {
                keysToDelete.Add(item.Key);
            }
        }

        foreach (var key in keysToDelete)
        {
            Remove(key);
        }
    }

    public void Stop()
    {
        _isRunning = false;
    }


    internal int GetAccountCount()
    {
        return _cache.Count();
    }

    internal void OnPost(RoutePacket routePacket)
    {
        using (routePacket)
        {
            var accountId = routePacket.AccountId;
            if (accountId != 0)
            {
                var apiActor = Get(accountId);
                if (apiActor == null)
                {
                    apiActor = new ApiActor
                    (
                        _serviceId,
                        _requestCache,
                        _clientCommunicator,
                        _apiReflection,
                        _apiReflectionCallback
                    );

                    _cache[accountId] = apiActor;
                }

                apiActor.Post(RoutePacket.MoveOf(routePacket));

                if (routePacket.MsgId == DisconnectNoticeMsg.Descriptor.Name)
                {
                    Remove(accountId);
                }
            }
            else
            {
                Task.Run(async () => { await DispatchAsync(RoutePacket.MoveOf(routePacket)); });
            }
        }
    }

    private async Task DispatchAsync(RoutePacket routePacket)
    {
        var routeHeader = routePacket.RouteHeader;
        var apiSender = new AllApiSender(_serviceId, _clientCommunicator, _requestCache);
        apiSender.SetCurrentPacketHeader(routeHeader);

        try
        {
            if (routePacket.IsBackend())
            {
                await _apiReflection.CallBackendMethodAsync(routePacket.ToContentsPacket(), apiSender);
            }
            else
            {
                await _apiReflection.CallMethodAsync(routePacket.ToContentsPacket(), apiSender);
            }
        }
        catch (ServiceException.NotRegisterMethod e)
        {
            if (routeHeader.Header.MsgSeq > 0)
            {
                apiSender.Reply((ushort)BaseErrorCode.NotRegisteredMessage);
            }

            _log.Error(() => $"{e}");
        }
        catch (ServiceException.NotRegisterInstance e)
        {
            if (routeHeader.Header.MsgSeq > 0)
            {
                apiSender.Reply((ushort)BaseErrorCode.SystemError);
            }

            _log.Error(() => $"{e}");
        }
        catch (Exception e)
        {
            if (routeHeader.Header.MsgSeq > 0)
            {
                apiSender.Reply((ushort)BaseErrorCode.UncheckedContentsError);
            }

            _log.Error(() => $"Packet processing failed due to an unexpected error. - [msgId:{routeHeader.MsgId}]");
            _log.Error(() => $"[exception message:{e.Message}]");
            _log.Error(() => $"[exception message:{e.StackTrace}]");

            if (e.InnerException != null)
            {
                _log.Error(() => $"[internal exception message:{e.InnerException.Message}");
                _log.Error(() => $"[internal exception trace:{e.InnerException.StackTrace}");
            }
        }
    }

    private void Remove(long accountId)
    {
        _cache.TryRemove(accountId, out var _);
    }

    public ApiActor? Get(long accountId)
    {
        return _cache.GetValueOrDefault(accountId);
    }
}