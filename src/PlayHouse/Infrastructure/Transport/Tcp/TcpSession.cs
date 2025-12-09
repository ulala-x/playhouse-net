#nullable enable

using System.Buffers;
using System.Buffers.Binary;
using System.IO.Pipelines;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;

namespace PlayHouse.Infrastructure.Transport.Tcp;

/// <summary>
/// TCP client session using System.IO.Pipelines for efficient I/O.
/// Manages bidirectional communication with automatic framing (length-prefixed messages).
/// </summary>
public sealed class TcpSession : IAsyncDisposable
{
    private readonly Socket _socket;
    private readonly Pipe _receivePipe;
    private readonly Pipe _sendPipe;
    private readonly CancellationTokenSource _cts;
    private readonly long _sessionId;
    private readonly ILogger<TcpSession> _logger;
    private readonly Action<long, ReadOnlyMemory<byte>> _onMessageReceived;
    private readonly Action<long, Exception?> _onDisconnected;
    private readonly TcpSessionOptions _options;
    private readonly Task _receiveTask;
    private readonly Task _sendTask;
    private readonly Task _processTask;
    private DateTime _lastHeartbeat;
    private bool _disposed;

    /// <summary>
    /// Initializes a new TCP session.
    /// </summary>
    /// <param name="sessionId">Unique session identifier.</param>
    /// <param name="socket">Connected socket.</param>
    /// <param name="options">Session configuration options.</param>
    /// <param name="onMessageReceived">Callback invoked when a complete message is received.</param>
    /// <param name="onDisconnected">Callback invoked when the session disconnects.</param>
    /// <param name="logger">Logger instance.</param>
    public TcpSession(
        long sessionId,
        Socket socket,
        TcpSessionOptions options,
        Action<long, ReadOnlyMemory<byte>> onMessageReceived,
        Action<long, Exception?> onDisconnected,
        ILogger<TcpSession> logger)
    {
        _sessionId = sessionId;
        _socket = socket;
        _options = options;
        _onMessageReceived = onMessageReceived;
        _onDisconnected = onDisconnected;
        _logger = logger;
        _cts = new CancellationTokenSource();
        _lastHeartbeat = DateTime.UtcNow;

        // Configure pipes with memory management
        var pipeOptions = new PipeOptions(
            pauseWriterThreshold: options.PauseWriterThreshold,
            resumeWriterThreshold: options.ResumeWriterThreshold,
            useSynchronizationContext: false);

        _receivePipe = new Pipe(pipeOptions);
        _sendPipe = new Pipe(pipeOptions);

        // Configure socket
        ConfigureSocket();

        // Start I/O tasks
        _receiveTask = Task.Run(() => ReceiveLoopAsync(_cts.Token), _cts.Token);
        _sendTask = Task.Run(() => SendLoopAsync(_cts.Token), _cts.Token);
        _processTask = Task.Run(() => ProcessMessagesAsync(_cts.Token), _cts.Token);

        _logger.LogInformation("TCP session {SessionId} started", _sessionId);
    }

    /// <summary>
    /// Gets the unique session identifier.
    /// </summary>
    public long SessionId => _sessionId;

    /// <summary>
    /// Gets whether the session is currently connected.
    /// </summary>
    public bool IsConnected => !_disposed && _socket.Connected;

    /// <summary>
    /// Starts the session and begins processing messages.
    /// </summary>
    public async Task StartAsync()
    {
        await Task.CompletedTask;
    }

