#nullable enable

using Microsoft.Extensions.Logging;
using PlayHouse.Abstractions.Play;
using PlayHouse.Abstractions.System;
using PlayHouse.Core.Messaging;
using PlayHouse.Core.Play;
using PlayHouse.Core.Play.Base;
using PlayHouse.Core.Shared;
using PlayHouse.Runtime.ServerMesh;
using PlayHouse.Runtime.ServerMesh.Communicator;
using PlayHouse.Runtime.ServerMesh.Discovery;
using PlayHouse.Runtime.ServerMesh.Message;
using PlayHouse.Runtime.ClientTransport;
using PlayHouse.Runtime.ClientTransport.Tcp;
using PlayHouse.Runtime.ClientTransport.WebSocket;
using PlayHouse.Abstractions;
using Google.Protobuf;

namespace PlayHouse.Bootstrap;

/// <summary>
/// Play Server 인스턴스.
/// Stage와 Actor를 관리하고 클라이언트와 실시간 통신을 담당합니다.
/// TCP, WebSocket, SSL/TLS를 지원하며 동시에 여러 Transport를 사용할 수 있습니다.
/// </summary>
public sealed class PlayServer : IAsyncDisposable, ICommunicateListener, IClientReplyHandler
{
    private readonly PlayServerOption _options;
    private readonly PlayProducer _producer;
    private readonly Type? _systemControllerType;
    private readonly ServerConfig _serverConfig;
    private readonly ILogger? _logger;

    private PlayCommunicator? _communicator;
    private PlayDispatcher? _dispatcher;
    private RequestCache? _requestCache;
    private ITransportServer? _transportServer;
    private ServerAddressResolver? _addressResolver;
    private CancellationTokenSource? _cts;

    private bool _isRunning;
    private bool _disposed;

    /// <summary>
    /// 클라이언트 메시지 핸들러.
    /// 테스트나 커스텀 메시지 처리를 위해 설정합니다.
    /// </summary>
    internal Func<ITransportSession, string, ushort, long, ReadOnlyMemory<byte>, Task>? MessageHandler { get; set; }

    /// <summary>
    /// 서버가 실행 중인지 여부.
    /// </summary>
    public bool IsRunning => _isRunning;

    /// <summary>
    /// TCP 클라이언트 연결 포트 (설정값).
    /// 실제 바인딩된 포트를 확인하려면 ActualTcpPort를 사용하세요.
    /// null이면 TCP가 비활성화되어 있습니다.
    /// </summary>
    public int? TcpPort => _options.TcpPort;

    /// <summary>
    /// 실제 바인딩된 TCP 포트.
    /// TcpPort가 0인 경우 (자동 할당) 서버 시작 후 실제 포트를 반환합니다.
    /// </summary>
    public int ActualTcpPort => GetActualTcpPort();

    /// <summary>
    /// 클라이언트 연결 포트 (레거시 호환).
    /// ActualTcpPort를 사용하세요.
    /// </summary>
    [Obsolete("Use ActualTcpPort instead")]
    public int ClientPort => ActualTcpPort;

    /// <summary>
    /// WebSocket 경로 (활성화된 경우).
    /// </summary>
    public string? WebSocketPath => _options.WebSocketPath;

    /// <summary>
    /// Transport 서버 인스턴스 (WebSocket 미들웨어 등록에 사용).
    /// </summary>
    public ITransportServer? TransportServer => _transportServer;

    internal PlayServer(PlayServerOption options, PlayProducer producer, Type? systemControllerType, ILogger? logger = null)
    {
        _options = options;
        _producer = producer;
        _systemControllerType = systemControllerType;
        _logger = logger;

        _serverConfig = new ServerConfig(
            options.ServiceId,
            options.ServerId,
            options.BindEndpoint,
            options.RequestTimeoutMs);
    }

