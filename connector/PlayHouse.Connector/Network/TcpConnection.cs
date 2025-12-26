#nullable enable

using System;
using System.Buffers;
using System.IO;
using System.Net.Security;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace PlayHouse.Connector.Network;

/// <summary>
/// TCP 기반 연결 구현
/// </summary>
internal sealed class TcpConnection : IConnection
{
    private readonly ConnectorConfig _config;
    private TcpClient? _client;
    private Stream? _stream; // NetworkStream 또는 SslStream
    private CancellationTokenSource? _receiveCts;
    private Task? _receiveTask;
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private volatile bool _isConnected;

    private const int SendBufferSize = 65536;
    private const int ReceiveBufferSize = 65536;

    public event EventHandler<ReadOnlyMemory<byte>>? DataReceived;
    public event EventHandler<Exception?>? Disconnected;

    public bool IsConnected => _isConnected;

    public TcpConnection(ConnectorConfig config)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
    }

    public async Task ConnectAsync(string host, int port, bool useSsl = false, CancellationToken cancellationToken = default)
    {
        if (_isConnected)
        {
            throw new InvalidOperationException("Already connected.");
        }

        try
        {
            _client = new TcpClient
            {
                SendBufferSize = SendBufferSize,
                ReceiveBufferSize = ReceiveBufferSize,
                NoDelay = true
            };

            // Configure keep-alive
            _client.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);

            // netstandard2.1에서는 TcpClient.ConnectAsync가 CancellationToken을 지원하지 않음
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(_config.ConnectionIdleTimeoutMs);

            // Task.WhenAny를 사용하여 타임아웃 처리
            var connectTask = _client.ConnectAsync(host, port);
            var timeoutTask = Task.Delay(_config.ConnectionIdleTimeoutMs, timeoutCts.Token);
            var completedTask = await Task.WhenAny(connectTask, timeoutTask).ConfigureAwait(false);

            if (completedTask == timeoutTask)
            {
                throw new TimeoutException($"Connection timeout after {_config.ConnectionIdleTimeoutMs}ms");
            }

            await connectTask.ConfigureAwait(false);

            var networkStream = _client.GetStream();

            // SSL/TLS 래핑
            if (useSsl)
            {
                var sslStream = new SslStream(networkStream, false);
                await sslStream.AuthenticateAsClientAsync(host).ConfigureAwait(false);
                _stream = sslStream;
            }
            else
            {
                _stream = networkStream;
            }

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
        if (!_isConnected || _stream == null)
        {
            throw new InvalidOperationException("Not connected.");
        }

        await _sendLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await _stream.WriteAsync(data, cancellationToken).ConfigureAwait(false);
            await _stream.FlushAsync(cancellationToken).ConfigureAwait(false);
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
        try
        {
            while (!cancellationToken.IsCancellationRequested && _isConnected && _stream != null)
            {
                try
                {
                    var bytesRead = await _stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);

                    if (bytesRead == 0)
                    {
                        HandleDisconnection(null);
                        break;
                    }

                    // Create a copy for the consumer to own
                    var data = new byte[bytesRead];
                    new ReadOnlySpan<byte>(buffer, 0, bytesRead).CopyTo(data);

                    DataReceived?.Invoke(this, new ReadOnlyMemory<byte>(data));
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    // Expected when canceling
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
