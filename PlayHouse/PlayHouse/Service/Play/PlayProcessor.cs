﻿using Microsoft.Extensions.Logging;
using PlayHouse.Communicator.Message;
using PlayHouse.Communicator;
using PlayHouse.Service.Play.Base;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Playhouse.Protocol;
using System.Numerics;
using PlayHouse.Utils;

namespace PlayHouse.Service.Play
{
    public class PlayProcessor : IProcessor
    {
        private AtomicEnum<ServerState> _state = new(ServerState.DISABLE);
        private readonly ConcurrentDictionary<long, BaseActor> _baseUsers = new();
        private readonly ConcurrentDictionary<long, BaseStage> _baseRooms = new();
        private Thread _threadForCoroutine;
        private readonly ConcurrentQueue<RoutePacket> _msgQueue = new();
        private readonly TimerManager _timerManager;
        private readonly XSender _sender;
        private readonly short _serviceId;
        private readonly string _publicEndpoint;
        private readonly PlayOption _playOption;
        private readonly IClientCommunicator _clientCommunicator;
        private readonly RequestCache _requestCache;
        private readonly IServerInfoCenter _serverInfoCenter;

        public short ServiceId => _serviceId;

        public PlayProcessor(short serviceId, string publicEndpoint, PlayOption playOption,
            IClientCommunicator clientCommunicator, RequestCache requestCache, IServerInfoCenter serverInfoCenter)
        {
            _serviceId = serviceId;
            _publicEndpoint = publicEndpoint;
            _playOption = playOption;
            _clientCommunicator = clientCommunicator;
            _requestCache = requestCache;
            _serverInfoCenter = serverInfoCenter;
            _timerManager = new TimerManager(this);
            _sender = new XSender(serviceId, clientCommunicator, requestCache);
            _threadForCoroutine = new Thread(() => MessageLoop()) { Name = "play:message-loop" };
        }

        public  void OnStart()
        {
            _state.Set(ServerState.RUNNING);
            _threadForCoroutine.Start();
            _timerManager.Start();
        }

        public void RemoveRoom(long stageId)
        {
            _baseRooms.Remove(stageId,out _);
        }

        public void RemoveUser(long accountId)
        {
            _baseUsers.Remove(accountId, out _);
        }

        public void ErrorReply(RouteHeader routeHeader, short errorCode)
        {
            _sender.ErrorReply(routeHeader, errorCode);
        }
        private BaseStage MakeBaseRoom(long stageId)
        {
            var baseStage = new BaseStage(stageId, this, _clientCommunicator, _requestCache, _serverInfoCenter);
            _baseRooms[stageId] = baseStage;
            return baseStage;
        }
        private void MessageLoop()
        {

            while (_state.Get() != ServerState.DISABLE)
            {
                while (_msgQueue.TryDequeue(out var routePacket))
                {
                    using (routePacket)
                    {
                        var msgId = routePacket.GetMsgId();
                        var isBase = routePacket.IsBase();
                        var stageId = routePacket.RouteHeader.StageId;
                        var roomPacket = RoutePacket.MoveOf(routePacket);

                        if (isBase)
                        {
                            Task.Run(async () => await DoBaseRoomPacket(msgId, roomPacket, stageId));
                        }
                        else
                        {
                            Task.Run(async () =>
                            {
                                var baseStage = _baseRooms[stageId];
                                if (baseStage != null)
                                {
                                   await  baseStage.Send(roomPacket);
                                }
                                else
                                {
                                    LOG.Error($"stageId:{stageId} is not exist, msgName:{msgId}", this.GetType());
                                }
                            });
                        }
                    }

                     _msgQueue.TryDequeue(out routePacket);
                }

                Thread.Sleep(10);
            }
        }


