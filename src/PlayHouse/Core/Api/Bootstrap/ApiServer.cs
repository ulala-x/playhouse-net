#nullable enable

using Microsoft.Extensions.DependencyInjection;
using PlayHouse.Abstractions;
using PlayHouse.Abstractions.Api;
using PlayHouse.Abstractions.System;
using PlayHouse.Bootstrap;
using PlayHouse.Core.Messaging;
using PlayHouse.Core.Shared;
using PlayHouse.Runtime.ServerMesh;
using PlayHouse.Runtime.ServerMesh.Communicator;
using PlayHouse.Runtime.ServerMesh.Discovery;
using PlayHouse.Runtime.ServerMesh.Message;

namespace PlayHouse.Core.Api.Bootstrap;

/// <summary>
/// API Server 인스턴스.
/// Play Server와 ZMQ로 통신하며 Stateless 요청 처리를 담당합니다.
/// </summary>
public sealed class ApiServer : IApiServerControl, IAsyncDisposable, ICommunicateListener
{
    private readonly ApiServerOption _options;
    private readonly List<Type> _controllerTypes;
    private readonly Type? _systemControllerType;
    private readonly ServerConfig _serverConfig;
    private readonly IServiceProvider _serviceProvider;

    private PlayCommunicator? _communicator;
    private ApiDispatcher? _dispatcher;
    private RequestCache? _requestCache;
    private ServerAddressResolver? _addressResolver;
    private CancellationTokenSource? _cts;

    private bool _isRunning;
    private bool _disposed;

    /// <summary>
    /// 서버가 실행 중인지 여부.
    /// </summary>
    public bool IsRunning => _isRunning;

    /// <summary>
    /// API Sender 인터페이스.
    /// DI에 등록하여 Play Server에 요청을 보낼 때 사용합니다.
    /// </summary>
    public IApiSender? ApiSender { get; private set; }

    internal ApiServer(
        ApiServerOption options,
        List<Type> controllerTypes,
        Type? systemControllerType,
        IServiceProvider serviceProvider)
    {
        _options = options;
        _controllerTypes = controllerTypes;
        _systemControllerType = systemControllerType;
        _serviceProvider = serviceProvider;

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

        _communicator = new PlayCommunicator(_serverConfig);
        _communicator.Bind(this);
        _communicator.Start();

        // 자기 자신에게 연결 (자기 자신에게 메시지를 보내기 위해 필요)
        _communicator.Connect(_options.ServerId, _options.BindEndpoint);

        // ApiDispatcher 생성 (외부 ServiceProvider 사용)
        _dispatcher = new ApiDispatcher(
            _options.ServiceId,
            _options.ServerId,
            _requestCache,
            new CommunicatorAdapter(_communicator),
            _serviceProvider);

        // ApiSender 생성
        ApiSender = new ApiSender(
            new CommunicatorAdapter(_communicator),
            _requestCache,
            _options.ServiceId,
            _options.ServerId);

        // ServerAddressResolver 시작 (SystemController가 등록된 경우)
        if (_systemControllerType != null)
        {
            var systemController = _serviceProvider.GetRequiredService<ISystemController>();
            var serverInfoCenter = new XServerInfoCenter();

            var myServerInfo = new XServerInfo(
                _options.ServiceId,
                _options.ServerId,
                _options.BindEndpoint);

            _addressResolver = new ServerAddressResolver(
                myServerInfo,
                systemController,
                serverInfoCenter,
                _communicator,
                TimeSpan.FromSeconds(3));

            _addressResolver.Start();
        }

        _isRunning = true;

        await Task.Delay(50); // 서버 초기화 대기
    }

    /// <summary>
    /// 서버를 중지합니다.
    /// </summary>
    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (!_isRunning) return;

        _isRunning = false;
        _cts?.Cancel();

        _addressResolver?.Stop();
        _addressResolver?.Dispose();
        _communicator?.Stop();
        _dispatcher?.Dispose();
        _requestCache?.CancelAll();

        _cts?.Dispose();
        _cts = null;

        await Task.CompletedTask;
    }

    /// <summary>
    /// 다른 서버에 수동으로 연결합니다.
    /// </summary>
    /// <param name="targetNid">대상 서버 NID (예: "2:2" for ApiServer, "1:1" for PlayServer)</param>
    /// <param name="address">대상 서버 주소 (예: "tcp://localhost:5000")</param>
    /// <remarks>
    /// UseSystemController()를 사용한 경우 ServerAddressResolver가 자동으로 서버를 연결하므로
    /// 이 메서드를 호출할 필요가 없습니다. 수동 연결이 필요한 경우에만 사용하세요.
    /// </remarks>
    public void Connect(string targetNid, string address)
    {
        _communicator?.Connect(targetNid, address);
    }

    /// <inheritdoc/>
    public void OnReceive(RoutePacket packet)
    {
        // 응답 패킷인 경우 RequestCache에서 처리
        // IsReply 플래그로 응답/요청을 구분 (MsgSeq만으로는 구분 불가)
        if (packet.Header.IsReply && packet.MsgSeq > 0)
        {
            // Zero-copy: Transfer payload ownership from RoutePacket to CPacket
            var response = CPacket.Of(packet.MsgId, packet.Payload);
            if (_requestCache?.TryComplete(packet.MsgSeq, response) == true)
            {
                // CPacket now owns the payload - don't dispose RoutePacket
                // RoutePacket will be GC'd, CPacket.Dispose() will free the payload
                return;
            }
            // TryComplete failed - RoutePacket still owns payload, will go to dispatcher
        }

        // API Handler로 라우팅
        _dispatcher?.Post(packet);
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        await StopAsync();
        _communicator?.Dispose();
    }

    /// <summary>
    /// Communicator를 IClientCommunicator로 래핑.
    /// </summary>
    private sealed class CommunicatorAdapter(PlayCommunicator communicator) : IClientCommunicator
    {
        public void Send(string targetNid, RoutePacket packet)
        {
            communicator.Send(targetNid, packet);
        }

        public void Connect(string targetNid, string address)
        {
            communicator.Connect(targetNid, address);
        }

        public void Disconnect(string targetNid, string endpoint)
        {
            communicator.Disconnect(targetNid, endpoint);
        }

        public void Stop()
        {
            communicator.Stop();
        }
    }
}
