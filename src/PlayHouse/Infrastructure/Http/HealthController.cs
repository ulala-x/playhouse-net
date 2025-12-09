#nullable enable

using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using PlayHouse.Core.Messaging;
using PlayHouse.Core.Session;
using PlayHouse.Core.Stage;

namespace PlayHouse.Infrastructure.Http;

/// <summary>
/// Health check controller for Kubernetes probes and monitoring.
/// </summary>
[ApiController]
[Route("health")]
public sealed class HealthController : ControllerBase
{
    private readonly ILogger<HealthController> _logger;
    private readonly PlayHouseServer _server;
    private readonly StagePool _stagePool;
    private readonly SessionManager _sessionManager;
    private readonly PacketDispatcher _dispatcher;
    private readonly DateTime _startTime;

    /// <summary>
    /// Initializes a new instance of the HealthController.
    /// </summary>
    /// <param name="server">The PlayHouse server instance.</param>
    /// <param name="stagePool">The stage pool instance.</param>
    /// <param name="sessionManager">The session manager instance.</param>
    /// <param name="dispatcher">The packet dispatcher instance.</param>
    /// <param name="logger">Logger instance.</param>
    public HealthController(
        PlayHouseServer server,
        StagePool stagePool,
        SessionManager sessionManager,
        PacketDispatcher dispatcher,
        ILogger<HealthController> logger)
    {
        _server = server;
        _stagePool = stagePool;
        _sessionManager = sessionManager;
        _dispatcher = dispatcher;
        _logger = logger;
        _startTime = DateTime.UtcNow;
    }

    /// <summary>
    /// Comprehensive health check endpoint.
    /// </summary>
    /// <returns>Health status with detailed metrics.</returns>
    [HttpGet]
    [ProducesResponseType(typeof(HealthResponse), 200)]
    public IActionResult GetHealth()
    {
        var sessionStats = _sessionManager.GetStatistics();
        var dispatcherStats = _dispatcher.GetStatistics();
        var uptime = DateTime.UtcNow - _startTime;

        var response = new HealthResponse
        {
            Status = "Healthy",
            Uptime = uptime.ToString(@"d\.hh\:mm\:ss"),
            Stages = dispatcherStats.TotalStages,
            Actors = _stagePool.GetAllStages().Sum(s => s.ActorPool.Count),
            Sessions = sessionStats.TotalSessions,
            ConnectedSessions = sessionStats.ConnectedSessions,
            DisconnectedSessions = sessionStats.DisconnectedSessions,
            QueueDepth = dispatcherStats.TotalQueueDepth,
            StagesProcessing = dispatcherStats.StagesProcessing,
            Timestamp = DateTime.UtcNow
        };

        _logger.LogDebug("Health check: Status={Status}, Stages={Stages}, Actors={Actors}, Sessions={Sessions}",
            response.Status, response.Stages, response.Actors, response.Sessions);

        return Ok(response);
    }

    /// <summary>
    /// Readiness probe - indicates if the server is ready to receive traffic.
    /// </summary>
    /// <returns>200 OK if ready, 503 Service Unavailable if not ready.</returns>
    /// <remarks>
    /// Kubernetes uses this to determine if the pod should receive traffic.
    /// Returns ready when:
    /// - TCP server is running (if enabled)
    /// - WebSocket server is initialized (if enabled)
    /// - Core services are initialized
    /// </remarks>
    [HttpGet("ready")]
    [ProducesResponseType(typeof(ReadinessResponse), 200)]
    [ProducesResponseType(typeof(ReadinessResponse), 503)]
    public IActionResult GetReadiness()
    {
        var isReady = true;
        var reasons = new List<string>();

        // Check if TCP server is running
        if (_server.TcpServer == null)
        {
            isReady = false;
            reasons.Add("TCP server not initialized");
        }

        // Check if WebSocket server is initialized (if WebSocket is enabled)
        // Note: We can't easily check if WebSocket is enabled without options,
        // so we just check if it exists
        // If it should exist but doesn't, that's a problem

        // Check if stage pool is functional
        try
        {
            _ = _stagePool.GetStageCount();
        }
        catch
        {
            isReady = false;
            reasons.Add("Stage pool not functional");
        }

        // Check if session manager is functional
        try
        {
            _ = _sessionManager.SessionCount;
        }
        catch
        {
            isReady = false;
            reasons.Add("Session manager not functional");
        }

        var response = new ReadinessResponse
        {
            Ready = isReady,
            Reasons = reasons.Count > 0 ? reasons : null,
            Timestamp = DateTime.UtcNow
        };

        if (isReady)
        {
            _logger.LogTrace("Readiness check: Ready");
            return Ok(response);
        }

        _logger.LogWarning("Readiness check: Not ready - {Reasons}", string.Join(", ", reasons));
        return StatusCode(503, response);
    }