    /// <summary>
    /// Sends data to the remote endpoint.
    /// Data is automatically framed with a 4-byte length prefix.
    /// </summary>
    /// <param name="data">The data to send.</param>
    public async ValueTask SendAsync(ReadOnlyMemory<byte> data)
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(TcpSession));
        }

        try
        {
            // Create buffer with length prefix
            var buffer = new byte[4 + data.Length];
            BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(0, 4), data.Length);
            data.CopyTo(buffer.AsMemory(4));

            // Send directly to socket (bypassing send pipe for immediate delivery)
            await _socket.SendAsync(buffer, SocketFlags.None, _cts.Token);

            _logger.LogTrace("Sent {ByteCount} bytes to session {SessionId}", buffer.Length, _sessionId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending data on session {SessionId}", _sessionId);
            await DisconnectAsync();
            throw;
        }
    }

    /// <summary>
    /// Gracefully disconnects the session.
    /// </summary>
    public async ValueTask DisconnectAsync()
    {
        if (_disposed)
        {
            return;
        }

        _logger.LogInformation("Disconnecting session {SessionId}", _sessionId);
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

            // Wait for tasks to complete
            await Task.WhenAll(_receiveTask, _sendTask, _processTask).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Expected during cancellation
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during session {SessionId} disposal", _sessionId);
        }
        finally
        {
            _socket.Close();
            _socket.Dispose();
            _cts.Dispose();

            _onDisconnected(_sessionId, null);

            _logger.LogInformation("TCP session {SessionId} disposed", _sessionId);
        }
    }

    private void ConfigureSocket()
    {
        _socket.NoDelay = true; // Disable Nagle's algorithm
        _socket.ReceiveBufferSize = _options.ReceiveBufferSize;
        _socket.SendBufferSize = _options.SendBufferSize;

        if (_options.EnableKeepAlive)
        {
            _socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);
            // Note: Platform-specific keep-alive settings may require native calls
        }
    }

    private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
    {
        var writer = _receivePipe.Writer;

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var memory = writer.GetMemory(_options.ReceiveBufferSize);
                var bytesRead = await _socket.ReceiveAsync(memory, SocketFlags.None, cancellationToken);

                if (bytesRead == 0)
                {
                    _logger.LogInformation("Session {SessionId} closed by remote endpoint", _sessionId);
                    break;
                }

                writer.Advance(bytesRead);
                var result = await writer.FlushAsync(cancellationToken);

                if (result.IsCompleted)
                {
                    break;
                }

                _lastHeartbeat = DateTime.UtcNow;
            }
        }
        catch (OperationCanceledException)
        {
            // Expected during shutdown
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in receive loop for session {SessionId}", _sessionId);
        }
        finally
        {
            await writer.CompleteAsync();
        }
    }

    private async Task SendLoopAsync(CancellationToken cancellationToken)
    {
        var reader = _sendPipe.Reader;

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var result = await reader.ReadAsync(cancellationToken);
                var buffer = result.Buffer;

                if (!buffer.IsEmpty)
                {
                    foreach (var segment in buffer)
                    {
                        await _socket.SendAsync(segment, SocketFlags.None, cancellationToken);
                    }

                    reader.AdvanceTo(buffer.End);
                }

                if (result.IsCompleted)
                {
                    break;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Expected during shutdown
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in send loop for session {SessionId}", _sessionId);
        }
        finally
        {
            await reader.CompleteAsync();
        }
    }

    private async Task ProcessMessagesAsync(CancellationToken cancellationToken)
    {
        var reader = _receivePipe.Reader;

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var result = await reader.ReadAsync(cancellationToken);
                var buffer = result.Buffer;

                while (TryReadMessage(ref buffer, out var message))
                {
                    _onMessageReceived(_sessionId, message);
                }

                reader.AdvanceTo(buffer.Start, buffer.End);

                if (result.IsCompleted)
                {
                    break;
                }

                // Check heartbeat timeout
                if (DateTime.UtcNow - _lastHeartbeat > _options.HeartbeatTimeout)
                {
                    _logger.LogWarning("Session {SessionId} heartbeat timeout", _sessionId);
                    break;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Expected during shutdown
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing messages for session {SessionId}", _sessionId);
        }
        finally
        {
            await reader.CompleteAsync();
        }
    }

    private bool TryReadMessage(ref ReadOnlySequence<byte> buffer, out ReadOnlyMemory<byte> message)
    {
        message = ReadOnlyMemory<byte>.Empty;

        // Need at least 4 bytes for length prefix
        if (buffer.Length < 4)
        {
            return false;
        }

        // Read length prefix
        Span<byte> lengthBytes = stackalloc byte[4];
        buffer.Slice(0, 4).CopyTo(lengthBytes);
        var messageLength = BinaryPrimitives.ReadInt32LittleEndian(lengthBytes);

        // Validate message length
        if (messageLength <= 0 || messageLength > _options.MaxPacketSize)
        {
            throw new InvalidDataException($"Invalid message length: {messageLength}");
        }

        // Check if we have the complete message
        if (buffer.Length < 4 + messageLength)
        {
            return false;
        }

        // Extract message
        var messageBuffer = buffer.Slice(4, messageLength);
        message = messageBuffer.ToArray();

        // Advance buffer
        buffer = buffer.Slice(4 + messageLength);

        return true;
    }
}
