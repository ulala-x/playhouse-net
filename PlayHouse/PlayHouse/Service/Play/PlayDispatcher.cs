using System.Collections.Concurrent;
using PlayHouse.Communicator;
using PlayHouse.Communicator.Message;
using PlayHouse.Production.Play;
using PlayHouse.Production.Shared;
using Playhouse.Protocol;
using PlayHouse.Service.Play.Base;
using PlayHouse.Service.Shared;
using PlayHouse.Utils;

namespace PlayHouse.Service.Play;

internal interface IPlayDispatcher
{
    public void OnPost(RoutePacket routePacket);
}

internal class PlayDispatcher : IPlayDispatcher
{
    private readonly ConcurrentDictionary<long, BaseStage> _baseRooms = new();
    private readonly ConcurrentDictionary<long, BaseActor> _baseUsers = new();
    private readonly IClientCommunicator _clientCommunicator;
    private readonly LOG<PlayDispatcher> _log = new();
    private readonly string _nid;
    private readonly PlayOption _playOption;
    private readonly RequestCache _requestCache;
    private readonly XSender _sender;
    private readonly IServerInfoCenter _serverInfoCenter;
    private readonly ushort _serviceId;
    private readonly TimerManager _timerManager;

    public PlayDispatcher(
        ushort serviceId,
        IClientCommunicator clientCommunicator,
        RequestCache requestCache,
        IServerInfoCenter serverInfoCenter,
        string nid,
        PlayOption playOption)
    {
        _serviceId = serviceId;
        _clientCommunicator = clientCommunicator;
        _requestCache = requestCache;
        _serverInfoCenter = serverInfoCenter;
        _nid = nid;

        _timerManager = new TimerManager(this);
        _sender = new XSender(serviceId, clientCommunicator, requestCache);
        _playOption = playOption;
    }

    public void OnPost(RoutePacket routePacket)
    {
        using (routePacket)
        {
            var msgId = routePacket.MsgId;
            var isBase = routePacket.IsBase();
            var stageId = routePacket.RouteHeader.StageId;

            var roomPacket = routePacket;
            if (routePacket.Payload is not EmptyPayload)
            {
                roomPacket = RoutePacket.MoveOf(routePacket);
            }

            if (isBase)
            {
                DoBaseRoomPacket(msgId, roomPacket, stageId);
            }
            else
            {
                _baseRooms.TryGetValue(stageId, out var baseStage);
                if (baseStage != null)
                {
                    baseStage.Post(RoutePacket.MoveOf(roomPacket));
                }
                else
                {
                    _log.Error(() => $"stage is not exist - [stageId:{stageId},msgName:{msgId}]");
                }
            }
        }
    }

    public void Start()
    {
    }

    public void Stop()
    {
    }

    public void RemoveRoom(long stageId)
    {
        _baseRooms.Remove(stageId, out _);
    }

    public void RemoveUser(long accountId)
    {
        _baseUsers.Remove(accountId, out _);
    }

    private BaseStage MakeBaseRoom(long stageId)
    {
        var stageSender = new XStageSender(_serviceId, stageId, this, _clientCommunicator, _requestCache);
        var sessionUpdater = new XSessionUpdater(_nid, stageSender);
        var baseStage = new BaseStage(stageId, this, _clientCommunicator, _requestCache, _serverInfoCenter,
            sessionUpdater, stageSender);
        _baseRooms[stageId] = baseStage;
        return baseStage;
    }

    public BaseActor? FindUser(long accountId)
    {
        return _baseUsers.GetValueOrDefault(accountId);
    }

    public void AddUser(BaseActor baseActor)
    {
        _baseUsers[baseActor.ActorSender.AccountId()] = baseActor;
    }

    public BaseStage? FindRoom(long stageId)
    {
        return _baseRooms[stageId];
    }