    /// <summary>
    /// 서버를 시작합니다.
    /// </summary>
    public async Task StartAsync()
    {
        if (_isRunning)
            throw new InvalidOperationException("Server is already running");

        _cts = new CancellationTokenSource();
        _requestCache = new RequestCache();

        // NetMQ Communicator 시작
        _communicator = PlayCommunicator.Create(_serverConfig);
        _communicator.Bind(_options.BindEndpoint);
        _communicator.Bind(this);
        _communicator.Start();

        // 자기 자신에게 연결 (같은 서버 내 Stage 간 통신에 필요)
        _communicator.Connect(_options.ServerId, _options.BindEndpoint);

        // PlayDispatcher 생성
        _dispatcher = new PlayDispatcher(
            _producer,
            new CommunicatorAdapter(_communicator),
            _requestCache,
            _options.ServiceId,
            _options.ServerId,
            this, // client reply handler
            _logger);

        // Transport 서버 빌드 및 시작
        _transportServer = BuildTransportServer();
        await _transportServer.StartAsync(_cts.Token);

        // ServerAddressResolver 시작 (SystemController가 등록된 경우)
        if (_systemControllerType != null)
        {
            var systemController = Activator.CreateInstance(_systemControllerType) as ISystemController
                ?? throw new InvalidOperationException($"Failed to create SystemController instance: {_systemControllerType.Name}");

            var serverInfoCenter = new XServerInfoCenter();

            var myServerInfo = new XServerInfo(
                _options.ServiceId,
                _options.ServerId,
                _options.BindEndpoint,
                ServerState.Running);

            _addressResolver = new ServerAddressResolver(
                myServerInfo,
                systemController,
                serverInfoCenter,
                _communicator,
                TimeSpan.FromSeconds(3));

            _addressResolver.Start();
        }

        _isRunning = true;
        _logger?.LogInformation("PlayServer started: ServerId={ServerId}, TCP={TcpEnabled}, WebSocket={WsEnabled}",
            _options.ServerId, _options.IsTcpEnabled, _options.IsWebSocketEnabled);

        await Task.Delay(50); // 서버 초기화 대기
    }

    /// <summary>
    /// Transport 서버를 옵션에 따라 빌드합니다.
    /// </summary>
    private ITransportServer BuildTransportServer()
    {
        var builder = new TransportServerBuilder(
            OnClientMessage,
            OnClientDisconnect,
            _logger);

        builder.WithOptions(opts =>
        {
            opts.ReceiveBufferSize = _options.TransportOptions.ReceiveBufferSize;
            opts.SendBufferSize = _options.TransportOptions.SendBufferSize;
            opts.MaxPacketSize = _options.TransportOptions.MaxPacketSize;
            opts.HeartbeatTimeout = _options.TransportOptions.HeartbeatTimeout;
        });

        // TCP 추가
        if (_options.IsTcpEnabled)
        {
            var tcpPort = _options.TcpPort!.Value;
            if (_options.IsTcpSslEnabled)
            {
                builder.AddTcpWithSsl(tcpPort, _options.TcpSslCertificate!, _options.TcpBindAddress);
                _logger?.LogInformation("TCP+SSL enabled on port {Port}", tcpPort == 0 ? "auto" : tcpPort);
            }
            else
            {
                builder.AddTcp(tcpPort, _options.TcpBindAddress);
                _logger?.LogInformation("TCP enabled on port {Port}", tcpPort == 0 ? "auto" : tcpPort);
            }
        }

        // WebSocket 추가
        if (_options.IsWebSocketEnabled)
        {
            builder.AddWebSocket(_options.WebSocketPath!);
            _logger?.LogInformation("WebSocket enabled on path {Path}", _options.WebSocketPath);
        }

        return builder.Build();
    }

    /// <summary>
    /// 다른 서버에 수동으로 연결합니다 (NetMQ Router-Router 패턴).
    /// </summary>
    /// <param name="targetNid">대상 서버 NID (예: "2:1")</param>
    /// <param name="address">대상 서버 주소 (예: "tcp://127.0.0.1:15101")</param>
    /// <remarks>
    /// UseSystemController()를 사용한 경우 ServerAddressResolver가 자동으로 서버를 연결하므로
    /// 이 메서드를 호출할 필요가 없습니다. 수동 연결이 필요한 경우에만 사용하세요.
    /// </remarks>
    public void Connect(string targetNid, string address)
    {
        _communicator?.Connect(targetNid, address);
    }

    /// <summary>
    /// 서버를 중지합니다.
    /// </summary>
    public async Task StopAsync()
    {
        if (!_isRunning) return;

        _isRunning = false;
        _cts?.Cancel();

        _addressResolver?.Stop();
        _addressResolver?.Dispose();

        if (_transportServer != null)
        {
            await _transportServer.StopAsync();
        }

        _communicator?.Stop();
        _dispatcher?.Dispose();
        _requestCache?.CancelAll();

        _cts?.Dispose();
        _cts = null;

        _logger?.LogInformation("PlayServer stopped: ServerId={ServerId}", _options.ServerId);
    }

    /// <inheritdoc/>
    public void OnReceive(RuntimeRoutePacket packet)
    {
        // 응답 패킷인 경우 RequestCache에서 처리
        // IsReply 플래그로 응답/요청을 구분 (MsgSeq만으로는 구분 불가)
        if (packet.Header.IsReply && packet.MsgSeq > 0)
        {
            var response = CPacket.Of(packet.MsgId, packet.GetPayloadBytes());
            if (_requestCache?.TryComplete(packet.MsgSeq, response) == true)
            {
                packet.Dispose();
                return;
            }
        }

        // Stage로 라우팅
        _dispatcher?.OnPost(new RouteMessage(packet));
    }

