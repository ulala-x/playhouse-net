#nullable enable

using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PlayHouse.Abstractions;
using PlayHouse.Core.Messaging;
using PlayHouse.Core.Session;
using PlayHouse.Core.Stage;
using PlayHouse.Core.Timer;
using PlayHouse.Infrastructure.Http;
using PlayHouse.Infrastructure.Serialization;

namespace PlayHouse.Tests.Shared;

/// <summary>
/// 테스트용 서버 Fixture.
/// HTTP API + TCP를 모두 지원하는 통합 서버입니다.
/// </summary>
public class TestServerFixture : IAsyncDisposable
{
    private WebApplication? _app;
    private int _tcpPort;
    private int _httpPort;
    private readonly StageTypeRegistry _registry = new();

    public TestServerFixture()
    {
        _tcpPort = GetAvailablePort();
        _httpPort = GetAvailablePort();
    }

    /// <summary>
    /// TCP 서버 포트
    /// </summary>
    public int Port => _tcpPort;

    /// <summary>
    /// HTTP API 포트
    /// </summary>
    public int HttpPort => _httpPort;

    /// <summary>
    /// TCP 엔드포인트
    /// </summary>
    public string Endpoint => $"tcp://127.0.0.1:{_tcpPort}";

    /// <summary>
    /// HTTP API 기본 URL
    /// </summary>
    public string HttpBaseUrl => $"http://localhost:{_httpPort}";

    /// <summary>
    /// 서버가 시작되었는지 여부
    /// </summary>
    public bool IsStarted => _app != null;

    /// <summary>
    /// Stage 타입을 등록합니다.
    /// </summary>
    public TestServerFixture RegisterStage<TStage>(string stageTypeName) where TStage : IStage
    {
        _registry.RegisterStageType<TStage>(stageTypeName);
        return this;
    }

    /// <summary>
    /// Actor 타입을 등록합니다.
    /// </summary>
    public TestServerFixture RegisterActor<TActor>(string stageTypeName) where TActor : IActor
    {
        _registry.RegisterActorType<TActor>(stageTypeName);
        return this;
    }

    /// <summary>
    /// 서버를 시작합니다 (HTTP API + TCP).
    /// </summary>
    public async Task<TestServer> StartServerAsync()
    {
        if (_app != null)
        {
            throw new InvalidOperationException("Server is already started");
        }

        var builder = WebApplication.CreateBuilder();

        // ASP.NET Core 서비스 등록 (RoomController가 포함된 어셈블리 추가)
        builder.Services.AddControllers()
            .AddApplicationPart(typeof(RoomController).Assembly);

        // PlayHouse 옵션 설정
        builder.Services.AddOptions<PlayHouseOptions>()
            .Configure(opts =>
            {
                opts.Ip = "127.0.0.1";
                opts.Port = _tcpPort;
            })
            .ValidateOnStart();

        // PlayHouse Core 서비스
        builder.Services.AddSingleton<PacketSerializer>();
        builder.Services.AddSingleton<SessionManager>();
        builder.Services.AddSingleton<StagePool>();
        builder.Services.AddSingleton<PacketDispatcher>();
        builder.Services.AddSingleton<RoomTokenManager>();

        // StageTypeRegistry
        builder.Services.AddSingleton(_registry);

        // TimerManager
        builder.Services.AddSingleton<TimerManager>(sp =>
        {
            var dispatcher = sp.GetRequiredService<PacketDispatcher>();
            var logger = sp.GetRequiredService<ILoggerFactory>()
                .CreateLogger<TimerManager>();
            return new TimerManager(
                packet => dispatcher.Dispatch(packet),
                logger);
        });

        // StageFactory
        builder.Services.AddSingleton<StageFactory>(sp =>
        {
            var stagePool = sp.GetRequiredService<StagePool>();
            var dispatcher = sp.GetRequiredService<PacketDispatcher>();
            var timerManager = sp.GetRequiredService<TimerManager>();
            var sessionManager = sp.GetRequiredService<SessionManager>();
            var loggerFactory = sp.GetRequiredService<ILoggerFactory>();

            var factory = new StageFactory(
                stagePool,
                dispatcher,
                timerManager,
                sessionManager,
                loggerFactory);

            // Registry에 등록된 타입들을 StageFactory Registry로 복사
            foreach (var (stageTypeName, stageType) in _registry.GetAllStageTypes())
            {
                factory.Registry.RegisterStageType(stageTypeName, stageType);
            }
            foreach (var (stageTypeName, actorType) in _registry.GetAllActorTypes())
            {
                factory.Registry.RegisterActorType(stageTypeName, actorType);
            }

            return factory;
        });

        // PlayHouseServer (IHostedService)
        builder.Services.AddHostedService<PlayHouseServer>();
        builder.Services.AddSingleton<PlayHouseServer>(sp =>
            sp.GetServices<IHostedService>()
                .OfType<PlayHouseServer>()
                .First());

        // 로깅 설정
        builder.Logging.SetMinimumLevel(LogLevel.Debug);
        builder.Logging.AddConsole();

        // Kestrel HTTP 포트 설정
        builder.WebHost.UseUrls($"http://localhost:{_httpPort}");

        _app = builder.Build();

        // HTTP API 라우팅
        _app.MapControllers();

        await _app.StartAsync();
        return new TestServer(_app, _tcpPort, _httpPort);
    }

