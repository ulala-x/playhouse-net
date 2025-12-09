#nullable enable

using System.Buffers;
using System.Net.WebSockets;
using Microsoft.Extensions.Logging;

namespace PlayHouse.Infrastructure.Transport.WebSocket;

/// <summary>
/// WebSocket client session for bidirectional communication.
/// Manages WebSocket connection lifecycle and message framing.
/// </summary>
public sealed class WebSocketSession : IAsyncDisposable
{
    private readonly System.Net.WebSockets.WebSocket _webSocket;
    private readonly long _sessionId;
    private readonly ILogger<WebSocketSession> _logger;
    private readonly Action<long, ReadOnlyMemory<byte>> _onMessageReceived;
    private readonly Action<long, Exception?> _onDisconnected;
    private readonly CancellationTokenSource _cts;
    private readonly Task _receiveTask;
    private bool _disposed;

    private const int MaxMessageSize = 2 * 1024 * 1024; // 2 MB
    private const int ReceiveBufferSize = 64 * 1024; // 64 KB

    /// <summary>
    /// Initializes a new WebSocket session.
    /// </summary>
    /// <param name="sessionId">Unique session identifier.</param>
    /// <param name="webSocket">Connected WebSocket instance.</param>
    /// <param name="onMessageReceived">Callback invoked when a complete message is received.</param>
    /// <param name="onDisconnected">Callback invoked when the session disconnects.</param>
    /// <param name="logger">Logger instance.</param>
    public WebSocketSession(
        long sessionId,
        System.Net.WebSockets.WebSocket webSocket,
        Action<long, ReadOnlyMemory<byte>> onMessageReceived,
        Action<long, Exception?> onDisconnected,
        ILogger<WebSocketSession> logger)
    {
        _sessionId = sessionId;
        _webSocket = webSocket;
        _onMessageReceived = onMessageReceived;
        _onDisconnected = onDisconnected;
        _logger = logger;
        _cts = new CancellationTokenSource();

        _receiveTask = Task.Run(() => ReceiveLoopAsync(_cts.Token), _cts.Token);

        _logger.LogInformation("WebSocket session {SessionId} started", _sessionId);
    }

    /// <summary>
    /// Gets the unique session identifier.
    /// </summary>
    public long SessionId => _sessionId;

    /// <summary>
    /// Gets whether the session is currently connected.
    /// </summary>
    public bool IsConnected => !_disposed && _webSocket.State == WebSocketState.Open;

    /// <summary>
    /// Starts the session and begins processing messages.
    /// </summary>
    public async Task StartAsync()
    {
        await Task.CompletedTask;
    }

    /// <summary>
    /// Sends data to the remote endpoint.
    /// </summary>
    /// <param name="data">The data to send.</param>
    /// <param name="messageType">The WebSocket message type. Default: Binary.</param>
    public async ValueTask SendAsync(
        ReadOnlyMemory<byte> data,
        WebSocketMessageType messageType = WebSocketMessageType.Binary)
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(WebSocketSession));
        }

        if (_webSocket.State != WebSocketState.Open)
        {
            throw new InvalidOperationException($"WebSocket is not open: {_webSocket.State}");
        }

        try
        {
            await _webSocket.SendAsync(data, messageType, endOfMessage: true, _cts.Token);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending data on WebSocket session {SessionId}", _sessionId);
            await DisconnectAsync();
            throw;
        }
    }

    /// <summary>
    /// Sends a text message to the remote endpoint.
    /// </summary>
    /// <param name="text">The text to send.</param>
    public async ValueTask SendTextAsync(string text)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(text);
        await SendAsync(bytes, WebSocketMessageType.Text);
    }

    /// <summary>
    /// Gracefully closes the WebSocket connection.
    /// </summary>
    public async ValueTask DisconnectAsync()
    {
        if (_disposed)
        {
            return;
        }

        _logger.LogInformation("Disconnecting WebSocket session {SessionId}", _sessionId);

        try
        {
            if (_webSocket.State == WebSocketState.Open)
            {
                await _webSocket.CloseAsync(
                    WebSocketCloseStatus.NormalClosure,
                    "Normal closure",
                    CancellationToken.None);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error closing WebSocket session {SessionId}", _sessionId);
        }

        await DisposeAsync();
    }

    /// <summary>
    /// Disposes the session and releases all resources.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        try
        {
            _cts.Cancel();

            // Wait for receive task to complete
            await _receiveTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Expected during cancellation
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during WebSocket session {SessionId} disposal", _sessionId);
        }
        finally
        {
            _webSocket.Dispose();
            _cts.Dispose();

            _onDisconnected(_sessionId, null);

            _logger.LogInformation("WebSocket session {SessionId} disposed", _sessionId);
        }
    }

    private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(ReceiveBufferSize);

        try
        {
            while (!cancellationToken.IsCancellationRequested && _webSocket.State == WebSocketState.Open)
            {
                using var ms = new MemoryStream();
                ValueWebSocketReceiveResult result;

                do
                {
                    result = await _webSocket.ReceiveAsync(new Memory<byte>(buffer), cancellationToken);

                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        _logger.LogInformation(
                            "WebSocket session {SessionId} close requested",
                            _sessionId);

                        await _webSocket.CloseAsync(
                            WebSocketCloseStatus.NormalClosure,
                            "Closing",
                            CancellationToken.None);

                        return;
                    }

                    ms.Write(buffer, 0, result.Count);

                    if (ms.Length > MaxMessageSize)
                    {
                        _logger.LogWarning(
                            "WebSocket session {SessionId} message exceeds max size: {Size}",
                            _sessionId, ms.Length);

                        await _webSocket.CloseAsync(
                            WebSocketCloseStatus.MessageTooBig,
                            "Message too large",
                            CancellationToken.None);

                        return;
                    }
                }
                while (!result.EndOfMessage);

                // Process complete message
                if (ms.Length > 0)
                {
                    var message = ms.ToArray();
                    _onMessageReceived(_sessionId, message);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Expected during shutdown
        }
        catch (WebSocketException ex)
        {
            _logger.LogError(ex, "WebSocket error on session {SessionId}", _sessionId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in receive loop for WebSocket session {SessionId}", _sessionId);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }
}
