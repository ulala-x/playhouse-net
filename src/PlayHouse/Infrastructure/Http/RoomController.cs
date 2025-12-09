#nullable enable

using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace PlayHouse.Infrastructure.Http;

/// <summary>
/// HTTP API controller for room management operations.
/// Provides endpoints for creating, joining, and managing game rooms.
/// </summary>
[ApiController]
[Route("api/rooms")]
public sealed class RoomController : ControllerBase
{
    private readonly ILogger<RoomController> _logger;
    private readonly PlayHouseServer _server;

    /// <summary>
    /// Initializes a new instance of the RoomController.
    /// </summary>
    /// <param name="server">The PlayHouse server instance.</param>
    /// <param name="logger">Logger instance.</param>
    public RoomController(PlayHouseServer server, ILogger<RoomController> logger)
    {
        _server = server;
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
        var response = new ServerStatusResponse
        {
            TcpSessionCount = _server.TcpServer?.SessionCount ?? 0,
            WebSocketSessionCount = _server.WebSocketServer?.SessionCount ?? 0,
            Timestamp = DateTime.UtcNow
        };

        return Ok(response);
    }

    /// <summary>
    /// Creates a new room.
    /// </summary>
    /// <param name="request">Room creation request.</param>
    /// <returns>Created room information.</returns>
    [HttpPost]
    [ProducesResponseType(typeof(CreateRoomResponse), 201)]
    [ProducesResponseType(400)]
    public IActionResult CreateRoom([FromBody] CreateRoomRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.RoomId))
        {
            return BadRequest("RoomId is required");
        }

        _logger.LogInformation("Creating room: {RoomId}", request.RoomId);

        // TODO: Implement room creation logic
        var response = new CreateRoomResponse
        {
            RoomId = request.RoomId,
            MaxPlayers = request.MaxPlayers,
            CreatedAt = DateTime.UtcNow
        };

        return CreatedAtAction(nameof(GetRoom), new { roomId = response.RoomId }, response);
    }

    /// <summary>
    /// Gets room information.
    /// </summary>
    /// <param name="roomId">The room identifier.</param>
    /// <returns>Room information.</returns>
    [HttpGet("{roomId}")]
    [ProducesResponseType(typeof(RoomInfoResponse), 200)]
    [ProducesResponseType(404)]
    public IActionResult GetRoom(string roomId)
    {
        _logger.LogInformation("Getting room: {RoomId}", roomId);

        // TODO: Implement room lookup logic
        return NotFound();
    }

    /// <summary>
    /// Joins a room.
    /// </summary>
    /// <param name="roomId">The room identifier.</param>
    /// <param name="request">Join room request.</param>
    /// <returns>Join result.</returns>
    [HttpPost("{roomId}/join")]
    [ProducesResponseType(typeof(JoinRoomResponse), 200)]
    [ProducesResponseType(404)]
    [ProducesResponseType(409)]
    public IActionResult JoinRoom(string roomId, [FromBody] JoinRoomRequest request)
    {
        _logger.LogInformation("Player {PlayerId} joining room {RoomId}", request.PlayerId, roomId);

        // TODO: Implement room join logic
        return NotFound();
    }

    /// <summary>
    /// Leaves a room.
    /// </summary>
    /// <param name="roomId">The room identifier.</param>
    /// <param name="playerId">The player identifier.</param>
    /// <returns>Leave result.</returns>
    [HttpPost("{roomId}/leave")]
    [ProducesResponseType(204)]
    [ProducesResponseType(404)]
    public IActionResult LeaveRoom(string roomId, [FromQuery] string playerId)
    {
        _logger.LogInformation("Player {PlayerId} leaving room {RoomId}", playerId, roomId);

        // TODO: Implement room leave logic
        return NoContent();
    }

    /// <summary>
    /// Deletes a room.
    /// </summary>
    /// <param name="roomId">The room identifier.</param>
    /// <returns>Deletion result.</returns>
    [HttpDelete("{roomId}")]
    [ProducesResponseType(204)]
    [ProducesResponseType(404)]
    public IActionResult DeleteRoom(string roomId)
    {
        _logger.LogInformation("Deleting room: {RoomId}", roomId);

        // TODO: Implement room deletion logic
        return NoContent();
    }
}

#region DTOs

/// <summary>
/// Server status response.
/// </summary>
public sealed class ServerStatusResponse
{
    public int TcpSessionCount { get; init; }
    public int WebSocketSessionCount { get; init; }
    public DateTime Timestamp { get; init; }
}

/// <summary>
/// Create room request.
/// </summary>
public sealed class CreateRoomRequest
{
    public required string RoomId { get; init; }
    public int MaxPlayers { get; init; } = 4;
    public Dictionary<string, string>? Metadata { get; init; }
}

/// <summary>
/// Create room response.
/// </summary>
public sealed class CreateRoomResponse
{
    public required string RoomId { get; init; }
    public int MaxPlayers { get; init; }
    public DateTime CreatedAt { get; init; }
}

/// <summary>
/// Room information response.
/// </summary>
public sealed class RoomInfoResponse
{
    public required string RoomId { get; init; }
    public int CurrentPlayers { get; init; }
    public int MaxPlayers { get; init; }
    public DateTime CreatedAt { get; init; }
}

/// <summary>
/// Join room request.
/// </summary>
public sealed class JoinRoomRequest
{
    public required string PlayerId { get; init; }
}

/// <summary>
/// Join room response.
/// </summary>
public sealed class JoinRoomResponse
{
    public required string RoomId { get; init; }
    public required string PlayerId { get; init; }
    public DateTime JoinedAt { get; init; }
}

#endregion