    /// <summary>
    /// 새 포트로 서버를 재시작합니다.
    /// </summary>
    public async Task<TestServer> RestartServerAsync()
    {
        if (_app != null)
        {
            await _app.StopAsync();
            await _app.DisposeAsync();
            _app = null;
        }

        _tcpPort = GetAvailablePort();
        _httpPort = GetAvailablePort();
        return await StartServerAsync();
    }

    /// <summary>
    /// 리소스를 정리합니다.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (_app != null)
        {
            await _app.StopAsync();
            await _app.DisposeAsync();
            _app = null;
        }
    }

    private static int GetAvailablePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}

/// <summary>
/// 실행 중인 테스트 서버 래퍼.
/// </summary>
public sealed class TestServer
{
    private readonly WebApplication _app;
    private readonly HttpClient _httpClient;

    public TestServer(WebApplication app, int tcpPort, int httpPort)
    {
        _app = app;
        TcpPort = tcpPort;
        HttpPort = httpPort;

        // HttpClient 생성 (테스트용)
        _httpClient = new HttpClient
        {
            BaseAddress = new Uri($"http://localhost:{httpPort}")
        };
    }

    /// <summary>
    /// TCP 포트
    /// </summary>
    public int TcpPort { get; }

    /// <summary>
    /// HTTP 포트
    /// </summary>
    public int HttpPort { get; }

    /// <summary>
    /// TCP 엔드포인트
    /// </summary>
    public string TcpEndpoint => $"tcp://127.0.0.1:{TcpPort}";

    /// <summary>
    /// HTTP 기본 URL
    /// </summary>
    public string HttpBaseUrl => $"http://localhost:{HttpPort}";

    /// <summary>
    /// 하위 호환성을 위한 Port 프로퍼티 (TcpPort와 동일)
    /// </summary>
    [Obsolete("Use TcpPort instead")]
    public int Port => TcpPort;

    /// <summary>
    /// 하위 호환성을 위한 Endpoint 프로퍼티 (TcpEndpoint와 동일)
    /// </summary>
    [Obsolete("Use TcpEndpoint instead")]
    public string Endpoint => TcpEndpoint;

    /// <summary>
    /// HTTP 클라이언트를 반환합니다.
    /// </summary>
    public HttpClient CreateHttpClient() => _httpClient;

    /// <summary>
    /// DI 컨테이너에서 서비스를 가져옵니다.
    /// </summary>
    public T GetService<T>() where T : notnull
        => _app.Services.GetRequiredService<T>();

    public StageFactory StageFactory => GetService<StageFactory>();
    public StagePool StagePool => GetService<StagePool>();
    public SessionManager SessionManager => GetService<SessionManager>();
    public PacketDispatcher PacketDispatcher => GetService<PacketDispatcher>();
    public PlayHouseServer Server => GetService<PlayHouseServer>();
    public RoomTokenManager TokenManager => GetService<RoomTokenManager>();
}
