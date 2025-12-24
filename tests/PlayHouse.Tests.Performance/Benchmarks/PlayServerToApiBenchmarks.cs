using System.Diagnostics;
using PlayHouse.Bootstrap;
using PlayHouse.Connector;
using PlayHouse.Tests.Integration.Proto;
using PlayHouse.Tests.Performance.Infrastructure;
using ClientConnector = PlayHouse.Connector.Connector;
using ClientPacket = PlayHouse.Connector.Protocol.Packet;

namespace PlayHouse.Tests.Performance.Benchmarks;

/// <summary>
/// 시나리오 B: PlayServer(Stage) → ApiServer 구간 성능 측정.
///
/// 측정 방식:
/// - Client → Stage → API → Stage → Client 전체 플로우를 실행하되,
/// - Stage 내부에서 RequestToApi() 구간만 측정하여 응답에 포함
/// - 측정 데이터: Latency(ElapsedTicks), Memory, GC
///
/// 측정 항목:
/// - Latency: Mean, P50, P95, P99, StdDev (ms)
/// - Throughput: msg/sec, MB/s
/// - Memory: 요청당 할당량, 총 할당량
/// - GC: Gen0/Gen1/Gen2 수집 횟수
///
/// 메시지 사이즈: 1KB, 64KB, 128KB, 256KB (API Response 페이로드)
/// </summary>
public class PlayServerToApiBenchmarks
{
    private PlayServer _playServer = null!;
    private ApiServer _apiServer = null!;
    private ClientConnector _connector = null!;
    private Timer? _callbackTimer;

    private const int WarmupIterations = 100;
    private const int MeasureIterations = 10000;

    public async Task SetupAsync()
    {
        var (playServer, apiServer) = BenchmarkServerFixture.CreatePlayToApiFixture();
        _playServer = playServer;
        _apiServer = apiServer;

        await _apiServer.StartAsync();
        await _playServer.StartAsync();

        // 서버 메시 연결 대기 (ServerAddressResolver가 서버 간 연결을 설정할 시간 필요)
        Console.WriteLine("Waiting for server mesh connection...");
        await Task.Delay(5000);

        _connector = new ClientConnector();
        _connector.Init(new ConnectorConfig { RequestTimeoutMs = 30000 });
        _connector.Connect("127.0.0.1", 16210, 1000);

        // 콜백 타이머 시작 (20ms 간격)
        _callbackTimer = new Timer(_ => _connector.MainThreadAction(), null, 0, 20);

        await Task.Delay(100);

        // 인증 (Stage 생성)
        var authRequest = new AuthenticateRequest { UserId = "bench-user", Token = "token" };
        await _connector.RequestAsync(new ClientPacket(authRequest));
    }

    public async Task CleanupAsync()
    {
        _callbackTimer?.Dispose();
        _callbackTimer = null;

        _connector?.Disconnect();

        if (_playServer != null)
            await _playServer.DisposeAsync();

        if (_apiServer != null)
            await _apiServer.DisposeAsync();
    }

    /// <summary>
    /// 전체 벤치마크 실행
    /// </summary>
    public async Task RunAsync()
    {
        await SetupAsync();

        try
        {
            Console.WriteLine("=".PadRight(80, '='));
            Console.WriteLine("PlayServer → ApiServer Benchmark");
            Console.WriteLine("=".PadRight(80, '='));
            Console.WriteLine();

            var responseSizes = new[] { 1024, 65536, 131072, 262144 };

            foreach (var responseSize in responseSizes)
            {
                await RunBenchmarkForResponseSize(responseSize);
                Console.WriteLine();
            }
        }
        finally
        {
            await CleanupAsync();
        }
    }

    private async Task RunBenchmarkForResponseSize(int responseSize)
    {
        Console.WriteLine($"Response Size: {FormatBytes(responseSize)}");
        Console.WriteLine("-".PadRight(80, '-'));

        // Warmup - 단일 요청 (count=100)
        Console.Write("Warming up... ");
        var warmupRequest = new TriggerBenchmarkApiRequest
        {
            Sequence = 0,
            ResponseSize = responseSize,
            Count = WarmupIterations
        };
        await _connector.RequestAsync(new ClientPacket(warmupRequest));
        Console.WriteLine($"Done ({WarmupIterations} iterations)");

        // GC 정리
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        await Task.Delay(500);

        // Measurement - 단일 요청 (count=1000)
        Console.Write($"Measuring... ");
        var totalStopwatch = Stopwatch.StartNew();

        var request = new TriggerBenchmarkApiRequest
        {
            Sequence = 1,
            ResponseSize = responseSize,
            Count = MeasureIterations
        };

        var response = await _connector.RequestAsync(new ClientPacket(request));
        var reply = TriggerBenchmarkApiReply.Parser.ParseFrom(response.Payload.Data.Span);

        totalStopwatch.Stop();
        Console.WriteLine($"Done ({MeasureIterations} iterations)");
        Console.WriteLine();

        // Stage에서 수집한 측정 데이터로 통계 계산
        var measurements = new List<MeasurementData>(reply.ElapsedTicksList.Count);
        for (int i = 0; i < reply.ElapsedTicksList.Count; i++)
        {
            measurements.Add(new MeasurementData
            {
                ElapsedTicks = reply.ElapsedTicksList[i],
                MemoryAllocated = reply.MemoryAllocatedList[i],
                GcGen0Count = reply.GcGen0Count,
                GcGen1Count = reply.GcGen1Count,
                GcGen2Count = reply.GcGen2Count,
                PayloadSize = responseSize
            });
        }

        // 통계 계산 및 출력
        PrintStatistics(measurements, totalStopwatch.Elapsed, responseSize);
    }

