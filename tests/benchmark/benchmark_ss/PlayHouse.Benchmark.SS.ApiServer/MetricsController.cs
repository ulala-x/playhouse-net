using Microsoft.AspNetCore.Mvc;
using Serilog;

namespace PlayHouse.Benchmark.SS.ApiServer;

/// <summary>
/// API 서버 관리 API
/// </summary>
[ApiController]
[Route("benchmark")]
public class MetricsController : ControllerBase
{
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
