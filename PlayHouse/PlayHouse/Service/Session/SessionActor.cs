using System.Collections.Concurrent;
using System.Diagnostics;
using PlayHouse.Communicator;
using PlayHouse.Communicator.Message;
using PlayHouse.Production.Session;
using PlayHouse.Production.Shared;
using Playhouse.Protocol;
using PlayHouse.Service.Session.Network;
using PlayHouse.Service.Shared;
using PlayHouse.Utils;

namespace PlayHouse.Service.Session;

internal class TargetAddress(string nid, long stageId)
{
    public string Nid { get; } = nid;
    public long StageId { get; } = stageId;
}

internal class StageIndexGenerator
{
    private byte _byteValue;

    public byte IncrementByte()
    {
        _byteValue = (byte)((_byteValue + 1) & 0xff);
        if (_byteValue == 0)
        {
            _byteValue = IncrementByte();
        }

        return _byteValue;
    }
}

internal class SessionActor
{
    private readonly AtomicBoolean _isMsgQueueUsing = new(false);

    private readonly AtomicBoolean _isSessionUserQueueUsing = new(false);
    private readonly Stopwatch _lastUpdateTime = new();
    private readonly LOG<SessionActor> _log = new();
    private readonly ConcurrentQueue<RoutePacket> _msgQueue = new();

    private readonly Dictionary<long, TargetAddress> _playEndpoints = new();
    private readonly string _remoteIp;
    private readonly IServerInfoCenter _serviceInfoCenter;
    private readonly ISession _session;

    private readonly XSessionSender _sessionSender;
    private readonly ISessionUser? _sessionUser;
    private readonly ConcurrentQueue<ClientPacket> _sessionUserQueue = new();
    private readonly HashSet<string> _signInUrIs = new();
    private readonly TargetServiceCache _targetServiceCache;
    private ushort _authenticateServiceId;
    private string _authServerNid = ServiceConst.DefaultNid;
    private bool _debugMode;

    public SessionActor(
        ushort serviceId,
        long sid,
        IServerInfoCenter serviceInfoCenter,
        ISession session,
        IClientCommunicator clientCommunicator,
        List<string> urls,
        RequestCache reqCache,
        string remoteIp,
        Func<ISessionSender, ISessionUser>? sessionUserFactory,
        ISessionDispatcher sessionDispatcher
    )
    {
        Sid = sid;
        _serviceInfoCenter = serviceInfoCenter;
        _session = session;

        _sessionSender = new XSessionSender(serviceId, clientCommunicator, reqCache, session, sessionDispatcher);
        _targetServiceCache = new TargetServiceCache(serviceInfoCenter);

        _signInUrIs.UnionWith(urls);
        _remoteIp = remoteIp;
        _sessionUser = sessionUserFactory?.Invoke(_sessionSender);
    }

    public bool IsAuthenticated { get; private set; }

    internal long AccountId { get; private set; }

    internal long Sid { get; }


    private void Authenticate(ushort serviceId, string apiNid, long accountId)
    {
        AccountId = accountId;
        IsAuthenticated = true;
        _authenticateServiceId = serviceId;
        _authServerNid = apiNid;

        _lastUpdateTime.Start();
    }

    private void UpdateStageInfo(string playNid, long stageId)
    {
        _playEndpoints[stageId] = new TargetAddress(playNid, stageId);
    }

    public void Disconnect()
    {
        if (IsAuthenticated)
        {
            var serverInfo = FindSuitableServer(_authenticateServiceId, _authServerNid);
            var disconnectPacket = RoutePacket.Of(new DisconnectNoticeMsg());
            _sessionSender.SendToBaseApi(serverInfo.GetNid(), Sid, AccountId, disconnectPacket);
            foreach (var targetId in _playEndpoints.Values)
            {
                IServerInfo targetServer = _serviceInfoCenter.FindServer(targetId.Nid);
                _sessionSender.SendToBaseStage(targetServer.GetNid(), Sid, targetId.StageId, AccountId,
                    disconnectPacket);
            }
        }
    }

    public void ClientDisconnect()
    {
        _session.ClientDisconnect();
    }

