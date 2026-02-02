using Microsoft.Extensions.Logging;
using PlayHouse.Core.Api.Bootstrap;
using PlayHouse.Core.Play.Bootstrap;
using PlayHouse.Runtime.ClientTransport.WebSocket;
using PlayHouse.TestServer.Play;
using PlayHouse.TestServer.Shared;

namespace PlayHouse.TestServer;

class Program
{
    static async Task<int> Main(string[] args)
    {
        // Configuration from environment variables or appsettings
        var config = LoadConfiguration();

        Console.WriteLine("========================================");
        Console.WriteLine("PlayHouse Test Server");
        Console.WriteLine("========================================");
        Console.WriteLine($"Play Server ID: {config.PlayServerId}");
        Console.WriteLine($"API Server ID: {config.ApiServerId}");
        Console.WriteLine($"TCP Port: {config.TcpPort}");
        Console.WriteLine($"HTTP Port: {config.HttpPort}");
        Console.WriteLine($"WebSocket: {(config.EnableWebSocket ? $"Enabled (path: {config.WebSocketPath})" : "Disabled")}");
        Console.WriteLine("========================================");

        try
        {
            // Start servers
            var serverContext = await StartServersAsync(config);

            Console.WriteLine("\nTest Server is running. Press Ctrl+C to stop.");

            // Wait for termination signal
            var tcs = new TaskCompletionSource<bool>();
            Console.CancelKeyPress += (sender, e) =>
            {
                e.Cancel = true;
                tcs.TrySetResult(true);
            };

            await tcs.Task;

            // Cleanup
            await StopServersAsync(serverContext);

            Console.WriteLine("\nTest Server stopped successfully.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\nFatal error: {ex.Message}");
            Console.WriteLine(ex.StackTrace);
            return 1;
        }
    }

    static ServerConfiguration LoadConfiguration()
    {
        // Load from environment variables with fallbacks
        var playServerId = Environment.GetEnvironmentVariable("PLAY_SERVER_ID") ?? "play-1";
        var apiServerId = Environment.GetEnvironmentVariable("API_SERVER_ID") ?? "api-1";

        var tcpPortStr = Environment.GetEnvironmentVariable("TCP_PORT");
        var httpPortStr = Environment.GetEnvironmentVariable("HTTP_PORT");

        var tcpPort = int.TryParse(tcpPortStr, out var tp) ? tp : 34001;
        var httpPort = int.TryParse(httpPortStr, out var hp) ? hp : 8080;

        // WebSocket 설정
        var enableWebSocket = Environment.GetEnvironmentVariable("ENABLE_WEBSOCKET")?.ToLower() != "false";
        var webSocketPath = Environment.GetEnvironmentVariable("WEBSOCKET_PATH") ?? "/ws";

        return new ServerConfiguration
        {
            PlayServerId = playServerId,
            ApiServerId = apiServerId,
            TcpPort = tcpPort,
            HttpPort = httpPort,
            ZmqPlayPort = 15000,
            ZmqApiPort = 15300,
            EnableWebSocket = enableWebSocket,
            WebSocketPath = webSocketPath
        };
    }

    static async Task<ServerContext> StartServersAsync(ServerConfiguration config)
    {
        Console.WriteLine("\n[서버 시작 중...]");

        // 1. PlayServer 생성 (WebSocket 서버 포함)
        var (playServer, wsServer) = await CreatePlayServerAsync(config);
        Console.WriteLine($"✓ PlayServer started on TCP:{config.TcpPort}, ZMQ:{config.ZmqPlayPort}");
        if (wsServer != null)
        {
            Console.WriteLine($"✓ WebSocket server initialized (path: {config.WebSocketPath})");
        }

        // 2. ApiServer + HTTP Server 생성 (WebSocket 핸들러 포함)
        var (apiServer, httpApp, actualHttpPort) = await CreateApiServerWithHttpAsync(config, wsServer);
        Console.WriteLine($"✓ ApiServer started on ZMQ:{config.ZmqApiPort}");
        Console.WriteLine($"✓ HTTP API Server started on HTTP:{actualHttpPort}");
        if (wsServer != null)
        {
            Console.WriteLine($"✓ WebSocket endpoint: ws://0.0.0.0:{actualHttpPort}{config.WebSocketPath}");
        }

        // 서버 간 연결 안정화 대기
        await Task.Delay(2000);

        Console.WriteLine("✓ All servers started successfully\n");

        return new ServerContext
        {
            PlayServer = playServer,
            ApiServer = apiServer,
            HttpApp = httpApp,
            HttpPort = actualHttpPort
        };
    }

