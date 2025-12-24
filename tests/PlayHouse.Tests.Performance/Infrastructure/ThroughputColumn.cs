using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Reports;
using BenchmarkDotNet.Running;
using System.Linq;

namespace PlayHouse.Tests.Performance.Infrastructure;

/// <summary>
/// BenchmarkDotNet 커스텀 컬럼: Throughput (bytes/sec) 표시.
/// ResponseSize 파라미터와 Operations/sec를 조합하여 데이터 처리량을 계산합니다.
/// </summary>
public class ThroughputColumn : IColumn
{
    public string Id => nameof(ThroughputColumn);
    public string ColumnName => "Throughput";
    public string Legend => "Throughput in MB/s or GB/s";
    public UnitType UnitType => UnitType.Size;
    public bool AlwaysShow => true;
    public ColumnCategory Category => ColumnCategory.Custom;
    public int PriorityInCategory => 0;
    public bool IsNumeric => true;
    public bool IsAvailable(Summary summary) => true;
    public bool IsDefault(Summary summary, BenchmarkCase benchmarkCase) => false;

    public string GetValue(Summary summary, BenchmarkCase benchmarkCase)
    {
        var report = summary[benchmarkCase];
        if (report?.ResultStatistics == null)
            return "N/A";

        // ResponseSize 파라미터 가져오기
        var responseSizeParam = benchmarkCase.Parameters.Items
            .FirstOrDefault(p => p.Name == "ResponseSize");

        if (responseSizeParam == null)
            return "N/A";

        var responseSize = (int)responseSizeParam.Value;
        var opsPerSecond = 1.0 / report.ResultStatistics.Mean; // Mean is in seconds
        var bytesPerSecond = responseSize * opsPerSecond;

        return FormatBytes(bytesPerSecond);
    }

    public string GetValue(Summary summary, BenchmarkCase benchmarkCase, SummaryStyle style)
    {
        return GetValue(summary, benchmarkCase);
    }

    public override string ToString() => ColumnName;

    private static string FormatBytes(double bytes)
    {
        const double KB = 1024;
        const double MB = KB * 1024;
        const double GB = MB * 1024;

        if (bytes >= GB)
            return $"{bytes / GB:F2} GB/s";
        if (bytes >= MB)
            return $"{bytes / MB:F2} MB/s";
        if (bytes >= KB)
            return $"{bytes / KB:F2} KB/s";

        return $"{bytes:F2} B/s";
    }
}