    public void Dispatch(ClientPacket clientPacket)
    {
        try
        {
            _log.Trace(() =>
                $"From:client - [sid:{Sid},accountId:{AccountId.ToString():accountId},packetInfo:{clientPacket.Header}]");

            var serviceId = clientPacket.ServiceId;
            var msgId = clientPacket.MsgId;

            _lastUpdateTime.Restart();


            if (IsAuthenticated)
            {
                RelayTo(serviceId, clientPacket);
            }
            else
            {
                var uri = $"{serviceId}:{msgId}";

                //for test check - don't remove
                //var packet = new ClientPacket(clientPacket.Header, new EmptyPayload());
                //RingBuffer ringBuffer = new RingBuffer(100);
                //RoutePacket.WriteClientPacketBytes(packet, ringBuffer);
                //NetMQFrame frame = new NetMQFrame(ringBuffer.Buffer(), ringBuffer.Count);
                //packet.Payload = new FramePayload(frame);
                //RelayToClient(packet);
                //return;

                if (_signInUrIs.Contains(uri))
                {
                    RelayTo(serviceId, clientPacket);
                }
                else
                {
                    _log.Warn(() => $"client is not authenticated :{msgId}");
                    _session.ClientDisconnect();
                }
            }
        }
        catch (Exception ex)
        {
            _log.Error(() => $"{ex.Message}");
        }
    }

    private IServerInfo FindSuitableServer(ushort serviceId, string nid)
    {
        IServerInfo serverInfo = _serviceInfoCenter.FindServer(nid);
        if (serverInfo.GetState() != ServerState.RUNNING)
        {
            serverInfo = _serviceInfoCenter.FindServerByAccountId(serviceId, AccountId);
        }

        return serverInfo;
    }

    private void RelayTo(ushort serviceId, ClientPacket clientPacket)
    {
        var type = _targetServiceCache.FindTypeBy(serviceId);

        IServerInfo? serverInfo;

        switch (type)
        {
            case ServiceType.API:
                if (_authServerNid == ServiceConst.DefaultNid)
                {
                    serverInfo = _serviceInfoCenter.FindRoundRobinServer(serviceId);
                }
                else
                {
                    serverInfo = FindSuitableServer(serviceId, _authServerNid);
                }

                _sessionSender.RelayToApi(serverInfo.GetNid(), Sid, AccountId, clientPacket);
                break;

            case ServiceType.Play:
                var targetId = _playEndpoints.GetValueOrDefault(clientPacket.Header.StageId);
                if (targetId == null)
                {
                    _log.Error(() => $"Target Stage is not exist - [service type:{type}, msgId:{clientPacket.MsgId}]");
                }
                else
                {
                    serverInfo = _serviceInfoCenter.FindServer(targetId.Nid);
                    _sessionSender.RelayToStage(serverInfo.GetNid(), targetId.StageId, Sid, AccountId,
                        clientPacket);
                }

                break;

            default:
                _log.Error(() => $"Invalid Service Type request - [service type:{type}, msgId:{clientPacket.MsgId}]");
                break;
        }
    }

    public void Post(RoutePacket routePacket)
    {
        _msgQueue.Enqueue(routePacket);
        if (_isMsgQueueUsing.CompareAndSet(false, true))
        {
            Task.Run(async () =>
            {
                while (_msgQueue.TryDequeue(out var item))
                {
                    try
                    {
                        using (item)
                        {
                            _sessionSender.SetCurrentPacketHeader(item.RouteHeader);
                            await DispatchAsync(item);
                        }
                    }
                    catch (Exception e)
                    {
                        _sessionSender.Reply((ushort)BaseErrorCode.SystemError);
                        _log.Error(() => $"{e}");
                    }
                }

                _isMsgQueueUsing.Set(false);
            });
        }
    }