    /// <summary>
    /// Transport에서 클라이언트 메시지 수신 시 호출됩니다.
    /// </summary>
    private void OnClientMessage(
        ITransportSession session,
        string msgId,
        ushort msgSeq,
        long stageId,
        ReadOnlyMemory<byte> payload)
    {
        // 미인증 클라이언트 체크
        if (!session.IsAuthenticated)
        {
            // 인증 메시지가 아니면 연결 끊기
            if (msgId != _options.AuthenticateMessageId)
            {
                _logger?.LogWarning("Unauthenticated session {SessionId} sent non-auth message: {MsgId}",
                    session.SessionId, msgId);
                _ = session.DisconnectAsync();
                return;
            }
        }

        // 커스텀 핸들러가 있으면 사용
        if (MessageHandler != null)
        {
            _ = MessageHandler(session, msgId, msgSeq, stageId, payload);
            return;
        }

        // 기본 핸들러: 에코 및 인증 처리
        _ = HandleDefaultMessageAsync(session, msgId, msgSeq, stageId, payload);
    }

    private async Task HandleDefaultMessageAsync(
        ITransportSession session,
        string msgId,
        ushort msgSeq,
        long stageId,
        ReadOnlyMemory<byte> payload)
    {
        // 인증 요청 처리
        if (msgId == _options.AuthenticateMessageId)
        {
            await HandleAuthenticationAsync(session, msgSeq, stageId, payload);
            return;
        }

        // HeartBeat 요청 처리
        if (msgId == "@Heart@Beat@")
        {
            var response = TcpTransportSession.CreateResponsePacket(
                msgId, msgSeq, stageId, 0, ReadOnlySpan<byte>.Empty);
            await session.SendAsync(response);
            return;
        }

        // 인증된 세션의 일반 메시지 처리
        if (session.IsAuthenticated && !string.IsNullOrEmpty(session.AccountId))
        {
            // ClientRouteMessage로 라우팅
            var clientRouteMsg = new ClientRouteMessage(
                stageId,
                session.AccountId,
                msgId,
                msgSeq,
                session.SessionId,
                payload);
            _dispatcher?.OnPost(clientRouteMsg);
        }
        else
        {
            _logger?.LogWarning("Unauthenticated session {SessionId} tried to send message: {MsgId}",
                session.SessionId, msgId);
        }
    }

