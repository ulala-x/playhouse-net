#nullable enable

using System.Buffers;
using System.Net.WebSockets;
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
    /// Parses a message from the WebSocket frame data using shared codec.
    /// WebSocket doesn't need length prefix (frames are self-delimiting).
    /// </summary>
    private void ParseAndDispatch(byte[] data)
    {
        if (!MessageCodec.TryParseMessage(data, out var msgId, out var msgSeq, out var stageId, out var payloadOffset))
        {
            throw new InvalidDataException("Invalid message format");
        }

        var payload = data.AsMemory(payloadOffset);
        _onMessage(this, msgId, msgSeq, stageId, payload);
    }

    /// <summary>
    /// Creates a response packet (WebSocket: no length prefix).
    /// </summary>
    internal static byte[] CreateResponsePacket(
        string msgId,
        ushort msgSeq,
        long stageId,
        ushort errorCode,
        ReadOnlySpan<byte> payload)
        => MessageCodec.CreateWebSocketResponsePacket(msgId, msgSeq, stageId, errorCode, payload);
}