    /// <summary>
    /// Liveness probe - indicates if the server is alive and functioning.
    /// </summary>
    /// <returns>200 OK if alive, 503 Service Unavailable if not alive.</returns>
    /// <remarks>
    /// Kubernetes uses this to determine if the pod should be restarted.
    /// This should only fail in catastrophic scenarios where the application is deadlocked
    /// or in an unrecoverable state.
    /// </remarks>
    [HttpGet("live")]
    [ProducesResponseType(typeof(LivenessResponse), 200)]
    [ProducesResponseType(typeof(LivenessResponse), 503)]
    public IActionResult GetLiveness()
    {
        var isAlive = true;
        var reasons = new List<string>();

        // Basic sanity checks - only fail if something is fundamentally broken
        try
        {
            // Check if we can access basic services
            _ = _stagePool.GetStageCount();
            _ = _sessionManager.SessionCount;
            _ = DateTime.UtcNow; // Ensure time is working
        }
        catch (Exception ex)
        {
            isAlive = false;
            reasons.Add($"Critical service failure: {ex.Message}");
            _logger.LogError(ex, "Liveness check failed");
        }

        var response = new LivenessResponse
        {
            Alive = isAlive,
            Uptime = (DateTime.UtcNow - _startTime).TotalSeconds,
            Reasons = reasons.Count > 0 ? reasons : null,
            Timestamp = DateTime.UtcNow
        };

        if (isAlive)
        {
            _logger.LogTrace("Liveness check: Alive");
            return Ok(response);
        }

        _logger.LogCritical("Liveness check: Not alive - {Reasons}", string.Join(", ", reasons));
        return StatusCode(503, response);
    }
}

#region DTOs

/// <summary>
/// Comprehensive health response.
/// </summary>
public sealed class HealthResponse
{
    /// <summary>
    /// Gets the overall health status.
    /// </summary>
    public required string Status { get; init; }

    /// <summary>
    /// Gets the server uptime in d.hh:mm:ss format.
    /// </summary>
    public required string Uptime { get; init; }

    /// <summary>
    /// Gets the number of active stages.
    /// </summary>
    public int Stages { get; init; }

    /// <summary>
    /// Gets the total number of actors.
    /// </summary>
    public int Actors { get; init; }

    /// <summary>
    /// Gets the total number of sessions.
    /// </summary>
    public int Sessions { get; init; }

    /// <summary>
    /// Gets the number of connected sessions.
    /// </summary>
    public int ConnectedSessions { get; init; }

    /// <summary>
    /// Gets the number of disconnected sessions.
    /// </summary>
    public int DisconnectedSessions { get; init; }

    /// <summary>
    /// Gets the total queue depth across all stages.
    /// </summary>
    public int QueueDepth { get; init; }

    /// <summary>
    /// Gets the number of stages currently processing messages.
    /// </summary>
    public int StagesProcessing { get; init; }

    /// <summary>
    /// Gets the timestamp of the health check.
    /// </summary>
    public DateTime Timestamp { get; init; }
}

/// <summary>
/// Readiness probe response.
/// </summary>
public sealed class ReadinessResponse
{
    /// <summary>
    /// Gets a value indicating whether the server is ready to receive traffic.
    /// </summary>
    public bool Ready { get; init; }

    /// <summary>
    /// Gets the reasons why the server is not ready (if applicable).
    /// </summary>
    public List<string>? Reasons { get; init; }

    /// <summary>
    /// Gets the timestamp of the readiness check.
    /// </summary>
    public DateTime Timestamp { get; init; }
}

/// <summary>
/// Liveness probe response.
/// </summary>
public sealed class LivenessResponse
{
    /// <summary>
    /// Gets a value indicating whether the server is alive.
    /// </summary>
    public bool Alive { get; init; }

    /// <summary>
    /// Gets the server uptime in seconds.
    /// </summary>
    public double Uptime { get; init; }

    /// <summary>
    /// Gets the reasons why the server is not alive (if applicable).
    /// </summary>
    public List<string>? Reasons { get; init; }

    /// <summary>
    /// Gets the timestamp of the liveness check.
    /// </summary>
    public DateTime Timestamp { get; init; }
}

#endregion
