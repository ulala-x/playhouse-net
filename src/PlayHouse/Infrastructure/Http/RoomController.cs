#nullable enable

using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PlayHouse.Abstractions;
using PlayHouse.Core.Session;
using PlayHouse.Core.Stage;

namespace PlayHouse.Infrastructure.Http;

/// <summary>
/// HTTP API controller for room management operations.
/// Provides endpoints for creating, joining, and managing game rooms (stages).
/// </summary>
[ApiController]
[Route("api/rooms")]
public sealed class RoomController : ControllerBase
{
    private readonly ILogger<RoomController> _logger;
    private readonly PlayHouseServer _server;
    private readonly StagePool _stagePool;
    private readonly StageFactory _stageFactory;
    private readonly RoomTokenManager _tokenManager;
    private readonly PlayHouseOptions _options;

    /// <summary>
    /// Initializes a new instance of the RoomController.
    /// </summary>
    /// <param name="server">The PlayHouse server instance.</param>
    /// <param name="stagePool">The stage pool instance.</param>
    /// <param name="stageFactory">The stage factory instance.</param>
    /// <param name="tokenManager">The room token manager instance.</param>
    /// <param name="options">PlayHouse options.</param>
    /// <param name="logger">Logger instance.</param>
    public RoomController(
        PlayHouseServer server,
        StagePool stagePool,
        StageFactory stageFactory,
        RoomTokenManager tokenManager,
        IOptions<PlayHouseOptions> options,
        ILogger<RoomController> logger)
    {
        _server = server;
        _stagePool = stagePool;
        _stageFactory = stageFactory;
        _tokenManager = tokenManager;
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>
    /// Gets server status and statistics.
    /// </summary>
    /// <returns>Server status information.</returns>
    [HttpGet("status")]
    [ProducesResponseType(typeof(ServerStatusResponse), 200)]
    public IActionResult GetStatus()
    {
        var stageStats = _stagePool.GetStatistics();

        var response = new ServerStatusResponse
        {
            TcpSessionCount = _server.TcpServer?.SessionCount ?? 0,
            WebSocketSessionCount = _server.WebSocketServer?.SessionCount ?? 0,
            TotalStages = (int)(stageStats["total_stages"] ?? 0),
            TotalActors = (int)(stageStats["total_actors"] ?? 0),
            Timestamp = DateTime.UtcNow
        };

        return Ok(response);
    }

    /// <summary>
    /// Creates a new room and issues a room token for TCP/WebSocket authentication.
    /// This is the recommended entry point for clients following the PlayHouse spec.
    /// </summary>
    /// <param name="request">Room creation request with room type and nickname.</param>
    /// <returns>Room token, endpoint, and stage ID.</returns>
    [HttpPost("create")]
    [ProducesResponseType(typeof(CreateRoomResponse), 201)]
    [ProducesResponseType(typeof(ErrorResponse), 400)]
    [ProducesResponseType(typeof(ErrorResponse), 500)]
    public async Task<IActionResult> CreateRoom([FromBody] CreateRoomRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.RoomType))
        {
            return BadRequest(new ErrorResponse
            {
                ErrorCode = ErrorCode.MissingParameter,
                Message = "RoomType is required"
            });
        }

        if (string.IsNullOrWhiteSpace(request.Nickname))
        {
            return BadRequest(new ErrorResponse
            {
                ErrorCode = ErrorCode.MissingParameter,
                Message = "Nickname is required"
            });
        }

        try
        {
            _logger.LogInformation("Creating room: RoomType={RoomType}, Nickname={Nickname}",
                request.RoomType, request.Nickname);

            // Create stage
            var creationPacket = new Infrastructure.Serialization.SimplePacket(
                "CreateRoom",
                EmptyPayload.Instance,
                0,
                0,
                0);

            var (stageContext, errorCode, _) = await _stageFactory.CreateStageAsync(
                request.RoomType,
                creationPacket);

            if (errorCode != ErrorCode.Success || stageContext == null)
            {
                _logger.LogWarning("Failed to create room: RoomType={RoomType}, ErrorCode={ErrorCode}",
                    request.RoomType, errorCode);

                return StatusCode(500, new ErrorResponse
                {
                    ErrorCode = errorCode,
                    Message = $"Failed to create stage: {GetErrorMessage(errorCode)}"
                });
            }

            // Generate room token
            var roomToken = _tokenManager.GenerateToken(stageContext.StageId, request.Nickname);

            // Build endpoint
            var endpoint = $"tcp://{_options.Ip}:{_options.Port}";

            var response = new CreateRoomResponse
            {
                RoomToken = roomToken,
                Endpoint = endpoint,
                StageId = stageContext.StageId
            };

            _logger.LogInformation("Room created successfully: StageId={StageId}, Token={Token}",
                stageContext.StageId, roomToken.Substring(0, Math.Min(8, roomToken.Length)) + "...");

            return CreatedAtAction(nameof(GetRoom), new { stageId = stageContext.StageId }, response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating room: RoomType={RoomType}", request.RoomType);
            return StatusCode(500, new ErrorResponse
            {
                ErrorCode = ErrorCode.InternalError,
                Message = "Internal server error"
            });
        }
    }

