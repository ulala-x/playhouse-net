using System.CommandLine;
using System.Text;
using System.Text.Json;
using PlayHouse.Benchmark.Client;
using Serilog;
using Serilog.Events;

// CLI 옵션 정의
var serverOption = new Option<string>(
    name: "--server",
    description: "Server address (host:port)",
    getDefaultValue: () => "127.0.0.1:16110");

var connectionsOption = new Option<int>(
    name: "--connections",
    description: "Number of concurrent connections",
    getDefaultValue: () => 1);

var messagesOption = new Option<int?>(
    name: "--messages",
    description: "Number of messages per connection (cannot be used with --duration)");

var messageSizeOption = new Option<int>(
    name: "--message-size",
    description: "Message payload size in bytes (request=response for Echo)",
    getDefaultValue: () => 64);

var responseSizeOption = new Option<string>(
    name: "--response-size",
    description: "Response size(s) in bytes. Comma-separated for multiple tests (e.g., 256,1500,65536)",
    getDefaultValue: () => "256");

var modeOption = new Option<string>(
    name: "--mode",
    description: "Benchmark mode: request-async, request-callback, send, or all (default: all)",
    getDefaultValue: () => "all");

var durationOption = new Option<int?>(
    name: "--duration",
    description: "Duration in seconds for time-based test (cannot be used with --messages)");

var httpPortOption = new Option<int>(
    name: "--http-port",
    description: "Server HTTP API port for metrics",
    getDefaultValue: () => 5080);

var outputDirOption = new Option<string>(
    name: "--output-dir",
    description: "Directory for benchmark result logs",
    getDefaultValue: () => "benchmark-results");

var labelOption = new Option<string>(
    name: "--label",
    description: "Label for this benchmark run (for comparison)",
    getDefaultValue: () => "");

var connectOnlyOption = new Option<bool>(
    name: "--connect-only",
    description: "Test connection establishment only (no message sending)",
    getDefaultValue: () => false);

var maxInFlightOption = new Option<int>(
    name: "--max-inflight",
    description: "Maximum in-flight requests (default: 200)",
    getDefaultValue: () => 200);

var rootCommand = new RootCommand("PlayHouse Benchmark Client")
{
    serverOption,
    connectionsOption,
    messagesOption,
    messageSizeOption,
    responseSizeOption,
    modeOption,
    durationOption,
    httpPortOption,
    outputDirOption,
    labelOption,
    connectOnlyOption,
    maxInFlightOption
};

rootCommand.SetHandler(async (context) =>
{
    var server = context.ParseResult.GetValueForOption(serverOption)!;
    var connections = context.ParseResult.GetValueForOption(connectionsOption);
    var messages = context.ParseResult.GetValueForOption(messagesOption);
    var messageSize = context.ParseResult.GetValueForOption(messageSizeOption);
    var responseSizes = context.ParseResult.GetValueForOption(responseSizeOption)!;
    var mode = context.ParseResult.GetValueForOption(modeOption)!;
    var duration = context.ParseResult.GetValueForOption(durationOption);
    var httpPort = context.ParseResult.GetValueForOption(httpPortOption);
    var outputDir = context.ParseResult.GetValueForOption(outputDirOption)!;
    var label = context.ParseResult.GetValueForOption(labelOption)!;
    var connectOnly = context.ParseResult.GetValueForOption(connectOnlyOption);
    var maxInFlight = context.ParseResult.GetValueForOption(maxInFlightOption);

    if (connectOnly)
    {
        await RunConnectOnlyTestAsync(server, connections);
    }
    else
    {
        // Validate mutual exclusivity of --messages and --duration
        if (messages.HasValue && duration.HasValue)
        {
            Console.WriteLine("Error: --messages and --duration cannot be used together.");
            Console.WriteLine("  Use --messages for message-count based test");
            Console.WriteLine("  Use --duration for time-based test");
            return;
        }

        if (!messages.HasValue && !duration.HasValue)
        {
            Console.WriteLine("Error: Either --messages or --duration must be specified.");
            Console.WriteLine("  Use --messages <count> for message-count based test");
            Console.WriteLine("  Use --duration <seconds> for time-based test (default: 10s)");
            return;
        }

        // Determine test mode
        int? messagesValue = messages;
        int? durationValue = duration;

        await RunBenchmarkAsync(server, connections, messagesValue, durationValue, messageSize, responseSizes, mode, httpPort, outputDir, label, maxInFlight);
    }
});

return await rootCommand.InvokeAsync(args);

