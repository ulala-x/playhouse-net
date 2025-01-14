using System.Collections.Concurrent;
using PlayHouse.Communicator;
using PlayHouse.Communicator.Message;
using Playhouse.Protocol;
using PlayHouse.Service.Api.Reflection;
using PlayHouse.Service.Shared;
using PlayHouse.Utils;

namespace PlayHouse.Service.Api;

internal class ApiActor(
    ushort serviceId,
    RequestCache requestCache,
    IClientCommunicator clientCommunicator,
    ApiReflection apiReflection,
    ApiReflectionCallback apiReflectionCallback)
{
    private readonly AtomicBoolean _isUsing = new(false);
    private readonly LOG<ApiActor> _log = new();

    private readonly ConcurrentQueue<RoutePacket> _msgQueue = new();
    private DateTime _lastUpdateTime = DateTime.UtcNow;

    public async Task DispatchAsync(RoutePacket routePacket)
    {
        var routeHeader = routePacket.RouteHeader;
        var apiSender = new AllApiSender(serviceId, clientCommunicator, requestCache);
        apiSender.SetCurrentPacketHeader(routeHeader);

        try
        {
            if (routeHeader.IsBase)
            {
                if (routeHeader.MsgId == DisconnectNoticeMsg.Descriptor.Name)
                {
                    try
                    {
                        await apiReflectionCallback.OnDisconnectAsync(apiSender);
                    }
                    catch (Exception e)
                    {
                        _log.Error(() => $"[exception message:{e.Message}]");
                        _log.Error(() => $"[exception message:{e.StackTrace}]");

                        if (e.InnerException != null)
                        {
                            _log.Error(() => $"[internal exception message:{e.InnerException.Message}");
                            _log.Error(() => $"[internal exception trace:{e.InnerException.StackTrace}");
                        }
                    }
                }
                else
                {
                    _log.Error(() => $"Invalid base Api packet - [packetInfo:{routeHeader}]");
                }
            }
            else
            {
                if (routeHeader.IsBackend)
                {
                    await apiReflection.CallBackendMethodAsync(routePacket.ToContentsPacket(), apiSender);
                }
                else
                {
                    await apiReflection.CallMethodAsync(routePacket.ToContentsPacket(), apiSender);
                }
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

    public bool IsExpired()
    {
        var differenceTime = DateTime.UtcNow - _lastUpdateTime;

        if (differenceTime.TotalSeconds > 60)
        {
            return true;
        }

        return false;
    }

    public void Post(RoutePacket packet)
    {
        _lastUpdateTime = DateTime.UtcNow;

        _msgQueue.Enqueue(packet);

        if (_isUsing.CompareAndSet(false, true))
        {
            Task.Run(async () =>
            {
                while (_msgQueue.TryDequeue(out var routePacket))
                {
                    using (routePacket)
                    {
                        //Stopwatch sw = Stopwatch.StartNew();
                        await DispatchAsync(routePacket);
                        //sw.Stop();
                        //if (sw.ElapsedMilliseconds > 1000)
                        //{
                        //    _log.Info(() => $"msgId:{routePacket.MsgId},elapsedTime:{sw.ElapsedMilliseconds}");
                        //}
                    }
                }

                _isUsing.Set(false);
            });
        }
    }
}