    /// <summary>
    /// Gets or creates a room of the specified type.
    /// </summary>
    /// <param name="request">Room creation request.</param>
    /// <returns>Created or existing room information.</returns>
    [HttpPost("get-or-create")]
    [ProducesResponseType(typeof(GetOrCreateRoomResponse), 200)]
    [ProducesResponseType(typeof(GetOrCreateRoomResponse), 201)]
    [ProducesResponseType(typeof(ErrorResponse), 400)]
    [ProducesResponseType(typeof(ErrorResponse), 500)]
    public async Task<IActionResult> GetOrCreateRoom([FromBody] GetOrCreateRoomRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.StageType))
        {
            return BadRequest(new ErrorResponse
            {
                ErrorCode = ErrorCode.MissingParameter,
                Message = "StageType is required"
            });
        }

        try
        {
            // Check if a stage of this type already exists
            var existingStages = _stagePool.GetStagesByType(request.StageType).ToList();

            if (existingStages.Any())
            {
                var existing = existingStages.First();
                _logger.LogInformation("Returning existing room: StageId={StageId}, StageType={StageType}",
                    existing.StageId, request.StageType);

                return Ok(new GetOrCreateRoomResponse
                {
                    StageId = existing.StageId,
                    StageType = request.StageType,
                    IsNew = false,
                    Timestamp = DateTime.UtcNow
                });
            }

            // Create new stage
            _logger.LogInformation("Creating new room: StageType={StageType}", request.StageType);

            // Create a simple packet for stage creation (empty payload)
            var creationPacket = new Infrastructure.Serialization.SimplePacket(
                "CreateStage",
                EmptyPayload.Instance,
                0,
                0,
                0);

            var (stageContext, errorCode, reply) = await _stageFactory.CreateStageAsync(
                request.StageType,
                creationPacket);

            if (errorCode != ErrorCode.Success || stageContext == null)
            {
                _logger.LogWarning("Failed to create room: StageType={StageType}, ErrorCode={ErrorCode}",
                    request.StageType, errorCode);

                return StatusCode(500, new ErrorResponse
                {
                    ErrorCode = errorCode,
                    Message = $"Failed to create stage: {GetErrorMessage(errorCode)}"
                });
            }

            _logger.LogInformation("Created new room: StageId={StageId}, StageType={StageType}",
                stageContext.StageId, request.StageType);

            return CreatedAtAction(
                nameof(GetRoom),
                new { stageId = stageContext.StageId },
                new GetOrCreateRoomResponse
                {
                    StageId = stageContext.StageId,
                    StageType = request.StageType,
                    IsNew = true,
                    Timestamp = DateTime.UtcNow
                });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in GetOrCreateRoom for StageType={StageType}", request.StageType);
            return StatusCode(500, new ErrorResponse
            {
                ErrorCode = ErrorCode.InternalError,
                Message = "Internal server error"
            });
        }
    }

    /// <summary>
    /// Gets room information by stage ID.
    /// </summary>
    /// <param name="stageId">The stage identifier.</param>
    /// <returns>Room information.</returns>
    [HttpGet("{stageId:int}")]
    [ProducesResponseType(typeof(RoomInfoResponse), 200)]
    [ProducesResponseType(typeof(ErrorResponse), 404)]
    public IActionResult GetRoom(int stageId)
    {
        _logger.LogInformation("Getting room: StageId={StageId}", stageId);

        var stageContext = _stagePool.GetStage(stageId);
        if (stageContext == null)
        {
            return NotFound(new ErrorResponse
            {
                ErrorCode = ErrorCode.StageNotFound,
                Message = $"Stage {stageId} not found"
            });
        }

        var response = new RoomInfoResponse
        {
            StageId = stageContext.StageId,
            StageType = stageContext.StageType,
            ActorCount = stageContext.ActorPool.Count,
            QueueDepth = stageContext.QueueDepth,
            IsProcessing = stageContext.IsProcessing,
            Timestamp = DateTime.UtcNow
        };

        return Ok(response);
    }

    /// <summary>
    /// Joins a room (for testing/admin purposes).
    /// </summary>
    /// <param name="stageId">The stage identifier.</param>
    /// <param name="request">Join room request.</param>
    /// <returns>Join result.</returns>
    [HttpPost("{stageId:int}/join")]
    [ProducesResponseType(typeof(JoinRoomResponse), 200)]
    [ProducesResponseType(typeof(ErrorResponse), 400)]
    [ProducesResponseType(typeof(ErrorResponse), 404)]
    public IActionResult JoinRoom(int stageId, [FromBody] JoinRoomRequest request)
    {
        if (request.AccountId <= 0)
        {
            return BadRequest(new ErrorResponse
            {
                ErrorCode = ErrorCode.MissingParameter,
                Message = "AccountId is required and must be greater than 0"
            });
        }

        _logger.LogInformation("Account {AccountId} joining room StageId={StageId}",
            request.AccountId, stageId);

        var stageContext = _stagePool.GetStage(stageId);
        if (stageContext == null)
        {
            return NotFound(new ErrorResponse
            {
                ErrorCode = ErrorCode.StageNotFound,
                Message = $"Stage {stageId} not found"
            });
        }

        // Check if actor already exists
        if (stageContext.ActorPool.HasActor(request.AccountId))
        {
            _logger.LogWarning("Actor {AccountId} already exists in stage {StageId}",
                request.AccountId, stageId);

            return Ok(new JoinRoomResponse
            {
                StageId = stageId,
                AccountId = request.AccountId,
                Success = true,
                Message = "Already joined",
                Timestamp = DateTime.UtcNow
            });
        }

        var response = new JoinRoomResponse
        {
            StageId = stageId,
            AccountId = request.AccountId,
            Success = true,
            Message = "Join request accepted (actual join happens via packet routing)",
            Timestamp = DateTime.UtcNow
        };

        return Ok(response);
    }

    /// <summary>
    /// Leaves a room.
    /// </summary>
    /// <param name="stageId">The stage identifier.</param>
    /// <param name="accountId">The account identifier.</param>
    /// <returns>Leave result.</returns>
    [HttpPost("{stageId:int}/leave")]
    [ProducesResponseType(204)]
    [ProducesResponseType(typeof(ErrorResponse), 400)]
    [ProducesResponseType(typeof(ErrorResponse), 404)]
    public IActionResult LeaveRoom(int stageId, [FromQuery] long accountId)
    {
        if (accountId <= 0)
        {
            return BadRequest(new ErrorResponse
            {
                ErrorCode = ErrorCode.MissingParameter,
                Message = "accountId is required and must be greater than 0"
            });
        }

        _logger.LogInformation("Account {AccountId} leaving room StageId={StageId}",
            accountId, stageId);

        var stageContext = _stagePool.GetStage(stageId);
        if (stageContext == null)
        {
            return NotFound(new ErrorResponse
            {
                ErrorCode = ErrorCode.StageNotFound,
                Message = $"Stage {stageId} not found"
            });
        }

        // Note: Actual leave logic should be handled via packet routing
        // This endpoint is primarily for testing/admin purposes

        return NoContent();
    }

    /// <summary>
    /// Deletes a room.
    /// </summary>
    /// <param name="stageId">The stage identifier.</param>
    /// <returns>Deletion result.</returns>
    [HttpDelete("{stageId:int}")]
    [ProducesResponseType(204)]
    [ProducesResponseType(typeof(ErrorResponse), 404)]
    [ProducesResponseType(typeof(ErrorResponse), 500)]
    public async Task<IActionResult> DeleteRoom(int stageId)
    {
        _logger.LogInformation("Deleting room: StageId={StageId}", stageId);

        if (!_stagePool.HasStage(stageId))
        {
            return NotFound(new ErrorResponse
            {
                ErrorCode = ErrorCode.StageNotFound,
                Message = $"Stage {stageId} not found"
            });
        }

        try
        {
            var destroyed = await _stageFactory.DestroyStageAsync(stageId);

            if (!destroyed)
            {
                return StatusCode(500, new ErrorResponse
                {
                    ErrorCode = ErrorCode.InternalError,
                    Message = "Failed to destroy stage"
                });
            }

            return NoContent();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting room StageId={StageId}", stageId);
            return StatusCode(500, new ErrorResponse
            {
                ErrorCode = ErrorCode.InternalError,
                Message = "Internal server error"
            });
        }
    }

    /// <summary>
    /// Gets all rooms.
    /// </summary>
    /// <returns>List of all rooms.</returns>
    [HttpGet]
    [ProducesResponseType(typeof(RoomListResponse), 200)]
    public IActionResult GetAllRooms()
    {
        _logger.LogInformation("Getting all rooms");

        var stages = _stagePool.GetAllStages()
            .Select(s => new RoomInfo
            {
                StageId = s.StageId,
                StageType = s.StageType,
                ActorCount = s.ActorPool.Count,
                QueueDepth = s.QueueDepth,
                IsProcessing = s.IsProcessing
            })
            .ToList();

        var response = new RoomListResponse
        {
            Rooms = stages,
            TotalCount = stages.Count,
            Timestamp = DateTime.UtcNow
        };

        return Ok(response);
    }

    private static string GetErrorMessage(ushort errorCode)
    {
        return errorCode switch
        {
            ErrorCode.Success => "Success",
            ErrorCode.StageNotFound => "Stage not found",
            ErrorCode.StageTypeNotFound => "Stage type not registered",
            ErrorCode.StageCreationFailed => "Failed to create stage",
            ErrorCode.InternalError => "Internal server error",
            _ => $"Error code {errorCode}"
        };
    }
}

