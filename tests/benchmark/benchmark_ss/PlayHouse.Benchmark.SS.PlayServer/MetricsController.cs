using Microsoft.AspNetCore.Mvc;
using PlayHouse.Core.Shared;
using Serilog;

namespace PlayHouse.Benchmark.SS.PlayServer;

/// <summary>
/// 벤치마크 메트릭 조회 및 리셋 API (Server-to-Server 벤치마크)
/// </summary>
[ApiController]
[Route("benchmark")]
public class MetricsController : ControllerBase
{
    /// <summary>
    /// GET /benchmark/stats - 통계 조회
    /// </summary>
    [HttpGet("stats")]
    public IActionResult GetStats()
    {
        var metrics = ServerMetricsCollector.Instance.GetMetrics();
        return Ok(metrics);
    }

    /// <summary>
    /// POST /benchmark/reset - 통계 리셋
    /// </summary>
    [HttpPost("reset")]
    public IActionResult Reset()
    {
        ServerMetricsCollector.Instance.Reset();
        return Ok(new { message = "Metrics reset successfully" });
    }

    /// <summary>
    /// POST /benchmark/shutdown - 서버 종료
    /// </summary>
    [HttpPost("shutdown")]
    public IActionResult Shutdown([FromServices] CancellationTokenSource cts)
    {
        Log.Information("Shutdown requested via HTTP API");
        cts.Cancel();
        return Ok(new { message = "Shutdown initiated" });
    }
}