    private void PrintStatistics(List<MeasurementData> measurements, TimeSpan totalElapsed, int responseSize)
    {
        // Latency 계산 (Stopwatch Ticks → ms)
        var latenciesMs = measurements
            .Select(m => (double)m.ElapsedTicks * 1000.0 / Stopwatch.Frequency)
            .OrderBy(x => x)
            .ToList();

        var meanLatency = latenciesMs.Average();
        var p50Latency = Percentile(latenciesMs, 0.50);
        var p95Latency = Percentile(latenciesMs, 0.95);
        var p99Latency = Percentile(latenciesMs, 0.99);
        var stdDevLatency = StandardDeviation(latenciesMs);

        // Throughput 계산
        var totalMessages = measurements.Count;
        var totalSeconds = totalElapsed.TotalSeconds;
        var messagesPerSecond = totalMessages / totalSeconds;
        var totalBytes = measurements.Sum(m => (long)m.PayloadSize);
        var bytesPerSecond = totalBytes / totalSeconds;

        // Memory 계산
        var totalMemory = measurements.Sum(m => m.MemoryAllocated);
        var avgMemoryPerRequest = totalMemory / (double)measurements.Count;

        // GC 계산
        var totalGcGen0 = measurements.Sum(m => m.GcGen0Count);
        var totalGcGen1 = measurements.Sum(m => m.GcGen1Count);
        var totalGcGen2 = measurements.Sum(m => m.GcGen2Count);

        // 출력
        Console.WriteLine($"  Latency:");
        Console.WriteLine($"    Mean   : {meanLatency:F3} ms");
        Console.WriteLine($"    P50    : {p50Latency:F3} ms");
        Console.WriteLine($"    P95    : {p95Latency:F3} ms");
        Console.WriteLine($"    P99    : {p99Latency:F3} ms");
        Console.WriteLine($"    StdDev : {stdDevLatency:F3} ms");
        Console.WriteLine();

        Console.WriteLine($"  Throughput:");
        Console.WriteLine($"    Messages : {messagesPerSecond:F1} msg/s");
        Console.WriteLine($"    Data     : {FormatBytes((long)bytesPerSecond)}/s");
        Console.WriteLine();

        Console.WriteLine($"  Memory:");
        Console.WriteLine($"    Per Request : {FormatBytes((long)avgMemoryPerRequest)}");
        Console.WriteLine($"    Total       : {FormatBytes(totalMemory)}");
        Console.WriteLine();

        Console.WriteLine($"  GC:");
        Console.WriteLine($"    Gen0 : {totalGcGen0}");
        Console.WriteLine($"    Gen1 : {totalGcGen1}");
        Console.WriteLine($"    Gen2 : {totalGcGen2}");
    }

    private static double Percentile(List<double> sortedValues, double percentile)
    {
        if (sortedValues.Count == 0)
            return 0;

        var index = (int)Math.Ceiling(percentile * sortedValues.Count) - 1;
        index = Math.Max(0, Math.Min(sortedValues.Count - 1, index));
        return sortedValues[index];
    }

    private static double StandardDeviation(List<double> values)
    {
        if (values.Count < 2)
            return 0;

        var mean = values.Average();
        var variance = values.Sum(v => Math.Pow(v - mean, 2)) / values.Count;
        return Math.Sqrt(variance);
    }

    private static string FormatBytes(long bytes)
    {
        string[] sizes = { "B", "KB", "MB", "GB" };
        double len = bytes;
        int order = 0;

        while (len >= 1024 && order < sizes.Length - 1)
        {
            order++;
            len /= 1024;
        }

        return $"{len:F2} {sizes[order]}";
    }

    private class MeasurementData
    {
        public long ElapsedTicks { get; set; }
        public long MemoryAllocated { get; set; }
        public int GcGen0Count { get; set; }
        public int GcGen1Count { get; set; }
        public int GcGen2Count { get; set; }
        public int PayloadSize { get; set; }
    }
}