    private async Task HandleAuthenticationAsync(
        ITransportSession session,
        ushort msgSeq,
        long stageId,
        ReadOnlyMemory<byte> payload)
    {
        try
        {
            // DefaultStageType이 설정되지 않은 경우: 인증만 처리
            if (string.IsNullOrEmpty(_options.DefaultStageType))
            {
                // 간단한 인증 플로우: Stage 없이 인증만 처리
                session.IsAuthenticated = true;
                session.AccountId = GenerateAccountId(); // 임시 AccountId 생성
                await SendAuthReplyAsync(session, msgSeq, 0, BaseErrorCode.Success, session.AccountId);
                return;
            }

            // 기존 로직: DefaultStageType이 있는 경우 Stage 생성 및 Actor 참가
            // 1. Stage 조회/생성 (DefaultStageType 사용)
            var targetStageId = stageId != 0 ? stageId : GenerateStageId();
            var baseStage = _dispatcher?.GetOrCreateStage(targetStageId, _options.DefaultStageType);

            if (baseStage == null)
            {
                _logger?.LogError("Failed to get or create stage {StageId} for authentication", targetStageId);
                await SendAuthReplyAsync(session, msgSeq, targetStageId, BaseErrorCode.StageCreationFailed);
                return;
            }

            // 2. Stage 초기화 (OnCreate 호출) - 처음 생성된 경우에만
            if (!baseStage.IsCreated)
            {
                var createPacket = CPacket.Empty("CreateStage");
                var (createSuccess, _) = await baseStage.CreateStage(_options.DefaultStageType, createPacket);

                if (!createSuccess)
                {
                    _logger?.LogError("Failed to initialize stage {StageId}", targetStageId);
                    await SendAuthReplyAsync(session, msgSeq, targetStageId, BaseErrorCode.StageCreationFailed);
                    return;
                }
            }

            // 3. 별도 Task에서 Actor 콜백 호출
            var (success, errorCode, actor) = await Task.Run(async () =>
            {
                try
                {
                    // XActorSender 생성 (transport session 포함하여 직접 클라이언트 통신 가능)
                    var actorSender = new XActorSender(_options.ServerId, session.SessionId, _options.ServerId, baseStage, session);

                    // IActor 생성
                    BaseActor actor;
                    try
                    {
                        IActor iActor = _producer.GetActor(_options.DefaultStageType, actorSender);
                        actor = new BaseActor(iActor, actorSender);
                    }
                    catch (KeyNotFoundException)
                    {
                        _logger?.LogError("Actor factory not found for stage type: {StageType}", _options.DefaultStageType);
                        return (false, BaseErrorCode.InvalidStageType, (BaseActor?)null);
                    }

                    // 4. Actor 콜백 순차 호출
                    await actor.Actor.OnCreate();

                    var authPacket = CPacket.Of(_options.AuthenticateMessageId, payload.ToArray());
                    var authResult = await actor.Actor.OnAuthenticate(authPacket);

                    if (!authResult)
                    {
                        _logger?.LogWarning("Authentication rejected for session {SessionId}", session.SessionId);
                        await actor.Actor.OnDestroy();
                        return (false, BaseErrorCode.AuthenticationFailed, (BaseActor?)null);
                    }

                    // 5. AccountId 검증
                    if (string.IsNullOrEmpty(actorSender.AccountId))
                    {
                        _logger?.LogError("AccountId not set after authentication for session {SessionId}", session.SessionId);
                        await actor.Actor.OnDestroy();
                        return (false, BaseErrorCode.InvalidAccountId, (BaseActor?)null);
                    }

                    // 6. 세션에 인증 정보 설정
                    session.AccountId = actorSender.AccountId;
                    session.IsAuthenticated = true;
                    session.StageId = targetStageId;
                    await actor.Actor.OnPostAuthenticate();

                    return (true, BaseErrorCode.Success, actor);
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Error during actor authentication callbacks");
                    return (false, BaseErrorCode.InternalError, (BaseActor?)null);
                }
            });

            if (!success || actor == null)
            {
                await SendAuthReplyAsync(session, msgSeq, targetStageId, errorCode);
                return;
            }

            // 7. Stage Queue에 JoinActorMessage 전달하고 완료 대기
            // 이렇게 하면 클라이언트가 인증 응답을 받기 전에 Actor가 Stage에 완전히 조인됨
            var joinCompletionSource = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            _dispatcher?.OnPost(new JoinActorMessage(targetStageId, actor, joinCompletionSource));

            try
            {
                // 최대 5초 대기 (타임아웃)
                using var cts = new CancellationTokenSource(5000);
                await joinCompletionSource.Task.WaitAsync(cts.Token);
            }
            catch (TimeoutException)
            {
                _logger?.LogError("Join actor timeout for session {SessionId}, accountId {AccountId}",
                    session.SessionId, actor.ActorSender.AccountId);
                await SendAuthReplyAsync(session, msgSeq, targetStageId, BaseErrorCode.InternalError);
                return;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to join actor for session {SessionId}", session.SessionId);
                await SendAuthReplyAsync(session, msgSeq, targetStageId, BaseErrorCode.InternalError);
                return;
            }

            // 8. Actor가 완전히 조인된 후 클라이언트에 응답
            await SendAuthReplyAsync(session, msgSeq, targetStageId, BaseErrorCode.Success, actor.ActorSender.AccountId);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Authentication failed for session {SessionId}", session.SessionId);
            await SendAuthReplyAsync(session, msgSeq, stageId, BaseErrorCode.InternalError);
        }
    }

    private async Task SendAuthReplyAsync(
        ITransportSession session,
        ushort msgSeq,
        long stageId,
        ushort errorCode,
        string? accountId = null)
    {
        var reply = new Proto.AuthenticateReply
        {
            Authenticated = errorCode == BaseErrorCode.Success,
            StageId = (int)stageId,
            AccountId = long.TryParse(accountId, out var id) ? id : 0,
            ErrorMessage = errorCode == BaseErrorCode.Success ? "" : $"Error code: {errorCode}"
        };

        var response = TcpTransportSession.CreateResponsePacket(
            _options.AuthenticateMessageId.Replace("Request", "Reply"),
            msgSeq,
            stageId,
            errorCode,
            reply.ToByteArray());

        await session.SendAsync(response);
    }

    private long GenerateStageId()
    {
        // Simple stage ID generation (timestamp-based)
        return DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    }

    private string GenerateAccountId()
    {
        // Simple account ID generation (timestamp-based)
        return DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString();
    }

