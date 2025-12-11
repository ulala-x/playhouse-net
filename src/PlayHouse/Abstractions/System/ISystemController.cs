#nullable enable

using PlayHouse.Runtime.Discovery;

namespace PlayHouse.Abstractions.System;

/// <summary>
/// 시스템 핸들러 등록 인터페이스.
/// </summary>
public interface ISystemHandlerRegister
{
    /// <summary>
    /// 시스템 메시지 핸들러를 등록합니다.
    /// </summary>
    /// <typeparam name="TMessage">Protobuf 메시지 타입.</typeparam>
    /// <param name="handler">핸들러 함수.</param>
    void Add<TMessage>(Func<TMessage, Task> handler) where TMessage : Google.Protobuf.IMessage, new();
}

/// <summary>
/// 서버 디스커버리를 위한 컨텐츠 인터페이스.
/// </summary>
/// <remarks>
/// 컨텐츠 개발자가 구현해야 하는 인터페이스입니다.
/// Redis, etcd, Consul 등의 서비스 디스커버리 시스템과 연동할 수 있습니다.
/// </remarks>
public interface ISystemController
{
    /// <summary>
    /// 시스템 메시지 핸들러를 등록합니다 (선택적).
    /// </summary>
    /// <param name="handlerRegister">핸들러 레지스터.</param>
    void Handles(ISystemHandlerRegister handlerRegister) { }

    /// <summary>
    /// 내 서버 정보를 등록하고 전체 서버 목록을 반환합니다.
    /// </summary>
    /// <remarks>
    /// ServerAddressResolver가 주기적으로 (기본 3초) 호출합니다.
    ///
    /// 구현 예시 (Redis):
    /// <code>
    /// public async Task&lt;IReadOnlyList&lt;IServerInfo&gt;&gt; UpdateServerInfoAsync(IServerInfo serverInfo)
    /// {
    ///     // 1. 내 서버 정보 저장 (TTL 10초)
    ///     await db.StringSetAsync($"server:{serverInfo.Nid}", Serialize(serverInfo), TimeSpan.FromSeconds(10));
    ///
    ///     // 2. 전체 서버 목록 조회
    ///     var keys = await db.Keys("server:*");
    ///     return await GetServersFromKeys(keys);
    /// }
    /// </code>
    /// </remarks>
    /// <param name="serverInfo">내 서버 정보.</param>
    /// <returns>현재 활성화된 전체 서버 목록.</returns>
    Task<IReadOnlyList<IServerInfo>> UpdateServerInfoAsync(IServerInfo serverInfo);
}

/// <summary>
/// 테스트/개발용 인메모리 SystemController 구현.
/// </summary>
/// <remarks>
/// 프로덕션 환경에서는 Redis, etcd 등을 사용하는 구현체를 사용해야 합니다.
/// </remarks>
public class InMemorySystemController : ISystemController
{
    private static readonly Dictionary<string, ServerInfoEntry> _servers = new();
    private static readonly object _lock = new();
    private readonly TimeSpan _ttl;

    /// <summary>
    /// 새 InMemorySystemController 인스턴스를 생성합니다.
    /// </summary>
    /// <param name="ttl">서버 정보 TTL (기본 10초).</param>
    public InMemorySystemController(TimeSpan? ttl = null)
    {
        _ttl = ttl ?? TimeSpan.FromSeconds(10);
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<IServerInfo>> UpdateServerInfoAsync(IServerInfo serverInfo)
    {
        lock (_lock)
        {
            // 내 서버 정보 저장
            _servers[serverInfo.Nid] = new ServerInfoEntry(serverInfo, DateTimeOffset.UtcNow);

            // 만료된 서버 정리
            var expiredTime = DateTimeOffset.UtcNow - _ttl;
            var expired = _servers.Where(kvp => kvp.Value.UpdatedAt < expiredTime).Select(kvp => kvp.Key).ToList();
            foreach (var key in expired)
            {
                _servers.Remove(key);
            }

            // 전체 서버 목록 반환
            var result = _servers.Values.Select(e => e.ServerInfo).ToList();
            return Task.FromResult<IReadOnlyList<IServerInfo>>(result);
        }
    }

    /// <summary>
    /// 서버 정보를 초기화합니다 (테스트용).
    /// </summary>
    public static void Clear()
    {
        lock (_lock)
        {
            _servers.Clear();
        }
    }

    private sealed class ServerInfoEntry
    {
        public IServerInfo ServerInfo { get; }
        public DateTimeOffset UpdatedAt { get; }

        public ServerInfoEntry(IServerInfo serverInfo, DateTimeOffset updatedAt)
        {
            ServerInfo = serverInfo;
            UpdatedAt = updatedAt;
        }
    }
}
