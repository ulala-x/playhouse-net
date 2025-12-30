using System.CommandLine;
using System.Runtime;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PlayHouse.Abstractions;
using PlayHouse.Benchmark.SS.PlayServer;
using PlayHouse.Bootstrap;
using Serilog;
using Serilog.Events;
using Serilog.Extensions.Logging;

// GC 최적화: Batch 모드 (고처리량 우선, Gen2 GC 빈도 감소)
if (GCSettings.IsServerGC)
{
    GCSettings.LatencyMode = GCLatencyMode.Batch;
    Console.WriteLine($"[GC] Server GC enabled, LatencyMode set to Batch");
}
else
{
    Console.WriteLine($"[GC] Warning: Workstation GC detected, Server GC recommended");
}

// CLI 인자 파싱
var tcpPortOption = new Option<int>(
    name: "--tcp-port",
    description: "PlayServer TCP port for client connections",
    getDefaultValue: () => 16110);

var zmqPortOption = new Option<int>(
    name: "--zmq-port",
    description: "ZMQ port for server-to-server communication",
    getDefaultValue: () => 16100);

var httpPortOption = new Option<int>(
    name: "--http-port",
    description: "HTTP API port for metrics",
    getDefaultValue: () => 5080);

var serverIdOption = new Option<string>(
    name: "--server-id",
    description: "Server ID (NID) for this PlayServer",
    getDefaultValue: () => "play-1");

var apiNidOption = new Option<string>(
    name: "--api-nid",
    description: "API Server NID (for play-to-api mode)",
    getDefaultValue: () => "api-1");

var apiPortOption = new Option<int>(
    name: "--api-port",
    description: "API Server ZMQ port (for play-to-api mode)",
    getDefaultValue: () => 16201);

var logDirOption = new Option<string>(
    name: "--log-dir",
    description: "Directory for log files",
    getDefaultValue: () => "logs");

var targetNidOption = new Option<string>(
    name: "--target-nid",
    description: "Target PlayServer NID (for play-to-stage mode)",
    getDefaultValue: () => "");

var targetPortOption = new Option<int>(
    name: "--target-port",
    description: "Target PlayServer ZMQ port (for play-to-stage mode)",
    getDefaultValue: () => 0);

var rootCommand = new RootCommand("PlayHouse Benchmark Server (Server-to-Server)")
{
    tcpPortOption,
    zmqPortOption,
    httpPortOption,
    serverIdOption,
    apiNidOption,
    apiPortOption,
    logDirOption,
    targetNidOption,
    targetPortOption
};

var tcpPort = 0;
var zmqPort = 0;
var httpPort = 0;
var serverId = "play-1";
var apiNid = "api-1";
var apiPort = 16201;
var logDir = "logs";
var targetNid = "";
var targetPort = 0;

rootCommand.SetHandler((context) =>
{
    tcpPort = context.ParseResult.GetValueForOption(tcpPortOption);
    zmqPort = context.ParseResult.GetValueForOption(zmqPortOption);
    httpPort = context.ParseResult.GetValueForOption(httpPortOption);
    serverId = context.ParseResult.GetValueForOption(serverIdOption)!;
    apiNid = context.ParseResult.GetValueForOption(apiNidOption)!;
    apiPort = context.ParseResult.GetValueForOption(apiPortOption);
    logDir = context.ParseResult.GetValueForOption(logDirOption)!;
    targetNid = context.ParseResult.GetValueForOption(targetNidOption)!;
    targetPort = context.ParseResult.GetValueForOption(targetPortOption);
});

await rootCommand.InvokeAsync(args);

if (tcpPort == 0)
{
    tcpPort = 16110;
    zmqPort = 16100;
    httpPort = 5080;
    apiPort = 16201;
}

// 로그 디렉토리 생성
Directory.CreateDirectory(logDir);

