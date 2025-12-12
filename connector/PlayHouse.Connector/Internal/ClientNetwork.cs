#nullable enable

using System;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using PlayHouse.Connector.Network;
using PlayHouse.Connector.Protocol;

namespace PlayHouse.Connector.Internal;

/// <summary>
/// 클라이언트 네트워크 - 연결 관리 및 메시지 송수신
/// </summary>
internal sealed class ClientNetwork : IAsyncDisposable
{
    private readonly ConnectorConfig _config;
    private readonly IConnectorCallback _callback;
    private readonly AsyncManager _asyncManager = new();

    private IConnection? _connection;
    private readonly ConcurrentDictionary<ushort, PendingRequest> _pendingRequests = new();
    private int _msgSeqCounter;
    private bool _isAuthenticated;
    private bool _debugMode;

    // 패킷 버퍼
    private readonly List<byte> _receiveBuffer = new();
    private int _expectedPacketSize = -1;

    // HeartBeat & IdleTimeout
    private readonly Stopwatch _lastReceivedTime = new();
    private readonly Stopwatch _lastSendHeartBeatTime = new();

    public ClientNetwork(ConnectorConfig config, IConnectorCallback callback)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _callback = callback ?? throw new ArgumentNullException(nameof(callback));
    }

    public bool IsConnect() => _connection?.IsConnected ?? false;

    public bool IsAuthenticated() => _isAuthenticated;

    public bool IsDebugMode() => _debugMode;

    public void Connect(string host, int port, bool debugMode = false)
    {
        _debugMode = debugMode;
        _ = ConnectInternalAsync(host, port, debugMode);
    }

    public async Task<bool> ConnectAsync(string host, int port, bool debugMode = false)
    {
        _debugMode = debugMode;
        return await ConnectInternalAsync(host, port, debugMode);
    }

    private async Task<bool> ConnectInternalAsync(string host, int port, bool debugMode)
    {
        try
        {
            // 기존 연결 정리 (재연결 지원)
            await CleanupConnectionAsync();

            // 상태 초기화
            _isAuthenticated = false;
            _receiveBuffer.Clear();
            _expectedPacketSize = -1;
            ClearPendingRequests();

            _connection = _config.UseWebsocket
                ? CreateWebSocketConnection()
                : CreateTcpConnection();

            _connection.DataReceived += OnDataReceived;
            _connection.Disconnected += OnDisconnected;

            await _connection.ConnectAsync(host, port, _config.UseSsl);

            // 타이머 시작
            _lastReceivedTime.Restart();
            _lastSendHeartBeatTime.Restart();

            _asyncManager.AddJob(() => _callback.ConnectCallback(true));
            return true;
        }
        catch (Exception)
        {
            _asyncManager.AddJob(() => _callback.ConnectCallback(false));
            return false;
        }
    }

    private async Task CleanupConnectionAsync()
    {
        if (_connection != null)
        {
            _connection.DataReceived -= OnDataReceived;
            _connection.Disconnected -= OnDisconnected;
            await _connection.DisposeAsync();
            _connection = null;
        }
    }

    private IConnection CreateTcpConnection()
    {
        return new TcpConnection(_config);
    }

    private IConnection CreateWebSocketConnection()
    {
        return new WebSocketConnection(_config);
    }

    public async Task DisconnectAsync()
    {
        _isAuthenticated = false;
        ClearPendingRequests();
        await CleanupConnectionAsync();
    }

    public void MainThreadAction()
    {
        UpdateClientConnection();
        _asyncManager.MainThreadAction();
    }

    public void ClearCache()
    {
        _receiveBuffer.Clear();
        _expectedPacketSize = -1;
    }

    private void UpdateClientConnection()
    {
        if (IsConnect())
        {
            if (!_debugMode)
            {
                SendHeartBeat();

                if (IsIdleState())
                {
                    _ = DisconnectAsync();
                }
            }
        }
    }

    private void SendHeartBeat()
    {
        if (_config.HeartBeatIntervalMs == 0)
        {
            return;
        }

        if (_lastSendHeartBeatTime.ElapsedMilliseconds > _config.HeartBeatIntervalMs)
        {
            var packet = Packet.Empty(PacketConst.HeartBeat);
            Send(packet, 0);
            _lastSendHeartBeatTime.Restart();
        }
    }

    private bool IsIdleState()
    {
        if (!_isAuthenticated || _config.ConnectionIdleTimeoutMs == 0)
        {
            return false;
        }

        return _lastReceivedTime.ElapsedMilliseconds > _config.ConnectionIdleTimeoutMs;
    }

    #region Send/Request

    public void Send(IPacket packet, long stageId)
    {
        if (!IsConnect())
        {
            return;
        }

        var data = EncodePacket(packet, 0, stageId);
        _ = _connection!.SendAsync(data);
    }

    public void Request(IPacket request, Action<IPacket> callback, long stageId, bool isAuthenticate = false)
    {
        if (!IsConnect())
        {
            return;
        }

        var msgSeq = GetNextMsgSeq();
        var pending = new PendingRequest
        {
            MsgSeq = msgSeq,
            Request = request,
            StageId = stageId,
            Callback = callback,
            IsAuthenticate = isAuthenticate,
            CreatedAt = DateTime.UtcNow
        };

        _pendingRequests[msgSeq] = pending;

        var data = EncodePacket(request, msgSeq, stageId);
        _ = _connection!.SendAsync(data);

        // 타임아웃 설정
        _ = Task.Delay(_config.RequestTimeoutMs).ContinueWith(_ =>
        {
            if (_pendingRequests.TryRemove(msgSeq, out var req))
            {
                _asyncManager.AddJob(() =>
                {
                    _callback.ErrorCallback(req.StageId, (ushort)ConnectorErrorCode.RequestTimeout, req.Request);
                });
            }
        });
    }

    public async Task<IPacket> RequestAsync(IPacket request, long stageId, bool isAuthenticate = false)
    {
        if (!IsConnect())
        {
            throw new ConnectorException(stageId, (ushort)ConnectorErrorCode.Disconnected, request, 0);
        }

        var tcs = new TaskCompletionSource<IPacket>(TaskCreationOptions.RunContinuationsAsynchronously);
        var msgSeq = GetNextMsgSeq();

        var pending = new PendingRequest
        {
            MsgSeq = msgSeq,
            Request = request,
            StageId = stageId,
            Tcs = tcs,
            IsAuthenticate = isAuthenticate,
            CreatedAt = DateTime.UtcNow
        };

        _pendingRequests[msgSeq] = pending;

        var data = EncodePacket(request, msgSeq, stageId);
        await _connection!.SendAsync(data);

        // 타임아웃 설정
        using var cts = new CancellationTokenSource(_config.RequestTimeoutMs);
        cts.Token.Register(() =>
        {
            if (_pendingRequests.TryRemove(msgSeq, out _))
            {
                tcs.TrySetException(new ConnectorException(stageId, (ushort)ConnectorErrorCode.RequestTimeout, request, msgSeq));
            }
        });

        return await tcs.Task;
    }

    #endregion

    #region Packet Encoding

    private byte[] EncodePacket(IPacket packet, ushort msgSeq, long stageId)
    {
        var msgIdBytes = Encoding.UTF8.GetBytes(packet.MsgId);
        if (msgIdBytes.Length > 255)
        {
            throw new ArgumentException($"Message ID too long: {packet.MsgId}");
        }

        var payloadBytes = packet.Payload.Data.ToArray();

        // 스펙에 따라 ServiceId 제거
        // MsgIdLen(1) + MsgId(N) + MsgSeq(2) + StageId(8) + Payload
        var contentSize = 1 + msgIdBytes.Length + 2 + 8 + payloadBytes.Length;
        var buffer = new byte[4 + contentSize];

        int offset = 0;

        // Length prefix (4 bytes, little-endian)
        BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(offset), contentSize);
        offset += 4;

        // MsgIdLen (1 byte)
        buffer[offset++] = (byte)msgIdBytes.Length;

        // MsgId (N bytes)
        msgIdBytes.CopyTo(buffer, offset);
        offset += msgIdBytes.Length;

        // MsgSeq (2 bytes, little-endian)
        BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(offset), msgSeq);
        offset += 2;

        // StageId (8 bytes, little-endian)
        BinaryPrimitives.WriteInt64LittleEndian(buffer.AsSpan(offset), stageId);
        offset += 8;

        // Payload
        payloadBytes.CopyTo(buffer, offset);

        return buffer;
    }

    #endregion

    #region Packet Decoding

    private void OnDataReceived(object? sender, byte[] data)
    {
        // 데이터 수신 시 타이머 리셋
        _lastReceivedTime.Restart();

        _receiveBuffer.AddRange(data);
        ProcessReceiveBuffer();
    }

    private void ProcessReceiveBuffer()
    {
        while (true)
        {
            if (_expectedPacketSize == -1)
            {
                if (_receiveBuffer.Count < 4)
                {
                    break;
                }

                _expectedPacketSize = BinaryPrimitives.ReadInt32LittleEndian(_receiveBuffer.GetRange(0, 4).ToArray());

                if (_expectedPacketSize <= 0 || _expectedPacketSize > 10 * 1024 * 1024)
                {
                    throw new InvalidOperationException($"Invalid packet size: {_expectedPacketSize}");
                }

                _receiveBuffer.RemoveRange(0, 4);
            }

            if (_receiveBuffer.Count < _expectedPacketSize)
            {
                break;
            }

            var packetData = _receiveBuffer.GetRange(0, _expectedPacketSize).ToArray();
            _receiveBuffer.RemoveRange(0, _expectedPacketSize);
            _expectedPacketSize = -1;

            try
            {
                var parsed = ParseServerPacket(packetData);
                HandleReceivedPacket(parsed);
            }
            catch (Exception)
            {
                // 파싱 에러 무시
            }
        }
    }

    private ParsedPacket ParseServerPacket(byte[] data)
    {
        int offset = 0;

        // 스펙에 따라 ServiceId 제거됨
        // MsgIdLen(1) + MsgId(N) + MsgSeq(2) + StageId(8) + ErrorCode(2) + OriginalSize(4) + Payload

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

        // ErrorCode (2 bytes)
        var errorCode = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(offset));
        offset += 2;

        // OriginalSize (4 bytes) - for LZ4 decompression
        var originalSize = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(offset));
        offset += 4;

        // Payload (with optional decompression)
        byte[] payload;
        if (originalSize > 0)
        {
            payload = K4os.Compression.LZ4.LZ4Pickler.Unpickle(data.AsSpan(offset).ToArray());
        }
        else
        {
            payload = data.AsSpan(offset).ToArray();
        }

        return new ParsedPacket
        {
            MsgId = msgId,
            MsgSeq = msgSeq,
            StageId = stageId,
            ErrorCode = errorCode,
            Payload = payload
        };
    }

    private void HandleReceivedPacket(ParsedPacket parsed)
    {
        // HeartBeat 메시지는 무시
        if (parsed.MsgId == PacketConst.HeartBeat)
        {
            return;
        }

        var packet = new Packet(parsed.MsgId, parsed.Payload);

        // Response 처리 (MsgSeq > 0)
        if (parsed.MsgSeq > 0 && _pendingRequests.TryRemove(parsed.MsgSeq, out var pending))
        {
            if (pending.IsAuthenticate && parsed.ErrorCode == 0)
            {
                _isAuthenticated = true;
            }

            if (parsed.ErrorCode != 0)
            {
                if (pending.Tcs != null)
                {
                    pending.Tcs.TrySetException(new ConnectorException(parsed.StageId, parsed.ErrorCode, pending.Request, parsed.MsgSeq));
                }
                else if (pending.Callback != null)
                {
                    _asyncManager.AddJob(() => _callback.ErrorCallback(parsed.StageId, parsed.ErrorCode, pending.Request));
                }
            }
            else
            {
                if (pending.Tcs != null)
                {
                    pending.Tcs.TrySetResult(packet);
                }
                else if (pending.Callback != null)
                {
                    _asyncManager.AddJob(() => pending.Callback(packet));
                }
            }

            return;
        }

        // Push 메시지 처리 (MsgSeq == 0)
        _asyncManager.AddJob(() => _callback.ReceiveCallback(parsed.StageId, packet));
    }

    private void OnDisconnected(object? sender, Exception? exception)
    {
        _isAuthenticated = false;
        ClearPendingRequests();
        _asyncManager.AddJob(() => _callback.DisconnectCallback());
    }

    #endregion

    #region Helpers

    private ushort GetNextMsgSeq()
    {
        int msgSeq;
        do
        {
            msgSeq = Interlocked.Increment(ref _msgSeqCounter);
        }
        while ((msgSeq & 0xFFFF) == 0);

        return (ushort)(msgSeq & 0xFFFF);
    }

    private void ClearPendingRequests()
    {
        var pending = _pendingRequests.ToArray();
        _pendingRequests.Clear();

        foreach (var kvp in pending)
        {
            kvp.Value.Tcs?.TrySetCanceled();
        }
    }

    #endregion

    public async ValueTask DisposeAsync()
    {
        if (_connection != null)
        {
            await _connection.DisposeAsync();
        }
    }

    private sealed class PendingRequest
    {
        public ushort MsgSeq { get; init; }
        public IPacket Request { get; init; } = null!;
        public long StageId { get; init; }
        public Action<IPacket>? Callback { get; init; }
        public TaskCompletionSource<IPacket>? Tcs { get; init; }
        public bool IsAuthenticate { get; init; }
        public DateTime CreatedAt { get; init; }
    }

    private sealed class ParsedPacket
    {
        public string MsgId { get; init; } = string.Empty;
        public ushort MsgSeq { get; init; }
        public long StageId { get; init; }
        public ushort ErrorCode { get; init; }
        public byte[] Payload { get; init; } = Array.Empty<byte>();
    }
}

/// <summary>
/// Connector 콜백 인터페이스
/// </summary>
internal interface IConnectorCallback
{
    void ConnectCallback(bool result);
    void ReceiveCallback(long stageId, IPacket packet);
    void ErrorCallback(long stageId, ushort errorCode, IPacket request);
    void DisconnectCallback();
}
