#nullable enable

using Microsoft.Extensions.DependencyInjection;
using PlayHouse.Abstractions.Api;
using PlayHouse.Core.Api;
using PlayHouse.Core.Messaging;
using PlayHouse.Core.Shared;
using PlayHouse.Runtime;
using PlayHouse.Runtime.Communicator;
using PlayHouse.Runtime.Message;

namespace PlayHouse.Bootstrap;

/// <summary>
/// API Server 인스턴스.
/// Play Server와 NetMQ로 통신하며 Stateless 요청 처리를 담당합니다.
/// </summary>
public sealed class ApiServer : IAsyncDisposable
{
    private readonly ApiServerOption _options;
    private readonly List<Type> _controllerTypes;
    private readonly ServerConfig _serverConfig;

    private PlayCommunicator? _communicator;
    private ApiDispatcher? _dispatcher;
    private RequestCache? _requestCache;
    private ServiceProvider? _serviceProvider;
    private CancellationTokenSource? _cts;

    private bool _isRunning;
    private bool _disposed;

    /// <summary>
    /// 서버가 실행 중인지 여부.
    /// </summary>
    public bool IsRunning => _isRunning;

    /// <summary>
    /// 서버 NID.
    /// </summary>
    public string Nid => _options.Nid;

    /// <summary>
    /// API Sender 인터페이스.
    /// DI에 등록하여 Play Server에 요청을 보낼 때 사용합니다.
    /// </summary>
    public IApiSender? ApiSender { get; private set; }

    internal ApiServer(ApiServerOption options, List<Type> controllerTypes)
    {
        _options = options;
        _controllerTypes = controllerTypes;

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

        // 서비스 컨테이너 구성
        var services = new ServiceCollection();
        foreach (var controllerType in _controllerTypes)
        {
            services.AddTransient(controllerType);
        }
        _serviceProvider = services.BuildServiceProvider();

        // NetMQ Communicator 시작
        _communicator = PlayCommunicator.Create(_serverConfig);
        _communicator.Bind(_options.BindEndpoint);
        _communicator.OnReceive(HandleReceivedMessage);
        _communicator.Start();

        // ApiDispatcher 생성
        _dispatcher = new ApiDispatcher(
            _options.ServiceId,
            _options.Nid,
            _requestCache,
            new CommunicatorAdapter(_communicator),
            _serviceProvider);

        // ApiSender 생성
        ApiSender = new Core.Api.ApiSender(
            new CommunicatorAdapter(_communicator),
            _requestCache,
            _options.ServiceId,
            _options.Nid);

        _isRunning = true;

        await Task.Delay(50); // 서버 초기화 대기
    }

    /// <summary>
    /// 서버를 중지합니다.
    /// </summary>
    public async Task StopAsync()
    {
        if (!_isRunning) return;

        _isRunning = false;
        _cts?.Cancel();

        _communicator?.Stop();
        _dispatcher?.Dispose();
        _requestCache?.CancelAll();
        _serviceProvider?.Dispose();

        _cts?.Dispose();
        _cts = null;

        await Task.CompletedTask;
    }

    /// <summary>
    /// Play Server에 연결합니다.
    /// </summary>
    /// <param name="playNid">Play Server NID (예: "1:1")</param>
    /// <param name="address">Play Server 주소 (예: "tcp://localhost:5000")</param>
    public void ConnectToPlayServer(string playNid, string address)
    {
        _communicator?.Connect(playNid, address);
    }

    private void HandleReceivedMessage(string senderNid, RuntimeRoutePacket packet)
    {
        // 응답 패킷인 경우 RequestCache에서 처리
        if (packet.MsgSeq > 0)
        {
            var response = CPacket.Of(packet.MsgId, packet.GetPayloadBytes());
            if (_requestCache?.TryComplete(packet.MsgSeq, response) == true)
            {
                packet.Dispose();
                return;
            }
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

        public string Nid => _communicator.Nid;

        public void Send(string targetNid, RuntimeRoutePacket packet)
        {
            _communicator.Send(targetNid, packet);
        }

        public void Connect(string targetNid, string address)
        {
            _communicator.Connect(targetNid, address);
        }

        public void Disconnect(string targetNid)
        {
            _communicator.Disconnect(targetNid);
        }
    }
}
