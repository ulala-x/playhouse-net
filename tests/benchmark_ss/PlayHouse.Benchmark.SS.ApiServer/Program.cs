using System.CommandLine;
using System.Runtime;
using PlayHouse.Abstractions;
using PlayHouse.Benchmark.SS.ApiServer;
using PlayHouse.Bootstrap;
using Serilog;
using Serilog.Events;

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
var zmqPortOption = new Option<int>(
    name: "--zmq-port",
    description: "ZMQ port for server-to-server communication",
    getDefaultValue: () => 16201);

var playNidOption = new Option<string>(
    name: "--play-nid",
    description: "PlayServer NID to connect to",
    getDefaultValue: () => "play-1");

var playPortOption = new Option<int>(
    name: "--play-port",
    description: "PlayServer ZMQ port to connect to",
    getDefaultValue: () => 16100);

var logDirOption = new Option<string>(
    name: "--log-dir",
    description: "Directory for log files",
    getDefaultValue: () => "logs");

var rootCommand = new RootCommand("PlayHouse Server-to-Server Benchmark API Server")
{
    zmqPortOption,
    playNidOption,
    playPortOption,
    logDirOption
};

var zmqPort = 0;
var playNid = "play-1";
var playPort = 16100;
var logDir = "logs";

rootCommand.SetHandler((zmq, pnid, pport, logDirectory) =>
{
    zmqPort = zmq;
    playNid = pnid;
    playPort = pport;
    logDir = logDirectory;
}, zmqPortOption, playNidOption, playPortOption, logDirOption);

await rootCommand.InvokeAsync(args);

if (zmqPort == 0)
{
    zmqPort = 16201;
    playPort = 16100;
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
        Path.Combine(logDir, "benchmark-api-server-.log"),
        rollingInterval: RollingInterval.Day,
        outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {Message:lj}{NewLine}{Exception}",
        retainedFileCountLimit: 30)
    .CreateLogger();

try
{
    Log.Information("================================================================================");
    Log.Information("PlayHouse Server-to-Server Benchmark API Server");
    Log.Information("================================================================================");
    Log.Information("ZMQ Port (server-to-server): {ZmqPort}", zmqPort);
    Log.Information("PlayServer NID: {PlayNid}", playNid);
    Log.Information("PlayServer ZMQ Port: {PlayPort}", playPort);
    Log.Information("Log Directory: {LogDir}", Path.GetFullPath(logDir));
    Log.Information("================================================================================");

    // ApiServer 구성
    var apiServer = new ApiServerBootstrap()
        .Configure(options =>
        {
            options.ServiceType = ServiceType.Api;
            options.ServerId = "api-1";
            options.BindEndpoint = $"tcp://127.0.0.1:{zmqPort}";
        })
        .UseController<BenchmarkApiController>()
        .Build();

    // MessagePool Prewarm (런타임 할당 방지)
    Net.Zmq.MessagePool.Shared.Prewarm(Net.Zmq.MessageSize.K2, 500);
    Log.Information("MessagePool prewarmed: K2 x 500");

    // ApiServer 시작
    await apiServer.StartAsync();
    Log.Information("API Server started (ZMQ: {ZmqPort})", zmqPort);

    // PlayServer에 수동 연결 (양방향 통신을 위해 필요)
    var playEndpoint = $"tcp://127.0.0.1:{playPort}";
    apiServer.Connect(playNid, playEndpoint);
    Log.Information("Connected to PlayServer (NID: {PlayNid}, Endpoint: {PlayEndpoint})", playNid, playEndpoint);

    Log.Information("Press Ctrl+C to stop...");

    // 종료 대기
    var cts = new CancellationTokenSource();
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
    await apiServer.StopAsync();

    Log.Information("API Server stopped.");
}
catch (Exception ex)
{
    Log.Fatal(ex, "API Server terminated unexpectedly");
}
finally
{
    await Log.CloseAndFlushAsync();
}
