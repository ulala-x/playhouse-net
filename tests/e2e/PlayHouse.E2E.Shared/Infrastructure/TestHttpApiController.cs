#nullable enable

using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using PlayHouse.Abstractions.Api;
using PlayHouse.Core.Shared;

namespace PlayHouse.E2E.Shared.Infrastructure;

/// <summary>
/// HTTP API Controller for E2E testing of ApiServer operations.
/// Provides HTTP endpoints that invoke IApiSender methods for verification purposes.
/// </summary>
[ApiController]
[Route("api")]
public class TestHttpApiController : ControllerBase
{
    private readonly IApiSender _apiSender;
    private readonly string _playServerId;

    /// <summary>
    /// Initializes a new instance of the TestHttpApiController.
    /// </summary>
    /// <param name="apiSender">The API sender for communicating with PlayServer</param>
    /// <param name="config">Configuration containing PlayServerId</param>
    public TestHttpApiController(IApiSender apiSender, IConfiguration config)
    {
        _apiSender = apiSender;
        _playServerId = config["PlayServerId"] ?? throw new InvalidOperationException("PlayServerId not configured");
    }

    /// <summary>
    /// Creates a new stage on the specified PlayServer.
    /// </summary>
    /// <param name="req">The create stage request</param>
    /// <returns>Result of the create operation</returns>
    [HttpPost("stages")]
    public async Task<IActionResult> CreateStage([FromBody] CreateStageRequest req)
    {
        var createPayload = new Proto.CreateStagePayload
        {
            StageName = req.StageType,
            MaxPlayers = 10
        };
        var result = await _apiSender.CreateStage(
            _playServerId,
            req.StageType,
            req.StageId,
            CPacket.Of(createPayload));

        // OnCreate reply 파싱하여 반환
        string? replyPayloadId = null;
        if (result.CreateStageRes.Payload.Length > 0)
        {
            var createReply = Proto.CreateStageReply.Parser.ParseFrom(result.CreateStageRes.Payload.DataSpan);
            replyPayloadId = $"{createReply.ReceivedStageName}:{createReply.ReceivedMaxPlayers}";
        }

        return Ok(new CreateStageResponse
        {
            Success = result.Result,
            StageId = req.StageId,
            ReplyPayloadId = replyPayloadId
        });
    }

    /// <summary>
    /// Gets or creates a stage on the specified PlayServer.
    /// </summary>
    /// <param name="req">The get or create stage request</param>
    /// <returns>Result of the get or create operation</returns>
    [HttpPost("stages/get-or-create")]
    public async Task<IActionResult> GetOrCreateStage([FromBody] GetOrCreateStageRequest req)
    {
        var createPayload = new Proto.CreateStagePayload
        {
            StageName = req.StageType,
            MaxPlayers = 10
        };
        var result = await _apiSender.GetOrCreateStage(
            _playServerId,
            req.StageType,
            req.StageId,
            CPacket.Of(createPayload));

        // OnCreate reply 파싱하여 반환 (새로 생성된 경우에만)
        string? replyPayloadId = null;
        if (result.IsCreated && result.Payload.Payload.Length > 0)
        {
            var createReply = Proto.CreateStageReply.Parser.ParseFrom(result.Payload.Payload.DataSpan);
            replyPayloadId = $"{createReply.ReceivedStageName}:{createReply.ReceivedMaxPlayers}";
        }

        return Ok(new GetOrCreateStageResponse
        {
            Success = result.Result,
            IsCreated = result.IsCreated,
            StageId = req.StageId,
            ReplyPayloadId = replyPayloadId
        });
    }
}

/// <summary>
/// Request DTO for creating a stage.
/// </summary>
public record CreateStageRequest
{
    /// <summary>
    /// The type of stage to create (e.g., "TestStage")
    /// </summary>
    public required string StageType { get; init; }

    /// <summary>
    /// The unique stage ID
    /// </summary>
    public required ushort StageId { get; init; }
}

/// <summary>
/// Response DTO for create stage operation.
/// </summary>
public record CreateStageResponse
{
    /// <summary>
    /// Indicates whether the stage was successfully created
    /// </summary>
    public required bool Success { get; init; }

    /// <summary>
    /// The stage ID that was created
    /// </summary>
    public required ushort StageId { get; init; }

    /// <summary>
    /// OnCreate reply payload info (format: "StageName:MaxPlayers")
    /// </summary>
    public string? ReplyPayloadId { get; init; }
}

/// <summary>
/// Request DTO for getting or creating a stage.
/// </summary>
public record GetOrCreateStageRequest
{
    /// <summary>
    /// The type of stage to get or create (e.g., "TestStage")
    /// </summary>
    public required string StageType { get; init; }

    /// <summary>
    /// The unique stage ID
    /// </summary>
    public required ushort StageId { get; init; }
}

/// <summary>
/// Response DTO for get or create stage operation.
/// </summary>
public record GetOrCreateStageResponse
{
    /// <summary>
    /// Indicates whether the operation was successful
    /// </summary>
    public required bool Success { get; init; }

    /// <summary>
    /// Indicates whether a new stage was created (true) or existing stage was returned (false)
    /// </summary>
    public required bool IsCreated { get; init; }

    /// <summary>
    /// The stage ID that was retrieved or created
    /// </summary>
    public required ushort StageId { get; init; }

    /// <summary>
    /// OnCreate reply payload info (format: "StageName:MaxPlayers", only when IsCreated=true)
    /// </summary>
    public string? ReplyPayloadId { get; init; }
}
