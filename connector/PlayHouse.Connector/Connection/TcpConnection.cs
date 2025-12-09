namespace PlayHouse.Connector.Connection;

using System.Buffers;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;

/// <summary>
/// TCP-based connection implementation.
/// </summary>
internal sealed class TcpConnection : IConnection
{
    private readonly PlayHouseClientOptions _options;
    private readonly ILogger<TcpConnection>? _logger;
    private TcpClient? _client;
    private NetworkStream? _stream;
    private CancellationTokenSource? _receiveCts;
    private Task? _receiveTask;
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private volatile bool _isConnected;

    public event EventHandler<byte[]>? DataReceived;
    public event EventHandler<Exception?>? Disconnected;

    public bool IsConnected => _isConnected;

    public TcpConnection(PlayHouseClientOptions options, ILogger<TcpConnection>? logger = null)
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
            _logger?.LogDebug("Connecting to TCP server {Host}:{Port}", host, port);

            _client = new TcpClient
            {
                SendBufferSize = _options.SendBufferSize,
                ReceiveBufferSize = _options.ReceiveBufferSize,
                NoDelay = _options.TcpNoDelay
            };

            // Configure keep-alive
            if (_options.TcpKeepAlive)
            {
                _client.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);
            }

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(_options.ConnectionTimeout);

            await _client.ConnectAsync(host, port, timeoutCts.Token).ConfigureAwait(false);

            _stream = _client.GetStream();
            _isConnected = true;

            // Start receiving data
            _receiveCts = new CancellationTokenSource();
            _receiveTask = ReceiveLoopAsync(_receiveCts.Token);

            _logger?.LogInformation("Connected to TCP server {Host}:{Port}", host, port);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to connect to TCP server {Host}:{Port}", host, port);
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

        _logger?.LogDebug("Disconnecting from TCP server");

        _isConnected = false;

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

        _logger?.LogInformation("Disconnected from TCP server");
    }

    public async ValueTask SendAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default)
    {
        if (!_isConnected || _stream == null)
        {
            throw new InvalidOperationException("Not connected.");
        }

        await _sendLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await _stream.WriteAsync(data, cancellationToken).ConfigureAwait(false);
            await _stream.FlushAsync(cancellationToken).ConfigureAwait(false);

            _logger?.LogTrace("Sent {ByteCount} bytes to TCP server", data.Length);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to send data to TCP server");
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
        try
        {
            while (!cancellationToken.IsCancellationRequested && _isConnected && _stream != null)
            {
                try
                {
                    var bytesRead = await _stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);

                    if (bytesRead == 0)
                    {
                        _logger?.LogDebug("TCP connection closed by server (0 bytes read)");
                        HandleDisconnection(null);
                        break;
                    }

                    _logger?.LogTrace("Received {ByteCount} bytes from TCP server", bytesRead);

                    // Copy data to avoid buffer reuse issues
                    var data = new byte[bytesRead];
                    Array.Copy(buffer, 0, data, 0, bytesRead);

                    DataReceived?.Invoke(this, data);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    // Expected when canceling
                    break;
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Error receiving data from TCP server");
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
        _logger?.LogWarning(exception, "TCP connection lost");

        Disconnected?.Invoke(this, exception);
    }

    private async Task CleanupAsync()
    {
        _receiveCts?.Dispose();
        _receiveCts = null;

        if (_stream != null)
        {
            await _stream.DisposeAsync().ConfigureAwait(false);
            _stream = null;
        }

        _client?.Dispose();
        _client = null;
    }

    public async ValueTask DisposeAsync()
    {
        await DisconnectAsync().ConfigureAwait(false);
        _sendLock.Dispose();
    }
}
