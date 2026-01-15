#nullable enable

using System.Net;
using System.Net.Sockets;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PlayHouse.Abstractions.Api;
using PlayHouse.Bootstrap;
using PlayHouse.Core.Api.Bootstrap;
using PlayHouse.Extensions;
using PlayHouse.Verification.Shared.Infrastructure;

namespace PlayHouse.Verification.Shared.Utils;

/// <summary>
/// PlayServer 및 ApiServer 생성을 위한 헬퍼 유틸리티.
/// </summary>
public static class ServerFactory
{
    /// <summary>
    /// 단일 PlayServer를 생성합니다.
    /// </summary>
    /// <param name="serverId">서버 ID (기본값: "1")</param>
    /// <param name="tcpPort">TCP 포트 (0이면 자동 할당)</param>
    /// <param name="zmqPort">ZMQ 포트 (0이면 자동 할당)</param>
    /// <param name="authenticateMessageId">인증 메시지 ID (기본값: "AuthenticateRequest")</param>
    /// <param name="defaultStageType">기본 Stage 타입 (기본값: "TestStage")</param>
    /// <param name="requestTimeoutMs">요청 타임아웃 (기본값: 30000ms)</param>
    /// <param name="logLevel">로그 레벨 (기본값: Warning)</param>
    public static async Task<PlayServer> CreatePlayServerAsync(
        string serverId = "1",
        int tcpPort = 0,
        int zmqPort = 0,
        string authenticateMessageId = "AuthenticateRequest",
        string defaultStageType = "TestStage",
        int requestTimeoutMs = 30000,
        LogLevel logLevel = LogLevel.Warning)
    {
        var loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.AddConsole().SetMinimumLevel(logLevel);
            // 서버 종료 시 정상적인 SocketException 로그 필터링
            builder.AddFilter((category, level) =>
            {
                if (category == "PlayHouse.Bootstrap.PlayServer" && level == LogLevel.Error)
                    return false; // Error in accept loop 로그 숨김
                return true;
            });
        });

        var playServer = new PlayServerBootstrap()
            .Configure(options =>
            {
                options.ServerId = serverId;
                options.BindEndpoint = $"tcp://127.0.0.1:{zmqPort}";
                options.TcpPort = tcpPort;
                options.RequestTimeoutMs = requestTimeoutMs;
                options.AuthenticateMessageId = authenticateMessageId;
                options.DefaultStageType = defaultStageType;
            })
            .UseLogger(loggerFactory.CreateLogger<PlayServer>())
            .UseStage<TestStageImpl, TestActorImpl>(defaultStageType)
            .UseSystemController<TestSystemController>()
            .Build();

        await playServer.StartAsync();
        return playServer;
    }

    /// <summary>
    /// DI를 지원하는 PlayServer를 생성합니다.
    /// AddPlayServer extension을 사용합니다.
    /// </summary>
    /// <param name="serverId">서버 ID (기본값: "1")</param>
    /// <param name="tcpPort">TCP 포트 (0이면 자동 할당)</param>
    /// <param name="zmqPort">ZMQ 포트 (0이면 자동 할당)</param>
    /// <param name="authenticateMessageId">인증 메시지 ID (기본값: "AuthenticateRequest")</param>
    /// <param name="defaultStageType">기본 Stage 타입 (기본값: "DITestStage")</param>
    /// <param name="requestTimeoutMs">요청 타임아웃 (기본값: 30000ms)</param>
    /// <param name="logLevel">로그 레벨 (기본값: Warning)</param>
    public static async Task<(PlayServer Server, IServiceProvider ServiceProvider)> CreateDIPlayServerAsync(
        string serverId = "di-1",
        int tcpPort = 0,
        int zmqPort = 0,
        string authenticateMessageId = "AuthenticateRequest",
        string defaultStageType = "DITestStage",
        int requestTimeoutMs = 30000,
        LogLevel logLevel = LogLevel.Warning)
    {
        var services = new ServiceCollection();

        // Logging 등록
        services.AddLogging(builder =>
        {
            builder.AddConsole().SetMinimumLevel(logLevel);
            // 서버 종료 시 정상적인 SocketException 로그 필터링
            builder.AddFilter((category, level) =>
            {
                if (category == "PlayHouse.Bootstrap.PlayServer" && level == LogLevel.Error)
                    return false; // Error in accept loop 로그 숨김
                return true;
            });
        });

        // 사용자 서비스 등록 (DI 검증용)
        services.AddSingleton<ITestService, TestService>();

        // PlayServer 등록 및 구성
        services.AddPlayServer(options =>
        {
            options.ServerId = serverId;
            options.BindEndpoint = $"tcp://127.0.0.1:{zmqPort}";
            options.TcpPort = tcpPort;
            options.RequestTimeoutMs = requestTimeoutMs;
            options.AuthenticateMessageId = authenticateMessageId;
            options.DefaultStageType = defaultStageType;
        })
        .UseStage<DITestStage, DITestActor>(defaultStageType)
        .UseSystemController<TestSystemController>();

        var serviceProvider = services.BuildServiceProvider();
        var playServer = serviceProvider.GetRequiredService<PlayServer>();

        await playServer.StartAsync();
        return (playServer, serviceProvider);
    }

    /// <summary>
    /// 단일 ApiServer를 생성합니다.
    /// </summary>
    /// <param name="serverId">서버 ID (기본값: "api-1")</param>
    /// <param name="zmqPort">ZMQ 포트 (0이면 자동 할당)</param>
    /// <param name="requestTimeoutMs">요청 타임아웃 (기본값: 30000ms)</param>
    /// <param name="logLevel">로그 레벨 (기본값: Warning)</param>
    public static async Task<ApiServer> CreateApiServerAsync(
        string serverId = "api-1",
        int zmqPort = 0,
        int requestTimeoutMs = 30000,
        LogLevel logLevel = LogLevel.Warning)
    {
        var apiServer = new ApiServerBootstrap()
            .Configure(options =>
            {
                options.ServerId = serverId;
                options.BindEndpoint = $"tcp://127.0.0.1:{zmqPort}";
                options.RequestTimeoutMs = requestTimeoutMs;
            })
            .UseController<TestApiController>()
            .UseSystemController<TestSystemController>()
            .Build();

        await apiServer.StartAsync();
        return apiServer;
    }

    /// <summary>
    /// ApiServer와 ASP.NET Core HTTP API를 함께 생성합니다.
    /// HTTP API를 통한 E2E 검증을 위해 사용됩니다.
    /// </summary>
    /// <param name="serverId">API 서버 ID (기본값: "api-1")</param>
    /// <param name="zmqPort">ZMQ 포트 (0이면 자동 할당)</param>
    /// <param name="playServerId">대상 PlayServer ID</param>
    /// <param name="requestTimeoutMs">요청 타임아웃 (기본값: 30000ms)</param>
    /// <param name="logLevel">로그 레벨 (기본값: Warning)</param>
    /// <returns>ApiServer 인스턴스, WebApplication 인스턴스, 바인딩된 HTTP 포트</returns>
    public static async Task<(ApiServer ApiServer, WebApplication HttpApp, int HttpPort)> CreateApiServerWithHttpAsync(
        string serverId = "api-1",
        int zmqPort = 0,
        string playServerId = "play-1",
        int requestTimeoutMs = 30000,
        LogLevel logLevel = LogLevel.Warning)
    {
        // 1. ApiServer 생성
        var apiServer = await CreateApiServerAsync(serverId, zmqPort, requestTimeoutMs, logLevel);

        // 2. ASP.NET Core WebApplication 생성
        var builder = WebApplication.CreateBuilder();

        // 동적 포트 할당 (0을 사용하여 자동 할당)
        builder.WebHost.UseUrls("http://127.0.0.1:0");

        // Logging 설정
        builder.Logging.ClearProviders();
        builder.Logging.AddConsole().SetMinimumLevel(logLevel);

        // Configuration 설정
        builder.Configuration["PlayServerId"] = playServerId;

        // Controllers 등록 (PlayHouse.Verification.Shared 어셈블리의 컨트롤러 포함)
        builder.Services.AddControllers()
            .AddApplicationPart(typeof(TestHttpApiController).Assembly);

        // ApiSender를 싱글톤으로 등록
        var apiSender = apiServer.ApiSender ?? throw new InvalidOperationException("ApiServer.ApiSender is null");
        builder.Services.AddSingleton<IApiSender>(apiSender);

        var app = builder.Build();

        // Controller 라우팅 설정
        app.MapControllers();

        // HTTP 서버 시작
        await app.StartAsync();

        // 실제 바인딩된 포트 추출
        var httpPort = ExtractHttpPort(app);

        return (apiServer, app, httpPort);
    }

    /// <summary>
    /// WebApplication에서 실제 바인딩된 HTTP 포트를 추출합니다.
    /// </summary>
    private static int ExtractHttpPort(WebApplication app)
    {
        // app.Urls에서 첫 번째 URL의 포트를 추출
        var url = app.Urls.FirstOrDefault()
            ?? throw new InvalidOperationException("No URLs found in WebApplication");

        // URL 형식: http://127.0.0.1:12345
        var uri = new Uri(url);
        return uri.Port;
    }

    /// <summary>
    /// 실제 바인딩된 TCP 포트를 가져옵니다.
    /// </summary>
    /// <param name="playServer">PlayServer 인스턴스</param>
    /// <returns>바인딩된 TCP 포트 번호</returns>
    public static int GetActualTcpPort(PlayServer playServer)
    {
        // PlayServer에서 실제 바인딩된 포트를 가져오는 로직
        // 포트 0으로 시작한 경우 자동 할당된 포트를 반환
        // 구현은 PlayServer의 내부 구조에 따라 조정 필요

        // 임시로 간단한 구현: reflection을 통해 가져오거나 public API 사용
        var tcpListenerField = playServer.GetType().GetField("_tcpListener",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        if (tcpListenerField?.GetValue(playServer) is TcpListener tcpListener)
        {
            return ((IPEndPoint)tcpListener.LocalEndpoint).Port;
        }

        throw new InvalidOperationException("Cannot get actual TCP port from PlayServer");
    }

    /// <summary>
    /// 사용 가능한 무작위 포트를 가져옵니다.
    /// </summary>
    public static int GetAvailablePort()
    {
        using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        socket.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        var port = ((IPEndPoint)socket.LocalEndPoint!).Port;
        return port;
    }
}
