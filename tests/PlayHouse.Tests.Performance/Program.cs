using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Running;
using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Diagnosers;
using BenchmarkDotNet.Exporters;
using BenchmarkDotNet.Exporters.Csv;
using PlayHouse.Tests.Performance.Benchmarks;
using PlayHouse.Tests.Performance.Infrastructure;

namespace PlayHouse.Tests.Performance;

public class BenchmarkProgram
{
    public static async Task Main(string[] args)
    {
        // 시나리오 B: PlayServer → ApiServer 벤치마크만 실행
        if (args.Contains("--scenario-b") || args.Contains("--play-to-api"))
        {
            var benchmark = new PlayServerToApiBenchmarks();
            await benchmark.RunAsync();
            return;
        }

        // BenchmarkDotNet을 사용한 전체 벤치마크 실행
        var config = DefaultConfig.Instance
            .AddExporter(MarkdownExporter.GitHub)
            .AddExporter(CsvExporter.Default)
            .AddColumn(RankColumn.Arabic)
            .AddColumn(new ThroughputColumn())
            .AddDiagnoser(ThreadingDiagnoser.Default);

        BenchmarkSwitcher.FromAssembly(typeof(BenchmarkProgram).Assembly).Run(args, config);
    }
}
