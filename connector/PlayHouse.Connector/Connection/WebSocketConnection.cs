#nullable enable

namespace PlayHouse.Connector.Connection;

using System.Buffers;
using System.Net.WebSockets;

/// <summary>
/// WebSocket 기반 연결 구현
/// </summary>
internal sealed class WebSocketConnection : IConnection
{
    private readonly ConnectorConfig _config;
    private ClientWebSocket? _webSocket;
    private CancellationTokenSource? _receiveCts;
    private Task? _receiveTask;
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private volatile bool _isConnected;

    private const int ReceiveBufferSize = 65536;

    public event EventHandler<byte[]>? DataReceived;
    public event EventHandler<Exception?>? Disconnected;

    public bool IsConnected => _isConnected;

    public WebSocketConnection(ConnectorConfig config)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
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

            _webSocket = new ClientWebSocket();

            // Configure options
            _webSocket.Options.KeepAliveInterval = TimeSpan.FromMilliseconds(_config.HeartBeatIntervalMs);

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(_config.ConnectionIdleTimeoutMs);

            await _webSocket.ConnectAsync(uri, timeoutCts.Token).ConfigureAwait(false);

            _isConnected = true;

            // Start receiving data
            _receiveCts = new CancellationTokenSource();
            _receiveTask = ReceiveLoopAsync(_receiveCts.Token);
        }
        catch (Exception)
        {
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
            catch (Exception)
            {
                // Ignore errors during graceful close
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
            catch (Exception)
            {
                // Ignore errors during disconnect
            }
        }

        await CleanupAsync().ConfigureAwait(false);
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
        }
        catch (Exception ex)
        {
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
        var buffer = ArrayPool<byte>.Shared.Rent(ReceiveBufferSize);
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
                    HandleDisconnection(ex);
                    break;
                }
                catch (Exception ex)
                {
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
        Disconnected?.Invoke(this, exception);
    }

    private Task CleanupAsync()
    {
        _receiveCts?.Dispose();
        _receiveCts = null;

        _webSocket?.Dispose();
        _webSocket = null;

        return Task.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        await DisconnectAsync().ConfigureAwait(false);
        _sendLock.Dispose();
    }
}