#region DTOs

/// <summary>
/// Server status response.
/// </summary>
public sealed class ServerStatusResponse
{
    /// <summary>
    /// Gets the number of TCP sessions.
    /// </summary>
    public int TcpSessionCount { get; init; }

    /// <summary>
    /// Gets the number of WebSocket sessions.
    /// </summary>
    public int WebSocketSessionCount { get; init; }

    /// <summary>
    /// Gets the total number of stages.
    /// </summary>
    public int TotalStages { get; init; }

    /// <summary>
    /// Gets the total number of actors across all stages.
    /// </summary>
    public int TotalActors { get; init; }

    /// <summary>
    /// Gets the timestamp of the response.
    /// </summary>
    public DateTime Timestamp { get; init; }
}

/// <summary>
/// Get or create room request.
/// </summary>
public sealed class GetOrCreateRoomRequest
{
    /// <summary>
    /// Gets the stage type name.
    /// </summary>
    public required string StageType { get; init; }
}

/// <summary>
/// Get or create room response.
/// </summary>
public sealed class GetOrCreateRoomResponse
{
    /// <summary>
    /// Gets the stage identifier.
    /// </summary>
    public int StageId { get; init; }

    /// <summary>
    /// Gets the stage type name.
    /// </summary>
    public required string StageType { get; init; }

