#nullable enable

using System.Collections.Concurrent;
using System.Net.WebSockets;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace PlayHouse.Runtime.ClientTransport.WebSocket;

/// <summary>
/// WebSocket transport server using ASP.NET Core middleware.
/// </summary>
/// <remarks>
/// To use this server, add the middleware in your ASP.NET Core pipeline:
/// <code>
/// app.UseWebSockets();
/// app.Map("/ws", async context => await webSocketServer.HandleAsync(context));
/// </code>
/// For WSS (WebSocket over HTTPS), configure HTTPS in your ASP.NET Core application.
/// </remarks>
public sealed class WebSocketTransportServer : ITransportServer
{
    private readonly string _path;
    private readonly TransportOptions _options;
    private readonly MessageReceivedCallback _onMessage;
    private readonly SessionDisconnectedCallback _onDisconnect;
    private readonly ILogger _logger;

    private readonly ConcurrentDictionary<long, WebSocketTransportSession> _sessions = new();
    private readonly CancellationTokenSource _cts = new();

    private long _nextSessionId;
    private bool _disposed;
    private bool _started;

    public int SessionCount => _sessions.Count;

    /// <summary>
    /// Gets the WebSocket path this server handles.
    /// </summary>
    public string Path => _path;

    /// <summary>
    /// Creates a new WebSocket transport server.
    /// </summary>
    /// <param name="path">The URL path to handle WebSocket connections (e.g., "/ws").</param>
    /// <param name="options">Transport options.</param>
    /// <param name="onMessage">Message received callback.</param>
    /// <param name="onDisconnect">Session disconnected callback.</param>
    /// <param name="logger">Logger instance.</param>
    public WebSocketTransportServer(
        string path,
        TransportOptions options,
        MessageReceivedCallback onMessage,
        SessionDisconnectedCallback onDisconnect,
        ILogger logger)
    {
        _path = path;
        _options = options;
        _onMessage = onMessage;
        _onDisconnect = onDisconnect;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        _started = true;
        _logger.LogInformation("WebSocket server started on path {Path}", _path);
        return Task.CompletedTask;
    }

    public async Task StopAsync()
    {
        if (_disposed) return;

        _logger.LogInformation("Stopping WebSocket server on path {Path}", _path);

        _cts.Cancel();
        await DisconnectAllSessionsAsync();

        _started = false;
    }

    /// <summary>
    /// Handles an incoming HTTP request for WebSocket upgrade.
    /// </summary>
    /// <param name="context">The HTTP context.</param>
    public async Task HandleAsync(HttpContext context)
    {
        if (!_started || _disposed)
        {
            context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            return;
        }

        if (!context.WebSockets.IsWebSocketRequest)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            await context.Response.WriteAsync("WebSocket request expected");
            return;
        }

        var webSocket = await context.WebSockets.AcceptWebSocketAsync();
        var sessionId = Interlocked.Increment(ref _nextSessionId);

        _logger.LogDebug("WebSocket connection accepted: session {SessionId} from {RemoteIp}",
            sessionId, context.Connection.RemoteIpAddress);

        var session = new WebSocketTransportSession(
            sessionId,
            webSocket,
            _options,
            _onMessage,
            OnSessionDisconnected,
            _logger,
            _cts.Token);

        if (_sessions.TryAdd(sessionId, session))
        {
            // Keep the connection open until the session is disposed
            while (session.IsConnected && !_cts.Token.IsCancellationRequested)
            {
                await Task.Delay(100, _cts.Token);
            }
        }
        else
        {
            await session.DisposeAsync();
        }
    }

    public ITransportSession? GetSession(long sessionId)
    {
        _sessions.TryGetValue(sessionId, out var session);
        return session;
    }

    public async ValueTask DisconnectSessionAsync(long sessionId)
    {
        if (_sessions.TryGetValue(sessionId, out var session))
        {
            await session.DisconnectAsync();
        }
    }

    public async Task DisconnectAllSessionsAsync()
    {
        var tasks = _sessions.Values.Select(s => s.DisconnectAsync().AsTask());
        await Task.WhenAll(tasks);
        _sessions.Clear();
    }

    public IEnumerable<ITransportSession> GetAllSessions()
    {
        return _sessions.Values;
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        await StopAsync();
        _cts.Dispose();
    }

    private void OnSessionDisconnected(ITransportSession session, Exception? ex)
    {
        _sessions.TryRemove(session.SessionId, out _);
        _onDisconnect(session, ex);
    }
}
