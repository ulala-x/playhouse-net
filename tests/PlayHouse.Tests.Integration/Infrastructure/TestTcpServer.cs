#nullable enable

using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Google.Protobuf;
using PlayHouse.Tests.Integration.Proto;

namespace PlayHouse.Tests.Integration.Infrastructure;

/// <summary>
/// E2E 테스트용 TCP 서버
/// Connector 클라이언트와 실제 TCP 통신을 수행합니다.
/// </summary>
public sealed class TestTcpServer : IAsyncDisposable
{
    private TcpListener? _listener;
    private CancellationTokenSource? _cts;
    private readonly ConcurrentBag<TcpClient> _clients = new();
    private readonly List<Task> _clientTasks = new();

    public int Port { get; private set; }
    public bool IsRunning => _listener != null;

    // 서버 이벤트
    public event Action<string, byte[]>? MessageReceived;
    public event Action? ClientConnected;
    public event Action? ClientDisconnected;

    // 테스트 검증용 데이터
    public ConcurrentQueue<ReceivedMessage> ReceivedMessages { get; } = new();
    public int ConnectedClientCount => _clients.Count(c => c.Connected);

    /// <summary>
    /// 서버 시작
    /// </summary>
    public async Task StartAsync(int port = 0)
    {
        _cts = new CancellationTokenSource();
        _listener = new TcpListener(IPAddress.Loopback, port);
        _listener.Start();

        Port = ((IPEndPoint)_listener.LocalEndpoint).Port;

        _ = AcceptClientsAsync(_cts.Token);

        await Task.Delay(50); // 서버 시작 대기
    }

    /// <summary>
    /// 서버 중지
    /// </summary>
    public async Task StopAsync()
    {
        _cts?.Cancel();
        _listener?.Stop();

        foreach (var client in _clients)
        {
            try { client.Close(); } catch { }
        }

        await Task.WhenAll(_clientTasks.Where(t => !t.IsCompleted));

        _listener = null;
        _cts?.Dispose();
        _cts = null;
    }

    private async Task AcceptClientsAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var client = await _listener!.AcceptTcpClientAsync(ct);
                _clients.Add(client);
                ClientConnected?.Invoke();

                var task = HandleClientAsync(client, ct);
                _clientTasks.Add(task);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (SocketException)
            {
                break;
            }
        }
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken ct)
    {
        var buffer = new byte[8192];
        var receiveBuffer = new List<byte>();
        var stream = client.GetStream();

        try
        {
            while (!ct.IsCancellationRequested && client.Connected)
            {
                var bytesRead = await stream.ReadAsync(buffer, ct);
                if (bytesRead == 0) break;

                receiveBuffer.AddRange(buffer.Take(bytesRead));

                while (TryParsePacket(receiveBuffer, out var packet))
                {
                    ReceivedMessages.Enqueue(packet);
                    MessageReceived?.Invoke(packet.MsgId, packet.Payload);

                    // 자동 응답 처리
                    await ProcessPacketAsync(stream, packet, ct);
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (IOException) { }
        finally
        {
            ClientDisconnected?.Invoke();
        }
    }

    private bool TryParsePacket(List<byte> buffer, out ReceivedMessage packet)
    {
        packet = default!;

        if (buffer.Count < 4) return false;

        var length = BinaryPrimitives.ReadInt32LittleEndian(buffer.ToArray().AsSpan(0, 4));
        if (buffer.Count < 4 + length) return false;

        buffer.RemoveRange(0, 4);
        var data = buffer.Take(length).ToArray();
        buffer.RemoveRange(0, length);

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
        var payload = data.Skip(offset).ToArray();

        packet = new ReceivedMessage
        {
            MsgId = msgId,
            MsgSeq = msgSeq,
            StageId = stageId,
            Payload = payload
        };

        return true;
    }

    private async Task ProcessPacketAsync(NetworkStream stream, ReceivedMessage request, CancellationToken ct)
    {
        // 에코 요청 처리
        if (request.MsgId == "EchoRequest")
        {
            var echoRequest = EchoRequest.Parser.ParseFrom(request.Payload);
            var echoReply = new EchoReply
            {
                Content = echoRequest.Content,
                Sequence = echoRequest.Sequence,
                ProcessedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };

            await SendResponseAsync(stream, "EchoReply", request.MsgSeq, request.StageId, 0, echoReply.ToByteArray(), ct);
        }
        // 인증 요청 처리 (AuthenticateRequest 가정)
        else if (request.MsgId == "AuthenticateRequest")
        {
            // 성공 응답
            await SendResponseAsync(stream, "AuthenticateReply", request.MsgSeq, request.StageId, 0, Array.Empty<byte>(), ct);
        }
        // 실패 시뮬레이션
        else if (request.MsgId == "FailRequest")
        {
            await SendResponseAsync(stream, "FailReply", request.MsgSeq, request.StageId, 500, Array.Empty<byte>(), ct);
        }
        // 상태 조회
        else if (request.MsgId == "StatusRequest")
        {
            var statusReply = new StatusReply
            {
                ActorCount = ConnectedClientCount,
                UptimeSeconds = 100,
                StageType = "TestStage"
            };

            await SendResponseAsync(stream, "StatusReply", request.MsgSeq, request.StageId, 0, statusReply.ToByteArray(), ct);
        }
    }

    /// <summary>
    /// 특정 클라이언트에게 Push 메시지 전송
    /// </summary>
    public async Task BroadcastPushAsync(string msgId, byte[] payload)
    {
        foreach (var client in _clients.Where(c => c.Connected))
        {
            try
            {
                var stream = client.GetStream();
                await SendResponseAsync(stream, msgId, 0, 0, 0, payload, CancellationToken.None);
            }
            catch { }
        }
    }

    private async Task SendResponseAsync(
        NetworkStream stream,
        string msgId,
        ushort msgSeq,
        long stageId,
        ushort errorCode,
        byte[] payload,
        CancellationToken ct)
    {
        var msgIdBytes = Encoding.UTF8.GetBytes(msgId);

        // 서버 응답 형식:
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

        // OriginalSize (4 bytes) - 0 = 압축 안됨
        BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(offset), 0);
        offset += 4;

        // Payload
        payload.CopyTo(buffer, offset);

        await stream.WriteAsync(buffer, ct);
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
    }
}

/// <summary>
/// 수신된 메시지 정보
/// </summary>
public record ReceivedMessage
{
    public string MsgId { get; init; } = string.Empty;
    public ushort MsgSeq { get; init; }
    public long StageId { get; init; }
    public byte[] Payload { get; init; } = Array.Empty<byte>();
}
