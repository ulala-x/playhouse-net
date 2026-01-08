using System.CommandLine;
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

var durationOption = new Option<int>(
    name: "--duration",
    description: "Duration in seconds",
    getDefaultValue: () => 10);

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

var maxInFlightOption = new Option<int>(
    name: "--max-inflight",
    description: "Maximum in-flight requests (default: 200)",
    getDefaultValue: () => 200);

var rootCommand = new RootCommand("PlayHouse Server-to-Server Benchmark Client")
{
    serverOption,
    connectionsOption,
    durationOption,
    messageSizeOption,
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
    createStagesOnlyOption,
    maxInFlightOption
};

rootCommand.SetHandler(async (context) =>
{
    var server = context.ParseResult.GetValueForOption(serverOption)!;
    var connections = context.ParseResult.GetValueForOption(connectionsOption);
    var duration = context.ParseResult.GetValueForOption(durationOption);
    var messageSize = context.ParseResult.GetValueForOption(messageSizeOption);
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
    var maxInFlight = context.ParseResult.GetValueForOption(maxInFlightOption);

    if (createStagesOnly)
    {
        await CreateStagesAsync(server, connections, stageId, targetStageId, targetNid);
    }
    else
    {
        await RunBenchmarkAsync(server, connections, duration, messageSize, responseSizes, mode, commMode, callType, httpPort, apiHttpPort, stageId, targetStageId, targetNid, outputDir, label, maxInFlight);
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
    int durationSeconds,
    int messageSize,
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
        // Only ss-echo mode is supported (old modes removed)
        await RunSSEchoBenchmarkAsync(host, port, connections, durationSeconds, messageSize, responseSizesStr,
            commModeStr, callTypeStr, targetStageId, targetNid, outputDir, runTimestamp, label, httpPort, apiHttpPort, maxInFlight);
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

/// <summary>
/// SS Echo 벤치마크 실행 (TriggerSSEchoRequest 사용)
/// </summary>
static async Task RunSSEchoBenchmarkAsync(
    string host,
    int port,
    int connections,
    int durationSeconds,
    int messageSize,
    string responseSizesStr,
    string commModeStr,
    string callTypeStr,
    long targetStageId,
    string targetNid,
    string outputDir,
    DateTime runTimestamp,
    string label,
    int httpPort,
    int apiHttpPort,
    int maxInFlight)
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
        await RunAllCommModesAsync(host, port, connections, durationSeconds, messageSize, responseSizes,
            callType, targetStageId, targetNid, outputDir, runTimestamp, label, httpPort, apiHttpPort, maxInFlight);
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
    Log.Information("Duration: {Duration:N0} seconds", durationSeconds);
    Log.Information("Message size: {RequestSize:N0} bytes", messageSize);
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

    var serverMetricsClient = new ServerMetricsClient(host, httpPort);
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

        // 서버 메트릭 리셋
        Log.Information("Resetting server metrics...");
        await serverMetricsClient.ResetMetricsAsync();
        await Task.Delay(1000);

        // 벤치마크 실행
        var runner = new SSEchoBenchmarkRunner(
            serverHost: host,
            serverPort: port,
            connections: connections,
            messageSize: messageSize,
            commMode: commMode,
            targetStageId: targetStageId,
            targetNid: targetNid,
            durationSeconds: durationSeconds,
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
        allResults.Add(new SSEchoBenchmarkResult
        {
            ResponseSize = responseSize,
            TotalElapsedSeconds = totalElapsed,
            ServerMetrics = serverMetrics,
            ClientMetrics = clientMetrics
        });

        Log.Information("  Success: {Sent}/{Total}, TPS: {TPS:N0}/s, P99: {P99:F2}ms",
            clientMetrics.ReceivedMessages, clientMetrics.SentMessages,
            clientMetrics.ThroughputMessagesPerSec, clientMetrics.E2eLatencyP99Ms);

        await Task.Delay(500); // 잠시 대기
    }

    // 비교 결과 출력
    Log.Information("");
    LogSSEchoResults(connections, durationSeconds, messageSize, responseSizes, commMode, callType, allResults);

    // 결과 파일 저장
    await SaveSSEchoResults(outputDir, runTimestamp, label, connections, durationSeconds, messageSize,
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
    int durationSeconds,
    int messageSize,
    int[] responseSizes,
    SSCommMode commMode,
    SSCallType callType,
    List<SSEchoBenchmarkResult> results)
{
    Log.Information("================================================================================");
    Log.Information("SS Echo Benchmark Results");
    Log.Information("================================================================================");
        Log.Information("Config: {Connections:N0} connections, {Duration:N0} seconds duration",
            connections, durationSeconds);
        Log.Information("        Request: {RequestSize:N0}B, CommMode: {CommMode}, CallType: {CallType}",
            messageSize, commMode, callType);
        Log.Information("");
    
        // 테이블 헤더 (C-S 벤치마크와 동일한 형식)
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
            
            // SS 벤치마크에서는 Cli RTT가 E2E Latency임
            var cliRtt = result.ClientMetrics.E2eLatencyP99Ms;
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
    int durationSeconds,
    int messageSize,
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
            DurationSeconds = durationSeconds,
            RequestSizeBytes = messageSize,
            CommMode = commMode.ToString(),
            CallType = callType.ToString(),
            TargetStageId = targetStageId,
            TargetNid = targetNid
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

    Log.Information("");
    Log.Information("Results saved to:");
    Log.Information("  JSON: {JsonPath}", jsonPath);
}

/// <summary>
/// 모든 CommMode에 대해 벤치마크 실행 (RequestAsync, RequestCallback, Send)
/// </summary>
static async Task RunAllCommModesAsync(
    string host,
    int port,
    int connections,
    int durationSeconds,
    int messageSize,
    int[] responseSizes,
    SSCallType callType,
    long targetStageId,
    string targetNid,
    string outputDir,
    DateTime runTimestamp,
    string label,
    int httpPort,
    int apiHttpPort,
    int maxInFlight)
{
    var commModes = new[]
    {
        (SSCommMode.RequestAsync, "RequestAsync"),
        (SSCommMode.RequestCallback, "RequestCallback"),
        (SSCommMode.Send, "Send")
    };

    var serverMetricsClient = new ServerMetricsClient(host, httpPort);
    var allResults = new Dictionary<string, Dictionary<int, SSEchoBenchmarkResult>>();

    // 배너 출력
    Log.Information("================================================================================");
    Log.Information("PlayHouse SS Echo Benchmark - All Modes Comparison");
    Log.Information("================================================================================");
    Log.Information("Server: {Host}:{Port}", host, port);
    Log.Information("Connections: {Connections:N0}", connections);
    Log.Information("Duration: {Duration:N0} seconds per mode/size", durationSeconds);
    Log.Information("Message size: {RequestSize:N0} bytes", messageSize);
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

            // 서버 메트릭 리셋
            Log.Information("Resetting server metrics...");
            await serverMetricsClient.ResetMetricsAsync();
            await Task.Delay(500);

            var runner = new SSEchoBenchmarkRunner(
                serverHost: host,
                serverPort: port,
                connections: connections,
                messageSize: messageSize,
                commMode: commMode,
                targetStageId: targetStageId,
                targetNid: targetNid,
                durationSeconds: durationSeconds,
                maxInFlight: maxInFlight);

            var startTime = DateTime.Now;
            await runner.RunAsync();
            var endTime = DateTime.Now;
            var totalElapsed = (endTime - startTime).TotalSeconds;

            Log.Information("Waiting for server metrics to stabilize...");
            await Task.Delay(1000);

            var serverMetrics = await serverMetricsClient.GetMetricsAsync();
            var clientMetrics = clientMetricsCollector.GetMetrics();

            modeResults[responseSize] = new SSEchoBenchmarkResult
            {
                ResponseSize = responseSize,
                TotalElapsedSeconds = totalElapsed,
                ServerMetrics = serverMetrics,
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
    LogAllModesComparison(connections, durationSeconds, messageSize, responseSizes, callType, allResults);

    // 결과 저장
    await SaveAllModesResults(outputDir, runTimestamp, label, connections, durationSeconds, messageSize,
        callType, allResults);
}

/// <summary>
/// 모든 모드 비교 결과 출력
/// </summary>
static void LogAllModesComparison(
    int connections,
    int durationSeconds,
    int messageSize,
    int[] responseSizes,
    SSCallType callType,
    Dictionary<string, Dictionary<int, SSEchoBenchmarkResult>> allResults)
{
    Log.Information("================================================================================");
    Log.Information("SS Echo Benchmark Results - All Modes Comparison");
    Log.Information("================================================================================");
    Log.Information("Config: {Connections:N0} CCU, {Duration:N0}s per mode, CallType: {CallType}",
        connections, durationSeconds, callType);
    Log.Information("");

    // 테이블 헤더
    Log.Information("{RespSize,8} | {Mode,16} | {Time,6} | {SrvTPS,9} | {SrvP99,8} | {SrvMem,8} | {SrvGC,10} | {CliRTT,8} | {CliTPS,9} | {CliMem,8} | {CliGC,10}",
        "RespSize", "Mode", "Time", "Srv TPS", "Srv P99", "Srv Mem", "Srv GC", "Cli RTT", "Cli TPS", "Cli Mem", "Cli GC");
    Log.Information("{D1} | {D2} | {D3} | {D4} | {D5} | {D6} | {D7} | {D8} | {D9} | {D10} | {D11}",
        "--------", "----------------", "------", "---------", "--------", "--------", "----------", "--------", "---------", "--------", "----------");

    foreach (var responseSize in responseSizes)
    {
        foreach (var (modeName, modeResults) in allResults)
        {
            if (modeResults.TryGetValue(responseSize, out var result))
            {
                var srvTps = result.ServerMetrics?.ThroughputMessagesPerSec ?? 0;
                var srvP99 = result.ServerMetrics?.LatencyP99Ms ?? 0;
                var srvMem = result.ServerMetrics?.MemoryAllocatedMb ?? 0;
                var srvGc = result.ServerMetrics != null
                    ? $"{result.ServerMetrics.GcGen0Count}/{result.ServerMetrics.GcGen1Count}/{result.ServerMetrics.GcGen2Count}"
                    : "-";
                
                var cliRtt = result.ClientMetrics.E2eLatencyP99Ms;
                var cliTps = result.ClientMetrics.ThroughputMessagesPerSec;
                var cliMem = result.ClientMetrics.MemoryAllocatedMB;
                var cliGc = $"{result.ClientMetrics.GcGen0Count}/{result.ClientMetrics.GcGen1Count}/{result.ClientMetrics.GcGen2Count}";

                Log.Information("{RespSize,7:N0}B | {Mode,16} | {Time,5:F2}s | {SrvTPS,8:N0}/s | {SrvP99,6:F2}ms | {SrvMem,6:F1}MB | {SrvGC,10} | {CliRTT,6:F2}ms | {CliTPS,8:N0}/s | {CliMem,6:F1}MB | {CliGC,10}",
                    responseSize, modeName, result.TotalElapsedSeconds, srvTps, srvP99, srvMem, srvGc, cliRtt, cliTps, cliMem, cliGc);
            }
        }

        // 모드 간 비교
        if (allResults.Count >= 2 && responseSize != responseSizes.Last())
        {
            Log.Information("{D1} | {D2} | {D3} | {D4} | {D5} | {D6} | {D7} | {D8} | {D9} | {D10} | {D11}",
                "--------", "----------------", "------", "---------", "--------", "--------", "----------", "--------", "---------", "--------", "----------");
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
    int durationSeconds,
    int messageSize,
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
            DurationSeconds = durationSeconds,
            RequestSizeBytes = messageSize,
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
/// SS Echo 벤치마크 테스트 결과
/// </summary>
internal class SSEchoBenchmarkResult
{
    public int ResponseSize { get; set; }
    public double TotalElapsedSeconds { get; set; }
    public ServerMetricsResponse? ServerMetrics { get; set; }
    public ClientMetrics ClientMetrics { get; set; } = new();
}
