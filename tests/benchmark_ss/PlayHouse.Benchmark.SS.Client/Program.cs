using System.CommandLine;
using System.Text;
using System.Text.Json;
using PlayHouse.Benchmark.SS.Client;
using PlayHouse.Benchmark.SS.Shared.Proto;
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

var messagesOption = new Option<int>(
    name: "--messages",
    description: "Number of messages per connection (ignored if --duration is set)",
    getDefaultValue: () => 10000);

var durationOption = new Option<int>(
    name: "--duration",
    description: "Test duration in seconds (overrides --messages if set)",
    getDefaultValue: () => 0);

var requestSizeOption = new Option<int>(
    name: "--request-size",
    description: "Request payload size in bytes",
    getDefaultValue: () => 64);

var responseSizeOption = new Option<string>(
    name: "--response-size",
    description: "Response size(s) in bytes. Comma-separated for multiple tests (e.g., 256,1500,65536)",
    getDefaultValue: () => "256");

var modeOption = new Option<string>(
    name: "--mode",
    description: "Benchmark mode: stage-to-api, ss-echo, or all",
    getDefaultValue: () => "all");

var commModeOption = new Option<string>(
    name: "--comm-mode",
    description: "Communication mode for ss-echo: send, request-async, request-callback, all (default: request-async)",
    getDefaultValue: () => "request-async");

var callTypeOption = new Option<string>(
    name: "--call-type",
    description: "S2S call type for ss-echo: stage-to-api, stage-to-stage (default: stage-to-api)",
    getDefaultValue: () => "stage-to-api");

var httpPortOption = new Option<int>(
    name: "--http-port",
    description: "PlayServer HTTP API port for metrics",
    getDefaultValue: () => 5080);

var apiHttpPortOption = new Option<int>(
    name: "--api-http-port",
    description: "ApiServer HTTP API port for shutdown",
    getDefaultValue: () => 5081);

var stageIdOption = new Option<long>(
    name: "--stage-id",
    description: "Initial Stage ID (default: 1000)",
    getDefaultValue: () => 1000);

var targetStageIdOption = new Option<long>(
    name: "--target-stage-id",
    description: "Target Stage ID for play-to-stage mode",
    getDefaultValue: () => 2000);

var targetNidOption = new Option<string>(
    name: "--target-nid",
    description: "Target PlayServer NID for play-to-stage mode",
    getDefaultValue: () => "play-2");

var outputDirOption = new Option<string>(
    name: "--output-dir",
    description: "Directory for benchmark result logs",
    getDefaultValue: () => "benchmark-results");

var labelOption = new Option<string>(
    name: "--label",
    description: "Label for this benchmark run (for comparison)",
    getDefaultValue: () => "");

var createStagesOnlyOption = new Option<bool>(
    name: "--create-stages-only",
    description: "Only create stages without running benchmark (for setup)",
    getDefaultValue: () => false);

var rootCommand = new RootCommand("PlayHouse Server-to-Server Benchmark Client")
{
    serverOption,
    connectionsOption,
    messagesOption,
    durationOption,
    requestSizeOption,
    responseSizeOption,
    modeOption,
    commModeOption,
    callTypeOption,
    httpPortOption,
    apiHttpPortOption,
    stageIdOption,
    targetStageIdOption,
    targetNidOption,
    outputDirOption,
    labelOption,
    createStagesOnlyOption
};

rootCommand.SetHandler(async (context) =>
{
    var server = context.ParseResult.GetValueForOption(serverOption)!;
    var connections = context.ParseResult.GetValueForOption(connectionsOption);
    var messages = context.ParseResult.GetValueForOption(messagesOption);
    var duration = context.ParseResult.GetValueForOption(durationOption);
    var requestSize = context.ParseResult.GetValueForOption(requestSizeOption);
    var responseSizes = context.ParseResult.GetValueForOption(responseSizeOption)!;
    var mode = context.ParseResult.GetValueForOption(modeOption)!;
    var commMode = context.ParseResult.GetValueForOption(commModeOption)!;
    var callType = context.ParseResult.GetValueForOption(callTypeOption)!;
    var httpPort = context.ParseResult.GetValueForOption(httpPortOption);
    var apiHttpPort = context.ParseResult.GetValueForOption(apiHttpPortOption);
    var stageId = context.ParseResult.GetValueForOption(stageIdOption);
    var targetStageId = context.ParseResult.GetValueForOption(targetStageIdOption);
    var targetNid = context.ParseResult.GetValueForOption(targetNidOption)!;
    var outputDir = context.ParseResult.GetValueForOption(outputDirOption)!;
    var label = context.ParseResult.GetValueForOption(labelOption)!;
    var createStagesOnly = context.ParseResult.GetValueForOption(createStagesOnlyOption);

    if (createStagesOnly)
    {
        await CreateStagesAsync(server, connections, stageId, targetStageId, targetNid);
    }
    else
    {
        await RunBenchmarkAsync(server, connections, messages, duration, requestSize, responseSizes, mode, commMode, callType, httpPort, apiHttpPort, stageId, targetStageId, targetNid, outputDir, label);
    }
});

return await rootCommand.InvokeAsync(args);