    public void CancelTimer(long stageId, long timerId)
    {
        if (_baseRooms.TryGetValue(stageId, out var room))
        {
            room.CancelTimer(timerId);
        }
    }

    public IStage CreateContentRoom(string stageType, XStageSender roomSender)
    {
        return _playOption.PlayProducer.GetStage(stageType, roomSender);
    }

    public IActor CreateContentUser(string stageType, XActorSender userSender)
    {
        return _playOption.PlayProducer.GetActor(stageType, userSender);
    }

    public bool IsValidType(string stageType)
    {
        return _playOption.PlayProducer.IsInvalidType(stageType);
    }

    private void DoBaseRoomPacket(string msgId, RoutePacket routePacket, long stageId)
    {
        if (msgId == CreateStageReq.Descriptor.Name)
        {
            var newStageId = routePacket.StageId;
            if (_baseRooms.ContainsKey(newStageId))
            {
                _sender.Reply((ushort)BaseErrorCode.AlreadyExistStage);
            }
            else
            {
                MakeBaseRoom(newStageId).Post(RoutePacket.MoveOf(routePacket));
            }
        }
        else if (msgId == CreateJoinStageReq.Descriptor.Name)
        {
            _baseRooms.TryGetValue(stageId, out var room);
            if (room != null)
            {
                room!.Post(RoutePacket.MoveOf(routePacket));
            }
            else
            {
                MakeBaseRoom(stageId).Post(RoutePacket.MoveOf(routePacket));
            }
        }
        else if (msgId == TimerMsg.Descriptor.Name)
        {
            var timerId = routePacket.TimerId;
            var protoPayload = (routePacket.Payload as ProtoPayload)!;
            TimerProcess(stageId, timerId, (protoPayload.GetProto() as TimerMsg)!, routePacket.TimerCallback!);
        }
        else if (msgId == DestroyStage.Descriptor.Name)
        {
            _baseRooms.Remove(stageId, out _);
        }
        else
        {
            if (!_baseRooms.TryGetValue(stageId, out var room))
            {
                if (msgId == StageTimer.Descriptor.Name) return;
                _log.Error(() => $"Room is not exist : {stageId},{msgId}");
                _sender.Reply((ushort)BaseErrorCode.StageIsNotExist);
                return;
            }

            if (msgId == JoinStageReq.Descriptor.Name ||
                msgId == StageTimer.Descriptor.Name ||
                msgId == DisconnectNoticeMsg.Descriptor.Name ||
                msgId == AsyncBlock.Descriptor.Name)
            {
                room!.Post(RoutePacket.MoveOf(routePacket));
            }
            else
            {
                _log.Error(() => $"message is not base packet - [msgId:{msgId}]");
            }
        }
    }

    private void TimerProcess(long stageId, long timerId, TimerMsg timerMsg, TimerCallbackTask timerCallback)
    {
        _baseRooms.TryGetValue(stageId, out var room);

        if (room != null)
        {
            switch (timerMsg.Type)
            {
                case TimerMsg.Types.Type.Repeat:
                    _timerManager.RegisterRepeatTimer(
                        stageId,
                        timerId,
                        timerMsg.InitialDelay,
                        timerMsg.Period,
                        timerCallback);
                    break;
                case TimerMsg.Types.Type.Count:
                    _timerManager.RegisterCountTimer(
                        stageId,
                        timerId,
                        timerMsg.InitialDelay,
                        timerMsg.Count,
                        timerMsg.Period,
                        timerCallback);
                    break;
                case TimerMsg.Types.Type.Cancel:
                    _timerManager.CancelTimer(timerId);
                    break;
                default:
                    _log.Error(() => $"Invalid timer type - [timerType:{timerMsg.Type}]");
                    break;
            }
        }
        else
        {
            _log.Debug(() => $"Stage for timer is not exist - [stageId:{stageId}, timerType:{timerMsg.Type}]");
        }
    }


    internal int GetActorCount()
    {
        return _baseUsers.Count;
    }
}