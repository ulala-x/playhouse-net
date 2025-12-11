#nullable enable

using System.Collections.Concurrent;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;

namespace PlayHouse.Runtime.ClientTransport.Session;

/// <summary>
/// 클라이언트 세션 관리자.
/// </summary>
/// <remarks>
/// 세션 ID 생성, 세션 저장소 관리, 세션 조회 기능을 제공합니다.
/// </remarks>
public sealed class SessionManager
{
    private readonly ConcurrentDictionary<long, ClientSession> _sessions = new();
    private readonly ConcurrentDictionary<string, long> _accountToSession = new();
    private readonly ILogger? _logger;
    private long _nextSessionId;

    /// <summary>
    /// 현재 세션 수.
    /// </summary>
    public int Count => _sessions.Count;

    /// <summary>
    /// 새 SessionManager 인스턴스를 생성합니다.
    /// </summary>
    /// <param name="logger">로거.</param>
    public SessionManager(ILogger? logger = null)
    {
        _logger = logger;
    }

    /// <summary>
    /// 새 세션을 생성합니다.
    /// </summary>
    /// <param name="client">TCP 클라이언트.</param>
    /// <param name="onMessage">메시지 핸들러.</param>
    /// <param name="onDisconnect">연결 해제 핸들러.</param>
    /// <param name="ct">취소 토큰.</param>
    /// <returns>생성된 ClientSession.</returns>
    public ClientSession Create(
        TcpClient client,
        Action<ClientSession, string, ushort, long, byte[]> onMessage,
        Action<ClientSession> onDisconnect,
        CancellationToken ct)
    {
        var sessionId = Interlocked.Increment(ref _nextSessionId);

        var session = new ClientSession(
            client,
            onMessage,
            onDisconnect,
            ct);

        session.SessionId = sessionId;

        _sessions[sessionId] = session;

        return session;
    }

    /// <summary>
    /// 세션 ID로 세션을 조회합니다.
    /// </summary>
    /// <param name="sessionId">세션 ID.</param>
    /// <returns>세션 또는 null.</returns>
    public ClientSession? Get(long sessionId)
    {
        _sessions.TryGetValue(sessionId, out var session);
        return session;
    }

    /// <summary>
    /// Account ID로 세션을 조회합니다.
    /// </summary>
    /// <param name="accountId">Account ID.</param>
    /// <returns>세션 또는 null.</returns>
    public ClientSession? GetByAccount(string accountId)
    {
        if (_accountToSession.TryGetValue(accountId, out var sessionId))
        {
            return Get(sessionId);
        }
        return null;
    }

    /// <summary>
    /// 세션에 Account ID를 바인딩합니다.
    /// </summary>
    /// <param name="sessionId">세션 ID.</param>
    /// <param name="accountId">Account ID.</param>
    /// <returns>바인딩 성공 여부.</returns>
    public bool BindAccount(long sessionId, string accountId)
    {
        var session = Get(sessionId);
        if (session == null)
            return false;

        // 기존 바인딩 제거
        if (!string.IsNullOrEmpty(session.AccountId))
        {
            _accountToSession.TryRemove(session.AccountId, out _);
        }

        // 새 바인딩
        session.AccountId = accountId;
        session.IsAuthenticated = true;
        _accountToSession[accountId] = sessionId;

        _logger?.LogDebug("Account bound: Session={SessionId}, Account={AccountId}",
            sessionId, accountId);

        return true;
    }

    /// <summary>
    /// 세션의 Account 바인딩을 해제합니다.
    /// </summary>
    /// <param name="sessionId">세션 ID.</param>
    public void UnbindAccount(long sessionId)
    {
        var session = Get(sessionId);
        if (session == null)
            return;

        if (!string.IsNullOrEmpty(session.AccountId))
        {
            _accountToSession.TryRemove(session.AccountId, out _);
            session.AccountId = string.Empty;
            session.IsAuthenticated = false;

            _logger?.LogDebug("Account unbound: Session={SessionId}", sessionId);
        }
    }

    /// <summary>
    /// 세션을 제거합니다.
    /// </summary>
    /// <param name="sessionId">세션 ID.</param>
    /// <returns>제거된 세션 또는 null.</returns>
    public ClientSession? Remove(long sessionId)
    {
        if (_sessions.TryRemove(sessionId, out var session))
        {
            // Account 바인딩도 제거
            if (!string.IsNullOrEmpty(session.AccountId))
            {
                _accountToSession.TryRemove(session.AccountId, out _);
            }

            return session;
        }
        return null;
    }

    /// <summary>
    /// 세션을 강제로 종료합니다.
    /// </summary>
    /// <param name="sessionId">세션 ID.</param>
    public async Task CloseAsync(long sessionId)
    {
        var session = Remove(sessionId);
        if (session != null)
        {
            await session.DisposeAsync();
        }
    }

    /// <summary>
    /// 모든 세션을 종료합니다.
    /// </summary>
    public async Task CloseAllAsync()
    {
        var sessions = _sessions.Values.ToList();
        _sessions.Clear();
        _accountToSession.Clear();

        foreach (var session in sessions)
        {
            try
            {
                await session.DisposeAsync();
            }
            catch
            {
                // Ignore disposal errors
            }
        }

        _logger?.LogInformation("All sessions closed: {Count}", sessions.Count);
    }

    /// <summary>
    /// 모든 세션 목록을 반환합니다.
    /// </summary>
    /// <returns>세션 목록.</returns>
    public IReadOnlyList<ClientSession> GetAll()
    {
        return _sessions.Values.ToList();
    }

    /// <summary>
    /// 인증된 세션 목록을 반환합니다.
    /// </summary>
    /// <returns>인증된 세션 목록.</returns>
    public IReadOnlyList<ClientSession> GetAuthenticated()
    {
        return _sessions.Values
            .Where(s => s.IsAuthenticated)
            .ToList();
    }
}

/// <summary>
/// ClientSession 내부 생성자 접근을 위한 팩토리.
/// </summary>
internal static class ClientSessionFactory
{
    /// <summary>
    /// ClientSession을 생성합니다 (내부용).
    /// </summary>
    public static ClientSession Create(
        TcpClient client,
        Action<ClientSession, string, ushort, long, byte[]> onMessage,
        Action<ClientSession> onDisconnect,
        CancellationToken ct)
    {
        return new ClientSession(client, onMessage, onDisconnect, ct);
    }
}
