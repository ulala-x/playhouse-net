using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Running;
using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Exporters;
using BenchmarkDotNet.Exporters.Csv;

namespace PlayHouse.Tests.Performance;

public class BenchmarkProgram
{
    public static void Main(string[] args)
    {
        var config = DefaultConfig.Instance
            .AddExporter(MarkdownExporter.GitHub)
            .AddExporter(CsvExporter.Default)
            .AddColumn(RankColumn.Arabic);

        // 전체 벤치마크 실행
        BenchmarkSwitcher.FromAssembly(typeof(BenchmarkProgram).Assembly).Run(args, config);
    }
}