static async Task RunBenchmarkAsync(
    string server,
    int connections,
    int? messages,
    int? duration,
    int messageSize,
    string responseSizesStr,
    string mode,
    int httpPort,
    string outputDir,
    string label,
    int maxInFlight)
{
    // 서버 주소 파싱
    var parts = server.Split(':');
    if (parts.Length != 2 || !int.TryParse(parts[1], out var port))
    {
        Console.WriteLine("Invalid server address format. Use: host:port");
        return;
    }

    var host = parts[0];
    var runTimestamp = DateTime.Now;

    // 응답 크기 파싱 (콤마 구분)
    var responseSizes = responseSizesStr
        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Select(s => int.TryParse(s, out var v) ? v : 256)
        .ToArray();

    if (responseSizes.Length == 0)
    {
        responseSizes = new[] { 256 };
    }

    // Determine test mode: message-count based or duration based
    var isTimeBased = duration.HasValue;
    var effectiveMessages = messages ?? 0;  // 0 means unlimited (time-based)
    var effectiveDuration = duration ?? 0;  // 0 means unlimited (message-based)

    // 출력 디렉토리 생성
    Directory.CreateDirectory(outputDir);

    // Serilog 설정
    var timestamp = runTimestamp.ToString("yyyyMMdd_HHmmss");
    var labelSuffix = string.IsNullOrEmpty(label) ? "" : $"_{label}";

    Log.Logger = new LoggerConfiguration()
        .MinimumLevel.Information()
        .WriteTo.Console(
            outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
        .WriteTo.File(
            Path.Combine(outputDir, $"benchmark_{timestamp}{labelSuffix}.log"),
            outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
        .CreateLogger();

    try
    {
        // "all" 모드 처리
        if (mode.ToLowerInvariant() == "all")
        {
            await RunAllModesAsync(server, connections, effectiveMessages, effectiveDuration, isTimeBased, messageSize, responseSizesStr, httpPort, outputDir, label, runTimestamp, maxInFlight);
            return;
        }

        // 단일 모드 파싱
        var benchmarkMode = mode.ToLowerInvariant() switch
        {
            "request-async" => BenchmarkMode.RequestAsync,
            "request-callback" => BenchmarkMode.RequestCallback,
            "send" => BenchmarkMode.Send,
            _ => BenchmarkMode.RequestAsync
        };

        // 배너 출력
        Log.Information("================================================================================");
        Log.Information("PlayHouse Benchmark Client");
        Log.Information("================================================================================");
        Log.Information("Server: {Host}:{Port}", host, port);
        Log.Information("HTTP API: {Host}:{HttpPort}", host, httpPort);
        Log.Information("Mode: {Mode}", benchmarkMode);
        Log.Information("Connections: {Connections:N0}", connections);
        if (isTimeBased)
        {
            Log.Information("Duration: {Duration:N0} seconds", effectiveDuration);
        }
        else
        {
            Log.Information("Messages per connection: {Messages:N0}", effectiveMessages);
            Log.Information("Total messages: {TotalMessages:N0}", connections * effectiveMessages);
        }
        Log.Information("Message size: {MessageSize:N0} bytes", messageSize);
        Log.Information("Response sizes: {ResponseSizes}", string.Join(", ", responseSizes.Select(s => $"{s:N0}B")));
        if (!string.IsNullOrEmpty(label))
            Log.Information("Label: {Label}", label);
        Log.Information("Output: {OutputDir}", Path.GetFullPath(outputDir));
        Log.Information("================================================================================");

        var serverMetricsClient = new ServerMetricsClient(host, httpPort);

        // 각 응답 크기별 결과 저장
        var allResults = new List<BenchmarkResult>();

        for (int i = 0; i < responseSizes.Length; i++)
        {
            var responseSize = responseSizes[i];

            if (responseSizes.Length > 1)
            {
                Log.Information("");
                Log.Information(">>> Test {Current}/{Total}: Response Size = {ResponseSize:N0} bytes <<<",
                    i + 1, responseSizes.Length, responseSize);
            }

            // 메트릭 수집기
            var clientMetricsCollector = new ClientMetricsCollector();

            // 이전 테스트의 Stage 정리 대기
            if (i > 0)
            {
                Log.Information("Waiting for previous test cleanup...");
                await Task.Delay(5000);
            }

            // 서버 메트릭 리셋
            Log.Information("Resetting server metrics...");
            await serverMetricsClient.ResetMetricsAsync();
            await Task.Delay(1000);

            // 벤치마크 실행 (테스트마다 다른 stageId 범위 사용)
            var stageIdOffset = i * connections;
            var runner = new BenchmarkRunner(
                host,
                port,
                connections,
                messageSize,
                benchmarkMode,
                clientMetricsCollector,
                stageIdOffset: stageIdOffset,
                stageName: "BenchStage",
                durationSeconds: effectiveDuration,
                maxInFlight: maxInFlight);

            var startTime = DateTime.Now;
            await runner.RunAsync();
            var endTime = DateTime.Now;
            var totalElapsed = (endTime - startTime).TotalSeconds;

            Log.Information("Waiting for server to cleanup stages and stabilize metrics...");
            await Task.Delay(3000);

            // 결과 조회
            var serverMetrics = await serverMetricsClient.GetMetricsAsync();
            var clientMetrics = clientMetricsCollector.GetMetrics();

            // 결과 저장
            allResults.Add(new BenchmarkResult
            {
                ResponseSize = responseSize,
                TotalElapsedSeconds = totalElapsed,
                ServerMetrics = serverMetrics,
                ClientMetrics = clientMetrics
            });
        }

        // 통합 결과 출력
        Log.Information("");
        LogResults(connections, effectiveMessages, effectiveDuration, isTimeBased, messageSize, responseSizes, benchmarkMode, allResults);

        // 결과 파일 저장
        await SaveResultsToFile(outputDir, runTimestamp, label, connections, effectiveMessages, effectiveDuration, isTimeBased, messageSize, benchmarkMode, allResults);

        // 서버 종료 요청은 스크립트가 담당 (여러 모드를 순차 실행하는 경우)
        // Log.Information("");
        // Log.Information("Sending shutdown request to server...");
        // using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        // try
        // {
        //     await httpClient.PostAsync($"http://{host}:{httpPort}/benchmark/shutdown", null);
        //     Log.Information("Server shutdown initiated");
        // }
        // catch (Exception)
        // {
        //     // 서버가 이미 종료되었거나 연결 실패 - 무시
        // }
    }
    catch (Exception ex)
    {
        Log.Error(ex, "Benchmark failed");
    }
    finally
    {
        await Log.CloseAndFlushAsync();
    }
}

static void LogResults(
    int connections,
    int messages,
    int duration,
    bool isTimeBased,
    int messageSize,
    int[] responseSizes,
    BenchmarkMode mode,
    List<BenchmarkResult> results)
{
    Log.Information("================================================================================");
    Log.Information("Benchmark Results Summary");
    Log.Information("================================================================================");
    if (isTimeBased)
    {
        Log.Information("Config: {Connections:N0} CCU x {Duration:N0}s, Request: {RequestSize:N0}B, Mode: {Mode}",
            connections, duration, messageSize, mode);
    }
    else
    {
        Log.Information("Config: {Connections:N0} CCU x {Messages:N0} msg, Request: {RequestSize:N0}B, Mode: {Mode}",
            connections, messages, messageSize, mode);
    }
    Log.Information("");

    // 테이블 헤더
    Log.Information("{RespSize,8} | {Elapsed,6} | {SrvTPS,9} | {SrvP99,8} | {SrvMem,8} | {SrvGC,10} | {CliRTT,8} | {CliTPS,9} | {CliMem,8} | {CliGC,10}",
        "RespSize", "Time", "Srv TPS", "Srv P99", "Srv Mem", "Srv GC", "Cli RTT", "Cli TPS", "Cli Mem", "Cli GC");
    Log.Information("{D1} | {D2} | {D3} | {D4} | {D5} | {D6} | {D7} | {D8} | {D9} | {D10}",
        "--------", "------", "---------", "--------", "--------", "----------", "--------", "---------", "--------", "----------");

    foreach (var result in results)
    {
        var srvTps = result.ServerMetrics?.ThroughputMessagesPerSec ?? 0;
        var srvP99 = result.ServerMetrics?.LatencyP99Ms ?? 0;
        var srvMem = result.ServerMetrics?.MemoryAllocatedMb ?? 0;
        var srvGc = result.ServerMetrics != null
            ? $"{result.ServerMetrics.GcGen0Count}/{result.ServerMetrics.GcGen1Count}/{result.ServerMetrics.GcGen2Count}"
            : "-";
        var cliRtt = result.ClientMetrics.RttLatencyP99Ms;
        var cliTps = result.ClientMetrics.ThroughputMessagesPerSec;
        var cliMem = result.ClientMetrics.MemoryAllocatedMB;
        var cliGc = $"{result.ClientMetrics.GcGen0Count}/{result.ClientMetrics.GcGen1Count}/{result.ClientMetrics.GcGen2Count}";

        Log.Information("{RespSize,7:N0}B | {Elapsed,5:F2}s | {SrvTPS,8:N0}/s | {SrvP99,6:F2}ms | {SrvMem,6:F1}MB | {SrvGC,10} | {CliRTT,6:F2}ms | {CliTPS,8:N0}/s | {CliMem,6:F1}MB | {CliGC,10}",
            result.ResponseSize, result.TotalElapsedSeconds, srvTps, srvP99, srvMem, srvGc, cliRtt, cliTps, cliMem, cliGc);
    }

    Log.Information("");

    // 각 테스트별 상세 결과
    foreach (var result in results)
    {
        Log.Information("--------------------------------------------------------------------------------");
        Log.Information("[Response Size: {ResponseSize:N0} bytes]", result.ResponseSize);
        Log.Information("  Elapsed: {Elapsed:F2}s", result.TotalElapsedSeconds);

        if (result.ServerMetrics != null)
        {
            Log.Information("  Server:");
            Log.Information("    Processed   : {Processed:N0} messages", result.ServerMetrics.ProcessedMessages);
            Log.Information("    Throughput  : {TPS:N0} msg/s ({MBps:F2} MB/s)",
                result.ServerMetrics.ThroughputMessagesPerSec, result.ServerMetrics.ThroughputMbPerSec);
            Log.Information("    Latency     : Mean={Mean:F2}ms, P50={P50:F2}ms, P95={P95:F2}ms, P99={P99:F2}ms",
                result.ServerMetrics.LatencyMeanMs, result.ServerMetrics.LatencyP50Ms,
                result.ServerMetrics.LatencyP95Ms, result.ServerMetrics.LatencyP99Ms);
            Log.Information("    Memory      : {Memory:F2} MB, GC: Gen0={Gen0}, Gen1={Gen1}, Gen2={Gen2}",
                result.ServerMetrics.MemoryAllocatedMb,
                result.ServerMetrics.GcGen0Count, result.ServerMetrics.GcGen1Count, result.ServerMetrics.GcGen2Count);
        }
        else
        {
            Log.Warning("  Server: Not available");
        }

        Log.Information("  Client:");
        Log.Information("    Sent/Recv   : {Sent:N0} / {Recv:N0} messages",
            result.ClientMetrics.SentMessages, result.ClientMetrics.ReceivedMessages);
        Log.Information("    RTT Latency : Mean={Mean:F2}ms, P50={P50:F2}ms, P95={P95:F2}ms, P99={P99:F2}ms",
            result.ClientMetrics.RttLatencyMeanMs, result.ClientMetrics.RttLatencyP50Ms,
            result.ClientMetrics.RttLatencyP95Ms, result.ClientMetrics.RttLatencyP99Ms);
        Log.Information("    Throughput  : {TPS:N0} msg/s",
            result.ClientMetrics.ThroughputMessagesPerSec);
        Log.Information("    Memory      : {Memory:F2} MB, GC: Gen0={Gen0}, Gen1={Gen1}, Gen2={Gen2}",
            result.ClientMetrics.MemoryAllocatedMB,
            result.ClientMetrics.GcGen0Count, result.ClientMetrics.GcGen1Count, result.ClientMetrics.GcGen2Count);
    }

    Log.Information("================================================================================");
}

/// <summary>
/// 세 모드(RequestAsync, RequestCallback, Send)를 모두 테스트하고 비교 결과 출력
/// </summary>
static async Task RunAllModesAsync(
    string server,
    int connections,
    int messages,
    int duration,
    bool isTimeBased,
    int messageSize,
    string responseSizesStr,
    int httpPort,
    string outputDir,
    string label,
    DateTime runTimestamp,
    int maxInFlight)
{
    // 서버 주소 파싱
    var parts = server.Split(':');
    if (parts.Length != 2 || !int.TryParse(parts[1], out var port))
    {
        Console.WriteLine("Invalid server address format. Use: host:port");
        return;
    }

    var host = parts[0];

    // 응답 크기 파싱
    var responseSizes = responseSizesStr
        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Select(s => int.TryParse(s, out var v) ? v : 256)
        .ToArray();

    if (responseSizes.Length == 0)
    {
        responseSizes = new[] { 256 };
    }

    // 배너 출력
    Log.Information("================================================================================");
    Log.Information("PlayHouse Benchmark Client - All Modes Comparison");
    Log.Information("================================================================================");
    Log.Information("Server: {Host}:{Port}", host, port);
    Log.Information("Connections: {Connections:N0}", connections);
    if (isTimeBased)
    {
        Log.Information("Duration: {Duration:N0} seconds", duration);
    }
    else
    {
        Log.Information("Messages per connection: {Messages:N0}", messages);
        Log.Information("Total messages: {TotalMessages:N0}", connections * messages);
    }
    Log.Information("Message size: {MessageSize:N0} bytes", messageSize);
    Log.Information("Response sizes: {ResponseSizes}", string.Join(", ", responseSizes.Select(s => $"{s:N0}B")));
    if (!string.IsNullOrEmpty(label))
        Log.Information("Label: {Label}", label);
    Log.Information("Output: {OutputDir}", Path.GetFullPath(outputDir));
    Log.Information("================================================================================");

    var serverMetricsClient = new ServerMetricsClient(host, httpPort);

    var resultsRequestAsync = new List<BenchmarkResult>();
    var resultsRequestCallback = new List<BenchmarkResult>();
    var resultsSend = new List<BenchmarkResult>();

    // 테스트 인덱스 (stageId 충돌 방지용)
    var testIndex = 0;

    // 1. RequestAsync 모드 테스트
    Log.Information("");
    Log.Information(">>> Testing RequestAsync Mode <<<");
    await Task.Delay(1000);

    foreach (var responseSize in responseSizes)
    {
        if (responseSizes.Length > 1)
        {
            Log.Information("  Response Size: {ResponseSize:N0} bytes", responseSize);
        }

        var clientMetricsCollector = new ClientMetricsCollector();

        Log.Information("Resetting server metrics...");
        await serverMetricsClient.ResetMetricsAsync();
        await Task.Delay(500);

        var stageIdOffset = testIndex * connections;
        var runner = new BenchmarkRunner(
            host, port, connections, messageSize,
            BenchmarkMode.RequestAsync, clientMetricsCollector,
            stageIdOffset: stageIdOffset,
            stageName: "BenchStage", durationSeconds: duration, maxInFlight: maxInFlight);
        testIndex++;

        var startTime = DateTime.Now;
        await runner.RunAsync();
        var endTime = DateTime.Now;
        var totalElapsed = (endTime - startTime).TotalSeconds;

        Log.Information("Waiting for server metrics to stabilize...");
        await Task.Delay(1000);

        var serverMetrics = await serverMetricsClient.GetMetricsAsync();
        var clientMetrics = clientMetricsCollector.GetMetrics();

        resultsRequestAsync.Add(new BenchmarkResult
        {
            ResponseSize = responseSize,
            TotalElapsedSeconds = totalElapsed,
            ServerMetrics = serverMetrics,
            ClientMetrics = clientMetrics
        });
    }

    // 2. RequestCallback 모드 테스트
    Log.Information("");
    Log.Information(">>> Testing RequestCallback Mode <<<");
    await Task.Delay(1000);

    foreach (var responseSize in responseSizes)
    {
        if (responseSizes.Length > 1)
        {
            Log.Information("  Response Size: {ResponseSize:N0} bytes", responseSize);
        }

        var clientMetricsCollector = new ClientMetricsCollector();

        Log.Information("Resetting server metrics...");
        await serverMetricsClient.ResetMetricsAsync();
        await Task.Delay(500);

        var stageIdOffset = testIndex * connections;
        var runner = new BenchmarkRunner(
            host, port, connections, messageSize,
            BenchmarkMode.RequestCallback, clientMetricsCollector,
            stageIdOffset: stageIdOffset,
            stageName: "BenchStage", durationSeconds: duration, maxInFlight: maxInFlight);
        testIndex++;

        var startTime = DateTime.Now;
        await runner.RunAsync();
        var endTime = DateTime.Now;
        var totalElapsed = (endTime - startTime).TotalSeconds;

        Log.Information("Waiting for server metrics to stabilize...");
        await Task.Delay(1000);

        var serverMetrics = await serverMetricsClient.GetMetricsAsync();
        var clientMetrics = clientMetricsCollector.GetMetrics();

        resultsRequestCallback.Add(new BenchmarkResult
        {
            ResponseSize = responseSize,
            TotalElapsedSeconds = totalElapsed,
            ServerMetrics = serverMetrics,
            ClientMetrics = clientMetrics
        });
    }

    // 3. Send 모드 테스트
    Log.Information("");
    Log.Information(">>> Testing Send Mode <<<");
    await Task.Delay(1000);

    foreach (var responseSize in responseSizes)
    {
        if (responseSizes.Length > 1)
        {
            Log.Information("  Response Size: {ResponseSize:N0} bytes", responseSize);
        }

        var clientMetricsCollector = new ClientMetricsCollector();

        Log.Information("Resetting server metrics...");
        await serverMetricsClient.ResetMetricsAsync();
        await Task.Delay(500);

        var stageIdOffset = testIndex * connections;
        var runner = new BenchmarkRunner(
            host, port, connections, messageSize,
            BenchmarkMode.Send, clientMetricsCollector,
            stageIdOffset: stageIdOffset,
            stageName: "BenchStage", durationSeconds: duration, maxInFlight: maxInFlight);
        testIndex++;

        var startTime = DateTime.Now;
        await runner.RunAsync();
        var endTime = DateTime.Now;
        var totalElapsed = (endTime - startTime).TotalSeconds;

        Log.Information("Waiting for server metrics to stabilize...");
        await Task.Delay(1000);

        var serverMetrics = await serverMetricsClient.GetMetricsAsync();
        var clientMetrics = clientMetricsCollector.GetMetrics();

        resultsSend.Add(new BenchmarkResult
        {
            ResponseSize = responseSize,
            TotalElapsedSeconds = totalElapsed,
            ServerMetrics = serverMetrics,
            ClientMetrics = clientMetrics
        });
    }

    // 비교 결과 출력
    Log.Information("");
    LogModeComparison(connections, messages, messageSize, responseSizes, resultsRequestAsync, resultsRequestCallback, resultsSend);

    // 결과 파일 저장
    await SaveComparisonResults(outputDir, runTimestamp, label, connections, messages, messageSize,
        responseSizes, resultsRequestAsync, resultsRequestCallback, resultsSend);

    // 서버 종료 요청
    Log.Information("");
    Log.Information("Sending shutdown request to server...");
    using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
    try
    {
        await httpClient.PostAsync($"http://{host}:{httpPort}/benchmark/shutdown", null);
        Log.Information("Server shutdown initiated");
    }
    catch (Exception)
    {
        // 서버가 이미 종료되었거나 연결 실패 - 무시
    }
}

/// <summary>
/// 세 모드 비교 결과 출력
/// </summary>
static void LogModeComparison(
    int connections,
    int messages,
    int messageSize,
    int[] responseSizes,
    List<BenchmarkResult> resultsRequestAsync,
    List<BenchmarkResult> resultsRequestCallback,
    List<BenchmarkResult> resultsSend)
{
    Log.Information("================================================================================");
    Log.Information("Benchmark Results - All Modes Comparison");
    Log.Information("================================================================================");
    Log.Information("Config: {Connections:N0} CCU, {Duration:N0}s duration",
        connections, messages);
    Log.Information("");

    // 테이블 헤더 출력
    Log.Information("             |     RequestAsync     |    RequestCallback   |        Send          ");
    Log.Information("Payload Size | TPS      | P99  | Mem| TPS      | P99  | Mem| TPS      | Mem     ");
    Log.Information("-------------|----------|------|-------|----------|------|-------|----------|-------");

    // 각 응답 크기별로 비교
    for (int i = 0; i < responseSizes.Length; i++)
    {
        var ra = resultsRequestAsync[i];
        var rc = resultsRequestCallback[i];
        var rs = resultsSend[i];

        var raTps = ra.ClientMetrics.ThroughputMessagesPerSec;
        var raP99 = ra.ClientMetrics.RttLatencyP99Ms;
        var raMem = ra.ClientMetrics.MemoryAllocatedMB;

        var rcTps = rc.ClientMetrics.ThroughputMessagesPerSec;
        var rcP99 = rc.ClientMetrics.RttLatencyP99Ms;
        var rcMem = rc.ClientMetrics.MemoryAllocatedMB;

        var rsTps = rs.ClientMetrics.ThroughputMessagesPerSec;
        var rsMem = rs.ClientMetrics.MemoryAllocatedMB;

        Log.Information("{Size,11:N0} B | {RATps,7:N0}/s| {RAP99,4:F1}ms| {RAMem,4:N0}MB| {RCTps,7:N0}/s| {RCP99,4:F1}ms| {RCMem,4:N0}MB| {RSTps,7:N0}/s| {RSMem,4:N0}MB",
            ra.ResponseSize, raTps, raP99, raMem, rcTps, rcP99, rcMem, rsTps, rsMem);
    }

    Log.Information("");
    Log.Information("================================================================================");
}

/// <summary>
/// 비교 결과 파일 저장
/// </summary>
static async Task SaveComparisonResults(
    string outputDir,
    DateTime runTimestamp,
    string label,
    int connections,
    int messages,
    int messageSize,
    int[] responseSizes,
    List<BenchmarkResult> resultsRequestAsync,
    List<BenchmarkResult> resultsRequestCallback,
    List<BenchmarkResult> resultsSend)
{
    var timestamp = runTimestamp.ToString("yyyyMMdd_HHmmss");
    var labelSuffix = string.IsNullOrEmpty(label) ? "" : $"_{label}";

    // JSON 결과 파일
    var jsonFileName = $"benchmark_comparison_{timestamp}{labelSuffix}.json";
    var jsonPath = Path.Combine(outputDir, jsonFileName);

    var jsonResult = new
    {
        Timestamp = runTimestamp.ToString("yyyy-MM-dd HH:mm:ss"),
        Label = label,
        Config = new
        {
            Connections = connections,
            MessagesPerConnection = messages,
            TotalMessages = connections * messages,
            RequestSizeBytes = messageSize
        },
        ResultsRequestAsync = resultsRequestAsync.Select(r => new
        {
            ResponseSizeBytes = r.ResponseSize,
            TotalElapsedSeconds = r.TotalElapsedSeconds,
            ClientThroughputMsgPerSec = r.ClientMetrics.ThroughputMessagesPerSec,
            ClientRttLatencyP99Ms = r.ClientMetrics.RttLatencyP99Ms,
            ClientMemoryAllocatedMB = r.ClientMetrics.MemoryAllocatedMB
        }).ToArray(),
        ResultsRequestCallback = resultsRequestCallback.Select(r => new
        {
            ResponseSizeBytes = r.ResponseSize,
            TotalElapsedSeconds = r.TotalElapsedSeconds,
            ClientThroughputMsgPerSec = r.ClientMetrics.ThroughputMessagesPerSec,
            ClientRttLatencyP99Ms = r.ClientMetrics.RttLatencyP99Ms,
            ClientMemoryAllocatedMB = r.ClientMetrics.MemoryAllocatedMB
        }).ToArray(),
        ResultsSend = resultsSend.Select(r => new
        {
            ResponseSizeBytes = r.ResponseSize,
            TotalElapsedSeconds = r.TotalElapsedSeconds,
            ClientThroughputMsgPerSec = r.ClientMetrics.ThroughputMessagesPerSec,
            ClientMemoryAllocatedMB = r.ClientMetrics.MemoryAllocatedMB
        }).ToArray()
    };

    var jsonOptions = new System.Text.Json.JsonSerializerOptions { WriteIndented = true };
    await File.WriteAllTextAsync(jsonPath, System.Text.Json.JsonSerializer.Serialize(jsonResult, jsonOptions));

    Log.Information("Results saved to:");
    Log.Information("  JSON: {JsonPath}", jsonPath);
}

static async Task SaveResultsToFile(
    string outputDir,
    DateTime runTimestamp,
    string label,
    int connections,
    int messages,
    int duration,
    bool isTimeBased,
    int messageSize,
    BenchmarkMode mode,
    List<BenchmarkResult> results)
{
    var timestamp = runTimestamp.ToString("yyyyMMdd_HHmmss");
    var labelSuffix = string.IsNullOrEmpty(label) ? "" : $"_{label}";

    // 1. JSON 결과 파일 (모든 테스트)
    var jsonFileName = $"benchmark_{timestamp}{labelSuffix}.json";
    var jsonPath = Path.Combine(outputDir, jsonFileName);

    var jsonResult = new
    {
        Timestamp = runTimestamp.ToString("yyyy-MM-dd HH:mm:ss"),
        Label = label,
        Config = new
        {
            Connections = connections,
            MessagesPerConnection = isTimeBased ? (int?)null : messages,
            DurationSeconds = isTimeBased ? duration : (int?)null,
            IsTimeBased = isTimeBased,
            TotalMessages = isTimeBased ? (int?)null : connections * messages,
            RequestSizeBytes = messageSize,
            Mode = mode.ToString()
        },
        Results = results.Select(r => new
        {
            ResponseSizeBytes = r.ResponseSize,
            TotalElapsedSeconds = r.TotalElapsedSeconds,
            Server = r.ServerMetrics != null ? new
            {
                ProcessedMessages = r.ServerMetrics.ProcessedMessages,
                ThroughputMsgPerSec = r.ServerMetrics.ThroughputMessagesPerSec,
                ThroughputMBPerSec = r.ServerMetrics.ThroughputMbPerSec,
                LatencyMeanMs = r.ServerMetrics.LatencyMeanMs,
                LatencyP50Ms = r.ServerMetrics.LatencyP50Ms,
                LatencyP95Ms = r.ServerMetrics.LatencyP95Ms,
                LatencyP99Ms = r.ServerMetrics.LatencyP99Ms,
                MemoryAllocatedMB = r.ServerMetrics.MemoryAllocatedMb,
                GcGen0 = r.ServerMetrics.GcGen0Count,
                GcGen1 = r.ServerMetrics.GcGen1Count,
                GcGen2 = r.ServerMetrics.GcGen2Count
            } : null,
            Client = new
            {
                SentMessages = r.ClientMetrics.SentMessages,
                ReceivedMessages = r.ClientMetrics.ReceivedMessages,
                RttLatencyMeanMs = r.ClientMetrics.RttLatencyMeanMs,
                RttLatencyP50Ms = r.ClientMetrics.RttLatencyP50Ms,
                RttLatencyP95Ms = r.ClientMetrics.RttLatencyP95Ms,
                RttLatencyP99Ms = r.ClientMetrics.RttLatencyP99Ms,
                ThroughputMsgPerSec = r.ClientMetrics.ThroughputMessagesPerSec,
                MemoryAllocatedMB = r.ClientMetrics.MemoryAllocatedMB,
                GcGen0 = r.ClientMetrics.GcGen0Count,
                GcGen1 = r.ClientMetrics.GcGen1Count,
                GcGen2 = r.ClientMetrics.GcGen2Count
            }
        }).ToArray()
    };

    var jsonOptions = new JsonSerializerOptions { WriteIndented = true };
    await File.WriteAllTextAsync(jsonPath, JsonSerializer.Serialize(jsonResult, jsonOptions));

    // 2. CSV 요약 파일 (누적)
    var csvPath = Path.Combine(outputDir, "benchmark_summary.csv");
    var csvExists = File.Exists(csvPath);

    var csvBuilder = new StringBuilder();
    if (!csvExists)
    {
        // 헤더 추가
        csvBuilder.AppendLine("Timestamp,Label,CCU,MsgPerConn,TotalMsg,ReqSize,RespSize,Mode,ElapsedSec," +
            "Srv_Processed,Srv_TPS,Srv_MBps,Srv_LatMean,Srv_LatP50,Srv_LatP95,Srv_LatP99,Srv_MemMB,Srv_GC0,Srv_GC1,Srv_GC2," +
            "Cli_Sent,Cli_Recv,Cli_RttMean,Cli_RttP50,Cli_RttP95,Cli_RttP99,Cli_TPS,Cli_MemMB,Cli_GC0,Cli_GC1,Cli_GC2");
    }

    foreach (var r in results)
    {
        csvBuilder.Append($"{runTimestamp:yyyy-MM-dd HH:mm:ss},{label},{connections},{messages},{connections * messages},{messageSize},{r.ResponseSize},{mode},{r.TotalElapsedSeconds:F2},");

        if (r.ServerMetrics != null)
        {
            csvBuilder.Append($"{r.ServerMetrics.ProcessedMessages},{r.ServerMetrics.ThroughputMessagesPerSec:F0},{r.ServerMetrics.ThroughputMbPerSec:F2}," +
                $"{r.ServerMetrics.LatencyMeanMs:F2},{r.ServerMetrics.LatencyP50Ms:F2},{r.ServerMetrics.LatencyP95Ms:F2},{r.ServerMetrics.LatencyP99Ms:F2}," +
                $"{r.ServerMetrics.MemoryAllocatedMb:F2},{r.ServerMetrics.GcGen0Count},{r.ServerMetrics.GcGen1Count},{r.ServerMetrics.GcGen2Count},");
        }
        else
        {
            csvBuilder.Append(",,,,,,,,,,");
        }

        csvBuilder.AppendLine($"{r.ClientMetrics.SentMessages},{r.ClientMetrics.ReceivedMessages}," +
            $"{r.ClientMetrics.RttLatencyMeanMs:F2},{r.ClientMetrics.RttLatencyP50Ms:F2},{r.ClientMetrics.RttLatencyP95Ms:F2},{r.ClientMetrics.RttLatencyP99Ms:F2}," +
            $"{r.ClientMetrics.ThroughputMessagesPerSec:F0},{r.ClientMetrics.MemoryAllocatedMB:F2}," +
            $"{r.ClientMetrics.GcGen0Count},{r.ClientMetrics.GcGen1Count},{r.ClientMetrics.GcGen2Count}");
    }

    await File.AppendAllTextAsync(csvPath, csvBuilder.ToString());

    Log.Information("");
    Log.Information("Results saved to:");
    Log.Information("  Log:  {LogPath}", Path.Combine(outputDir, $"benchmark_{timestamp}{labelSuffix}.log"));
    Log.Information("  JSON: {JsonPath}", jsonPath);
    Log.Information("  CSV:  {CsvPath}", csvPath);
}

/// <summary>
/// 연결 + 인증만 테스트 (패킷 전송 없음)
/// </summary>
static async Task RunConnectOnlyTestAsync(string server, int connections)
{
    // 서버 주소 파싱
    var parts = server.Split(':');
    if (parts.Length != 2 || !int.TryParse(parts[1], out var port))
    {
        Console.WriteLine("Invalid server address format. Use: host:port");
        return;
    }

    var host = parts[0];

    // Serilog 설정
    Log.Logger = new LoggerConfiguration()
        .MinimumLevel.Information()
        .WriteTo.Console(
            outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
        .CreateLogger();

    try
    {
        var test = new ConnectionOnlyTest(host, port, connections);
        await test.RunAsync();
    }
    catch (Exception ex)
    {
        Log.Error(ex, "Connection test failed");
    }
    finally
    {
        await Log.CloseAndFlushAsync();
    }
}

/// <summary>
/// 단일 벤치마크 테스트 결과
/// </summary>
internal class BenchmarkResult
{
    public int ResponseSize { get; set; }
    public double TotalElapsedSeconds { get; set; }
    public ServerMetricsResponse? ServerMetrics { get; set; }
    public ClientMetrics ClientMetrics { get; set; } = new();
}
