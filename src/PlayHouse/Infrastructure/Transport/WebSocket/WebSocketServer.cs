#nullable enable

using System.Collections.Concurrent;
using System.Net.WebSockets;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace PlayHouse.Infrastructure.Transport.WebSocket;

/// <summary>
/// WebSocket server using ASP.NET Core middleware.
/// Manages multiple concurrent WebSocket sessions.
/// </summary>
public sealed class WebSocketServer
{
    private readonly ILogger<WebSocketServer> _logger;
    private readonly Func<long, System.Net.WebSockets.WebSocket, Task<WebSocketSession>> _sessionFactory;
    private readonly ConcurrentDictionary<long, WebSocketSession> _sessions;
    private long _nextSessionId;

    /// <summary>
    /// Initializes a new WebSocket server.
    /// </summary>
    /// <param name="sessionFactory">Factory function for creating new sessions.</param>
    /// <param name="logger">Logger instance.</param>
    public WebSocketServer(
        Func<long, System.Net.WebSockets.WebSocket, Task<WebSocketSession>> sessionFactory,
        ILogger<WebSocketServer> logger)
    {
        _sessionFactory = sessionFactory;
        _logger = logger;
        _sessions = new ConcurrentDictionary<long, WebSocketSession>();
        _nextSessionId = 1;
    }

    /// <summary>
    /// Gets the number of active sessions.
    /// </summary>
    public int SessionCount => _sessions.Count;

    /// <summary>
    /// Handles an incoming WebSocket request.
    /// This should be called from ASP.NET Core middleware.
    /// </summary>
    /// <param name="context">The HTTP context.</param>
    public async Task HandleWebSocketAsync(HttpContext context)
    {
        if (!context.WebSockets.IsWebSocketRequest)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            await context.Response.WriteAsync("Not a WebSocket request");
            return;
        }

        var webSocket = await context.WebSockets.AcceptWebSocketAsync();
        var sessionId = Interlocked.Increment(ref _nextSessionId);

        _logger.LogInformation("Accepted WebSocket connection: session {SessionId} from {RemoteIpAddress}",
            sessionId, context.Connection.RemoteIpAddress);

        try
        {
            var session = await _sessionFactory(sessionId, webSocket);

            if (_sessions.TryAdd(sessionId, session))
            {
                await session.StartAsync();

                // Wait until the session is closed
                while (session.IsConnected)
                {
                    await Task.Delay(100);
                }
            }
            else
            {
                _logger.LogError("Failed to add WebSocket session {SessionId} to dictionary", sessionId);
                await session.DisposeAsync();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling WebSocket session {SessionId}", sessionId);
        }
        finally
        {
            RemoveSession(sessionId);
        }
    }

    /// <summary>
    /// Gets a session by ID.
    /// </summary>
    /// <param name="sessionId">The session identifier.</param>
    /// <returns>The session if found; otherwise, null.</returns>
    public WebSocketSession? GetSession(long sessionId)
    {
        _sessions.TryGetValue(sessionId, out var session);
        return session;
    }

    /// <summary>
    /// Disconnects a specific session.
    /// </summary>
    /// <param name="sessionId">The session identifier.</param>
    public async Task DisconnectSessionAsync(long sessionId)
    {
        if (_sessions.TryGetValue(sessionId, out var session))
        {
            await session.DisconnectAsync();
        }
    }

    /// <summary>
    /// Disconnects all active sessions.
    /// </summary>
    public async Task DisconnectAllSessionsAsync()
    {
        var disconnectTasks = _sessions.Values.Select(session => session.DisconnectAsync().AsTask());
        await Task.WhenAll(disconnectTasks);
        _sessions.Clear();
    }

    internal void RemoveSession(long sessionId)
    {
        _sessions.TryRemove(sessionId, out _);
        _logger.LogInformation("WebSocket session {SessionId} removed", sessionId);
    }
}