    /// <summary>
    /// Transport에서 클라이언트 연결 해제 시 호출됩니다.
    /// </summary>
    private void OnClientDisconnect(ITransportSession session, Exception? ex)
    {
        if (ex != null)
        {
            _logger?.LogWarning(ex, "Session {SessionId} disconnected with error", session.SessionId);
        }
        else
        {
            _logger?.LogDebug("Session {SessionId} disconnected", session.SessionId);
        }

        // 인증된 세션인 경우 DisconnectMessage 전달
        if (session.IsAuthenticated && !string.IsNullOrEmpty(session.AccountId) && session.StageId != 0)
        {
            _dispatcher?.OnPost(new DisconnectMessage(session.StageId, session.AccountId));
        }
    }

    /// <summary>
    /// 특정 세션에 Push 메시지를 전송합니다.
    /// </summary>
    public async ValueTask SendPushAsync(long sessionId, string msgId, long stageId, ReadOnlyMemory<byte> payload)
    {
        var session = _transportServer?.GetSession(sessionId);
        if (session?.IsConnected == true)
        {
            var response = TcpTransportSession.CreateResponsePacket(
                msgId, 0, stageId, 0, payload.Span);
            await session.SendAsync(response);
        }
    }


    /// <summary>
    /// 특정 세션을 연결 해제합니다.
    /// </summary>
    public async ValueTask DisconnectSessionAsync(long sessionId)
    {
        if (_transportServer != null)
        {
            await _transportServer.DisconnectSessionAsync(sessionId);
        }
    }

    /// <summary>
    /// 특정 세션을 조회합니다.
    /// </summary>
    public ITransportSession? GetSession(long sessionId)
    {
        return _transportServer?.GetSession(sessionId);
    }

    /// <summary>
    /// 연결된 클라이언트 수를 반환합니다.
    /// </summary>
    public int ConnectedClientCount => _transportServer?.SessionCount ?? 0;

    /// <summary>
    /// 실제 바인딩된 TCP 포트를 가져옵니다.
    /// TCP가 비활성화된 경우 0을 반환합니다.
    /// </summary>
    private int GetActualTcpPort()
    {
        if (_transportServer is CompositeTransportServer composite)
        {
            var tcpServer = composite.TcpServers.FirstOrDefault();
            return tcpServer?.ActualPort ?? _options.TcpPort ?? 0;
        }

        if (_transportServer is TcpTransportServer tcp)
        {
            return tcp.ActualPort;
        }

        return _options.TcpPort ?? 0;
    }

    /// <summary>
    /// WebSocket 서버 목록을 반환합니다 (ASP.NET Core 미들웨어 등록용).
    /// </summary>
    public IEnumerable<WebSocketTransportServer> GetWebSocketServers()
    {
        if (_transportServer is CompositeTransportServer composite)
        {
            return composite.WebSocketServers;
        }

        if (_transportServer is WebSocketTransportServer wsServer)
        {
            return new[] { wsServer };
        }

        return Enumerable.Empty<WebSocketTransportServer>();
    }

    /// <inheritdoc/>
    public async ValueTask SendClientReplyAsync(long sessionId, string msgId, ushort msgSeq, long stageId, ushort errorCode, ReadOnlyMemory<byte> payload)
    {
        var session = _transportServer?.GetSession(sessionId);
        if (session?.IsConnected == true)
        {
            var response = TcpTransportSession.CreateResponsePacket(
                msgId, msgSeq, stageId, errorCode, payload.Span);
            await session.SendAsync(response);
        }
        else
        {
            _logger?.LogWarning("Cannot send client reply: session {SessionId} not found or disconnected", sessionId);
        }
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        await StopAsync();

        if (_transportServer != null)
        {
            await _transportServer.DisposeAsync();
        }

        _communicator?.Dispose();
    }

    /// <summary>
    /// Communicator를 IClientCommunicator로 래핑.
    /// </summary>
    private sealed class CommunicatorAdapter : IClientCommunicator
    {
        private readonly PlayCommunicator _communicator;

        public CommunicatorAdapter(PlayCommunicator communicator)
        {
            _communicator = communicator;
        }

        public string ServerId => _communicator.ServerId;

        public void Send(string targetNid, RuntimeRoutePacket packet)
        {
            _communicator.Send(targetNid, packet);
        }

        public void Connect(string targetNid, string address)
        {
            _communicator.Connect(targetNid, address);
        }

        public void Disconnect(string targetNid, string endpoint)
        {
            _communicator.Disconnect(targetNid, endpoint);
        }

        public void Communicate()
        {
            _communicator.Communicate();
        }

        public void Stop()
        {
            _communicator.Stop();
        }
    }
}
