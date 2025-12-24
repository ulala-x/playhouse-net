using System.Diagnostics;

namespace PlayHouse.Benchmark.Client;

/// <summary>
/// 클라이언트 측 성능 메트릭을 수집합니다.
/// </summary>
public class ClientMetricsCollector
{
    private readonly object _lock = new();
    private readonly List<double> _rttLatencies = new();
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
            _rttLatencies.Clear();
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

    public void RecordReceived(long elapsedTicks)
    {
        var rttMs = (double)elapsedTicks / Stopwatch.Frequency * 1000;

        lock (_lock)
        {
            _rttLatencies.Add(rttMs);
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
            var sortedLatencies = _rttLatencies.OrderBy(x => x).ToArray();

            return new ClientMetrics
            {
                SentMessages = _sentMessages,
                ReceivedMessages = _receivedMessages,
                ThroughputMessagesPerSec = elapsedSeconds > 0 ? _receivedMessages / elapsedSeconds : 0,
                RttLatencyMeanMs = sortedLatencies.Length > 0 ? sortedLatencies.Average() : 0,
                RttLatencyP50Ms = GetPercentile(sortedLatencies, 0.50),
                RttLatencyP95Ms = GetPercentile(sortedLatencies, 0.95),
                RttLatencyP99Ms = GetPercentile(sortedLatencies, 0.99),
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
    public double RttLatencyMeanMs { get; init; }
    public double RttLatencyP50Ms { get; init; }
    public double RttLatencyP95Ms { get; init; }
    public double RttLatencyP99Ms { get; init; }
    public double MemoryAllocatedMB { get; init; }
    public int GcGen0Count { get; init; }
    public int GcGen1Count { get; init; }
    public int GcGen2Count { get; init; }
}