    public async Task DispatchAsync(RoutePacket packet)
    {
        var msgId = packet.MsgId;
        var isBase = packet.IsBase();


        if (isBase)
        {
            if (msgId == AuthenticateMsg.Descriptor.Name)
            {
                var authenticateMsg = AuthenticateMsg.Parser.ParseFrom(packet.Span);
                var apiNid = authenticateMsg.ApiNid;
                Authenticate((ushort)authenticateMsg.ServiceId, apiNid, authenticateMsg.AccountId);
                //_sessionSender.Reply(XPacket.Of(new AuthenticateMsgRes()));

                _log.Debug(() => $"session authenticated - [accountId:{AccountId.ToString():accountId}]");
            }
            else if (msgId == SessionCloseMsg.Descriptor.Name)
            {
                _session.ClientDisconnect();
                _log.Debug(() => $"force session close - [accountId:{AccountId.ToString():accountId}]");
            }
            else if (msgId == JoinStageInfoUpdateReq.Descriptor.Name)
            {
                var joinStageMsg = JoinStageInfoUpdateReq.Parser.ParseFrom(packet.Span);
                var playEndpoint = joinStageMsg.PlayNid;
                var stageId = joinStageMsg.StageId;
                UpdateStageInfo(playEndpoint, stageId);

                _sessionSender.Reply(XPacket.Of(new JoinStageInfoUpdateRes()));

                _log.Debug(() =>
                    $"stageInfo updated - [accountId:{AccountId.ToString():accountId},playEndpoint:{playEndpoint},stageId:{stageId}");
            }
            else if (msgId == LeaveStageMsg.Descriptor.Name)
            {
                var stageId = LeaveStageMsg.Parser.ParseFrom(packet.Span).StageId;
                ClearRoomInfo(stageId);
                _log.Debug(() =>
                    $"stage info clear - [accountId:{AccountId.ToString():accountId}, stageId: {stageId}]");
            }
            else if (msgId == RemoteIpReq.Descriptor.Name)
            {
                _sessionSender.Reply(XPacket.Of(new RemoteIpRes { Ip = _remoteIp }));
            }
            else
            {
                _log.Error(() => $"invalid base packet - [msgId:{msgId}]");
            }
        }
        else
        {
            RelayToClient(packet.ToClientPacket());
        }

        await Task.CompletedTask;
    }


    private void ClearRoomInfo(long stageId)
    {
        if (_playEndpoints.ContainsKey(stageId))
        {
            _playEndpoints.Remove(stageId);
        }
    }

    private void RelayToClient(ClientPacket clientPacket)
    {
        using (clientPacket)
        {
            _log.Trace(() =>
                $"sendTo:client - [accountId:{AccountId.ToString():accountId},packetInfo:{clientPacket.Header}]");
            _sessionSender.RelayToClient(clientPacket);
        }
    }

    internal bool IsIdleState(int idleTime)
    {
        if (IsAuthenticated == false)
        {
            return false;
        }

        if (_debugMode)
        {
            return false;
        }

        if (idleTime == 0)
        {
            return false;
        }

        if (_lastUpdateTime.ElapsedMilliseconds > idleTime)
        {
            return true;
        }

        return false;
    }

    internal long IdleTime()
    {
        return _lastUpdateTime.ElapsedMilliseconds;
    }

    public void UserPost(ClientPacket clientPacket)
    {
        _sessionUserQueue.Enqueue(clientPacket);
        if (_isSessionUserQueueUsing.CompareAndSet(false, true))
        {
            Task.Run(async () =>
            {
                while (_sessionUserQueue.TryDequeue(out var packet))
                {
                    try
                    {
                        _sessionSender.SetClientRequestMsgSeq(packet.Header.MsgSeq);
                        using var routePacket = clientPacket.ToRoutePacket();
                        if (_sessionUser != null)
                        {
                            await _sessionUser.OnDispatch(routePacket.ToContentsPacket());
                        }
                        else
                        {
                            _log.Debug(() => $"session user is not exist");
                        }
                    }
                    catch (Exception e)
                    {
                        _sessionSender.Reply((ushort)BaseErrorCode.SystemError);
                        _log.Error(() => $"{e}");
                    }
                }

                _isSessionUserQueueUsing.Set(false);
            });
        }
    }

    public void SetDebugMode(bool mode)
    {
        _debugMode = mode;
    }

    public void SendHeartBeat(ClientPacket packet)
    {
        _sessionSender.SendToClient(packet);
    }
}