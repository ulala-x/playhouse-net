using System.Diagnostics;

namespace PlayHouse.Benchmark.SS.Client;

/// <summary>
/// 클라이언트 측 성능 메트릭을 수집합니다.
/// SS Latency (Server-to-Server)와 E2E Latency (End-to-End)를 모두 추적합니다.
/// </summary>
public class ClientMetricsCollector
{
    private readonly object _lock = new();
    private readonly List<double> _e2eLatencies = new();
    private readonly List<double> _ssLatencies = new();
    private long _sentMessages;
    private long _receivedMessages;
    private readonly Stopwatch _stopwatch = Stopwatch.StartNew();
    private int _gcGen0Start;
    private int _gcGen1Start;
    private int _gcGen2Start;
    private long _memoryStart;

    public ClientMetricsCollector()
    {
        Reset();
    }

    public void Reset()
    {
        lock (_lock)
        {
            _e2eLatencies.Clear();
            _ssLatencies.Clear();
            _sentMessages = 0;
            _receivedMessages = 0;
            _stopwatch.Restart();
            _gcGen0Start = GC.CollectionCount(0);
            _gcGen1Start = GC.CollectionCount(1);
            _gcGen2Start = GC.CollectionCount(2);
            _memoryStart = GC.GetTotalAllocatedBytes(precise: false);
        }
    }

    public void RecordSent()
    {
        Interlocked.Increment(ref _sentMessages);
    }

    /// <summary>
    /// 메시지 수신을 기록합니다.
    /// </summary>
    /// <param name="e2eElapsedTicks">E2E Latency (클라이언트가 측정한 전체 왕복 시간)</param>
    /// <param name="ssElapsedTicks">SS Latency (서버가 응답에 포함한 서버간 통신 시간)</param>
    public void RecordReceived(long e2eElapsedTicks, long ssElapsedTicks)
    {
        var e2eLatencyMs = (double)e2eElapsedTicks / Stopwatch.Frequency * 1000;
        var ssLatencyMs = (double)ssElapsedTicks / Stopwatch.Frequency * 1000;

        lock (_lock)
        {
            _e2eLatencies.Add(e2eLatencyMs);
            _ssLatencies.Add(ssLatencyMs);
            Interlocked.Increment(ref _receivedMessages);
        }
    }

    /// <summary>
    /// 현재 통계를 반환합니다.
    /// </summary>
    public ClientMetrics GetMetrics()
    {
        lock (_lock)
        {
            var elapsedSeconds = _stopwatch.Elapsed.TotalSeconds;
            var sortedE2eLatencies = _e2eLatencies.OrderBy(x => x).ToArray();
            var sortedSsLatencies = _ssLatencies.OrderBy(x => x).ToArray();

            return new ClientMetrics
            {
                SentMessages = _sentMessages,
                ReceivedMessages = _receivedMessages,
                ThroughputMessagesPerSec = elapsedSeconds > 0 ? _receivedMessages / elapsedSeconds : 0,

                // E2E Latency (클라이언트 측정)
                E2eLatencyMeanMs = sortedE2eLatencies.Length > 0 ? sortedE2eLatencies.Average() : 0,
                E2eLatencyP50Ms = GetPercentile(sortedE2eLatencies, 0.50),
                E2eLatencyP95Ms = GetPercentile(sortedE2eLatencies, 0.95),
                E2eLatencyP99Ms = GetPercentile(sortedE2eLatencies, 0.99),

                // SS Latency (서버간 통신)
                SsLatencyMeanMs = sortedSsLatencies.Length > 0 ? sortedSsLatencies.Average() : 0,
                SsLatencyP50Ms = GetPercentile(sortedSsLatencies, 0.50),
                SsLatencyP95Ms = GetPercentile(sortedSsLatencies, 0.95),
                SsLatencyP99Ms = GetPercentile(sortedSsLatencies, 0.99),

                MemoryAllocatedMB = (GC.GetTotalAllocatedBytes(precise: false) - _memoryStart) / 1024.0 / 1024.0,
                GcGen0Count = GC.CollectionCount(0) - _gcGen0Start,
                GcGen1Count = GC.CollectionCount(1) - _gcGen1Start,
                GcGen2Count = GC.CollectionCount(2) - _gcGen2Start
            };
        }
    }

    private static double GetPercentile(double[] sortedValues, double percentile)
    {
        if (sortedValues.Length == 0) return 0;

        var index = (int)Math.Ceiling(sortedValues.Length * percentile) - 1;
        index = Math.Max(0, Math.Min(sortedValues.Length - 1, index));
        return sortedValues[index];
    }
}

/// <summary>
/// 클라이언트 메트릭 스냅샷
/// </summary>
public record ClientMetrics
{
    public long SentMessages { get; init; }
    public long ReceivedMessages { get; init; }
    public double ThroughputMessagesPerSec { get; init; }

    // E2E Latency (End-to-End: 클라이언트가 측정한 전체 왕복 시간)
    public double E2eLatencyMeanMs { get; init; }
    public double E2eLatencyP50Ms { get; init; }
    public double E2eLatencyP95Ms { get; init; }
    public double E2eLatencyP99Ms { get; init; }

    // SS Latency (Server-to-Server: 서버가 응답에 포함한 서버간 통신 시간)
    public double SsLatencyMeanMs { get; init; }
    public double SsLatencyP50Ms { get; init; }
    public double SsLatencyP95Ms { get; init; }
    public double SsLatencyP99Ms { get; init; }

    public double MemoryAllocatedMB { get; init; }
    public int GcGen0Count { get; init; }
    public int GcGen1Count { get; init; }
    public int GcGen2Count { get; init; }
}