    /// <summary>
    /// Gets a value indicating whether this is a newly created stage.
    /// </summary>
    public bool IsNew { get; init; }

    /// <summary>
    /// Gets the timestamp of the response.
    /// </summary>
    public DateTime Timestamp { get; init; }
}

/// <summary>
/// Room information response.
/// </summary>
public sealed class RoomInfoResponse
{
    /// <summary>
    /// Gets the stage identifier.
    /// </summary>
    public int StageId { get; init; }

    /// <summary>
    /// Gets the stage type name.
    /// </summary>
    public required string StageType { get; init; }

    /// <summary>
    /// Gets the current number of actors in the stage.
    /// </summary>
    public int ActorCount { get; init; }

    /// <summary>
    /// Gets the current queue depth.
    /// </summary>
    public int QueueDepth { get; init; }

    /// <summary>
    /// Gets a value indicating whether the stage is currently processing messages.
    /// </summary>
    public bool IsProcessing { get; init; }

    /// <summary>
    /// Gets the timestamp of the response.
    /// </summary>
    public DateTime Timestamp { get; init; }
}

/// <summary>
/// Join room request.
/// </summary>
public sealed class JoinRoomRequest
{
    /// <summary>
    /// Gets the account identifier.
    /// </summary>
    public long AccountId { get; init; }
}

