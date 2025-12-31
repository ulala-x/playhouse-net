#nullable enable

using System.Collections.Concurrent;
using PlayHouse.Abstractions.System;
using PlayHouse.Runtime.ServerMesh.Discovery;

namespace PlayHouse.Benchmark.SS.PlayServer;

/// <summary>
/// 벤치마크용 단순 SystemController 구현.
/// </summary>
public class BenchmarkSystemController : ISystemController
{
    private static readonly ConcurrentDictionary<string, ServerInfoEntry> _servers = new();
    private readonly TimeSpan _ttl = TimeSpan.FromSeconds(30);

    public Task<IReadOnlyList<IServerInfo>> UpdateServerInfoAsync(IServerInfo serverInfo)
    {
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

        var result = _servers.Values
            .Select(e => e.ServerInfo)
            .ToList();

        return Task.FromResult<IReadOnlyList<IServerInfo>>(result);
    }

    private sealed class ServerInfoEntry(IServerInfo serverInfo, DateTimeOffset updatedAt)
    {
        public IServerInfo ServerInfo { get; } = serverInfo;
        public DateTimeOffset UpdatedAt { get; } = updatedAt;
    }
}
