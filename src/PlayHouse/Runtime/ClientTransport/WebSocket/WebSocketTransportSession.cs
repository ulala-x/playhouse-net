#nullable enable

using System.Buffers;
using System.Buffers.Binary;
using System.Net.WebSockets;
using System.Text;
using Microsoft.Extensions.Logging;
using WS = System.Net.WebSockets.WebSocket;

namespace PlayHouse.Runtime.ClientTransport.WebSocket;

/// <summary>
/// WebSocket transport session for bidirectional communication.
/// </summary>
/// <remarks>
/// WebSocket messages are self-framed, so we use binary messages directly.
/// Message format inside WebSocket frame:
/// [MsgIdLen:1][MsgId:N][MsgSeq:2][StageId:8][Payload]
/// </remarks>
internal sealed class WebSocketTransportSession : ITransportSession
{
    private readonly WS _webSocket;
    private readonly TransportOptions _options;
    private readonly MessageReceivedCallback _onMessage;
    private readonly SessionDisconnectedCallback _onDisconnect;
    private readonly ILogger? _logger;
    private readonly CancellationTokenSource _cts;
    private readonly Task _receiveTask;

    private bool _disposed;

    public long SessionId { get; }
    public long AccountId { get; set; }
    public bool IsAuthenticated { get; set; }
    public bool IsConnected => !_disposed && _webSocket.State == WebSocketState.Open;

    internal WebSocketTransportSession(
        long sessionId,
        WS webSocket,
        TransportOptions options,
        MessageReceivedCallback onMessage,
        SessionDisconnectedCallback onDisconnect,
        ILogger? logger,
        CancellationToken externalCt)
    {
        SessionId = sessionId;
        _webSocket = webSocket;
        _options = options;
        _onMessage = onMessage;
        _onDisconnect = onDisconnect;
        _logger = logger;
        _cts = CancellationTokenSource.CreateLinkedTokenSource(externalCt);

        _receiveTask = Task.Run(() => ReceiveLoopAsync(_cts.Token));

        _logger?.LogDebug("WebSocket session {SessionId} started", sessionId);
    }

    public async ValueTask SendAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default)
    {
        if (_disposed || _webSocket.State != WebSocketState.Open) return;

        try
        {
            await _webSocket.SendAsync(data, WebSocketMessageType.Binary, endOfMessage: true, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error sending data on WebSocket session {SessionId}", SessionId);
            await DisconnectAsync();
        }
    }

    public async ValueTask DisconnectAsync()
    {
        if (_disposed) return;

        try
        {
            if (_webSocket.State == WebSocketState.Open)
            {
                await _webSocket.CloseAsync(
                    WebSocketCloseStatus.NormalClosure,
                    "Closing",
                    CancellationToken.None);
            }
        }
        catch { }

        await DisposeAsync();
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        try
        {
            _cts.Cancel();

            var timeout = Task.Delay(TimeSpan.FromSeconds(5));
            await Task.WhenAny(_receiveTask, timeout);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error during WebSocket session {SessionId} disposal", SessionId);
        }
        finally
        {
            try { _webSocket.Dispose(); } catch { }
            _cts.Dispose();

            _onDisconnect(this, null);
            _logger?.LogDebug("WebSocket session {SessionId} disposed", SessionId);
        }
    }

    private async Task ReceiveLoopAsync(CancellationToken ct)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(_options.ReceiveBufferSize);

        try
        {
            while (!ct.IsCancellationRequested && _webSocket.State == WebSocketState.Open)
            {
                using var ms = new MemoryStream();
                ValueWebSocketReceiveResult result;

                do
                {
                    result = await _webSocket.ReceiveAsync(new Memory<byte>(buffer), ct);

                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        _logger?.LogDebug("WebSocket session {SessionId} close requested", SessionId);

                        await _webSocket.CloseAsync(
                            WebSocketCloseStatus.NormalClosure,
                            "Closing",
                            CancellationToken.None);
                        return;
                    }

                    ms.Write(buffer, 0, result.Count);

                    if (ms.Length > _options.MaxPacketSize)
                    {
                        _logger?.LogWarning("WebSocket session {SessionId} message too large: {Size}",
                            SessionId, ms.Length);

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
                    try
                    {
                        var data = ms.ToArray();
                        ParseAndDispatch(data);
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogError(ex, "Error parsing message on WebSocket session {SessionId}", SessionId);
                    }
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (WebSocketException ex)
        {
            _logger?.LogError(ex, "WebSocket error on session {SessionId}", SessionId);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error in receive loop for WebSocket session {SessionId}", SessionId);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
            await DisconnectAsync();
        }
    }

    /// <summary>
    /// Parses a message from the WebSocket frame data.
    /// </summary>
    /// <remarks>
    /// WebSocket Message Format (no length prefix needed):
    /// [MsgIdLen:1][MsgId:N][MsgSeq:2][StageId:8][Payload]
    /// </remarks>
    private void ParseAndDispatch(byte[] data)
    {
        if (data.Length < 11) // Minimum: 1 + 0 + 2 + 8
        {
            throw new InvalidDataException($"Message too short: {data.Length}");
        }

        int offset = 0;

        // MsgIdLen (1 byte)
        var msgIdLen = data[offset++];
        if (offset + msgIdLen + 10 > data.Length)
        {
            throw new InvalidDataException("Invalid message format");
        }

        var msgId = Encoding.UTF8.GetString(data, offset, msgIdLen);
        offset += msgIdLen;

        // MsgSeq (2 bytes)
        var msgSeq = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(offset));
        offset += 2;

        // StageId (8 bytes)
        var stageId = BinaryPrimitives.ReadInt64LittleEndian(data.AsSpan(offset));
        offset += 8;

        // Payload
        var payload = data.AsMemory(offset);

        _onMessage(this, msgId, msgSeq, stageId, payload);
    }

    /// <summary>
    /// Creates a response packet to send to the client.
    /// </summary>
    /// <remarks>
    /// WebSocket Response Format (no length prefix):
    /// [MsgIdLen:1][MsgId:N][MsgSeq:2][StageId:8][ErrorCode:2][OriginalSize:4][Payload]
    /// </remarks>
    internal static byte[] CreateResponsePacket(
        string msgId,
        ushort msgSeq,
        long stageId,
        ushort errorCode,
        ReadOnlySpan<byte> payload)
    {
        var msgIdBytes = Encoding.UTF8.GetBytes(msgId);

        // Size: MsgIdLen(1) + MsgId(N) + MsgSeq(2) + StageId(8) + ErrorCode(2) + OriginalSize(4) + Payload
        var size = 1 + msgIdBytes.Length + 2 + 8 + 2 + 4 + payload.Length;
        var buffer = new byte[size];

        int offset = 0;

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
