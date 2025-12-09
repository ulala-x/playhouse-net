namespace PlayHouse.Connector.Connection;

using System.Buffers;
using System.Net.WebSockets;
using Microsoft.Extensions.Logging;

/// <summary>
/// WebSocket-based connection implementation.
/// </summary>
internal sealed class WebSocketConnection : IConnection
{
    private readonly PlayHouseClientOptions _options;
    private readonly ILogger<WebSocketConnection>? _logger;
    private ClientWebSocket? _webSocket;
    private CancellationTokenSource? _receiveCts;
    private Task? _receiveTask;
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private volatile bool _isConnected;

    public event EventHandler<byte[]>? DataReceived;
    public event EventHandler<Exception?>? Disconnected;

    public bool IsConnected => _isConnected;

    public WebSocketConnection(PlayHouseClientOptions options, ILogger<WebSocketConnection>? logger = null)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger;
    }

    public async Task ConnectAsync(string host, int port, CancellationToken cancellationToken = default)
    {
        if (_isConnected)
        {
            throw new InvalidOperationException("Already connected.");
        }

        try
        {
            var uri = new Uri($"ws://{host}:{port}");
            _logger?.LogDebug("Connecting to WebSocket server {Uri}", uri);

            _webSocket = new ClientWebSocket();

            // Configure options
            _webSocket.Options.KeepAliveInterval = _options.HeartbeatInterval;

            if (!string.IsNullOrEmpty(_options.WebSocketSubProtocol))
            {
                _webSocket.Options.AddSubProtocol(_options.WebSocketSubProtocol);
            }

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(_options.ConnectionTimeout);

            await _webSocket.ConnectAsync(uri, timeoutCts.Token).ConfigureAwait(false);

            _isConnected = true;

            // Start receiving data
            _receiveCts = new CancellationTokenSource();
            _receiveTask = ReceiveLoopAsync(_receiveCts.Token);

            _logger?.LogInformation("Connected to WebSocket server {Uri}", uri);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to connect to WebSocket server");
            await CleanupAsync().ConfigureAwait(false);
            throw;
        }
    }

    public async Task DisconnectAsync()
    {
        if (!_isConnected)
        {
            return;
        }

        _logger?.LogDebug("Disconnecting from WebSocket server");

        _isConnected = false;

        // Gracefully close WebSocket
        if (_webSocket?.State == WebSocketState.Open)
        {
            try
            {
                using var closeCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                await _webSocket.CloseAsync(
                    WebSocketCloseStatus.NormalClosure,
                    "Client disconnecting",
                    closeCts.Token).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Error during WebSocket graceful close");
            }
        }

        // Cancel receive loop
        _receiveCts?.Cancel();

        // Wait for receive task to complete
        if (_receiveTask != null)
        {
            try
            {
                await _receiveTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Expected when canceling
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Error during receive task completion");
            }
        }

        await CleanupAsync().ConfigureAwait(false);

        _logger?.LogInformation("Disconnected from WebSocket server");
    }

    public async ValueTask SendAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default)
    {
        if (!_isConnected || _webSocket == null || _webSocket.State != WebSocketState.Open)
        {
            throw new InvalidOperationException("Not connected.");
        }

        await _sendLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await _webSocket.SendAsync(
                data,
                WebSocketMessageType.Binary,
                endOfMessage: true,
                cancellationToken).ConfigureAwait(false);

            _logger?.LogTrace("Sent {ByteCount} bytes to WebSocket server", data.Length);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to send data to WebSocket server");
            HandleDisconnection(ex);
            throw;
        }
        finally
        {
            _sendLock.Release();
        }
    }

    private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(_options.ReceiveBufferSize);
        var messageBuffer = new List<byte>();

        try
        {
            while (!cancellationToken.IsCancellationRequested && _isConnected && _webSocket != null)
            {
                try
                {
                    messageBuffer.Clear();

                    WebSocketReceiveResult result;
                    do
                    {
                        result = await _webSocket.ReceiveAsync(
                            new ArraySegment<byte>(buffer),
                            cancellationToken).ConfigureAwait(false);

                        if (result.MessageType == WebSocketMessageType.Close)
                        {
                            _logger?.LogDebug(
                                "WebSocket closed by server: {Status} - {Description}",
                                result.CloseStatus,
                                result.CloseStatusDescription);
                            HandleDisconnection(null);
                            return;
                        }

                        if (result.Count > 0)
                        {
                            messageBuffer.AddRange(buffer.Take(result.Count));
                        }
                    }
                    while (!result.EndOfMessage);

                    if (messageBuffer.Count > 0)
                    {
                        _logger?.LogTrace("Received {ByteCount} bytes from WebSocket server", messageBuffer.Count);

                        var data = messageBuffer.ToArray();
                        DataReceived?.Invoke(this, data);
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    // Expected when canceling
                    break;
                }
                catch (WebSocketException ex) when (ex.WebSocketErrorCode == WebSocketError.ConnectionClosedPrematurely)
                {
                    _logger?.LogWarning("WebSocket connection closed prematurely");
                    HandleDisconnection(ex);
                    break;
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Error receiving data from WebSocket server");
                    HandleDisconnection(ex);
                    break;
                }
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private void HandleDisconnection(Exception? exception)
    {
        if (!_isConnected)
        {
            return;
        }

        _isConnected = false;
        _logger?.LogWarning(exception, "WebSocket connection lost");

        Disconnected?.Invoke(this, exception);
    }

    private async Task CleanupAsync()
    {
        _receiveCts?.Dispose();
        _receiveCts = null;

        if (_webSocket != null)
        {
            _webSocket.Dispose();
            _webSocket = null;
        }
    }

    public async ValueTask DisposeAsync()
    {
        await DisconnectAsync().ConfigureAwait(false);
        _sendLock.Dispose();
    }
}
