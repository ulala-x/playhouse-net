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
    description: "Benchmark mode: request-async or send-onreceive",
    getDefaultValue: () => "request-async");

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

var rootCommand = new RootCommand("PlayHouse Benchmark Client")
{
    serverOption,
    connectionsOption,
    messagesOption,
    requestSizeOption,
    responseSizeOption,
    modeOption,
    httpPortOption,
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
    var outputDir = context.ParseResult.GetValueForOption(outputDirOption)!;
    var label = context.ParseResult.GetValueForOption(labelOption)!;

    await RunBenchmarkAsync(server, connections, messages, requestSize, responseSizes, mode, httpPort, outputDir, label);
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
            Path.Combine(outputDir, $"benchmark_{timestamp}{labelSuffix}.log"),
            outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
        .CreateLogger();

    try
    {
        // 모드 파싱
        var benchmarkMode = mode.ToLowerInvariant() switch
        {
            "request-async" => BenchmarkMode.RequestAsync,
            "send-onreceive" => BenchmarkMode.SendOnReceive,
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
        Log.Information("Messages per connection: {Messages:N0}", messages);
        Log.Information("Total messages: {TotalMessages:N0}", connections * messages);
        Log.Information("Request size: {RequestSize:N0} bytes", requestSize);
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
    var jsonFileName = $"benchmark_{timestamp}{labelSuffix}.json";
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
/// 단일 벤치마크 테스트 결과
/// </summary>
internal class BenchmarkResult
{
    public int ResponseSize { get; set; }
    public double TotalElapsedSeconds { get; set; }
    public ServerMetricsResponse? ServerMetrics { get; set; }
    public ClientMetrics ClientMetrics { get; set; } = new();
}