/// <summary>
/// Stage 생성 전용 함수 (벤치마크 사전 준비)
/// API 서버가 없는 경우에는 동작하지 않습니다.
/// 대신 최초 연결 시 자동으로 Stage를 생성하도록 합니다.
/// </summary>
static async Task CreateStagesAsync(
    string server,
    int connections,
    long stageId,
    long targetStageId,
    string targetNid)
{
    var parts = server.Split(':');
    if (parts.Length != 2 || !int.TryParse(parts[1], out var port))
    {
        Console.WriteLine("Invalid server address format. Use: host:port");
        return;
    }

    var host = parts[0];

    Log.Logger = new LoggerConfiguration()
        .MinimumLevel.Information()
        .WriteTo.Console(
            outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
        .CreateLogger();

    Log.Information("================================================================================");
    Log.Information("PlayHouse Stage Creation Utility");
    Log.Information("================================================================================");
    Log.Information("Server: {Host}:{Port}", host, port);
    Log.Information("Creating {Count} stages starting from ID {StageId}", connections, stageId);
    Log.Information("Target Stage ID for p2p: {TargetStageId} on {TargetNid}", targetStageId, targetNid);
    Log.Information("================================================================================");

    // Note: 현재 아키텍처에서는 Stage를 생성하려면:
    // 1) API 서버를 통해 CreateStageRequest를 보내거나
    // 2) PlayServer에 DefaultStageType을 설정해야 함
    //
    // DefaultStageType을 제거했으므로, Stage는 반드시 API 서버를 통해 생성되어야 합니다.
    // 하지만 벤치마크 환경에서는 API 서버가 항상 있는 것은 아닙니다.
    //
    // 해결책: PlayServer가 최초 연결 시 Stage를 자동 생성하도록 임시로 허용하거나,
    // 벤치마크 스크립트에서 API 서버를 먼저 시작하고 Stage를 생성하도록 해야 합니다.

    Log.Warning("Stage auto-creation is not supported without DefaultStageType.");
    Log.Warning("Please ensure API server is running and stages are created via API.");
    Log.Warning("Or temporarily re-enable DefaultStageType for benchmark purposes.");

    await Task.CompletedTask;
}

static async Task RunBenchmarkAsync(
    string server,
    int connections,
    int messages,
    int durationSeconds,
    int requestSize,
    string responseSizesStr,
    string mode,
    string commModeStr,
    string callTypeStr,
    int httpPort,
    int apiHttpPort,
    long stageId,
    long targetStageId,
    string targetNid,
    string outputDir,
    string label)
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
            Path.Combine(outputDir, $"benchmark_ss_{timestamp}{labelSuffix}.log"),
            outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
        .CreateLogger();

    try
    {
        // ss-echo 모드 처리
        if (mode.ToLowerInvariant() == "ss-echo")
        {
            await RunSSEchoBenchmarkAsync(host, port, connections, messages, durationSeconds, requestSize, responseSizesStr,
                commModeStr, callTypeStr, targetStageId, targetNid, outputDir, runTimestamp, label, httpPort, apiHttpPort);
            return;
        }

        // 모드 파싱
        var benchmarkMode = mode.ToLowerInvariant() switch
        {
            "play-to-api" => BenchmarkMode.PlayToApi,
            "play-to-stage" => BenchmarkMode.PlayToStage,
            "stage-to-api" => BenchmarkMode.StageToApi,
            "stage-to-stage" => BenchmarkMode.StageToStage,
            "api-to-api" => BenchmarkMode.ApiToApi,
            "all" => BenchmarkMode.All,
            _ => BenchmarkMode.All
        };

        // 배너 출력
        Log.Information("================================================================================");
        Log.Information("PlayHouse Server-to-Server Benchmark Client");
        Log.Information("================================================================================");
        Log.Information("Server: {Host}:{Port}", host, port);
        Log.Information("HTTP API: {Host}:{HttpPort}", host, httpPort);
        Log.Information("Mode: {Mode}", benchmarkMode);
        Log.Information("Connections: {Connections:N0}", connections);
        Log.Information("Messages per connection: {Messages:N0}", messages);
        Log.Information("Total messages: {TotalMessages:N0}", connections * messages);
        Log.Information("Request size: {RequestSize:N0} bytes", requestSize);
        Log.Information("Response sizes: {ResponseSizes}", string.Join(", ", responseSizes.Select(s => $"{s:N0}B")));

        if (benchmarkMode == BenchmarkMode.PlayToStage)
        {
            Log.Information("Target Stage ID: {TargetStageId}", targetStageId);
            Log.Information("Target NID: {TargetNid}", targetNid);
        }

        if (!string.IsNullOrEmpty(label))
            Log.Information("Label: {Label}", label);
        Log.Information("Output: {OutputDir}", Path.GetFullPath(outputDir));
        Log.Information("================================================================================");

        // 신규 내부 반복 모드 처리
        if (benchmarkMode == BenchmarkMode.All ||
            benchmarkMode == BenchmarkMode.StageToApi ||
            benchmarkMode == BenchmarkMode.StageToStage ||
            benchmarkMode == BenchmarkMode.ApiToApi)
        {
            await RunInternalBenchmarkAsync(host, port, messages, requestSize, responseSizesStr,
                benchmarkMode, targetStageId, targetNid, outputDir, runTimestamp, label, httpPort, apiHttpPort);
            return;
        }

        // 구 방식 (PlayToApi, PlayToStage)
        var serverMetricsClient = new ServerMetricsClient(host, httpPort);

        // CommMode별 결과 저장 (PlayToApi 모드는 두 모드 분리, 나머지는 단일 모드)
        var commModes = benchmarkMode == BenchmarkMode.PlayToApi
            ? new[] { (SSCommMode.RequestAsync, "RequestAsync"), (SSCommMode.RequestCallback, "RequestCallback") }
            : new[] { (SSCommMode.RequestAsync, "Default") };

        var resultsByCommMode = new Dictionary<string, List<BenchmarkResult>>();

        foreach (var (commMode, commModeName) in commModes)
        {
            if (benchmarkMode == BenchmarkMode.PlayToApi)
            {
                Log.Information("");
                Log.Information("================================================================================");
                Log.Information(">>> Testing: {Mode} ({CommMode}) <<<", benchmarkMode, commModeName);
                Log.Information("================================================================================");
            }

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

                // 서버 메트릭 리셋
                Log.Information("Resetting server metrics...");
                await serverMetricsClient.ResetMetricsAsync();
                await Task.Delay(500);

                // 벤치마크 실행
                var runner = new BenchmarkRunner(
                    host,
                    port,
                    connections,
                    messages,
                    requestSize,
                    responseSize,
                    benchmarkMode,
                    stageId,
                    targetStageId,
                    targetNid,
                    clientMetricsCollector,
                    commMode);

                var startTime = DateTime.Now;
                await runner.RunAsync();
                var endTime = DateTime.Now;
                var totalElapsed = (endTime - startTime).TotalSeconds;

                Log.Information("Waiting for server metrics to stabilize...");
                await Task.Delay(1000);

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

            resultsByCommMode[commModeName] = allResults;
        }

        // 통합 결과 출력
        Log.Information("");
        if (benchmarkMode == BenchmarkMode.PlayToApi && resultsByCommMode.Count == 2)
        {
            LogResultsWithCommModeComparison(connections, messages, requestSize, responseSizes, benchmarkMode, resultsByCommMode);
        }
        else
        {
            LogResults(connections, messages, requestSize, responseSizes, benchmarkMode, resultsByCommMode.Values.First());
        }

        // 결과 파일 저장
        await SaveResultsToFile(outputDir, runTimestamp, label, connections, messages, requestSize, benchmarkMode, resultsByCommMode.Values.First());

        // 서버 종료 요청
        await ShutdownServersAsync(host, httpPort, apiHttpPort, benchmarkMode);
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
    int requestSize,
    int[] responseSizes,
    BenchmarkMode mode,
    List<BenchmarkResult> results)
{
    Log.Information("================================================================================");
    Log.Information("Benchmark Results Summary");
    Log.Information("================================================================================");
    Log.Information("Config: {Connections:N0} CCU x {Messages:N0} msg, Request: {RequestSize:N0}B, Mode: {Mode}",
        connections, messages, requestSize, mode);
    Log.Information("");

    // 테이블 헤더
    Log.Information("{RespSize,8} | {Elapsed,6} | {SrvTPS,9} | {SrvP99,8} | {SrvMem,8} | {SrvGC,10} | {SsP99,8} | {E2eP99,8} | {CliTPS,9} | {CliMem,8} | {CliGC,10}",
        "RespSize", "Time", "Srv TPS", "Srv P99", "Srv Mem", "Srv GC", "SS P99", "E2E P99", "Cli TPS", "Cli Mem", "Cli GC");
    Log.Information("{D1} | {D2} | {D3} | {D4} | {D5} | {D6} | {D7} | {D8} | {D9} | {D10} | {D11}",
        "--------", "------", "---------", "--------", "--------", "----------", "--------", "--------", "---------", "--------", "----------");

    foreach (var result in results)
    {
        var srvTps = result.ServerMetrics?.ThroughputMessagesPerSec ?? 0;
        var srvP99 = result.ServerMetrics?.LatencyP99Ms ?? 0;
        var srvMem = result.ServerMetrics?.MemoryAllocatedMb ?? 0;
        var srvGc = result.ServerMetrics != null
            ? $"{result.ServerMetrics.GcGen0Count}/{result.ServerMetrics.GcGen1Count}/{result.ServerMetrics.GcGen2Count}"
            : "-";
        var ssP99 = result.ClientMetrics.SsLatencyP99Ms;
        var e2eP99 = result.ClientMetrics.E2eLatencyP99Ms;
        var cliTps = result.ClientMetrics.ThroughputMessagesPerSec;
        var cliMem = result.ClientMetrics.MemoryAllocatedMB;
        var cliGc = $"{result.ClientMetrics.GcGen0Count}/{result.ClientMetrics.GcGen1Count}/{result.ClientMetrics.GcGen2Count}";

        Log.Information("{RespSize,7:N0}B | {Elapsed,5:F2}s | {SrvTPS,8:N0}/s | {SrvP99,6:F2}ms | {SrvMem,6:F1}MB | {SrvGC,10} | {SsP99,6:F2}ms | {E2eP99,6:F2}ms | {CliTPS,8:N0}/s | {CliMem,6:F1}MB | {CliGC,10}",
            result.ResponseSize, result.TotalElapsedSeconds, srvTps, srvP99, srvMem, srvGc, ssP99, e2eP99, cliTps, cliMem, cliGc);
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
        Log.Information("    SS Latency  : Mean={Mean:F2}ms, P50={P50:F2}ms, P95={P95:F2}ms, P99={P99:F2}ms",
            result.ClientMetrics.SsLatencyMeanMs, result.ClientMetrics.SsLatencyP50Ms,
            result.ClientMetrics.SsLatencyP95Ms, result.ClientMetrics.SsLatencyP99Ms);
        Log.Information("    E2E Latency : Mean={Mean:F2}ms, P50={P50:F2}ms, P95={P95:F2}ms, P99={P99:F2}ms",
            result.ClientMetrics.E2eLatencyMeanMs, result.ClientMetrics.E2eLatencyP50Ms,
            result.ClientMetrics.E2eLatencyP95Ms, result.ClientMetrics.E2eLatencyP99Ms);
        Log.Information("    Throughput  : {TPS:N0} msg/s",
            result.ClientMetrics.ThroughputMessagesPerSec);
        Log.Information("    Memory      : {Memory:F2} MB, GC: Gen0={Gen0}, Gen1={Gen1}, Gen2={Gen2}",
            result.ClientMetrics.MemoryAllocatedMB,
            result.ClientMetrics.GcGen0Count, result.ClientMetrics.GcGen1Count, result.ClientMetrics.GcGen2Count);
    }

    Log.Information("================================================================================");
}

/// <summary>
/// RequestAsync vs RequestCallback 비교 결과 출력
/// </summary>
static void LogResultsWithCommModeComparison(
    int connections,
    int messages,
    int requestSize,
    int[] responseSizes,
    BenchmarkMode mode,
    Dictionary<string, List<BenchmarkResult>> resultsByCommMode)
{
    Log.Information("================================================================================");
    Log.Information("Benchmark Results: Stage → API (RequestAsync vs RequestCallback)");
    Log.Information("================================================================================");
    Log.Information("Config: {Connections:N0} CCU x {Messages:N0} msg/conn = {Total:N0} total",
        connections, messages, connections * messages);
    Log.Information("        Request: {RequestSize:N0}B, Response: {ResponseSizes}",
        requestSize, string.Join(", ", responseSizes.Select(s => $"{s:N0}B")));
    Log.Information("");

    var asyncResults = resultsByCommMode.GetValueOrDefault("RequestAsync") ?? new List<BenchmarkResult>();
    var callbackResults = resultsByCommMode.GetValueOrDefault("RequestCallback") ?? new List<BenchmarkResult>();

    // 비교 테이블 헤더
    Log.Information("[Stage → API Comparison]");
    Log.Information("{RespSize,8} | {Mode,16} | {Time,6} | {CliTPS,9} | {E2eP99,8} | {SsP99,8} | {SrvMem,8} | {SrvGC,10} | {CliMem,8} | {CliGC,10}",
        "RespSize", "Mode", "Time", "Cli TPS", "E2E P99", "SS P99", "Srv Mem", "Srv GC", "Cli Mem", "Cli GC");
    Log.Information("{D1} | {D2} | {D3} | {D4} | {D5} | {D6} | {D7} | {D8} | {D9} | {D10}",
        "--------", "----------------", "------", "---------", "--------", "--------", "--------", "----------", "--------", "----------");

    for (int i = 0; i < responseSizes.Length; i++)
    {
        var asyncResult = i < asyncResults.Count ? asyncResults[i] : null;
        var callbackResult = i < callbackResults.Count ? callbackResults[i] : null;
        var responseSize = responseSizes[i];

        if (asyncResult != null)
        {
            var cliTps = asyncResult.ClientMetrics.ThroughputMessagesPerSec;
            var e2eP99 = asyncResult.ClientMetrics.E2eLatencyP99Ms;
            var ssP99 = asyncResult.ClientMetrics.SsLatencyP99Ms;
            var srvMem = asyncResult.ServerMetrics?.MemoryAllocatedMb ?? 0;
            var srvGc = asyncResult.ServerMetrics != null
                ? $"{asyncResult.ServerMetrics.GcGen0Count}/{asyncResult.ServerMetrics.GcGen1Count}/{asyncResult.ServerMetrics.GcGen2Count}"
                : "-";
            var cliMem = asyncResult.ClientMetrics.MemoryAllocatedMB;
            var cliGc = $"{asyncResult.ClientMetrics.GcGen0Count}/{asyncResult.ClientMetrics.GcGen1Count}/{asyncResult.ClientMetrics.GcGen2Count}";

            Log.Information("{RespSize,7:N0}B | {Mode,16} | {Time,5:F2}s | {CliTPS,8:N0}/s | {E2eP99,6:F2}ms | {SsP99,6:F2}ms | {SrvMem,6:F0}MB | {SrvGC,10} | {CliMem,6:F0}MB | {CliGC,10}",
                responseSize, "RequestAsync", asyncResult.TotalElapsedSeconds, cliTps, e2eP99, ssP99, srvMem, srvGc, cliMem, cliGc);
        }

        if (callbackResult != null)
        {
            var cliTps = callbackResult.ClientMetrics.ThroughputMessagesPerSec;
            var e2eP99 = callbackResult.ClientMetrics.E2eLatencyP99Ms;
            var ssP99 = callbackResult.ClientMetrics.SsLatencyP99Ms;
            var srvMem = callbackResult.ServerMetrics?.MemoryAllocatedMb ?? 0;
            var srvGc = callbackResult.ServerMetrics != null
                ? $"{callbackResult.ServerMetrics.GcGen0Count}/{callbackResult.ServerMetrics.GcGen1Count}/{callbackResult.ServerMetrics.GcGen2Count}"
                : "-";
            var cliMem = callbackResult.ClientMetrics.MemoryAllocatedMB;
            var cliGc = $"{callbackResult.ClientMetrics.GcGen0Count}/{callbackResult.ClientMetrics.GcGen1Count}/{callbackResult.ClientMetrics.GcGen2Count}";

            Log.Information("{RespSize,7:N0}B | {Mode,16} | {Time,5:F2}s | {CliTPS,8:N0}/s | {E2eP99,6:F2}ms | {SsP99,6:F2}ms | {SrvMem,6:F0}MB | {SrvGC,10} | {CliMem,6:F0}MB | {CliGC,10}",
                responseSize, "RequestCallback", callbackResult.TotalElapsedSeconds, cliTps, e2eP99, ssP99, srvMem, srvGc, cliMem, cliGc);
        }

        // 비교 계산 (동일 응답 크기에 대해)
        if (asyncResult != null && callbackResult != null)
        {
            var asyncCliTps = asyncResult.ClientMetrics.ThroughputMessagesPerSec;
            var callbackCliTps = callbackResult.ClientMetrics.ThroughputMessagesPerSec;
            var tpsDiff = asyncCliTps > 0 ? ((callbackCliTps - asyncCliTps) / asyncCliTps) * 100 : 0;

            var asyncE2eP99 = asyncResult.ClientMetrics.E2eLatencyP99Ms;
            var callbackE2eP99 = callbackResult.ClientMetrics.E2eLatencyP99Ms;
            var latDiff = asyncE2eP99 > 0 ? ((callbackE2eP99 - asyncE2eP99) / asyncE2eP99) * 100 : 0;

            var asyncSrvMem = asyncResult.ServerMetrics?.MemoryAllocatedMb ?? 0;
            var callbackSrvMem = callbackResult.ServerMetrics?.MemoryAllocatedMb ?? 0;
            var srvMemDiff = asyncSrvMem > 0 ? ((callbackSrvMem - asyncSrvMem) / asyncSrvMem) * 100 : 0;

            var asyncCliMem = asyncResult.ClientMetrics.MemoryAllocatedMB;
            var callbackCliMem = callbackResult.ClientMetrics.MemoryAllocatedMB;
            var cliMemDiff = asyncCliMem > 0 ? ((callbackCliMem - asyncCliMem) / asyncCliMem) * 100 : 0;

            Log.Information("{Empty,8} | {Comparison,16} | {Empty2,6} | {TpsDiff,10:+0.0;-0.0}% | {LatDiff,8:+0.0;-0.0}% | {Empty3,8} | {SrvMemDiff,8:+0.0;-0.0}% | {Empty4,10} | {CliMemDiff,8:+0.0;-0.0}% |",
                "", "→ Callback diff", "", tpsDiff, latDiff, "", srvMemDiff, "", cliMemDiff);
        }

        if (i < responseSizes.Length - 1)
        {
            Log.Information("{D1} | {D2} | {D3} | {D4} | {D5} | {D6} | {D7} | {D8} | {D9} | {D10}",
                "--------", "----------------", "------", "---------", "--------", "--------", "--------", "----------", "--------", "----------");
        }
    }

    Log.Information("================================================================================");
}

/// <summary>
/// 신규 내부 반복 벤치마크 실행 (서버 측에서 반복)
/// </summary>
static async Task RunInternalBenchmarkAsync(
    string host,
    int port,
    int iterations,
    int requestSize,
    string responseSizesStr,
    BenchmarkMode mode,
    long targetStageId,
    string targetNid,
    string outputDir,
    DateTime runTimestamp,
    string label,
    int httpPort,
    int apiHttpPort)
{
    var responseSizes = responseSizesStr
        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Select(s => int.TryParse(s, out var v) ? v : 256)
        .ToArray();

    if (responseSizes.Length == 0)
    {
        responseSizes = new[] { 256 };
    }

    // 테스트할 조합 결정
    var testCases = new List<(SSCallType CallType, SSCommMode CommMode, string Name)>();

    if (mode == BenchmarkMode.All || mode == BenchmarkMode.StageToApi)
    {
        // Stage → API 테스트만 실행
        testCases.Add((SSCallType.StageToApi, SSCommMode.RequestAsync, "Stage → API (RequestAsync)"));
        testCases.Add((SSCallType.StageToApi, SSCommMode.RequestCallback, "Stage → API (RequestCallback)"));
    }

    var allResults = new Dictionary<string, List<StartSSBenchmarkReply>>();

    foreach (var (callType, commMode, name) in testCases)
    {
        Log.Information("");
        Log.Information(">>> Testing: {Name} <<<", name);

        var results = new List<StartSSBenchmarkReply>();

        foreach (var responseSize in responseSizes)
        {
            if (responseSizes.Length > 1)
            {
                Log.Information("  Response Size: {ResponseSize:N0} bytes", responseSize);
            }

            var reply = await BenchmarkRunner.RunInternalBenchmarkAsync(
                host, port, iterations, requestSize, responseSize,
                callType, commMode, 1000, targetStageId, targetNid, "api-2");

            if (reply != null)
            {
                results.Add(reply);
                Log.Information("  Success: {Success}/{Total}, TPS: {TPS:N0}/s, P99: {P99:F2}ms",
                    reply.SuccessCount, reply.TotalIterations, reply.ThroughputPerSec, reply.LatencyP99Ms);
            }
            else
            {
                Log.Warning("  Failed to execute benchmark");
            }

            await Task.Delay(500); // 잠시 대기
        }

        allResults[name] = results;
    }

    // 비교 결과 출력
    Log.Information("");
    LogInternalBenchmarkComparison(iterations, requestSize, responseSizes, allResults);

    // 결과 파일 저장
    await SaveInternalBenchmarkResults(outputDir, runTimestamp, label, iterations, requestSize, allResults);

    // 서버 종료 요청
    await ShutdownServersAsync(host, httpPort, apiHttpPort, mode);
}

/// <summary>
/// 내부 반복 벤치마크 비교 결과 출력
/// </summary>
static void LogInternalBenchmarkComparison(
    int iterations,
    int requestSize,
    int[] responseSizes,
    Dictionary<string, List<StartSSBenchmarkReply>> allResults)
{
    Log.Information("================================================================================");
    Log.Information("Server-to-Server Benchmark Results");
    Log.Information("================================================================================");
    Log.Information("Config: {Iterations:N0} iterations, Request: {RequestSize:N0}B", iterations, requestSize);
    Log.Information("");

    // 호출 유형별로 그룹화
    var groups = new Dictionary<string, List<(string Name, List<StartSSBenchmarkReply> Results)>>();
    groups["Stage → API"] = new();
    groups["Stage → Stage"] = new();
    groups["API → API"] = new();

    foreach (var (name, results) in allResults)
    {
        if (name.StartsWith("Stage → API"))
            groups["Stage → API"].Add((name, results));
        else if (name.StartsWith("Stage → Stage"))
            groups["Stage → Stage"].Add((name, results));
        else if (name.StartsWith("API → API"))
            groups["API → API"].Add((name, results));
    }

    foreach (var (groupName, tests) in groups)
    {
        if (tests.Count == 0) continue;

        Log.Information("[{GroupName}]", groupName);

        if (tests.Count == 2 && responseSizes.Length == 1)
        {
            // RequestAsync vs RequestCallback 비교 (단일 응답 크기)
            var requestAsync = tests.FirstOrDefault(t => t.Name.Contains("RequestAsync")).Results?.FirstOrDefault();
            var requestCallback = tests.FirstOrDefault(t => t.Name.Contains("RequestCallback")).Results?.FirstOrDefault();

            if (requestAsync != null && requestCallback != null)
            {
                var tpsDiff = ((requestCallback.ThroughputPerSec - requestAsync.ThroughputPerSec) / requestAsync.ThroughputPerSec) * 100;
                var p99Diff = ((requestCallback.LatencyP99Ms - requestAsync.LatencyP99Ms) / requestAsync.LatencyP99Ms) * 100;

                Log.Information("               | RequestAsync | RequestCallback | Diff");
                Log.Information("---------------|--------------|-----------------|-------");
                Log.Information("Throughput     | {RA,9:N0}/s | {RC,12:N0}/s | {Diff,6:+0.0;-0.0}%",
                    requestAsync.ThroughputPerSec, requestCallback.ThroughputPerSec, tpsDiff);
                Log.Information("P99 Latency    | {RA,9:F2}ms | {RC,12:F2}ms | {Diff,6:+0.0;-0.0}%",
                    requestAsync.LatencyP99Ms, requestCallback.LatencyP99Ms, p99Diff);
            }
        }
        else
        {
            // 여러 응답 크기 또는 단일 모드
            foreach (var (name, results) in tests)
            {
                Log.Information("  {Name}:", name);
                foreach (var r in results)
                {
                    Log.Information("    TPS: {TPS,8:N0}/s, P99: {P99,6:F2}ms",
                        r.ThroughputPerSec, r.LatencyP99Ms);
                }
            }
        }

        Log.Information("");
    }

    Log.Information("================================================================================");
}

/// <summary>
/// 내부 반복 벤치마크 결과 파일 저장
/// </summary>
static async Task SaveInternalBenchmarkResults(
    string outputDir,
    DateTime runTimestamp,
    string label,
    int iterations,
    int requestSize,
    Dictionary<string, List<StartSSBenchmarkReply>> allResults)
{
    var timestamp = runTimestamp.ToString("yyyyMMdd_HHmmss");
    var labelSuffix = string.IsNullOrEmpty(label) ? "" : $"_{label}";

    // JSON 결과 파일
    var jsonFileName = $"benchmark_ss_internal_{timestamp}{labelSuffix}.json";
    var jsonPath = Path.Combine(outputDir, jsonFileName);

    var jsonResult = new
    {
        Timestamp = runTimestamp.ToString("yyyy-MM-dd HH:mm:ss"),
        Label = label,
        Config = new
        {
            Iterations = iterations,
            RequestSizeBytes = requestSize
        },
        Results = allResults.Select(kv => new
        {
            TestName = kv.Key,
            Results = kv.Value.Select(r => new
            {
                CallType = r.CallType.ToString(),
                CommMode = r.CommMode.ToString(),
                TotalIterations = r.TotalIterations,
                SuccessCount = r.SuccessCount,
                FailedCount = r.FailedCount,
                ElapsedSeconds = r.ElapsedSeconds,
                ThroughputPerSec = r.ThroughputPerSec,
                LatencyMeanMs = r.LatencyMeanMs,
                LatencyP50Ms = r.LatencyP50Ms,
                LatencyP95Ms = r.LatencyP95Ms,
                LatencyP99Ms = r.LatencyP99Ms
            }).ToArray()
        }).ToArray()
    };

    var jsonOptions = new System.Text.Json.JsonSerializerOptions { WriteIndented = true };
    await File.WriteAllTextAsync(jsonPath, System.Text.Json.JsonSerializer.Serialize(jsonResult, jsonOptions));

    Log.Information("");
    Log.Information("Results saved to:");
    Log.Information("  JSON: {JsonPath}", jsonPath);
}

static async Task SaveResultsToFile(
    string outputDir,
    DateTime runTimestamp,
    string label,
    int connections,
    int messages,
    int requestSize,
    BenchmarkMode mode,
    List<BenchmarkResult> results)
{
    var timestamp = runTimestamp.ToString("yyyyMMdd_HHmmss");
    var labelSuffix = string.IsNullOrEmpty(label) ? "" : $"_{label}";

    // 1. JSON 결과 파일 (모든 테스트)
    var jsonFileName = $"benchmark_ss_{timestamp}{labelSuffix}.json";
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
            RequestSizeBytes = requestSize,
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
                SsLatencyMeanMs = r.ClientMetrics.SsLatencyMeanMs,
                SsLatencyP50Ms = r.ClientMetrics.SsLatencyP50Ms,
                SsLatencyP95Ms = r.ClientMetrics.SsLatencyP95Ms,
                SsLatencyP99Ms = r.ClientMetrics.SsLatencyP99Ms,
                E2eLatencyMeanMs = r.ClientMetrics.E2eLatencyMeanMs,
                E2eLatencyP50Ms = r.ClientMetrics.E2eLatencyP50Ms,
                E2eLatencyP95Ms = r.ClientMetrics.E2eLatencyP95Ms,
                E2eLatencyP99Ms = r.ClientMetrics.E2eLatencyP99Ms,
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
    var csvPath = Path.Combine(outputDir, "benchmark_ss_summary.csv");
    var csvExists = File.Exists(csvPath);

    var csvBuilder = new StringBuilder();
    if (!csvExists)
    {
        // 헤더 추가
        csvBuilder.AppendLine("Timestamp,Label,CCU,MsgPerConn,TotalMsg,ReqSize,RespSize,Mode,ElapsedSec," +
            "Srv_Processed,Srv_TPS,Srv_MBps,Srv_LatMean,Srv_LatP50,Srv_LatP95,Srv_LatP99,Srv_MemMB,Srv_GC0,Srv_GC1,Srv_GC2," +
            "Cli_Sent,Cli_Recv,Cli_SsMean,Cli_SsP50,Cli_SsP95,Cli_SsP99,Cli_E2eMean,Cli_E2eP50,Cli_E2eP95,Cli_E2eP99,Cli_TPS,Cli_MemMB,Cli_GC0,Cli_GC1,Cli_GC2");
    }

    foreach (var r in results)
    {
        csvBuilder.Append($"{runTimestamp:yyyy-MM-dd HH:mm:ss},{label},{connections},{messages},{connections * messages},{requestSize},{r.ResponseSize},{mode},{r.TotalElapsedSeconds:F2},");

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
            $"{r.ClientMetrics.SsLatencyMeanMs:F2},{r.ClientMetrics.SsLatencyP50Ms:F2},{r.ClientMetrics.SsLatencyP95Ms:F2},{r.ClientMetrics.SsLatencyP99Ms:F2}," +
            $"{r.ClientMetrics.E2eLatencyMeanMs:F2},{r.ClientMetrics.E2eLatencyP50Ms:F2},{r.ClientMetrics.E2eLatencyP95Ms:F2},{r.ClientMetrics.E2eLatencyP99Ms:F2}," +
            $"{r.ClientMetrics.ThroughputMessagesPerSec:F0},{r.ClientMetrics.MemoryAllocatedMB:F2}," +
            $"{r.ClientMetrics.GcGen0Count},{r.ClientMetrics.GcGen1Count},{r.ClientMetrics.GcGen2Count}");
    }

    await File.AppendAllTextAsync(csvPath, csvBuilder.ToString());

    Log.Information("");
    Log.Information("Results saved to:");
    Log.Information("  Log:  {LogPath}", Path.Combine(outputDir, $"benchmark_ss_{timestamp}{labelSuffix}.log"));
    Log.Information("  JSON: {JsonPath}", jsonPath);
    Log.Information("  CSV:  {CsvPath}", csvPath);
}

