#nullable enable

using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using PlayHouse.Abstractions.Play;
using PlayHouse.Core.Messaging;
using PlayHouse.Core.Play;
using PlayHouse.Core.Session;
using PlayHouse.Core.Shared;
using PlayHouse.Runtime;
using PlayHouse.Runtime.Communicator;
using PlayHouse.Runtime.Message;

namespace PlayHouse.Bootstrap;

/// <summary>
/// Play Server 인스턴스.
/// Stage와 Actor를 관리하고 클라이언트와 실시간 통신을 담당합니다.
/// </summary>
public sealed class PlayServer : IAsyncDisposable
{
    private readonly PlayServerOption _options;
    private readonly PlayProducer _producer;
    private readonly ServerConfig _serverConfig;

    private PlayCommunicator? _communicator;
    private PlayDispatcher? _dispatcher;
    private RequestCache? _requestCache;
    private TcpListener? _tcpListener;
    private CancellationTokenSource? _cts;
    private Task? _acceptTask;
    private readonly List<Task> _clientTasks = new();
    private readonly ConcurrentDictionary<long, ClientSession> _sessions = new();
    private long _sessionIdCounter;

    private bool _isRunning;
    private bool _disposed;

    /// <summary>
    /// 클라이언트 메시지 핸들러.
    /// 테스트나 커스텀 메시지 처리를 위해 설정합니다.
    /// </summary>
    internal Func<ClientSession, string, ushort, long, byte[], Task>? MessageHandler { get; set; }

    /// <summary>
    /// 서버가 실행 중인지 여부.
    /// </summary>
    public bool IsRunning => _isRunning;

    /// <summary>
    /// 서버 NID.
    /// </summary>
    public string Nid => _options.Nid;

    /// <summary>
    /// 클라이언트 연결 포트.
    /// </summary>
    public int ClientPort { get; private set; }

    internal PlayServer(PlayServerOption options, PlayProducer producer)
    {
        _options = options;
        _producer = producer;

        _serverConfig = new ServerConfig(
            options.ServiceId,
            options.ServerId,
            options.BindEndpoint,
            options.RequestTimeoutMs);
    }

    /// <summary>
    /// 서버를 시작합니다.
    /// </summary>
    public async Task StartAsync()
    {
        if (_isRunning)
            throw new InvalidOperationException("Server is already running");

        _cts = new CancellationTokenSource();
        _requestCache = new RequestCache();

        // NetMQ Communicator 시작
        _communicator = PlayCommunicator.Create(_serverConfig);
        _communicator.Bind(_options.BindEndpoint);
        _communicator.OnReceive(HandleReceivedMessage);
        _communicator.Start();

        // PlayDispatcher 생성
        _dispatcher = new PlayDispatcher(
            _producer,
            new CommunicatorAdapter(_communicator),
            _requestCache,
            _options.ServiceId,
            _options.Nid);

        // TCP 서버 시작
        var uri = new Uri(_options.ClientEndpoint.Replace("tcp://", "http://"));
        var port = uri.Port;
        var host = uri.Host == "0.0.0.0" ? IPAddress.Any : IPAddress.Parse(uri.Host);

        _tcpListener = new TcpListener(host, port);
        _tcpListener.Start();

        ClientPort = ((IPEndPoint)_tcpListener.LocalEndpoint).Port;

        _acceptTask = AcceptClientsAsync(_cts.Token);
        _isRunning = true;

        await Task.Delay(50); // 서버 초기화 대기
    }

    /// <summary>
    /// 서버를 중지합니다.
    /// </summary>
    public async Task StopAsync()
    {
        if (!_isRunning) return;

        _isRunning = false;
        _cts?.Cancel();

        _tcpListener?.Stop();

        if (_acceptTask != null)
        {
            try
            {
                await _acceptTask.WaitAsync(TimeSpan.FromSeconds(3));
            }
            catch (TimeoutException) { }
        }

        await Task.WhenAll(_clientTasks.Where(t => !t.IsCompleted));

        _communicator?.Stop();
        _dispatcher?.Dispose();
        _requestCache?.CancelAll();

        _cts?.Dispose();
        _cts = null;
    }

