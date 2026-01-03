using Microsoft.AspNetCore.Mvc;
using PlayHouse.Bootstrap;
using Serilog;

namespace PlayHouse.Benchmark.Echo.Server;

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

    /// <summary>
    /// POST /benchmark/stages - Stage 일괄 생성
    /// </summary>
    [HttpPost("stages")]
    public IActionResult CreateStages(
        [FromBody] CreateStagesRequest request,
        [FromServices] PlayServer playServer)
    {
        var createdCount = 0;

        for (int i = 0; i < request.Count; i++)
        {
            var stageId = request.BaseStageId + i;
            if (playServer.CreateStageIfNotExists(stageId, "EchoStage"))
            {
                createdCount++;
            }
        }

        Log.Information("Created {Count} stages (baseStageId: {BaseStageId})", createdCount, request.BaseStageId);

        return Ok(new CreateStagesResponse
        {
            CreatedCount = createdCount,
            Message = $"Created {createdCount} stages starting from {request.BaseStageId}"
        });
    }

    /// <summary>
    /// DELETE /benchmark/stages - Stage 일괄 삭제
    /// </summary>
    [HttpDelete("stages")]
    public IActionResult DeleteStages(
        [FromBody] DeleteStagesRequest request,
        [FromServices] PlayServer playServer)
    {
        for (int i = 0; i < request.Count; i++)
        {
            var stageId = request.BaseStageId + i;
            playServer.SendToStage(stageId, "CloseStageCommand");
        }

        Log.Information("Sent CloseStageCommand to {Count} stages (baseStageId: {BaseStageId})",
            request.Count, request.BaseStageId);

        return Ok(new DeleteStagesResponse
        {
            Message = $"Sent close command to {request.Count} stages starting from {request.BaseStageId}"
        });
    }
}

/// <summary>
/// Stage 일괄 생성 요청
/// </summary>
public class CreateStagesRequest
{
    public int Count { get; set; }
    public long BaseStageId { get; set; } = 10000;
}

/// <summary>
/// Stage 일괄 생성 응답
/// </summary>
public class CreateStagesResponse
{
    public int CreatedCount { get; set; }
    public string Message { get; set; } = "";
}

/// <summary>
/// Stage 일괄 삭제 요청
/// </summary>
public class DeleteStagesRequest
{
    public int Count { get; set; }
    public long BaseStageId { get; set; } = 10000;
}

/// <summary>
/// Stage 일괄 삭제 응답
/// </summary>
public class DeleteStagesResponse
{
    public string Message { get; set; } = "";
}
