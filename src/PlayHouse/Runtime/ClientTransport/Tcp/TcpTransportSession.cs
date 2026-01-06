#nullable enable

using System.Buffers;
using System.Buffers.Binary;
using System.IO.Pipelines;
using System.Net.Sockets;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using PlayHouse.Abstractions;

namespace PlayHouse.Runtime.ClientTransport.Tcp;

/// <summary>
/// TCP transport session using System.IO.Pipelines for efficient I/O.
/// </summary>
/// <remarks>
/// Uses length-prefixed message framing and zero-copy buffer management.
/// Message processing is fire-and-forget for high throughput.
/// </remarks>
internal sealed class TcpTransportSession : ITransportSession
{
    private readonly Socket _socket;
    private readonly Stream _stream;
    private readonly TransportOptions _options;
    private readonly MessageReceivedCallback _onMessage;
    private readonly SessionDisconnectedCallback _onDisconnect;
    private readonly ILogger? _logger;
    private readonly CancellationTokenSource _cts;
    private Task? _receiveAndProcessTask;
    private readonly Channel<SendItem> _sendChannel;
    private Task? _sendTask;

    private DateTime _lastActivity;
    private bool _disposed;

    private readonly record struct SendItem(byte[] Buffer, int Size);

    public long SessionId { get; }
    public string AccountId { get; set; } = string.Empty;
    public bool IsAuthenticated { get; set; }
    public long StageId { get; set; }
    public bool IsConnected => !_disposed && _socket.Connected;
    public object? ProcessorContext { get; set; }

