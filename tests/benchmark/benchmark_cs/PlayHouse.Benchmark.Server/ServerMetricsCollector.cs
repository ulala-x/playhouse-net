using System.Diagnostics;

namespace PlayHouse.Benchmark.Server;

/// <summary>
/// 서버 측 성능 메트릭을 수집합니다.
/// </summary>
public class ServerMetricsCollector
{
    public static readonly ServerMetricsCollector Instance = new();

    private readonly object _lock = new();
    private readonly List<double> _latencies = new();
    private long _processedMessages;
    private long _totalBytes;
    private readonly Stopwatch _stopwatch = Stopwatch.StartNew();
    private int _gcGen0Start;
    private int _gcGen1Start;
    private int _gcGen2Start;
    private long _memoryStart;

    private ServerMetricsCollector()
    {
        Reset();
    }

    public void Reset()
    {
        lock (_lock)
        {
            _latencies.Clear();
            _processedMessages = 0;
            _totalBytes = 0;
            _stopwatch.Restart();
            _gcGen0Start = GC.CollectionCount(0);
            _gcGen1Start = GC.CollectionCount(1);
            _gcGen2Start = GC.CollectionCount(2);
            _memoryStart = GC.GetTotalAllocatedBytes(precise: false);
        }
    }

    /// <summary>
    /// 메시지 처리 완료 시 호출
    /// </summary>
    public void RecordMessage(long elapsedTicks, int messageSize)
    {
        var latencyMs = (double)elapsedTicks / Stopwatch.Frequency * 1000;

        lock (_lock)
        {
            _latencies.Add(latencyMs);
            Interlocked.Increment(ref _processedMessages);
            Interlocked.Add(ref _totalBytes, messageSize);
        }
    }

    /// <summary>
    /// 현재 통계를 반환합니다.
    /// </summary>
    public ServerMetrics GetMetrics()
    {
        lock (_lock)
        {
            var elapsedSeconds = _stopwatch.Elapsed.TotalSeconds;
            var sortedLatencies = _latencies.OrderBy(x => x).ToArray();

            return new ServerMetrics
            {
                ProcessedMessages = _processedMessages,
                ThroughputMessagesPerSec = elapsedSeconds > 0 ? _processedMessages / elapsedSeconds : 0,
                ThroughputMBPerSec = elapsedSeconds > 0 ? (_totalBytes / 1024.0 / 1024.0) / elapsedSeconds : 0,
                LatencyMeanMs = sortedLatencies.Length > 0 ? sortedLatencies.Average() : 0,
                LatencyP50Ms = GetPercentile(sortedLatencies, 0.50),
                LatencyP95Ms = GetPercentile(sortedLatencies, 0.95),
                LatencyP99Ms = GetPercentile(sortedLatencies, 0.99),
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
/// 서버 메트릭 스냅샷
/// </summary>
public record ServerMetrics
{
    public long ProcessedMessages { get; init; }
    public double ThroughputMessagesPerSec { get; init; }
    public double ThroughputMBPerSec { get; init; }
    public double LatencyMeanMs { get; init; }
    public double LatencyP50Ms { get; init; }
    public double LatencyP95Ms { get; init; }
    public double LatencyP99Ms { get; init; }
    public double MemoryAllocatedMB { get; init; }
    public int GcGen0Count { get; init; }
    public int GcGen1Count { get; init; }
    public int GcGen2Count { get; init; }
}