// Serilog 설정
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
    .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
    .Enrich.FromLogContext()
    .WriteTo.Console(
        outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
    .WriteTo.File(
        Path.Combine(logDir, "benchmark-ss-playserver-.log"),
        rollingInterval: RollingInterval.Day,
        outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {Message:lj}{NewLine}{Exception}",
        retainedFileCountLimit: 30)
    .CreateLogger();

try
{
    Log.Information("================================================================================");
    Log.Information("PlayHouse Benchmark Server (Server-to-Server)");
    Log.Information("================================================================================");
    Log.Information("Server ID: {ServerId}", serverId);
    Log.Information("API Server NID: {ApiNid}", apiNid);
    Log.Information("ZMQ Port (server-to-server): {ZmqPort}", zmqPort);
    Log.Information("TCP Port (client connections): {TcpPort}", tcpPort);
    Log.Information("HTTP API Port: {HttpPort}", httpPort);
    Log.Information("Log Directory: {LogDir}", Path.GetFullPath(logDir));
    Log.Information("HTTP API Endpoints:");
    Log.Information("  GET  http://localhost:{HttpPort}/benchmark/stats - Get metrics", httpPort);
    Log.Information("  POST http://localhost:{HttpPort}/benchmark/reset - Reset metrics", httpPort);
    Log.Information("  POST http://localhost:{HttpPort}/benchmark/shutdown - Shutdown server", httpPort);
    Log.Information("================================================================================");

    // PlayServer 구성
    using var loggerFactory = LoggerFactory.Create(builder =>
    {
        builder.AddSerilog(Log.Logger);
    });
    var logger = loggerFactory.CreateLogger<PlayServer>();

    var playServer = new PlayServerBootstrap()
        .Configure(options =>
        {
            options.ServiceType = ServiceType.Play;
            options.ServerId = serverId;
            options.BindEndpoint = $"tcp://127.0.0.1:{zmqPort}";
            options.TcpPort = tcpPort;
            options.AuthenticateMessageId = "Authenticate";
            options.DefaultStageType = "BenchmarkStage";
        })
        .UseLogger(logger)
        .UseStage<BenchmarkStage>("BenchmarkStage")
        .UseActor<BenchmarkActor>()
        .Build();

    // 종료 토큰 생성
    var cts = new CancellationTokenSource();

    // HTTP API 서버 구성
    Environment.SetEnvironmentVariable("ASPNETCORE_URLS", $"http://0.0.0.0:{httpPort}");
    var builder = WebApplication.CreateBuilder(args);

    // Serilog를 ASP.NET Core에 통합
    builder.Host.UseSerilog();
    builder.Services.AddControllers();
    builder.Services.AddEndpointsApiExplorer();

    // CancellationTokenSource를 DI에 등록
    builder.Services.AddSingleton(cts);

    var app = builder.Build();
    app.MapControllers();

    // MessagePool Prewarm (런타임 할당 방지)
    Net.Zmq.MessagePool.Shared.Prewarm(Net.Zmq.MessageSize.K2, 500);
    Log.Information("MessagePool prewarmed: K2 x 500");

    // PlayServer 시작
    await playServer.StartAsync();
    Log.Information("PlayServer started (ID: {ServerId}, ZMQ: {ZmqPort}, TCP: {TcpPort})", serverId, zmqPort, tcpPort);

    // ApiServer에 수동 연결 (프로세스 간 TestSystemController 공유 불가로 수동 연결 필요)
    var apiEndpoint = $"tcp://127.0.0.1:{apiPort}";
    playServer.Connect(apiNid, apiEndpoint);
    Log.Information("Connected to ApiServer (NID: {ApiNid}, Endpoint: {ApiEndpoint})", apiNid, apiEndpoint);

    // 다른 PlayServer에 연결 (play-to-stage 모드)
    if (!string.IsNullOrEmpty(targetNid) && targetPort > 0)
    {
        var targetEndpoint = $"tcp://127.0.0.1:{targetPort}";
        playServer.Connect(targetNid, targetEndpoint);
        Log.Information("Connected to target PlayServer (NID: {TargetNid}, Endpoint: {TargetEndpoint})", targetNid, targetEndpoint);
    }

    // HTTP API 서버 시작
    await app.StartAsync();
    Log.Information("HTTP API server started on port {HttpPort}", httpPort);
    Log.Information("Press Ctrl+C to stop...");

    // 종료 대기
    Console.CancelKeyPress += (s, e) =>
    {
        e.Cancel = true;
        cts.Cancel();
    };

    try
    {
        await Task.Delay(-1, cts.Token);
    }
    catch (TaskCanceledException)
    {
        Log.Information("Shutting down...");
    }

    // 정리
    await app.StopAsync();
    await playServer.StopAsync();

    Log.Information("Server stopped.");
}
catch (Exception ex)
{
    Log.Fatal(ex, "Server terminated unexpectedly");
}
finally
{
    await Log.CloseAndFlushAsync();
}