    internal TcpTransportSession(
        long sessionId,
        Socket socket,
        Stream stream,
        TransportOptions options,
        MessageReceivedCallback onMessage,
        SessionDisconnectedCallback onDisconnect,
        ILogger? logger,
        CancellationToken externalCt)
    {
        SessionId = sessionId;
        _socket = socket;
        _stream = stream;
        _options = options;
        _onMessage = onMessage;
        _onDisconnect = onDisconnect;
        _logger = logger;
        _cts = CancellationTokenSource.CreateLinkedTokenSource(externalCt);
        _lastActivity = DateTime.UtcNow;

        // Configure socket
        ConfigureSocket();

        // Configure send channel
        _sendChannel = Channel.CreateUnbounded<SendItem>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false
        });
    }

    /// <summary>
    /// Starts the I/O tasks for receiving and sending messages.
    /// Combined receiving and processing into one task to minimize overhead.
    /// </summary>
    public void Start()
    {
        _receiveAndProcessTask = Task.Run(() => ReceiveAndProcessLoopAsync(_cts.Token));
        _sendTask = Task.Run(() => SendLoopAsync(_cts.Token));

        _logger?.LogDebug("TCP session {SessionId} started (2 tasks)", SessionId);
    }

    public async ValueTask SendAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default)
    {
        if (_disposed) return;

        try
        {
            await _stream.WriteAsync(data, cancellationToken);
            await _stream.FlushAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error sending data on session {SessionId}", SessionId);
            await DisconnectAsync();
        }
    }

    /// <summary>
    /// Sends a response by enqueueing it to the send channel.
    /// Thread-safe for concurrent calls.
    /// </summary>
    public void SendResponse(
        string msgId,
        ushort msgSeq,
        long stageId,
        ushort errorCode,
        ReadOnlySpan<byte> payload)
    {
        if (_disposed) return;

        var totalSize = MessageCodec.CalculateResponseSize(msgId.Length, payload.Length, includeLengthPrefix: true);

        // Rent buffer from ArrayPool
        var buffer = ArrayPool<byte>.Shared.Rent(totalSize);

        // Prepare buffer synchronously (Span operations)
        PrepareResponseBuffer(buffer, totalSize, msgId, msgSeq, stageId, errorCode, payload);

        // Enqueue to send channel (synchronous)
        _sendChannel.Writer.TryWrite(new SendItem(buffer, totalSize));
    }

    /// <summary>
    /// Prepares the response buffer synchronously (Span operations).
    /// </summary>
    private static void PrepareResponseBuffer(
        byte[] buffer,
        int totalSize,
        string msgId,
        ushort msgSeq,
        long stageId,
        ushort errorCode,
        ReadOnlySpan<byte> payload)
    {
        var span = buffer.AsSpan(0, totalSize);

        // Length prefix (4 bytes)
        var bodySize = totalSize - 4;
        BinaryPrimitives.WriteInt32LittleEndian(span, bodySize);

        // Body
        MessageCodec.WriteResponseBody(span[4..], msgId, msgSeq, stageId, errorCode, payload);
    }

    /// <summary>
    /// Send loop that processes queued messages from the channel using batching.
    /// Reduces system call overhead by flushing only after writing a batch of messages.
    /// </summary>
    private async Task SendLoopAsync(CancellationToken ct)
    {
        try
        {
            while (await _sendChannel.Reader.WaitToReadAsync(ct))
            {
                int batchCount = 0;
                while (batchCount < 100 && _sendChannel.Reader.TryRead(out var item))
                {
                    try
                    {
                        await _stream.WriteAsync(item.Buffer.AsMemory(0, item.Size), ct);
                        batchCount++;
                    }
                    finally
                    {
                        ArrayPool<byte>.Shared.Return(item.Buffer);
                    }
                }

                if (batchCount > 0)
                {
                    await _stream.FlushAsync(ct);
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error in send loop for session {SessionId}", SessionId);
        }
    }

    public async ValueTask DisconnectAsync()
    {
        if (_disposed) return;
        await DisposeAsync();
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        try
        {
            _cts.Cancel();

            // Complete send channel
            _sendChannel.Writer.Complete();

            // Wait for tasks with timeout (if they were started)
            if (_receiveAndProcessTask != null && _sendTask != null)
            {
                var timeout = Task.Delay(TimeSpan.FromSeconds(5));
                var completed = Task.WhenAll(_receiveAndProcessTask, _sendTask);
                await Task.WhenAny(completed, timeout);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error during session {SessionId} disposal", SessionId);
        }
        finally
        {
            try { _stream.Close(); } catch { }
            try { _socket.Close(); } catch { }
            _cts.Dispose();

            _onDisconnect(this, null);
            _logger?.LogDebug("TCP session {SessionId} disposed", SessionId);
        }
    }

    private void ConfigureSocket()
    {
        _socket.NoDelay = true;
        _socket.ReceiveBufferSize = _options.ReceiveBufferSize;
        _socket.SendBufferSize = _options.SendBufferSize;

        if (_options.EnableKeepAlive)
        {
            _socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);
        }
    }

    /// <summary>
    /// Combined loop for receiving data and processing messages.
    /// This eliminates the need for IO.Pipelines and one extra task per session.
    /// </summary>
    private async Task ReceiveAndProcessLoopAsync(CancellationToken ct)
    {
        // Use a linear buffer for receiving and parsing
        byte[] buffer = ArrayPool<byte>.Shared.Rent(_options.ReceiveBufferSize * 2);
        int bytesInBuffer = 0;

        try
        {
            while (!ct.IsCancellationRequested)
            {
                // Read from stream into available buffer space
                var availableMemory = buffer.AsMemory(bytesInBuffer);
                if (availableMemory.Length == 0)
                {
                    // Buffer full - need to expand (should not happen with correct sizing)
                    var newBuffer = ArrayPool<byte>.Shared.Rent(buffer.Length * 2);
                    buffer.AsSpan(0, bytesInBuffer).CopyTo(newBuffer);
                    ArrayPool<byte>.Shared.Return(buffer);
                    buffer = newBuffer;
                    availableMemory = buffer.AsMemory(bytesInBuffer);
                }

                int bytesRead = await _stream.ReadAsync(availableMemory, ct);
                if (bytesRead == 0) break;

                bytesInBuffer += bytesRead;
                _lastActivity = DateTime.UtcNow;

                // Parse and dispatch all complete messages in the buffer
                bytesInBuffer = ParseAndDispatchMessages(buffer, bytesInBuffer);

                // Check heartbeat timeout
                if (DateTime.UtcNow - _lastActivity > _options.HeartbeatTimeout)
                {
                    _logger?.LogWarning("Session {SessionId} heartbeat timeout", SessionId);
                    break;
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            if (!_disposed)
                _logger?.LogError(ex, "Error in receive loop for session {SessionId}", SessionId);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
            await DisconnectAsync();
        }
    }

    /// <summary>
    /// Synchronously parses and dispatches all complete messages in the buffer.
    /// Returns the number of bytes remaining in the buffer.
    /// </summary>
    private int ParseAndDispatchMessages(byte[] buffer, int bytesInBuffer)
    {
        int consumed = 0;
        while (true)
        {
            var remainingSpan = buffer.AsSpan(consumed, bytesInBuffer - consumed);
            if (TryParseMessage(remainingSpan, out var msgId, out var msgSeq, out var stageId, out var payload, out int packetSize))
            {
                try
                {
                    _onMessage(this, msgId, msgSeq, stageId, payload);
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Error processing message {MsgId} on session {SessionId}", msgId, SessionId);
                }
                consumed += packetSize;
            }
            else
            {
                break; // Incomplete packet
            }
        }

        // Move remaining data to the beginning
        if (consumed > 0)
        {
            int remaining = bytesInBuffer - consumed;
            if (remaining > 0)
            {
                buffer.AsSpan(consumed, remaining).CopyTo(buffer);
                return remaining;
            }
            return 0;
        }

        return bytesInBuffer;
    }

    /// <summary>
    /// Parses a single message from the byte span.
    /// </summary>
    private bool TryParseMessage(
        ReadOnlySpan<byte> span,
        out string msgId,
        out ushort msgSeq,
        out long stageId,
        out IPayload payload,
        out int totalPacketSize)
    {
        msgId = string.Empty;
        msgSeq = 0;
        stageId = 0;
        payload = null!;
        totalPacketSize = 0;

        if (span.Length < 4) return false;

        var packetLength = BinaryPrimitives.ReadInt32LittleEndian(span);
        if (packetLength <= 0 || packetLength > _options.MaxPacketSize)
        {
            throw new InvalidDataException($"Invalid packet length: {packetLength}");
        }

        totalPacketSize = 4 + packetLength;
        if (span.Length < totalPacketSize) return false;

        var bodySpan = span.Slice(4, packetLength);
        if (!MessageCodec.TryParseMessage(bodySpan, out msgId, out msgSeq, out stageId, out var payloadOffset))
        {
            throw new InvalidDataException("Invalid message format");
        }

        var payloadLength = packetLength - payloadOffset;
        var rentedBuffer = ArrayPool<byte>.Shared.Rent(payloadLength);
        bodySpan.Slice(payloadOffset, payloadLength).CopyTo(rentedBuffer);
        payload = new ArrayPoolPayload(rentedBuffer, payloadLength);

        return true;
    }
}
