#nullable enable

using System.Buffers;
using System.IO.Pipelines;
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
    /// <summary>
    /// Pooled buffer for collecting fragmented WebSocket messages.
    /// Uses ArrayPool to avoid allocations.
    /// </summary>
    private sealed class PooledBuffer : IDisposable
    {
        private byte[] _buffer;
        private int _position;

        public PooledBuffer(int initialSize)
        {
            _buffer = ArrayPool<byte>.Shared.Rent(initialSize);
            _position = 0;
        }

        public void Write(ReadOnlySpan<byte> data)
        {
            EnsureCapacity(_position + data.Length);
            data.CopyTo(_buffer.AsSpan(_position));
            _position += data.Length;
        }

        public ReadOnlyMemory<byte> GetMemory() => _buffer.AsMemory(0, _position);

        public int Length => _position;

        private void EnsureCapacity(int requiredSize)
        {
            if (_buffer.Length >= requiredSize)
                return;

            var newSize = Math.Max(_buffer.Length * 2, requiredSize);
            var newBuffer = ArrayPool<byte>.Shared.Rent(newSize);
            _buffer.AsSpan(0, _position).CopyTo(newBuffer);
            ArrayPool<byte>.Shared.Return(_buffer);
            _buffer = newBuffer;
        }

        public void Dispose()
        {
            if (_buffer != null)
            {
                ArrayPool<byte>.Shared.Return(_buffer);
                _buffer = null!;
            }
        }
    }

    private readonly WS _webSocket;
    private readonly TransportOptions _options;
    private readonly MessageReceivedCallback _onMessage;
    private readonly SessionDisconnectedCallback _onDisconnect;
    private readonly ILogger? _logger;
    private readonly CancellationTokenSource _cts;
    private readonly Task _receiveTask;

    private bool _disposed;

    public long SessionId { get; }
    public string AccountId { get; set; } = string.Empty;
    public bool IsAuthenticated { get; set; }
    public long StageId { get; set; }
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

    /// <summary>
    /// Sends a response packet with ArrayPool optimization.
    /// WebSocket has its own framing, so we don't need PipeWriter.
    /// </summary>
    public ValueTask<FlushResult> SendResponseAsync(
        string msgId,
        ushort msgSeq,
        long stageId,
        ushort errorCode,
        ReadOnlySpan<byte> payload)
    {
        if (_disposed || _webSocket.State != WebSocketState.Open)
            return default;

        var size = MessageCodec.CalculateResponseSize(msgId.Length, payload.Length, includeLengthPrefix: false);
        var buffer = ArrayPool<byte>.Shared.Rent(size);

        try
        {
            // Write response body synchronously (Span can be used here)
            MessageCodec.WriteResponseBody(buffer.AsSpan(), msgId, msgSeq, stageId, errorCode, payload);

            // Send asynchronously
            return SendBufferAsync(buffer, size);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error preparing response on WebSocket session {SessionId}", SessionId);
            ArrayPool<byte>.Shared.Return(buffer);
            return default;
        }
    }

    private async ValueTask<FlushResult> SendBufferAsync(byte[] buffer, int size)
    {
        try
        {
            await _webSocket.SendAsync(
                buffer.AsMemory(0, size),
                WebSocketMessageType.Binary,
                endOfMessage: true,
                CancellationToken.None);
            return default;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error sending response on WebSocket session {SessionId}", SessionId);
            await DisconnectAsync();
            return default;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
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
                using var pooledBuffer = new PooledBuffer(_options.ReceiveBufferSize);
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

                    pooledBuffer.Write(buffer.AsSpan(0, result.Count));

                    if (pooledBuffer.Length > _options.MaxPacketSize)
                    {
                        _logger?.LogWarning("WebSocket session {SessionId} message too large: {Size}",
                            SessionId, pooledBuffer.Length);

                        await _webSocket.CloseAsync(
                            WebSocketCloseStatus.MessageTooBig,
                            "Message too large",
                            CancellationToken.None);
                        return;
                    }
                }
                while (!result.EndOfMessage);

                // Process complete message
                if (pooledBuffer.Length > 0)
                {
                    try
                    {
                        var data = pooledBuffer.GetMemory();
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
    private void ParseAndDispatch(ReadOnlyMemory<byte> data)
    {
        if (!MessageCodec.TryParseMessage(data.Span, out var msgId, out var msgSeq, out var stageId, out var payloadOffset))
        {
            throw new InvalidDataException("Invalid message format");
        }

        var payload = data.Slice(payloadOffset);
        _onMessage(this, msgId, msgSeq, stageId, payload);
    }

}