/// <summary>
/// SS Echo 벤치마크 실행 (TriggerSSEchoRequest 사용)
/// </summary>
static async Task RunSSEchoBenchmarkAsync(
    string host,
    int port,
    int connections,
    int iterations,
    int durationSeconds,
    int requestSize,
    string responseSizesStr,
    string commModeStr,
    string callTypeStr,
    long targetStageId,
    string targetNid,
    string outputDir,
    DateTime runTimestamp,
    string label,
    int httpPort,
    int apiHttpPort)
{
    var responseSizes = responseSizesStr
        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Select(s => int.TryParse(s, out var v) ? v : 256)
        .ToArray();

    if (responseSizes.Length == 0)
    {
        responseSizes = new[] { 256 };
    }

    // CallType 파싱
    var callType = callTypeStr.ToLower() switch
    {
        "stage-to-api" => SSCallType.StageToApi,
        "stage-to-stage" => SSCallType.StageToStage,
        _ => SSCallType.StageToApi
    };

    // --comm-mode all인 경우 모든 모드 테스트
    if (commModeStr.ToLower() == "all")
    {
        await RunAllCommModesAsync(host, port, connections, iterations, durationSeconds, requestSize, responseSizes,
            callType, targetStageId, targetNid, outputDir, runTimestamp, label, httpPort, apiHttpPort);
        return;
    }

    // CommMode 파싱
    var commMode = commModeStr.ToLower() switch
    {
        "send" => SSCommMode.Send,
        "request-async" => SSCommMode.RequestAsync,
        "request-callback" => SSCommMode.RequestCallback,
        _ => SSCommMode.RequestAsync
    };

    // 배너 출력
    Log.Information("================================================================================");
    Log.Information("PlayHouse SS Echo Benchmark Client");
    Log.Information("================================================================================");
    Log.Information("Server: {Host}:{Port}", host, port);
    Log.Information("HTTP API: {Host}:{HttpPort}", host, httpPort);
    Log.Information("Connections: {Connections:N0}", connections);
    Log.Information("Iterations per connection: {Iterations:N0}", iterations);
    Log.Information("Total messages: {TotalMessages:N0}", connections * iterations);
    Log.Information("Request size: {RequestSize:N0} bytes", requestSize);
    Log.Information("Response sizes: {ResponseSizes}", string.Join(", ", responseSizes.Select(s => $"{s:N0}B")));
    Log.Information("CommMode: {CommMode}", commMode);
    Log.Information("CallType: {CallType}", callType);

    if (callType == SSCallType.StageToStage)
    {
        Log.Information("Target Stage ID: {TargetStageId}", targetStageId);
        Log.Information("Target NID: {TargetNid}", targetNid);
    }

    if (!string.IsNullOrEmpty(label))
        Log.Information("Label: {Label}", label);
    Log.Information("Output: {OutputDir}", Path.GetFullPath(outputDir));
    Log.Information("================================================================================");

    var allResults = new List<SSEchoBenchmarkResult>();

    foreach (var responseSize in responseSizes)
    {
        if (responseSizes.Length > 1)
        {
            Log.Information("");
            Log.Information(">>> Response Size: {ResponseSize:N0} bytes <<<", responseSize);
        }

        // 메트릭 수집기
        var clientMetricsCollector = new ClientMetricsCollector();

        // 벤치마크 실행
        var runner = new SSEchoBenchmarkRunner(
            host,
            port,
            connections,
            iterations,
            requestSize,
            responseSize,
            commMode,
            callType,
            targetStageId,
            targetNid,
            clientMetricsCollector);

        var startTime = DateTime.Now;
        await runner.RunAsync();
        var endTime = DateTime.Now;
        var totalElapsed = (endTime - startTime).TotalSeconds;

        Log.Information("Waiting for metrics to stabilize...");
        await Task.Delay(1000);

        // 결과 조회
        var clientMetrics = clientMetricsCollector.GetMetrics();

        // 결과 저장
        allResults.Add(new SSEchoBenchmarkResult
        {
            ResponseSize = responseSize,
            TotalElapsedSeconds = totalElapsed,
            ClientMetrics = clientMetrics
        });

        Log.Information("  Success: {Sent}/{Total}, TPS: {TPS:N0}/s, P99: {P99:F2}ms",
            clientMetrics.ReceivedMessages, clientMetrics.SentMessages,
            clientMetrics.ThroughputMessagesPerSec, clientMetrics.E2eLatencyP99Ms);

        await Task.Delay(500); // 잠시 대기
    }

    // 비교 결과 출력
    Log.Information("");
    LogSSEchoResults(connections, iterations, requestSize, responseSizes, commMode, callType, allResults);

    // 결과 파일 저장
    await SaveSSEchoResults(outputDir, runTimestamp, label, connections, iterations, requestSize,
        commMode, callType, targetStageId, targetNid, allResults);

    // 서버 종료 요청 (비활성화 - 벤치마크 스크립트에서 처리)
    // var mode = callType == SSCallType.StageToApi ? BenchmarkMode.StageToApi : BenchmarkMode.StageToStage;
    // await ShutdownServersAsync(host, httpPort, apiHttpPort, mode);
}

