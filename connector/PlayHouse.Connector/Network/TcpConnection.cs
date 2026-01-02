#nullable enable

using System;
using System.Buffers;
using System.Buffers.Binary;
using System.IO;
using System.Net.Security;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using PlayHouse.Connector.Infrastructure.Buffers;
using PlayHouse.Connector.Protocol;

namespace PlayHouse.Connector.Network;

/// <summary>
/// TCP 기반 연결 구현
/// Zero-copy receive with RingBuffer and direct packet parsing
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
    private const int ReceiveBufferSize = 262144; // 256KB for large payloads

    // Zero-copy receive buffer
    private readonly RingBuffer _receiveBuffer = new(ReceiveBufferSize);
    private int _expectedPacketSize = -1;

    public event EventHandler<IPacket>? PacketReceived;
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
        var tempBuffer = ArrayPool<byte>.Shared.Rent(ReceiveBufferSize);
        try
        {
            while (!cancellationToken.IsCancellationRequested && _isConnected && _stream != null)
            {
                try
                {
                    // Read directly into temp buffer
                    var bytesRead = await _stream.ReadAsync(tempBuffer, cancellationToken).ConfigureAwait(false);

                    if (bytesRead == 0)
                    {
                        HandleDisconnection(null);
                        break;
                    }

                    // Write to RingBuffer (zero-copy)
                    _receiveBuffer.WriteBytes(new ReadOnlySpan<byte>(tempBuffer, 0, bytesRead));

                    // Parse packets from RingBuffer
                    ProcessReceiveBuffer();
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
            ArrayPool<byte>.Shared.Return(tempBuffer);
        }
    }

    private void ProcessReceiveBuffer()
    {
        while (true)
        {
            // Read packet size header (4 bytes)
            if (_expectedPacketSize == -1)
            {
                if (_receiveBuffer.Count < 4)
                {
                    break;
                }

                // Peek and parse packet size (zero-copy)
                _expectedPacketSize = ReadInt32LittleEndian(_receiveBuffer);

                if (_expectedPacketSize <= 0 || _expectedPacketSize > 10 * 1024 * 1024)
                {
                    throw new InvalidOperationException($"Invalid packet size: {_expectedPacketSize}");
                }

                // Consume the size header
                _receiveBuffer.Consume(4);
            }

            // Check if we have the complete packet
            if (_receiveBuffer.Count < _expectedPacketSize)
            {
                break;
            }

            // Parse packet from RingBuffer
            try
            {
                var packet = ParseServerPacket(_receiveBuffer, _expectedPacketSize);
                _receiveBuffer.Consume(_expectedPacketSize);
                _expectedPacketSize = -1;

                // Raise event
                PacketReceived?.Invoke(this, packet);
            }
            catch (Exception)
            {
                // Parsing error - skip this packet
                _receiveBuffer.Consume(_expectedPacketSize);
                _expectedPacketSize = -1;
            }
        }
    }

    private static int ReadInt32LittleEndian(RingBuffer buffer)
    {
        // Handle wrapped case
        if (buffer.Count < 4)
        {
            throw new InvalidOperationException("Not enough data");
        }

        Span<byte> temp = stackalloc byte[4];
        for (int i = 0; i < 4; i++)
        {
            temp[i] = buffer.PeekByte(i);
        }
        return BinaryPrimitives.ReadInt32LittleEndian(temp);
    }

    private static IPacket ParseServerPacket(RingBuffer buffer, int packetSize)
    {
        int offset = 0;

        // Protocol: MsgIdLen(1) + MsgId(N) + MsgSeq(2) + StageId(8) + ErrorCode(2) + OriginalSize(4) + Payload

        // MsgIdLen (1 byte)
        var msgIdLen = buffer.PeekByte(offset++);

        // MsgId (N bytes)
        Span<byte> msgIdBytes = stackalloc byte[msgIdLen];
        for (int i = 0; i < msgIdLen; i++)
        {
            msgIdBytes[i] = buffer.PeekByte(offset++);
        }
        var msgId = Encoding.UTF8.GetString(msgIdBytes);

        // MsgSeq (2 bytes)
        Span<byte> msgSeqBytes = stackalloc byte[2];
        msgSeqBytes[0] = buffer.PeekByte(offset++);
        msgSeqBytes[1] = buffer.PeekByte(offset++);
        var msgSeq = BinaryPrimitives.ReadUInt16LittleEndian(msgSeqBytes);

        // StageId (8 bytes)
        Span<byte> stageIdBytes = stackalloc byte[8];
        for (int i = 0; i < 8; i++)
        {
            stageIdBytes[i] = buffer.PeekByte(offset++);
        }
        var stageId = BinaryPrimitives.ReadInt64LittleEndian(stageIdBytes);

        // ErrorCode (2 bytes)
        Span<byte> errorCodeBytes = stackalloc byte[2];
        errorCodeBytes[0] = buffer.PeekByte(offset++);
        errorCodeBytes[1] = buffer.PeekByte(offset++);
        var errorCode = BinaryPrimitives.ReadUInt16LittleEndian(errorCodeBytes);

        // OriginalSize (4 bytes)
        Span<byte> originalSizeBytes = stackalloc byte[4];
        for (int i = 0; i < 4; i++)
        {
            originalSizeBytes[i] = buffer.PeekByte(offset++);
        }
        var originalSize = BinaryPrimitives.ReadInt32LittleEndian(originalSizeBytes);

        // Payload (with optional decompression)
        var payloadSize = packetSize - offset;
        IPayload payload;

        if (originalSize > 0)
        {
            // Decompression path: Use ArrayPool for temporary compressed data
            var compressedBuffer = ArrayPool<byte>.Shared.Rent(payloadSize);
            try
            {
                buffer.PeekBytes(offset, compressedBuffer.AsSpan(0, payloadSize));
                offset += payloadSize;

                var decompressed = K4os.Compression.LZ4.LZ4Pickler.Unpickle(compressedBuffer.AsSpan(0, payloadSize).ToArray());
                payload = new MemoryPayload(new ReadOnlyMemory<byte>(decompressed));
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(compressedBuffer);
            }
        }
        else
        {
            // Non-compressed path: Use ArrayPool
            var payloadBuffer = ArrayPool<byte>.Shared.Rent(payloadSize);
            buffer.PeekBytes(offset, payloadBuffer.AsSpan(0, payloadSize));
            offset += payloadSize;
            payload = new ArrayPoolPayload(payloadBuffer, payloadSize);
        }

        // Create ParsedPacket wrapper to pass metadata
        return new ParsedPacket(msgId, msgSeq, stageId, errorCode, payload);
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

        _receiveBuffer.Clear();
        _expectedPacketSize = -1;
    }

    public async ValueTask DisposeAsync()
    {
        await DisconnectAsync().ConfigureAwait(false);
        _sendLock.Dispose();
        _receiveBuffer.Dispose();
    }
}

/// <summary>
/// Internal parsed packet wrapper for passing metadata
/// </summary>
internal sealed class ParsedPacket : IPacket
{
    public string MsgId { get; }
    public ushort MsgSeq { get; }
    public long StageId { get; }
    public ushort ErrorCode { get; }
    public IPayload Payload { get; }

    public ParsedPacket(string msgId, ushort msgSeq, long stageId, ushort errorCode, IPayload payload)
    {
        MsgId = msgId;
        MsgSeq = msgSeq;
        StageId = stageId;
        ErrorCode = errorCode;
        Payload = payload;
    }

    public void Dispose()
    {
        Payload?.Dispose();
    }
}