/// <summary>
/// Join room response.
/// </summary>
public sealed class JoinRoomResponse
{
    /// <summary>
    /// Gets the stage identifier.
    /// </summary>
    public int StageId { get; init; }

    /// <summary>
    /// Gets the account identifier.
    /// </summary>
    public long AccountId { get; init; }

    /// <summary>
    /// Gets a value indicating whether the join was successful.
    /// </summary>
    public bool Success { get; init; }

    /// <summary>
    /// Gets an optional message.
    /// </summary>
    public string? Message { get; init; }

    /// <summary>
    /// Gets the timestamp of the response.
    /// </summary>
    public DateTime Timestamp { get; init; }
}

/// <summary>
/// Room list response.
/// </summary>
public sealed class RoomListResponse
{
    /// <summary>
    /// Gets the list of rooms.
    /// </summary>
    public required List<RoomInfo> Rooms { get; init; }

    /// <summary>
    /// Gets the total count of rooms.
    /// </summary>
    public int TotalCount { get; init; }

    /// <summary>
    /// Gets the timestamp of the response.
    /// </summary>
    public DateTime Timestamp { get; init; }
}

/// <summary>
/// Room information for list response.
/// </summary>
public sealed class RoomInfo
{
    /// <summary>
    /// Gets the stage identifier.
    /// </summary>
    public int StageId { get; init; }

    /// <summary>
    /// Gets the stage type name.
    /// </summary>
    public required string StageType { get; init; }

    /// <summary>
    /// Gets the current number of actors.
    /// </summary>
    public int ActorCount { get; init; }

    /// <summary>
    /// Gets the current queue depth.
    /// </summary>
    public int QueueDepth { get; init; }

    /// <summary>
    /// Gets a value indicating whether the stage is processing.
    /// </summary>
    public bool IsProcessing { get; init; }
}

/// <summary>
/// Create room request (for token-based authentication flow).
/// </summary>
public sealed class CreateRoomRequest
{
    /// <summary>
    /// Gets the room type (stage type name).
    /// </summary>
    public required string RoomType { get; init; }

    /// <summary>
    /// Gets the user nickname.
    /// </summary>
    public required string Nickname { get; init; }
}

/// <summary>
/// Create room response (for token-based authentication flow).
/// Matches CreateRoomReply from proto definitions.
/// </summary>
public sealed class CreateRoomResponse
{
    /// <summary>
    /// Gets the room token for TCP/WebSocket authentication.
    /// </summary>
    public required string RoomToken { get; init; }

    /// <summary>
    /// Gets the TCP/WebSocket endpoint.
    /// </summary>
    public required string Endpoint { get; init; }

    /// <summary>
    /// Gets the stage identifier.
    /// </summary>
    public int StageId { get; init; }
}

/// <summary>
/// Create room DTO (for backwards compatibility).
/// </summary>
[Obsolete("Use CreateRoomRequest and CreateRoomResponse instead")]
public sealed class CreateRoomDto
{
    /// <summary>
    /// Gets the room type (stage type name).
    /// </summary>
    public required string RoomType { get; init; }

    /// <summary>
    /// Gets the user nickname.
    /// </summary>
    public required string Nickname { get; init; }

    /// <summary>
    /// Gets the room token for TCP/WebSocket authentication.
    /// </summary>
    public string RoomToken { get; init; } = string.Empty;

    /// <summary>
    /// Gets the TCP/WebSocket endpoint.
    /// </summary>
    public string Endpoint { get; init; } = string.Empty;

    /// <summary>
    /// Gets the stage identifier.
    /// </summary>
    public int StageId { get; init; }
}

/// <summary>
/// Error response.
/// </summary>
public sealed class ErrorResponse
{
    /// <summary>
    /// Gets the error code.
    /// </summary>
    public ushort ErrorCode { get; init; }

    /// <summary>
    /// Gets the error message.
    /// </summary>
    public required string Message { get; init; }

    /// <summary>
    /// Gets optional error details.
    /// </summary>
    public string? Details { get; init; }

    /// <summary>
    /// Gets the timestamp of the error.
    /// </summary>
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
}

#endregion