/// <summary>
/// SS Echo 벤치마크 결과 출력
/// </summary>
static void LogSSEchoResults(
    int connections,
    int iterations,
    int requestSize,
    int[] responseSizes,
    SSCommMode commMode,
    SSCallType callType,
    List<SSEchoBenchmarkResult> results)
{
    Log.Information("================================================================================");
    Log.Information("SS Echo Benchmark Results");
    Log.Information("================================================================================");
    Log.Information("Config: {Connections:N0} connections x {Iterations:N0} iterations = {Total:N0} messages",
        connections, iterations, connections * iterations);
    Log.Information("        Request: {RequestSize:N0}B, CommMode: {CommMode}, CallType: {CallType}",
        requestSize, commMode, callType);
    Log.Information("");

    // 테이블 헤더
    Log.Information("{RespSize,8} | {Elapsed,6} | {E2eP99,8} | {SsP99,8} | {CliTPS,9} | {CliMem,8} | {CliGC,10}",
        "RespSize", "Time", "E2E P99", "SS P99", "Cli TPS", "Cli Mem", "Cli GC");
    Log.Information("{D1} | {D2} | {D3} | {D4} | {D5} | {D6} | {D7}",
        "--------", "------", "--------", "--------", "---------", "--------", "----------");

    foreach (var result in results)
    {
        var e2eP99 = result.ClientMetrics.E2eLatencyP99Ms;
        var ssP99 = result.ClientMetrics.SsLatencyP99Ms;
        var cliTps = result.ClientMetrics.ThroughputMessagesPerSec;
        var cliMem = result.ClientMetrics.MemoryAllocatedMB;
        var cliGc = $"{result.ClientMetrics.GcGen0Count}/{result.ClientMetrics.GcGen1Count}/{result.ClientMetrics.GcGen2Count}";

        Log.Information("{RespSize,7:N0}B | {Elapsed,5:F2}s | {E2eP99,6:F2}ms | {SsP99,6:F2}ms | {CliTPS,8:N0}/s | {CliMem,6:F1}MB | {CliGC,10}",
            result.ResponseSize, result.TotalElapsedSeconds, e2eP99, ssP99, cliTps, cliMem, cliGc);
    }

    Log.Information("");

    // 각 테스트별 상세 결과
    foreach (var result in results)
    {
        Log.Information("--------------------------------------------------------------------------------");
        Log.Information("[Response Size: {ResponseSize:N0} bytes]", result.ResponseSize);
        Log.Information("  Elapsed: {Elapsed:F2}s", result.TotalElapsedSeconds);
        Log.Information("  Client:");
        Log.Information("    Sent/Recv   : {Sent:N0} / {Recv:N0} messages",
            result.ClientMetrics.SentMessages, result.ClientMetrics.ReceivedMessages);
        Log.Information("    SS Latency  : Mean={Mean:F2}ms, P50={P50:F2}ms, P95={P95:F2}ms, P99={P99:F2}ms",
            result.ClientMetrics.SsLatencyMeanMs, result.ClientMetrics.SsLatencyP50Ms,
            result.ClientMetrics.SsLatencyP95Ms, result.ClientMetrics.SsLatencyP99Ms);
        Log.Information("    E2E Latency : Mean={Mean:F2}ms, P50={P50:F2}ms, P95={P95:F2}ms, P99={P99:F2}ms",
            result.ClientMetrics.E2eLatencyMeanMs, result.ClientMetrics.E2eLatencyP50Ms,
            result.ClientMetrics.E2eLatencyP95Ms, result.ClientMetrics.E2eLatencyP99Ms);
        Log.Information("    Throughput  : {TPS:N0} msg/s",
            result.ClientMetrics.ThroughputMessagesPerSec);
        Log.Information("    Memory      : {Memory:F2} MB, GC: Gen0={Gen0}, Gen1={Gen1}, Gen2={Gen2}",
            result.ClientMetrics.MemoryAllocatedMB,
            result.ClientMetrics.GcGen0Count, result.ClientMetrics.GcGen1Count, result.ClientMetrics.GcGen2Count);
    }

    Log.Information("================================================================================");
}

