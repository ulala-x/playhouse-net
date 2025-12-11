#nullable enable

using System.Buffers.Binary;
using System.Net.Sockets;
using System.Text;
using PlayHouse.Abstractions;
using PlayHouse.Core.Shared;

namespace PlayHouse.Core.Session;

/// <summary>
/// Handles TCP client session for Play Server.
/// Manages protocol parsing and message encoding/decoding.
/// </summary>
public sealed class ClientSession : IAsyncDisposable
{
    private readonly TcpClient _client;
    private readonly NetworkStream _stream;
    private readonly Action<ClientSession, string, ushort, long, byte[]> _onMessage;
    private readonly Action<ClientSession> _onDisconnect;
    private readonly CancellationToken _ct;

    private readonly byte[] _readBuffer = new byte[8192];
    private readonly List<byte> _receiveBuffer = new();
    private int _expectedPacketSize = -1;

    private long _sessionId;
    private string _accountId = string.Empty;
    private bool _isAuthenticated;
    private bool _disposed;

    public long SessionId
    {
        get => _sessionId;
        set => _sessionId = value;
    }

    public string AccountId
    {
        get => _accountId;
        set => _accountId = value;
    }

    public bool IsAuthenticated
    {
        get => _isAuthenticated;
        set => _isAuthenticated = value;
    }

    public bool IsConnected => _client.Connected && !_disposed;

    internal ClientSession(
        TcpClient client,
        Action<ClientSession, string, ushort, long, byte[]> onMessage,
        Action<ClientSession> onDisconnect,
        CancellationToken ct)
    {
        _client = client;
        _stream = client.GetStream();
        _onMessage = onMessage;
        _onDisconnect = onDisconnect;
        _ct = ct;
    }

    /// <summary>
    /// Starts receiving messages from the client.
    /// </summary>
    public async Task StartAsync()
    {
        try
        {
            while (!_ct.IsCancellationRequested && _client.Connected)
            {
                var bytesRead = await _stream.ReadAsync(_readBuffer, _ct);
                if (bytesRead == 0) break;

                _receiveBuffer.AddRange(_readBuffer.Take(bytesRead));
                ProcessReceiveBuffer();
            }
        }
        catch (OperationCanceledException) { }
        catch (IOException) { }
        catch (SocketException) { }
        finally
        {
            _onDisconnect(this);
        }
    }

    private void ProcessReceiveBuffer()
    {
        while (true)
        {
            if (_expectedPacketSize == -1)
            {
                if (_receiveBuffer.Count < 4) break;

                _expectedPacketSize = BinaryPrimitives.ReadInt32LittleEndian(
                    _receiveBuffer.GetRange(0, 4).ToArray());

                if (_expectedPacketSize <= 0 || _expectedPacketSize > 10 * 1024 * 1024)
                {
                    // Invalid packet size, close connection
                    _client.Close();
                    return;
                }

                _receiveBuffer.RemoveRange(0, 4);
            }

            if (_receiveBuffer.Count < _expectedPacketSize) break;

            var packetData = _receiveBuffer.GetRange(0, _expectedPacketSize).ToArray();
            _receiveBuffer.RemoveRange(0, _expectedPacketSize);
            _expectedPacketSize = -1;

            try
            {
                ParseAndDispatch(packetData);
            }
            catch
            {
                // Parsing error, continue with next packet
            }
        }
    }

    private void ParseAndDispatch(byte[] data)
    {
        int offset = 0;

        // MsgIdLen (1 byte)
        var msgIdLen = data[offset++];
        var msgId = Encoding.UTF8.GetString(data, offset, msgIdLen);
        offset += msgIdLen;

        // MsgSeq (2 bytes)
        var msgSeq = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(offset));
        offset += 2;

        // StageId (8 bytes)
        var stageId = BinaryPrimitives.ReadInt64LittleEndian(data.AsSpan(offset));
        offset += 8;

        // Payload
        var payload = data.AsSpan(offset).ToArray();

        _onMessage(this, msgId, msgSeq, stageId, payload);
    }

    /// <summary>
    /// Sends a response to the client.
    /// </summary>
    public async Task SendResponseAsync(
        string msgId,
        ushort msgSeq,
        long stageId,
        ushort errorCode,
        byte[] payload)
    {
        if (!IsConnected) return;

        var msgIdBytes = Encoding.UTF8.GetBytes(msgId);

        // Server Response Format:
        // MsgIdLen(1) + MsgId(N) + MsgSeq(2) + StageId(8) + ErrorCode(2) + OriginalSize(4) + Payload
        var contentSize = 1 + msgIdBytes.Length + 2 + 8 + 2 + 4 + payload.Length;
        var buffer = new byte[4 + contentSize];

        int offset = 0;

        // Length (4 bytes)
        BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(offset), contentSize);
        offset += 4;

        // MsgIdLen (1 byte)
        buffer[offset++] = (byte)msgIdBytes.Length;

        // MsgId
        msgIdBytes.CopyTo(buffer, offset);
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
        payload.CopyTo(buffer, offset);

        try
        {
            await _stream.WriteAsync(buffer, _ct);
        }
        catch
        {
            // Send failed, connection may be closed
        }
    }

    /// <summary>
    /// Sends a push message to the client (no response expected).
    /// </summary>
    public Task SendPushAsync(string msgId, long stageId, byte[] payload)
    {
        return SendResponseAsync(msgId, 0, stageId, 0, payload);
    }

    /// <summary>
    /// Sends a success response.
    /// </summary>
    public Task SendSuccessAsync(string msgId, ushort msgSeq, long stageId, byte[] payload)
    {
        return SendResponseAsync(msgId, msgSeq, stageId, 0, payload);
    }

    /// <summary>
    /// Sends an error response.
    /// </summary>
    public Task SendErrorAsync(string msgId, ushort msgSeq, long stageId, ushort errorCode)
    {
        return SendResponseAsync(msgId, msgSeq, stageId, errorCode, Array.Empty<byte>());
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        try
        {
            _stream.Close();
            _client.Close();
        }
        catch { }

        await ValueTask.CompletedTask;
    }
}
