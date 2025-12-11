#nullable enable

using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;

namespace PlayHouse.Core.Session;

/// <summary>
/// TCP 클라이언트 세션 핸들러.
/// </summary>
/// <remarks>
/// TcpListener를 관리하고 클라이언트 연결을 수락합니다.
/// 각 연결에 대해 ClientSession을 생성하고 SessionManager에 등록합니다.
/// </remarks>
public sealed class TcpSessionHandler : IAsyncDisposable
{
    private readonly TcpListener _listener;
    private readonly SessionManager _sessionManager;
    private readonly ILogger? _logger;
    private readonly CancellationTokenSource _cts = new();
    private readonly int _maxConnections;
    private Task? _acceptTask;
    private bool _disposed;

    /// <summary>
    /// 현재 연결된 세션 수.
    /// </summary>
    public int ConnectionCount => _sessionManager.Count;

    /// <summary>
    /// 서버가 실행 중인지 여부.
    /// </summary>
    public bool IsRunning { get; private set; }

    /// <summary>
    /// 클라이언트 메시지 수신 시 호출되는 핸들러.
    /// </summary>
    /// <remarks>
    /// Parameters: session, msgId, msgSeq, stageId, payload
    /// </remarks>
    public event Action<ClientSession, string, ushort, long, byte[]>? OnMessage;

    /// <summary>
    /// 클라이언트 연결 해제 시 호출되는 핸들러.
    /// </summary>
    public event Action<ClientSession>? OnDisconnect;

    /// <summary>
    /// 새 TcpSessionHandler 인스턴스를 생성합니다.
    /// </summary>
    /// <param name="bindAddress">바인드 주소.</param>
    /// <param name="port">포트 번호.</param>
    /// <param name="maxConnections">최대 연결 수.</param>
    /// <param name="logger">로거.</param>
    public TcpSessionHandler(
        string bindAddress,
        int port,
        int maxConnections = 10000,
        ILogger? logger = null)
    {
        var address = bindAddress == "0.0.0.0" || bindAddress == "*"
            ? IPAddress.Any
            : IPAddress.Parse(bindAddress);

        _listener = new TcpListener(address, port);
        _sessionManager = new SessionManager(logger);
        _maxConnections = maxConnections;
        _logger = logger;
    }

    /// <summary>
    /// 새 TcpSessionHandler 인스턴스를 생성합니다.
    /// </summary>
    /// <param name="endpoint">TCP 엔드포인트 (예: "tcp://0.0.0.0:6000").</param>
    /// <param name="maxConnections">최대 연결 수.</param>
    /// <param name="logger">로거.</param>
    public TcpSessionHandler(
        string endpoint,
        int maxConnections = 10000,
        ILogger? logger = null)
    {
        var (address, port) = ParseEndpoint(endpoint);
        _listener = new TcpListener(address, port);
        _sessionManager = new SessionManager(logger);
        _maxConnections = maxConnections;
        _logger = logger;
    }

    /// <summary>
    /// 서버를 시작합니다.
    /// </summary>
    public void Start()
    {
        if (IsRunning) return;

        _listener.Start();
        IsRunning = true;
        _acceptTask = AcceptLoopAsync(_cts.Token);

        _logger?.LogInformation("TcpSessionHandler started on {Endpoint}",
            _listener.LocalEndpoint);
    }

    /// <summary>
    /// 서버를 중지합니다.
    /// </summary>
    public async Task StopAsync()
    {
        if (!IsRunning) return;

        IsRunning = false;
        _cts.Cancel();
        _listener.Stop();

        if (_acceptTask != null)
        {
            try
            {
                await _acceptTask;
            }
            catch (OperationCanceledException) { }
        }

        // 모든 세션 종료
        await _sessionManager.CloseAllAsync();

        _logger?.LogInformation("TcpSessionHandler stopped");
    }

    /// <summary>
    /// 세션 ID로 세션을 조회합니다.
    /// </summary>
    /// <param name="sessionId">세션 ID.</param>
    /// <returns>세션 또는 null.</returns>
    public ClientSession? GetSession(long sessionId)
    {
        return _sessionManager.Get(sessionId);
    }

    /// <summary>
    /// Account ID로 세션을 조회합니다.
    /// </summary>
    /// <param name="accountId">Account ID.</param>
    /// <returns>세션 또는 null.</returns>
    public ClientSession? GetSessionByAccount(string accountId)
    {
        return _sessionManager.GetByAccount(accountId);
    }

    /// <summary>
    /// 세션을 강제로 종료합니다.
    /// </summary>
    /// <param name="sessionId">세션 ID.</param>
    public async Task CloseSessionAsync(long sessionId)
    {
        await _sessionManager.CloseAsync(sessionId);
    }

    private async Task AcceptLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && IsRunning)
        {
            try
            {
                // 최대 연결 수 확인
                if (_sessionManager.Count >= _maxConnections)
                {
                    await Task.Delay(100, ct);
                    continue;
                }

                var client = await _listener.AcceptTcpClientAsync(ct);

                // 클라이언트 설정
                client.NoDelay = true;
                client.ReceiveTimeout = 30000;
                client.SendTimeout = 30000;

                // 세션 생성 및 시작
                var session = _sessionManager.Create(
                    client,
                    HandleMessage,
                    HandleDisconnect,
                    ct);

                _ = session.StartAsync();

                _logger?.LogDebug("Client connected: {SessionId} from {RemoteEndpoint}",
                    session.SessionId, client.Client.RemoteEndPoint);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (SocketException ex) when (ex.SocketErrorCode == SocketError.OperationAborted)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error accepting client connection");
            }
        }
    }

    private void HandleMessage(ClientSession session, string msgId, ushort msgSeq, long stageId, byte[] payload)
    {
        OnMessage?.Invoke(session, msgId, msgSeq, stageId, payload);
    }

    private void HandleDisconnect(ClientSession session)
    {
        _sessionManager.Remove(session.SessionId);
        OnDisconnect?.Invoke(session);

        _logger?.LogDebug("Client disconnected: {SessionId}", session.SessionId);
    }

    private static (IPAddress address, int port) ParseEndpoint(string endpoint)
    {
        // "tcp://0.0.0.0:6000" -> (IPAddress.Any, 6000)
        var uri = new Uri(endpoint.Replace("tcp://", "http://"));
        var address = uri.Host == "0.0.0.0" || uri.Host == "*"
            ? IPAddress.Any
            : IPAddress.Parse(uri.Host);
        return (address, uri.Port);
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        await StopAsync();
        _cts.Dispose();
    }
}
