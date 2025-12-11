#nullable enable

using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Logging;
using PlayHouse.Abstractions.Play;

namespace PlayHouse.Bootstrap;

/// <summary>
/// Play Server 부트스트랩 빌더.
/// </summary>
/// <remarks>
/// 사용 예시:
/// <code>
/// // TCP만 사용하는 경우
/// var playServer = new PlayServerBootstrap()
///     .Configure(options =>
///     {
///         options.ServiceType = ServiceType.Play;
///         options.ServerId = 1;
///         options.BindEndpoint = "tcp://0.0.0.0:5000";
///         options.TcpPort = 6000;
///         options.AuthenticateMessageId = "AuthRequest";
///     })
///     .UseStage&lt;GameRoomStage&gt;("GameRoom")
///     .UseActor&lt;PlayerActor&gt;()
///     .Build();
///
/// // TCP + WebSocket 동시 사용
/// var playServer = new PlayServerBootstrap()
///     .Configure(options =>
///     {
///         options.TcpPort = 6000;           // TCP 활성화
///         options.WebSocketPath = "/ws";    // WebSocket 활성화
///     })
///     .Build();
///
/// // TCP + SSL
/// var playServer = new PlayServerBootstrap()
///     .ConfigureTcpWithSsl(6000, certificate)
///     .Build();
///
/// await playServer.StartAsync();
/// </code>
/// </remarks>
public sealed class PlayServerBootstrap
{
    private readonly PlayServerOption _options = new();
    private readonly Dictionary<string, Type> _stageTypes = new();
    private Type? _actorType;
    private ILogger? _logger;

    /// <summary>
    /// 서버 옵션을 설정합니다.
    /// </summary>
    /// <param name="configure">설정 액션.</param>
    /// <returns>빌더 인스턴스.</returns>
    public PlayServerBootstrap Configure(Action<PlayServerOption> configure)
    {
        configure(_options);
        return this;
    }

    /// <summary>
    /// TCP 서버를 설정합니다.
    /// </summary>
    /// <param name="port">TCP 포트.</param>
    /// <param name="bindAddress">바인드 주소 (기본값: 모든 인터페이스).</param>
    /// <returns>빌더 인스턴스.</returns>
    public PlayServerBootstrap ConfigureTcp(int port, string? bindAddress = null)
    {
        _options.TcpPort = port;
        _options.TcpBindAddress = bindAddress;
        return this;
    }

    /// <summary>
    /// TCP + SSL 서버를 설정합니다.
    /// </summary>
    /// <param name="port">TCP 포트.</param>
    /// <param name="certificate">SSL 인증서.</param>
    /// <param name="bindAddress">바인드 주소 (기본값: 모든 인터페이스).</param>
    /// <returns>빌더 인스턴스.</returns>
    public PlayServerBootstrap ConfigureTcpWithSsl(int port, X509Certificate2 certificate, string? bindAddress = null)
    {
        _options.TcpPort = port;
        _options.TcpBindAddress = bindAddress;
        _options.TcpSslCertificate = certificate;
        return this;
    }

    /// <summary>
    /// WebSocket 서버를 설정합니다.
    /// </summary>
    /// <param name="path">WebSocket 경로 (예: "/ws").</param>
    /// <returns>빌더 인스턴스.</returns>
    /// <remarks>
    /// WSS를 사용하려면 ASP.NET Core에서 HTTPS를 구성하세요.
    /// </remarks>
    public PlayServerBootstrap ConfigureWebSocket(string path = "/ws")
    {
        _options.WebSocketPath = path;
        return this;
    }

    /// <summary>
    /// Logger를 설정합니다.
    /// </summary>
    /// <param name="logger">ILogger 인스턴스.</param>
    /// <returns>빌더 인스턴스.</returns>
    public PlayServerBootstrap UseLogger(ILogger logger)
    {
        _logger = logger;
        return this;
    }

    /// <summary>
    /// Stage 타입을 등록합니다.
    /// </summary>
    /// <typeparam name="TStage">IStage 구현 타입.</typeparam>
    /// <param name="stageType">Stage 타입 이름.</param>
    /// <returns>빌더 인스턴스.</returns>
    /// <remarks>
    /// TStage는 IStageSender를 받는 생성자가 필요합니다.
    /// <code>
    /// public MyStage(IStageSender stageSender) { StageSender = stageSender; }
    /// </code>
    /// </remarks>
    public PlayServerBootstrap UseStage<TStage>(string stageType) where TStage : class, IStage
    {
        _stageTypes[stageType] = typeof(TStage);
        return this;
    }

    /// <summary>
    /// Actor 타입을 등록합니다.
    /// </summary>
    /// <typeparam name="TActor">IActor 구현 타입.</typeparam>
    /// <returns>빌더 인스턴스.</returns>
    /// <remarks>
    /// TActor는 IActorSender를 받는 생성자가 필요합니다.
    /// <code>
    /// public MyActor(IActorSender actorSender) { ActorSender = actorSender; }
    /// </code>
    /// </remarks>
    public PlayServerBootstrap UseActor<TActor>() where TActor : class, IActor
    {
        _actorType = typeof(TActor);
        return this;
    }

    /// <summary>
    /// Play Server 인스턴스를 생성합니다.
    /// </summary>
    /// <returns>PlayServer 인스턴스.</returns>
    public PlayServer Build()
    {
        _options.Validate();

        if (_stageTypes.Count == 0)
            throw new InvalidOperationException("At least one Stage type must be registered. Use UseStage<T>().");

        if (_actorType == null)
            throw new InvalidOperationException("Actor type must be registered. Use UseActor<T>().");

        var producer = new PlayProducer(_stageTypes, _actorType);
        return new PlayServer(_options, producer, _logger);
    }
}
