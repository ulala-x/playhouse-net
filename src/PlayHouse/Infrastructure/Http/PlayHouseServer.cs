#nullable enable

using System.Net;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PlayHouse.Infrastructure.Transport.Tcp;
using PlayHouse.Infrastructure.Transport.WebSocket;

namespace PlayHouse.Infrastructure.Http;

/// <summary>
/// Main PlayHouse server class implementing IHostedService.
/// Manages TCP and WebSocket servers for client connections.
/// </summary>
public sealed class PlayHouseServer : IHostedService, IAsyncDisposable
{
    private readonly ILogger<PlayHouseServer> _logger;
    private readonly PlayHouseOptions _options;
    private readonly ILoggerFactory _loggerFactory;
    private TcpServer? _tcpServer;
    private WebSocketServer? _webSocketServer;
    private bool _disposed;

    /// <summary>
    /// Initializes a new PlayHouse server instance.
    /// </summary>
    /// <param name="options">Server configuration options.</param>
    /// <param name="loggerFactory">Logger factory for creating loggers.</param>
    public PlayHouseServer(
        IOptions<PlayHouseOptions> options,
        ILoggerFactory loggerFactory)
    {
        _options = options.Value;
        _loggerFactory = loggerFactory;
        _logger = loggerFactory.CreateLogger<PlayHouseServer>();
    }

    /// <summary>
    /// Gets the TCP server instance.
    /// </summary>
    public TcpServer? TcpServer => _tcpServer;

    /// <summary>
    /// Gets the WebSocket server instance.
    /// </summary>
    public WebSocketServer? WebSocketServer => _webSocketServer;

    /// <summary>
    /// Starts the PlayHouse server.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Starting PlayHouse server on {Ip}:{Port}", _options.Ip, _options.Port);

        try
        {
            // Initialize TCP server
            _tcpServer = new TcpServer(
                _options.Session,
                CreateTcpSessionAsync,
                _loggerFactory.CreateLogger<TcpServer>());

            var endpoint = new IPEndPoint(IPAddress.Parse(_options.Ip), _options.Port);
            await _tcpServer.StartAsync(endpoint);

            _logger.LogInformation("PlayHouse TCP server started successfully");

            // Initialize WebSocket server if enabled
            if (_options.EnableWebSocket)
            {
                _webSocketServer = new WebSocketServer(
                    CreateWebSocketSessionAsync,
                    _loggerFactory.CreateLogger<WebSocketServer>());

                _logger.LogInformation("PlayHouse WebSocket server initialized on path {Path}",
                    _options.WebSocketPath);
            }

            _logger.LogInformation("PlayHouse server started successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start PlayHouse server");
            throw;
        }
    }

    /// <summary>
    /// Stops the PlayHouse server.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Stopping PlayHouse server");

        try
        {
            if (_tcpServer != null)
            {
                await _tcpServer.StopAsync();
            }

            if (_webSocketServer != null)
            {
                await _webSocketServer.DisconnectAllSessionsAsync();
            }

            _logger.LogInformation("PlayHouse server stopped successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error stopping PlayHouse server");
            throw;
        }
    }

    /// <summary>
    /// Disposes the server and releases all resources.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (_tcpServer != null)
        {
            await _tcpServer.DisposeAsync();
        }

        _logger.LogInformation("PlayHouse server disposed");
    }

    private async Task<TcpSession> CreateTcpSessionAsync(long sessionId, System.Net.Sockets.Socket socket)
    {
        var session = new TcpSession(
            sessionId,
            socket,
            _options.Session,
            OnTcpMessageReceived,
            OnTcpSessionDisconnected,
            _loggerFactory.CreateLogger<TcpSession>());

        await Task.CompletedTask;
        return session;
    }

    private async Task<WebSocketSession> CreateWebSocketSessionAsync(
        long sessionId,
        System.Net.WebSockets.WebSocket webSocket)
    {
        var session = new WebSocketSession(
            sessionId,
            webSocket,
            OnWebSocketMessageReceived,
            OnWebSocketSessionDisconnected,
            _loggerFactory.CreateLogger<WebSocketSession>());

        await Task.CompletedTask;
        return session;
    }

    private void OnTcpMessageReceived(long sessionId, ReadOnlyMemory<byte> data)
    {
        _logger.LogDebug("TCP message received from session {SessionId}: {Size} bytes", sessionId, data.Length);

        // TODO: Process message (deserialize packet, route to appropriate handler)
    }

    private void OnTcpSessionDisconnected(long sessionId, Exception? exception)
    {
        _logger.LogInformation("TCP session {SessionId} disconnected", sessionId);

        if (exception != null)
        {
            _logger.LogWarning(exception, "TCP session {SessionId} disconnected with error", sessionId);
        }

        _tcpServer?.RemoveSession(sessionId);
    }

    private void OnWebSocketMessageReceived(long sessionId, ReadOnlyMemory<byte> data)
    {
        _logger.LogDebug("WebSocket message received from session {SessionId}: {Size} bytes",
            sessionId, data.Length);

        // TODO: Process message (deserialize packet, route to appropriate handler)
    }

    private void OnWebSocketSessionDisconnected(long sessionId, Exception? exception)
    {
        _logger.LogInformation("WebSocket session {SessionId} disconnected", sessionId);

        if (exception != null)
        {
            _logger.LogWarning(exception, "WebSocket session {SessionId} disconnected with error", sessionId);
        }

        _webSocketServer?.RemoveSession(sessionId);
    }
}