    static async Task<(PlayHouse.Core.Play.Bootstrap.PlayServer Server, WebSocketTransportServer? WsServer)> CreatePlayServerAsync(
        ServerConfiguration config)
    {
        var loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.AddConsole().SetMinimumLevel(LogLevel.Information);
            builder.AddFilter((category, level) =>
            {
                // Filter out normal socket errors during shutdown
                if (category == "PlayHouse.Bootstrap.PlayServer" && level == LogLevel.Error)
                    return false;
                return true;
            });
        });

        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        services.AddSingleton(loggerFactory);
        services.AddSingleton<ILogger<PlayHouse.Core.Play.Bootstrap.PlayServer>>(
            sp => loggerFactory.CreateLogger<PlayHouse.Core.Play.Bootstrap.PlayServer>());
        services.AddSingleton<ILogger<TestStageActor>>(
            sp => loggerFactory.CreateLogger<TestStageActor>());
        services.AddSingleton<ILogger<TestActor>>(
            sp => loggerFactory.CreateLogger<TestActor>());
        services.AddSingleton<ILogger<TestSystemController>>(
            sp => loggerFactory.CreateLogger<TestSystemController>());
        services.AddSingleton<PlayHouse.Abstractions.System.ISystemController, TestSystemController>();

        var serviceProvider = services.BuildServiceProvider();

        var bootstrap = new PlayServerBootstrap()
            .Configure(options =>
            {
                options.ServerId = config.PlayServerId;
                options.BindEndpoint = $"tcp://127.0.0.1:{config.ZmqPlayPort}";
                options.TcpPort = config.TcpPort;
                options.RequestTimeoutMs = 30000;
                options.AuthenticateMessageId = "AuthenticateRequest";
                options.DefaultStageType = "TestStage";
            })
            .UseLoggerFactory(loggerFactory)
            .UseServiceProvider(serviceProvider)
            .UseStage<TestStageActor, TestActor>("TestStage")
            .UseSystemController<TestSystemController>();

        // WebSocket 활성화
        if (config.EnableWebSocket)
        {
            bootstrap.ConfigureWebSocket(config.WebSocketPath);
        }

        var playServer = bootstrap.Build();
        await playServer.StartAsync();

        // WebSocket 서버 추출
        WebSocketTransportServer? wsServer = null;
        if (config.EnableWebSocket)
        {
            var wsServers = playServer.GetWebSocketServers();
            wsServer = wsServers.FirstOrDefault();
        }

        return (playServer, wsServer);
    }

    static async Task<(PlayHouse.Core.Api.Bootstrap.ApiServer, WebApplication, int)>
        CreateApiServerWithHttpAsync(ServerConfiguration config, WebSocketTransportServer? wsServer)
    {
        var loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.AddConsole().SetMinimumLevel(LogLevel.Information);
        });

        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        services.AddSingleton<ILoggerFactory>(loggerFactory);
        services.AddSingleton<PlayHouse.Abstractions.System.ISystemController, TestSystemController>();

        var serviceProvider = services.BuildServiceProvider();

        // 1. ApiServer 생성
        var apiServer = new ApiServerBootstrap()
            .Configure(options =>
            {
                options.ServerId = config.ApiServerId;
                options.BindEndpoint = $"tcp://127.0.0.1:{config.ZmqApiPort}";
                options.RequestTimeoutMs = 30000;
            })
            .UseLoggerFactory(loggerFactory)
            .UseServiceProvider(serviceProvider)
            .UseSystemController<TestSystemController>()
            .Build();

        await apiServer.StartAsync();

        // 2. ASP.NET Core WebApplication 생성
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls($"http://0.0.0.0:{config.HttpPort}");

        // Logging
        builder.Logging.ClearProviders();
        builder.Logging.AddConsole().SetMinimumLevel(LogLevel.Information);

        // Configuration
        builder.Configuration["PlayServerId"] = config.PlayServerId;

        // Controllers
        builder.Services.AddControllers();

        // ApiLink를 싱글톤으로 등록
        var apiLink = apiServer.ApiLink
            ?? throw new InvalidOperationException("ApiServer.ApiLink is null");
        builder.Services.AddSingleton<PlayHouse.Abstractions.Api.IApiLink>(apiLink);

        var app = builder.Build();

        // WebSocket 미들웨어 설정
        if (wsServer != null && config.EnableWebSocket)
        {
            app.UseWebSockets();
            app.Map(config.WebSocketPath, async context =>
            {
                await wsServer.HandleAsync(context);
            });
        }

        // Routing
        app.MapControllers();

        await app.StartAsync();

        // Extract actual bound port
        var httpPort = ExtractHttpPort(app);

        return (apiServer, app, httpPort);
    }

    static int ExtractHttpPort(WebApplication app)
    {
        var url = app.Urls.FirstOrDefault()
            ?? throw new InvalidOperationException("No URLs found in WebApplication");
        var uri = new Uri(url);
        return uri.Port;
    }

    static async Task StopServersAsync(ServerContext ctx)
    {
        Console.WriteLine("\n[서버 종료 중...]");

        if (ctx.HttpApp != null)
            await ctx.HttpApp.StopAsync();

        if (ctx.ApiServer != null)
            await ctx.ApiServer.DisposeAsync();

        if (ctx.PlayServer != null)
            await ctx.PlayServer.DisposeAsync();

        Console.WriteLine("✓ All servers stopped");
    }
}

record ServerConfiguration
{
    public required string PlayServerId { get; init; }
    public required string ApiServerId { get; init; }
    public required int TcpPort { get; init; }
    public required int HttpPort { get; init; }
    public required int ZmqPlayPort { get; init; }
    public required int ZmqApiPort { get; init; }
    public bool EnableWebSocket { get; init; } = true;
    public string WebSocketPath { get; init; } = "/ws";
}

record ServerContext
{
    public required PlayHouse.Core.Play.Bootstrap.PlayServer PlayServer { get; init; }
    public required PlayHouse.Core.Api.Bootstrap.ApiServer ApiServer { get; init; }
    public required WebApplication HttpApp { get; init; }
    public required int HttpPort { get; init; }
}
