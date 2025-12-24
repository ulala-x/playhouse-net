using Microsoft.AspNetCore.Mvc;

namespace PlayHouse.Benchmark.Server;

/// <summary>
/// 벤치마크 메트릭 조회 및 리셋 API
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
}
