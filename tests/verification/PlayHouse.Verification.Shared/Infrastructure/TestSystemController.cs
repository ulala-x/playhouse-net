#nullable enable

using System.Collections.Concurrent;
using PlayHouse.Abstractions.System;
using PlayHouse.Runtime.ServerMesh.Discovery;

namespace PlayHouse.Verification.Shared.Infrastructure;

/// <summary>
/// E2E 검증용 SystemController 구현체.
/// 서버 간 자동 연결 및 디스커버리를 지원합니다.
/// </summary>
/// <remarks>
/// InMemorySystemController와 유사하지만, E2E 테스트에서 명시적으로 서버를 등록하고
/// 초기화할 수 있는 추가 메서드를 제공합니다.
/// </remarks>
public class TestSystemController : ISystemController
{
    private static readonly ConcurrentDictionary<string, ServerInfoEntry> _servers = new();
    private readonly TimeSpan _ttl;

    /// <summary>
    /// 새 TestSystemController 인스턴스를 생성합니다.
    /// </summary>
    public TestSystemController()
    {
        _ttl = TimeSpan.FromSeconds(30);
    }

    /// <summary>
    /// 새 TestSystemController 인스턴스를 생성합니다.
    /// </summary>
    /// <param name="ttl">서버 정보 TTL.</param>
    public TestSystemController(TimeSpan ttl)
    {
        _ttl = ttl;
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<IServerInfo>> UpdateServerInfoAsync(IServerInfo serverInfo)
    {
        // 내 서버 정보 저장
        _servers[serverInfo.ServerId] = new ServerInfoEntry(serverInfo, DateTimeOffset.UtcNow);

        // 만료된 서버 정리
        var expiredTime = DateTimeOffset.UtcNow - _ttl;
        var expired = _servers
            .Where(kvp => kvp.Value.UpdatedAt < expiredTime)
            .Select(kvp => kvp.Key)
            .ToList();

        foreach (var key in expired)
        {
            _servers.TryRemove(key, out _);
        }

        // 전체 서버 목록 반환
        var result = _servers.Values
            .Select(e => e.ServerInfo)
            .ToList();

        return Task.FromResult<IReadOnlyList<IServerInfo>>(result);
    }

    /// <summary>
    /// 전체 서버 정보를 초기화합니다 (테스트용).
    /// </summary>
    public static void Reset()
    {
        _servers.Clear();
    }

    /// <summary>
    /// 서버 정보를 수동으로 등록합니다 (테스트용).
    /// </summary>
    /// <remarks>
    /// 테스트 초기화 시점에 서버 정보를 미리 등록하면 ServerAddressResolver가
    /// 자동으로 해당 서버에 연결할 수 있습니다.
    /// </remarks>
    /// <param name="serverInfo">등록할 서버 정보.</param>
    public static void RegisterServer(IServerInfo serverInfo)
    {
        _servers[serverInfo.ServerId] = new ServerInfoEntry(serverInfo, DateTimeOffset.UtcNow);
    }

    /// <summary>
    /// 현재 등록된 모든 서버 정보를 반환합니다 (테스트용).
    /// </summary>
    public static IReadOnlyList<IServerInfo> GetAllServers()
    {
        return _servers.Values.Select(e => e.ServerInfo).ToList();
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