        private async Task DoBaseRoomPacket(int msgId, RoutePacket routePacket, long stageId)
        {

            if (msgId == CreateStageReq.Descriptor.Index)
            {
                var newStageId = routePacket.StageId;
                if (_baseRooms.ContainsKey(newStageId))
                {
                    ErrorReply(routePacket.RouteHeader, (short)BaseErrorCode.AlreadyExistStage);
                }
                else
                {
                    await MakeBaseRoom(newStageId).Send(routePacket);
                }
            }
            else if (msgId == CreateJoinStageReq.Descriptor.Index)
            {
                _baseRooms.TryGetValue(stageId, out var room);
                if (room != null)
                {
                    await room!.Send(routePacket);
                }
                else
                {
                    await MakeBaseRoom(stageId).Send(routePacket);
                }
            }
            else if (msgId == TimerMsg.Descriptor.Index)
            {
                var timerId = routePacket.TimerId;
                var protoPayload = (routePacket.GetPayload() as ProtoPayload)!;
                TimerProcess(stageId, timerId, (protoPayload.GetProto() as TimerMsg)!, routePacket.TimerCallback!);
            }
            else if (msgId == DestroyStage.Descriptor.Index)
            {
                _baseRooms.Remove(stageId, out _);
            }
            else
            {
                if (!_baseRooms.TryGetValue(stageId, out var room))
                {
                    LOG.Error($"Room is not exist : {stageId},{msgId}", this.GetType());
                    ErrorReply(routePacket.RouteHeader, (short)BaseErrorCode.StageIsNotExist);
                    return;
                }

                if (msgId == JoinStageReq.Descriptor.Index ||
                    msgId == StageTimer.Descriptor.Index ||
                    msgId == DisconnectNoticeMsg.Descriptor.Index ||
                    msgId == AsyncBlock.Descriptor.Index)
                {
                    await room!.Send(routePacket);
                }
                else
                {
                    LOG.Error($"{msgId} is not base packet", this.GetType());
                }
            }
        }

        private void TimerProcess(long stageId, long timerId, TimerMsg timerMsg, TimerCallbackTask timerCallback)
        {
            var room = _baseRooms[stageId];

            if (room != null)
            {
                if (timerMsg.Type == TimerMsg.Types.Type.Repeat)
                {

                    _timerManager.RegisterRepeatTimer(
                        stageId,
                        timerId,
                        timerMsg.InitialDelay,
                        timerMsg.Period,
                        timerCallback);
                }
                else if (timerMsg.Type == TimerMsg.Types.Type.Count)
                {
                    _timerManager.RegisterCountTimer(
                          stageId,
                          timerId,
                          timerMsg.InitialDelay,
                          timerMsg.Count,
                          timerMsg.Period,
                          timerCallback);
                }
                else if (timerMsg.Type == TimerMsg.Types.Type.Cancel)
                {
                    _timerManager.CancelTimer(timerId);
                }
                else
                {
                    LOG.Error($"Invalid timer type : {timerMsg.Type}", this.GetType());
                }

            }
            else
            {
                LOG.Error($"Stage for timer is not exist: stageId:{stageId}, timerType:{timerMsg.Type}", this.GetType());
            }
        }

        public void OnReceive(RoutePacket routePacket)
        {
            _msgQueue.Enqueue(routePacket);
        }

        public void OnStop()
        {
            _state.Set(ServerState.DISABLE);
        }
        public int GetWeightPoint()
        {
            return _baseUsers.Count;
        }
        public ServerState GetServerState()
        {
            return _state.Get();
        }

        public ServiceType GetServiceType()
        {
            return ServiceType.Play;
        }

        public void Pause()
        {
            _state.Set(ServerState.PAUSE);
        }

        public void Resume()
        {
            _state.Set(ServerState.RUNNING);
        }

        public BaseActor? FindUser(long accountId)
        {
            return _baseUsers[accountId];
        }

        public void AddUser(BaseActor baseActor)
        {
            _baseUsers[baseActor.ActorSender.AccountId()] = baseActor;
        }

        public BaseStage? FindRoom(long stageId)
        {
            return _baseRooms[stageId];
        }

        public string Endpoint()
        {
            return _publicEndpoint;
        }

        public void CancelTimer(long stageId, long timerId)
        {
            if (_baseRooms.ContainsKey(stageId))
            {
                _baseRooms[stageId].CancelTimer(timerId);
            }
        }

        public IStage<IActor> CreateContentRoom(string stageType, XStageSender roomSender)
        {
            return _playOption.ElementConfigurator.GetStage(stageType, roomSender);
        }

        public IActor CreateContentUser(string stageType, XActorSender userSender)
        {
            return _playOption.ElementConfigurator.GetActor(stageType, userSender);
        }

        public bool IsValidType(string stageType)
        {
            return _playOption.ElementConfigurator.IsInvalidType(stageType);
        }
    }

}
