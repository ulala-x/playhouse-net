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
    description: "Number of messages per connection",
    getDefaultValue: () => 10000);

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
    description: "Benchmark mode: play-to-api, play-to-stage, stage-to-api, stage-to-stage, api-to-api, or all",
    getDefaultValue: () => "all");

var httpPortOption = new Option<int>(
    name: "--http-port",
    description: "Server HTTP API port for metrics",
    getDefaultValue: () => 5080);

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

var rootCommand = new RootCommand("PlayHouse Server-to-Server Benchmark Client")
{
    serverOption,
    connectionsOption,
    messagesOption,
    requestSizeOption,
    responseSizeOption,
    modeOption,
    httpPortOption,
    stageIdOption,
    targetStageIdOption,
    targetNidOption,
    outputDirOption,
    labelOption
};

rootCommand.SetHandler(async (context) =>
{
    var server = context.ParseResult.GetValueForOption(serverOption)!;
    var connections = context.ParseResult.GetValueForOption(connectionsOption);
    var messages = context.ParseResult.GetValueForOption(messagesOption);
    var requestSize = context.ParseResult.GetValueForOption(requestSizeOption);
    var responseSizes = context.ParseResult.GetValueForOption(responseSizeOption)!;
    var mode = context.ParseResult.GetValueForOption(modeOption)!;
    var httpPort = context.ParseResult.GetValueForOption(httpPortOption);
    var stageId = context.ParseResult.GetValueForOption(stageIdOption);
    var targetStageId = context.ParseResult.GetValueForOption(targetStageIdOption);
    var targetNid = context.ParseResult.GetValueForOption(targetNidOption)!;
    var outputDir = context.ParseResult.GetValueForOption(outputDirOption)!;
    var label = context.ParseResult.GetValueForOption(labelOption)!;

    await RunBenchmarkAsync(server, connections, messages, requestSize, responseSizes, mode, httpPort, stageId, targetStageId, targetNid, outputDir, label);
});

return await rootCommand.InvokeAsync(args);

static async Task RunBenchmarkAsync(
    string server,
    int connections,
    int messages,
    int requestSize,
    string responseSizesStr,
    string mode,
    int httpPort,
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
                benchmarkMode, targetStageId, targetNid, outputDir, runTimestamp, label);
            return;
        }

        // 구 방식 (PlayToApi, PlayToStage)
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
                clientMetricsCollector);

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

        // 통합 결과 출력
        Log.Information("");
        LogResults(connections, messages, requestSize, responseSizes, benchmarkMode, allResults);

        // 결과 파일 저장
        await SaveResultsToFile(outputDir, runTimestamp, label, connections, messages, requestSize, benchmarkMode, allResults);
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
    string label)
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

    if (mode == BenchmarkMode.All)
    {
        // 3가지 호출 유형 x 2가지 통신 모드 = 6가지 테스트
        testCases.Add((SSCallType.StageToApi, SSCommMode.RequestAsync, "Stage → API (RequestAsync)"));
        testCases.Add((SSCallType.StageToApi, SSCommMode.RequestCallback, "Stage → API (RequestCallback)"));
        testCases.Add((SSCallType.StageToStage, SSCommMode.RequestAsync, "Stage → Stage (RequestAsync)"));
        testCases.Add((SSCallType.StageToStage, SSCommMode.RequestCallback, "Stage → Stage (RequestCallback)"));
        testCases.Add((SSCallType.ApiToApi, SSCommMode.RequestAsync, "API → API (RequestAsync)"));
        testCases.Add((SSCallType.ApiToApi, SSCommMode.RequestCallback, "API → API (RequestCallback)"));
    }
    else if (mode == BenchmarkMode.StageToApi)
    {
        testCases.Add((SSCallType.StageToApi, SSCommMode.RequestAsync, "Stage → API (RequestAsync)"));
        testCases.Add((SSCallType.StageToApi, SSCommMode.RequestCallback, "Stage → API (RequestCallback)"));
    }
    else if (mode == BenchmarkMode.StageToStage)
    {
        testCases.Add((SSCallType.StageToStage, SSCommMode.RequestAsync, "Stage → Stage (RequestAsync)"));
        testCases.Add((SSCallType.StageToStage, SSCommMode.RequestCallback, "Stage → Stage (RequestCallback)"));
    }
    else if (mode == BenchmarkMode.ApiToApi)
    {
        testCases.Add((SSCallType.ApiToApi, SSCommMode.RequestAsync, "API → API (RequestAsync)"));
        testCases.Add((SSCallType.ApiToApi, SSCommMode.RequestCallback, "API → API (RequestCallback)"));
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
/// 단일 벤치마크 테스트 결과
/// </summary>
internal class BenchmarkResult
{
    public int ResponseSize { get; set; }
    public double TotalElapsedSeconds { get; set; }
    public ServerMetricsResponse? ServerMetrics { get; set; }
    public ClientMetrics ClientMetrics { get; set; } = new();
}
