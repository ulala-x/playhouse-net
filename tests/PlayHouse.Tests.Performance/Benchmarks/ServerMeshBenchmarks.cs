using System.Diagnostics;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Diagnosers;

namespace PlayHouse.Tests.Performance.Benchmarks;

/// <summary>
/// 서버 메시(ZMQ) 통신 성능 측정.
/// 폴링 타임아웃, HWM 설정 등의 영향 분석.
/// </summary>
[MemoryDiagnoser]
[ThreadingDiagnoser]
public class ServerMeshBenchmarks
{
    // 이 벤치마크는 최적화 전후 비교를 위해 수동으로 실행
    // BenchmarkDotNet으로는 서버 내부 타이밍을 정확히 측정하기 어려움

    /// <summary>
    /// 수동 RTT 측정용 헬퍼 메서드.
    /// 벤치마크가 아닌 수동 테스트에서 사용.
    /// </summary>
    public static async Task<double> MeasureRTT(Func<Task> action, int iterations = 100)
    {
        var sw = new Stopwatch();
        var times = new List<double>(iterations);

        // Warmup
        for (int i = 0; i < 10; i++)
        {
            await action();
        }

        // Measure
        for (int i = 0; i < iterations; i++)
        {
            sw.Restart();
            await action();
            sw.Stop();
            times.Add(sw.Elapsed.TotalMilliseconds);
        }

        // 통계 (통계적 유의성 분석)
        times.Sort();
        var median = times[times.Count / 2];
        var p95 = times[(int)(times.Count * 0.95)];
        var p99 = times[(int)(times.Count * 0.99)];
        var avg = times.Average();
        var stdDev = Math.Sqrt(times.Select(t => Math.Pow(t - avg, 2)).Average());
        var cv = stdDev / avg; // Coefficient of Variation

        Console.WriteLine($"RTT Statistics (ms):");
        Console.WriteLine($"  Average: {avg:F2}");
        Console.WriteLine($"  Median:  {median:F2}");
        Console.WriteLine($"  P95:     {p95:F2}");
        Console.WriteLine($"  P99:     {p99:F2}");
        Console.WriteLine($"  Min:     {times.Min():F2}");
        Console.WriteLine($"  Max:     {times.Max():F2}");
        Console.WriteLine($"  StdDev:  {stdDev:F2}");
        Console.WriteLine($"  CV:      {cv:F2} (< 0.1 = good consistency)");

        // 통계적 유의성 기준
        if (cv > 0.2)
        {
            Console.WriteLine($"  [Warning] High variability detected (CV > 0.2). Increase iterations or check system load.");
        }

        return avg;
    }
}
