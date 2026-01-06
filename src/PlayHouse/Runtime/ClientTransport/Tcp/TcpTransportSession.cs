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
    private readonly Pipe _receivePipe;
    private readonly TransportOptions _options;
    private readonly MessageReceivedCallback _onMessage;
    private readonly SessionDisconnectedCallback _onDisconnect;
    private readonly ILogger? _logger;
    private readonly CancellationTokenSource _cts;
    private Task? _receiveTask;
    private Task? _processTask;
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

        // Configure receive pipe
        var pipeOptions = new PipeOptions(
            pauseWriterThreshold: options.PauseWriterThreshold,
            resumeWriterThreshold: options.ResumeWriterThreshold,
            useSynchronizationContext: false);
        _receivePipe = new Pipe(pipeOptions);

        // Configure send channel
        _sendChannel = Channel.CreateUnbounded<SendItem>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false
        });
    }

    /// <summary>
    /// Starts the I/O tasks for receiving, processing, and sending messages.
    /// This should be called AFTER the session is registered in the session dictionary
    /// to avoid race conditions where messages are processed before the session is findable.
    /// </summary>
    public void Start()
    {
        _receiveTask = Task.Run(() => ReceiveLoopAsync(_cts.Token));
        _processTask = Task.Run(() => ProcessMessagesAsync(_cts.Token));
        _sendTask = Task.Run(() => SendLoopAsync(_cts.Token));

        _logger?.LogDebug("TCP session {SessionId} started", SessionId);
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
            if (_receiveTask != null && _processTask != null && _sendTask != null)
            {
                var timeout = Task.Delay(TimeSpan.FromSeconds(5));
                var completed = Task.WhenAll(_receiveTask, _processTask, _sendTask);
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

    private async Task ReceiveLoopAsync(CancellationToken ct)
    {
        var writer = _receivePipe.Writer;

        try
        {
            while (!ct.IsCancellationRequested)
            {
                var memory = writer.GetMemory(_options.ReceiveBufferSize);
                var bytesRead = await _stream.ReadAsync(memory, ct);

                if (bytesRead == 0)
                {
                    _logger?.LogDebug("Session {SessionId} closed by remote", SessionId);
                    break;
                }

                writer.Advance(bytesRead);
                var result = await writer.FlushAsync(ct);

                if (result.IsCompleted) break;

                _lastActivity = DateTime.UtcNow;
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error in receive loop for session {SessionId}", SessionId);
        }
        finally
        {
            await writer.CompleteAsync();
        }
    }

    private async Task ProcessMessagesAsync(CancellationToken ct)
    {
        var reader = _receivePipe.Reader;

        try
        {
            while (!ct.IsCancellationRequested)
            {
                var result = await reader.ReadAsync(ct);
                var buffer = result.Buffer;

                while (TryParseMessage(ref buffer, out var msgId, out var msgSeq, out var stageId, out var payload))
                {
                    try
                    {
                        // Fire-and-forget: no await for high throughput
                        _onMessage(this, msgId, msgSeq, stageId, payload);
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogError(ex, "Error processing message {MsgId} on session {SessionId}", msgId, SessionId);
                    }
                }

                reader.AdvanceTo(buffer.Start, buffer.End);

                if (result.IsCompleted) break;

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
            _logger?.LogError(ex, "Error processing messages for session {SessionId}", SessionId);
        }
        finally
        {
            await reader.CompleteAsync();
            await DisconnectAsync();
        }
    }

    /// <summary>
    /// Parses a message from the buffer.
    /// TCP adds a 4-byte length prefix for stream framing.
    /// </summary>
    private bool TryParseMessage(
        ref ReadOnlySequence<byte> buffer,
        out string msgId,
        out ushort msgSeq,
        out long stageId,
        out IPayload payload)
    {
        msgId = string.Empty;
        msgSeq = 0;
        stageId = 0;
        payload = null!;

        // Need at least 4 bytes for length prefix
        if (buffer.Length < 4) return false;

        // Read length prefix
        Span<byte> lengthBytes = stackalloc byte[4];
        buffer.Slice(0, 4).CopyTo(lengthBytes);
        var packetLength = BinaryPrimitives.ReadInt32LittleEndian(lengthBytes);

        // Validate length
        if (packetLength <= 0 || packetLength > _options.MaxPacketSize)
        {
            throw new InvalidDataException($"Invalid packet length: {packetLength}");
        }

        // Check if we have the complete packet
        if (buffer.Length < 4 + packetLength) return false;

        // Extract packet data and parse using shared codec
        var packetBuffer = buffer.Slice(4, packetLength);

        // Zero-copy optimization: avoid ToArray() when possible
        if (packetBuffer.IsSingleSegment)
        {
            // Single segment: use directly without copying
            var span = packetBuffer.FirstSpan;
            if (!MessageCodec.TryParseMessage(span, out msgId, out msgSeq, out stageId, out var payloadOffset))
            {
                throw new InvalidDataException("Invalid message format");
            }

            // Copy payload to ArrayPool buffer: Stage processes asynchronously while Pipe buffer is reused
            var payloadLength = span.Length - payloadOffset;
            var rentedBuffer = ArrayPool<byte>.Shared.Rent(payloadLength);
            span.Slice(payloadOffset, payloadLength).CopyTo(rentedBuffer);
            payload = new ArrayPoolPayload(rentedBuffer, payloadLength);
        }
        else
        {
            // Multiple segments (rare): use ArrayPool with single copy optimization
            var rentedBuffer = ArrayPool<byte>.Shared.Rent((int)packetLength);
            packetBuffer.CopyTo(rentedBuffer);

            if (!MessageCodec.TryParseMessage(rentedBuffer.AsSpan(0, (int)packetLength), out msgId, out msgSeq, out stageId, out var payloadOffset))
            {
                ArrayPool<byte>.Shared.Return(rentedBuffer);
                throw new InvalidDataException("Invalid message format");
            }

            // No second copy: use offset-based payload (ArrayPoolPayloadWithOffset.Dispose() handles buffer return)
            var payloadLength = (int)packetLength - payloadOffset;
            payload = new ArrayPoolPayloadWithOffset(rentedBuffer, payloadOffset, payloadLength);
        }

        // Advance buffer
        buffer = buffer.Slice(4 + packetLength);

        return true;
    }

}
