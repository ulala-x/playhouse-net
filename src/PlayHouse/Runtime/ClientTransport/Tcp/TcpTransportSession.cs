#nullable enable

using System.Buffers;
using System.Buffers.Binary;
using System.IO.Pipelines;
using System.Net.Sockets;
using System.Net.Security;
using System.Text;
using Microsoft.Extensions.Logging;

namespace PlayHouse.Runtime.ClientTransport.Tcp;

/// <summary>
/// TCP transport session using System.IO.Pipelines for efficient I/O.
/// </summary>
/// <remarks>
/// Uses length-prefixed message framing and zero-copy buffer management.
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
    private readonly Task _receiveTask;
    private readonly Task _processTask;

    private DateTime _lastActivity;
    private bool _disposed;

    public long SessionId { get; }
    public long AccountId { get; set; }
    public bool IsAuthenticated { get; set; }
    public bool IsConnected => !_disposed && _socket.Connected;

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

        // Configure pipe
        var pipeOptions = new PipeOptions(
            pauseWriterThreshold: options.PauseWriterThreshold,
            resumeWriterThreshold: options.ResumeWriterThreshold,
            useSynchronizationContext: false);
        _receivePipe = new Pipe(pipeOptions);

        // Start I/O tasks
        _receiveTask = Task.Run(() => ReceiveLoopAsync(_cts.Token));
        _processTask = Task.Run(() => ProcessMessagesAsync(_cts.Token));

        _logger?.LogDebug("TCP session {SessionId} started", sessionId);
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

            // Wait for tasks with timeout
            var timeout = Task.Delay(TimeSpan.FromSeconds(5));
            var completed = Task.WhenAll(_receiveTask, _processTask);
            await Task.WhenAny(completed, timeout);
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
    /// </summary>
    /// <remarks>
    /// Client Request Format:
    /// [Length:4] + [MsgIdLen:1][MsgId:N][MsgSeq:2][StageId:8][Payload]
    /// </remarks>
    private bool TryParseMessage(
        ref ReadOnlySequence<byte> buffer,
        out string msgId,
        out ushort msgSeq,
        out long stageId,
        out ReadOnlyMemory<byte> payload)
    {
        msgId = string.Empty;
        msgSeq = 0;
        stageId = 0;
        payload = ReadOnlyMemory<byte>.Empty;

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

        // Extract packet data
        var packetBuffer = buffer.Slice(4, packetLength);
        var packetData = packetBuffer.ToArray();
        int offset = 0;

        // MsgIdLen (1 byte)
        var msgIdLen = packetData[offset++];
        msgId = Encoding.UTF8.GetString(packetData, offset, msgIdLen);
        offset += msgIdLen;

        // MsgSeq (2 bytes)
        msgSeq = BinaryPrimitives.ReadUInt16LittleEndian(packetData.AsSpan(offset));
        offset += 2;

        // StageId (8 bytes)
        stageId = BinaryPrimitives.ReadInt64LittleEndian(packetData.AsSpan(offset));
        offset += 8;

        // Payload
        payload = packetData.AsMemory(offset);

        // Advance buffer
        buffer = buffer.Slice(4 + packetLength);

        return true;
    }

    /// <summary>
    /// Creates a response packet to send to the client.
    /// </summary>
    /// <remarks>
    /// Server Response Format:
    /// [Length:4] + [MsgIdLen:1][MsgId:N][MsgSeq:2][StageId:8][ErrorCode:2][OriginalSize:4][Payload]
    /// </remarks>
    internal static byte[] CreateResponsePacket(
        string msgId,
        ushort msgSeq,
        long stageId,
        ushort errorCode,
        ReadOnlySpan<byte> payload)
    {
        var msgIdBytes = Encoding.UTF8.GetBytes(msgId);

        // Calculate content size: MsgIdLen(1) + MsgId(N) + MsgSeq(2) + StageId(8) + ErrorCode(2) + OriginalSize(4) + Payload
        var contentSize = 1 + msgIdBytes.Length + 2 + 8 + 2 + 4 + payload.Length;
        var buffer = new byte[4 + contentSize];

        int offset = 0;

        // Length (4 bytes)
        BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(offset), contentSize);
        offset += 4;

        // MsgIdLen (1 byte)
        buffer[offset++] = (byte)msgIdBytes.Length;

        // MsgId
        msgIdBytes.CopyTo(buffer.AsSpan(offset));
        offset += msgIdBytes.Length;

        // MsgSeq (2 bytes)
        BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(offset), msgSeq);
        offset += 2;

        // StageId (8 bytes)
        BinaryPrimitives.WriteInt64LittleEndian(buffer.AsSpan(offset), stageId);
        offset += 8;

        // ErrorCode (2 bytes)
        BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(offset), errorCode);
        offset += 2;

        // OriginalSize (4 bytes) - 0 = no compression
        BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(offset), 0);
        offset += 4;

        // Payload
        payload.CopyTo(buffer.AsSpan(offset));

        return buffer;
    }
}