    private void HandleReceivedMessage(string senderNid, RuntimeRoutePacket packet)
    {
        // 응답 패킷인 경우 RequestCache에서 처리
        if (packet.MsgSeq > 0)
        {
            var response = CPacket.Of(packet.MsgId, packet.GetPayloadBytes());
            if (_requestCache?.TryComplete(packet.MsgSeq, response) == true)
            {
                packet.Dispose();
                return;
            }
        }

        // Stage로 라우팅
        _dispatcher?.Post(packet);
    }

    private async Task AcceptClientsAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var client = await _tcpListener!.AcceptTcpClientAsync(ct);
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
        var sessionId = Interlocked.Increment(ref _sessionIdCounter);

        var session = new ClientSession(
            client,
            OnClientMessage,
            OnClientDisconnect,
            ct);

        session.SessionId = sessionId;
        _sessions[sessionId] = session;

        await session.StartAsync();
    }

    private void OnClientMessage(ClientSession session, string msgId, ushort msgSeq, long stageId, byte[] payload)
    {
        // 커스텀 핸들러가 있으면 사용
        if (MessageHandler != null)
        {
            _ = MessageHandler(session, msgId, msgSeq, stageId, payload);
            return;
        }

        // 기본 핸들러: 에코 및 인증 처리
        _ = HandleDefaultMessageAsync(session, msgId, msgSeq, stageId, payload);
    }

    private async Task HandleDefaultMessageAsync(
        ClientSession session,
        string msgId,
        ushort msgSeq,
        long stageId,
        byte[] payload)
    {
        // 인증 요청 처리
        if (msgId.Contains("Authenticate") || msgId.Contains("Auth"))
        {
            session.IsAuthenticated = true;
            await session.SendSuccessAsync(msgId.Replace("Request", "Reply"), msgSeq, stageId, Array.Empty<byte>());
            return;
        }

        // 에코 요청 처리
        if (msgId.Contains("Echo"))
        {
            // 에코 응답: 동일한 payload를 반환
            var replyMsgId = msgId.Replace("Request", "Reply");
            await session.SendSuccessAsync(replyMsgId, msgSeq, stageId, payload);
            return;
        }

        // 실패 요청 시뮬레이션
        if (msgId.Contains("Fail"))
        {
            await session.SendErrorAsync(msgId.Replace("Request", "Reply"), msgSeq, stageId, 500);
            return;
        }

        // 응답이 필요 없는 메시지 (NoResponse)
        if (msgId.Contains("NoResponse"))
        {
            return;
        }

        // 기본: 성공 응답
        if (msgSeq > 0)
        {
            await session.SendSuccessAsync(msgId + "Reply", msgSeq, stageId, Array.Empty<byte>());
        }
    }

    private void OnClientDisconnect(ClientSession session)
    {
        _sessions.TryRemove(session.SessionId, out _);
    }

    /// <summary>
    /// 모든 연결된 클라이언트에게 Push 메시지를 전송합니다.
    /// </summary>
    public async Task BroadcastAsync(string msgId, long stageId, byte[] payload)
    {
        foreach (var session in _sessions.Values.Where(s => s.IsConnected))
        {
            await session.SendPushAsync(msgId, stageId, payload);
        }
    }

    /// <summary>
    /// 연결된 클라이언트 수를 반환합니다.
    /// </summary>
    public int ConnectedClientCount => _sessions.Count(s => s.Value.IsConnected);

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        await StopAsync();
    }

    /// <summary>
    /// Communicator를 IClientCommunicator로 래핑.
    /// </summary>
    private sealed class CommunicatorAdapter : IClientCommunicator
    {
        private readonly PlayCommunicator _communicator;

        public CommunicatorAdapter(PlayCommunicator communicator)
        {
            _communicator = communicator;
        }

        public string Nid => _communicator.Nid;

        public void Send(string targetNid, RuntimeRoutePacket packet)
        {
            _communicator.Send(targetNid, packet);
        }

        public void Connect(string targetNid, string address)
        {
            _communicator.Connect(targetNid, address);
        }

        public void Disconnect(string targetNid)
        {
            _communicator.Disconnect(targetNid);
        }
    }
}
