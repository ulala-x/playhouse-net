#nullable enable

using System;
using System.Buffers.Binary;
using System.Collections.Concurrent;
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
            ClearPendingRequests();

            _connection = _config.UseWebsocket
                ? CreateWebSocketConnection()
                : CreateTcpConnection();

            _connection.PacketReceived += OnPacketReceived;
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
            _connection.PacketReceived -= OnPacketReceived;
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

    private void UpdateClientConnection()
    {
        if (IsConnect())
        {
            if (!_debugMode)
            {
                SendHeartBeat();

                if (IsHeartbeatTimeout() || IsIdleState())
                {
                    // Heartbeat/Idle timeout - 직접 콜백 호출 후 연결 정리
                    _callback.DisconnectCallback();
                    _ = DisconnectAsync();
                }
            }
        }
    }

    private bool IsHeartbeatTimeout()
    {
        if (_config.HeartbeatTimeoutMs == 0)
        {
            return false;
        }

        return _lastReceivedTime.ElapsedMilliseconds > _config.HeartbeatTimeoutMs;
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
        var timeoutCts = new CancellationTokenSource();
        var pending = new PendingRequest
        {
            MsgSeq = msgSeq,
            Request = request,
            StageId = stageId,
            Callback = callback,
            IsAuthenticate = isAuthenticate,
            CreatedAt = DateTime.UtcNow,
            TimeoutCts = timeoutCts
        };

        _pendingRequests[msgSeq] = pending;

        var data = EncodePacket(request, msgSeq, stageId);

        // SendAsync와 타임아웃을 fire-and-forget으로 실행
        _ = SendRequestAsync(msgSeq, data, timeoutCts.Token);
    }

    private async Task SendRequestAsync(ushort msgSeq, byte[] data, CancellationToken timeoutToken)
    {
        try
        {
            await _connection!.SendAsync(data);
        }
        catch
        {
            // 전송 실패 시 pending request 제거하고 에러 콜백
            if (_pendingRequests.TryRemove(msgSeq, out var req))
            {
                req.Dispose();
                _asyncManager.AddJob(() =>
                {
                    _callback.ErrorCallback(req.StageId, (ushort)ConnectorErrorCode.Disconnected, req.Request);
                });
            }
            return;
        }

        // 타임아웃 설정 - 응답 도착 시 취소됨
        try
        {
            await Task.Delay(_config.RequestTimeoutMs, timeoutToken);
        }
        catch (OperationCanceledException)
        {
            // 응답이 도착해서 타임아웃이 취소됨 - 정상
            return;
        }

        // 타임아웃 발생
        if (_pendingRequests.TryRemove(msgSeq, out var timedOutReq))
        {
            timedOutReq.Dispose();
            _asyncManager.AddJob(() =>
            {
                _callback.ErrorCallback(timedOutReq.StageId, (ushort)ConnectorErrorCode.RequestTimeout, timedOutReq.Request);
            });
        }
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
        var msgIdByteCount = Encoding.UTF8.GetByteCount(packet.MsgId);
        if (msgIdByteCount > 255)
        {
            throw new ArgumentException($"Message ID too long: {packet.MsgId}");
        }

        var payloadSpan = packet.Payload.DataSpan;

        // 스펙에 따라 ServiceId 제거
        // MsgIdLen(1) + MsgId(N) + MsgSeq(2) + StageId(8) + Payload
        var contentSize = 1 + msgIdByteCount + 2 + 8 + payloadSpan.Length;
        var buffer = new byte[4 + contentSize];

        int offset = 0;

        // Length prefix (4 bytes, little-endian)
        BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(offset), contentSize);
        offset += 4;

        // MsgIdLen (1 byte)
        buffer[offset++] = (byte)msgIdByteCount;

        // MsgId (N bytes) - direct encoding to buffer
        Encoding.UTF8.GetBytes(packet.MsgId, buffer.AsSpan(offset, msgIdByteCount));
        offset += msgIdByteCount;

        // MsgSeq (2 bytes, little-endian)
        BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(offset), msgSeq);
        offset += 2;

        // StageId (8 bytes, little-endian)
        BinaryPrimitives.WriteInt64LittleEndian(buffer.AsSpan(offset), stageId);
        offset += 8;

        // Payload - direct copy from span
        payloadSpan.CopyTo(buffer.AsSpan(offset));

        return buffer;
    }

    #endregion

    #region Packet Handling

    private void OnPacketReceived(object? sender, IPacket packet)
    {
        // 데이터 수신 시 타이머 리셋
        _lastReceivedTime.Restart();

        // Cast to ParsedPacket to get metadata
        if (packet is not ParsedPacket parsed)
        {
            return;
        }

        // HeartBeat 메시지는 무시
        if (parsed.MsgId == PacketConst.HeartBeat)
        {
            return;
        }

        // Response 처리 (MsgSeq > 0)
        if (parsed.MsgSeq > 0 && _pendingRequests.TryRemove(parsed.MsgSeq, out var pending))
        {
            // 타임아웃 타이머 취소
            pending.Dispose();

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
            kvp.Value.Dispose();
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

    private sealed class PendingRequest : IDisposable
    {
        public ushort MsgSeq { get; init; }
        public IPacket Request { get; init; } = null!;
        public long StageId { get; init; }
        public Action<IPacket>? Callback { get; init; }
        public TaskCompletionSource<IPacket>? Tcs { get; init; }
        public bool IsAuthenticate { get; init; }
        public DateTime CreatedAt { get; init; }
        public CancellationTokenSource? TimeoutCts { get; init; }

        public void Dispose()
        {
            TimeoutCts?.Cancel();
            TimeoutCts?.Dispose();
        }
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