/// <summary>
/// SS Echo 벤치마크 결과 파일 저장
/// </summary>
static async Task SaveSSEchoResults(
    string outputDir,
    DateTime runTimestamp,
    string label,
    int connections,
    int iterations,
    int requestSize,
    SSCommMode commMode,
    SSCallType callType,
    long targetStageId,
    string targetNid,
    List<SSEchoBenchmarkResult> results)
{
    var timestamp = runTimestamp.ToString("yyyyMMdd_HHmmss");
    var labelSuffix = string.IsNullOrEmpty(label) ? "" : $"_{label}";

    // JSON 결과 파일
    var jsonFileName = $"benchmark_ss_echo_{timestamp}{labelSuffix}.json";
    var jsonPath = Path.Combine(outputDir, jsonFileName);

    var jsonResult = new
    {
        Timestamp = runTimestamp.ToString("yyyy-MM-dd HH:mm:ss"),
        Label = label,
        Config = new
        {
            Connections = connections,
            IterationsPerConnection = iterations,
            TotalMessages = connections * iterations,
            RequestSizeBytes = requestSize,
            CommMode = commMode.ToString(),
            CallType = callType.ToString(),
            TargetStageId = targetStageId,
            TargetNid = targetNid
        },
        Results = results.Select(r => new
        {
            ResponseSizeBytes = r.ResponseSize,
            TotalElapsedSeconds = r.TotalElapsedSeconds,
            Client = new
            {
                SentMessages = r.ClientMetrics.SentMessages,
                ReceivedMessages = r.ClientMetrics.ReceivedMessages,
                SsLatencyMeanMs = r.ClientMetrics.SsLatencyMeanMs,
                SsLatencyP50Ms = r.ClientMetrics.SsLatencyP50Ms,
                SsLatencyP95Ms = r.ClientMetrics.SsLatencyP95Ms,
                SsLatencyP99Ms = r.ClientMetrics.SsLatencyP99Ms,
                E2eLatencyMeanMs = r.ClientMetrics.E2eLatencyMeanMs,
                E2eLatencyP50Ms = r.ClientMetrics.E2eLatencyP50Ms,
                E2eLatencyP95Ms = r.ClientMetrics.E2eLatencyP95Ms,
                E2eLatencyP99Ms = r.ClientMetrics.E2eLatencyP99Ms,
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

    Log.Information("");
    Log.Information("Results saved to:");
    Log.Information("  JSON: {JsonPath}", jsonPath);
}

/// <summary>
/// 서버 종료 요청
/// </summary>
static async Task ShutdownServersAsync(string host, int httpPort, int apiHttpPort, BenchmarkMode mode)
{
    Log.Information("");
    Log.Information("Sending shutdown requests to servers...");

    using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };

    // PlayServer 종료 요청 (모든 모드에서 필요)
    try
    {
        await httpClient.PostAsync($"http://{host}:{httpPort}/benchmark/shutdown", null);
        Log.Information("  PlayServer shutdown initiated (port {Port})", httpPort);
    }
    catch (Exception)
    {
        // 서버가 이미 종료되었거나 연결 실패 - 무시
    }

    // ApiServer 종료 요청 (PlayToApi, StageToApi, ApiToApi 모드에서 필요)
    if (mode == BenchmarkMode.PlayToApi ||
        mode == BenchmarkMode.StageToApi ||
        mode == BenchmarkMode.ApiToApi ||
        mode == BenchmarkMode.All)
    {
        try
        {
            await httpClient.PostAsync($"http://{host}:{apiHttpPort}/benchmark/shutdown", null);
            Log.Information("  ApiServer shutdown initiated (port {Port})", apiHttpPort);
        }
        catch (Exception)
        {
            // 서버가 이미 종료되었거나 연결 실패 - 무시
        }
    }
}

/// <summary>
/// 모든 CommMode에 대해 벤치마크 실행 (RequestAsync, RequestCallback, Send)
/// </summary>
static async Task RunAllCommModesAsync(
    string host,
    int port,
    int connections,
    int iterations,
    int durationSeconds,
    int requestSize,
    int[] responseSizes,
    SSCallType callType,
    long targetStageId,
    string targetNid,
    string outputDir,
    DateTime runTimestamp,
    string label,
    int httpPort,
    int apiHttpPort)
{
    var commModes = new[]
    {
        (SSCommMode.RequestAsync, "RequestAsync"),
        (SSCommMode.RequestCallback, "RequestCallback"),
        (SSCommMode.Send, "Send")
    };

    var allResults = new Dictionary<string, Dictionary<int, SSEchoBenchmarkResult>>();

    // 배너 출력
    Log.Information("================================================================================");
    Log.Information("PlayHouse SS Echo Benchmark - All Modes Comparison");
    Log.Information("================================================================================");
    Log.Information("Server: {Host}:{Port}", host, port);
    Log.Information("Connections: {Connections:N0}", connections);
    if (durationSeconds > 0)
    {
        Log.Information("Duration: {Duration}s per mode/size", durationSeconds);
    }
    else
    {
        Log.Information("Iterations per connection: {Iterations:N0}", iterations);
        Log.Information("Total messages per mode: {TotalMessages:N0}", connections * iterations);
    }
    Log.Information("Request size: {RequestSize:N0} bytes", requestSize);
    Log.Information("Response sizes: {ResponseSizes}", string.Join(", ", responseSizes.Select(s => $"{s:N0}B")));
    Log.Information("Modes: {Modes}", string.Join(", ", commModes.Select(m => m.Item2)));
    Log.Information("CallType: {CallType}", callType);
    Log.Information("================================================================================");
    Log.Information("");

    foreach (var (commMode, modeName) in commModes)
    {
        Log.Information("");
        Log.Information(">>> Testing: {ModeName} <<<", modeName);

        var modeResults = new Dictionary<int, SSEchoBenchmarkResult>();

        foreach (var responseSize in responseSizes)
        {
            Log.Information("  Response Size: {ResponseSize:N0} bytes", responseSize);

            var clientMetricsCollector = new ClientMetricsCollector();

            var runner = new SSEchoBenchmarkRunner(
                host,
                port,
                connections,
                durationSeconds > 0 ? 0 : iterations,  // duration mode면 0으로 설정
                requestSize,
                responseSize,
                commMode,
                callType,
                targetStageId,
                targetNid,
                clientMetricsCollector,
                durationSeconds);  // duration 전달

            var startTime = DateTime.Now;
            await runner.RunAsync();
            var endTime = DateTime.Now;
            var totalElapsed = (endTime - startTime).TotalSeconds;

            await Task.Delay(1000);

            var clientMetrics = clientMetricsCollector.GetMetrics();

            modeResults[responseSize] = new SSEchoBenchmarkResult
            {
                ResponseSize = responseSize,
                TotalElapsedSeconds = totalElapsed,
                ClientMetrics = clientMetrics
            };

            Log.Information("    TPS: {TPS:N0}/s, P99: {P99:F2}ms",
                clientMetrics.ThroughputMessagesPerSec, clientMetrics.E2eLatencyP99Ms);

            await Task.Delay(500);
        }

        allResults[modeName] = modeResults;
    }

    // 비교 결과 출력
    Log.Information("");
    LogAllModesComparison(connections, iterations, durationSeconds, requestSize, responseSizes, callType, allResults);

    // 결과 저장
    await SaveAllModesResults(outputDir, runTimestamp, label, connections, iterations, durationSeconds, requestSize,
        callType, allResults);
}

/// <summary>
/// 모든 모드 비교 결과 출력
/// </summary>
static void LogAllModesComparison(
    int connections,
    int iterations,
    int durationSeconds,
    int requestSize,
    int[] responseSizes,
    SSCallType callType,
    Dictionary<string, Dictionary<int, SSEchoBenchmarkResult>> allResults)
{
    Log.Information("================================================================================");
    Log.Information("SS Echo Benchmark Results - All Modes Comparison");
    Log.Information("================================================================================");
    if (durationSeconds > 0)
    {
        Log.Information("Config: {Connections:N0} CCU, {Duration}s per mode, CallType: {CallType}",
            connections, durationSeconds, callType);
    }
    else
    {
        Log.Information("Config: {Connections:N0} CCU x {Iterations:N0} msg/conn = {Total:N0} total, CallType: {CallType}",
            connections, iterations, connections * iterations, callType);
    }
    Log.Information("");

    // 테이블 헤더
    Log.Information("{RespSize,8} | {Mode,16} | {Time,6} | {TPS,9} | {P99,8} | {Mem,8} | {GC,10}",
        "RespSize", "Mode", "Time", "TPS", "P99", "Mem", "GC");
    Log.Information("{D1} | {D2} | {D3} | {D4} | {D5} | {D6} | {D7}",
        "--------", "----------------", "------", "---------", "--------", "--------", "----------");

    foreach (var responseSize in responseSizes)
    {
        foreach (var (modeName, modeResults) in allResults)
        {
            if (modeResults.TryGetValue(responseSize, out var result))
            {
                var tps = result.ClientMetrics.ThroughputMessagesPerSec;
                var p99 = result.ClientMetrics.E2eLatencyP99Ms;
                var mem = result.ClientMetrics.MemoryAllocatedMB;
                var gc = $"{result.ClientMetrics.GcGen0Count}/{result.ClientMetrics.GcGen1Count}/{result.ClientMetrics.GcGen2Count}";

                Log.Information("{RespSize,7:N0}B | {Mode,16} | {Time,5:F2}s | {TPS,8:N0}/s | {P99,6:F2}ms | {Mem,6:F1}MB | {GC,10}",
                    responseSize, modeName, result.TotalElapsedSeconds, tps, p99, mem, gc);
            }
        }

        // 모드 간 비교
        if (allResults.Count >= 2 && responseSize != responseSizes.Last())
        {
            Log.Information("{D1} | {D2} | {D3} | {D4} | {D5} | {D6} | {D7}",
                "--------", "----------------", "------", "---------", "--------", "--------", "----------");
        }
    }

    Log.Information("================================================================================");
}

/// <summary>
/// 모든 모드 결과 저장
/// </summary>
static async Task SaveAllModesResults(
    string outputDir,
    DateTime runTimestamp,
    string label,
    int connections,
    int iterations,
    int durationSeconds,
    int requestSize,
    SSCallType callType,
    Dictionary<string, Dictionary<int, SSEchoBenchmarkResult>> allResults)
{
    var timestamp = runTimestamp.ToString("yyyyMMdd_HHmmss");
    var labelSuffix = string.IsNullOrEmpty(label) ? "" : $"_{label}";

    var jsonFileName = $"benchmark_ss_all_modes_{timestamp}{labelSuffix}.json";
    var jsonPath = Path.Combine(outputDir, jsonFileName);

    var jsonResult = new
    {
        Timestamp = runTimestamp.ToString("yyyy-MM-dd HH:mm:ss"),
        Label = label,
        Config = new
        {
            Connections = connections,
            IterationsPerConnection = iterations,
            DurationSeconds = durationSeconds,
            RequestSizeBytes = requestSize,
            CallType = callType.ToString()
        },
        Results = allResults.Select(kv => new
        {
            Mode = kv.Key,
            Results = kv.Value.Select(r => new
            {
                ResponseSizeBytes = r.Key,
                TotalElapsedSeconds = r.Value.TotalElapsedSeconds,
                ThroughputMsgPerSec = r.Value.ClientMetrics.ThroughputMessagesPerSec,
                E2eLatencyP99Ms = r.Value.ClientMetrics.E2eLatencyP99Ms,
                MemoryAllocatedMB = r.Value.ClientMetrics.MemoryAllocatedMB
            }).ToArray()
        }).ToArray()
    };

    var jsonOptions = new JsonSerializerOptions { WriteIndented = true };
    await File.WriteAllTextAsync(jsonPath, JsonSerializer.Serialize(jsonResult, jsonOptions));

    Log.Information("");
    Log.Information("Results saved to:");
    Log.Information("  JSON: {JsonPath}", jsonPath);
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

/// <summary>
/// SS Echo 벤치마크 테스트 결과
/// </summary>
internal class SSEchoBenchmarkResult
{
    public int ResponseSize { get; set; }
    public double TotalElapsedSeconds { get; set; }
    public ClientMetrics ClientMetrics { get; set; } = new();
}
