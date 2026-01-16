#nullable enable

using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PlayHouse.Abstractions.System;
using PlayHouse.Runtime.ServerMesh.Discovery;

namespace PlayHouse.Benchmark.Server;

/// <summary>
/// 벤치마크용 단순 SystemController 구현.
/// </summary>
public class BenchmarkSystemController : ISystemController
{
    private static readonly ConcurrentDictionary<string, ServerInfoEntry> Servers = new();
    private readonly TimeSpan _ttl = TimeSpan.FromSeconds(30);
    private readonly ILogger<BenchmarkSystemController> _logger;

    public BenchmarkSystemController(ILogger<BenchmarkSystemController>? logger = null)
    {
        _logger = logger ?? NullLogger<BenchmarkSystemController>.Instance;
    }

    public Task<IReadOnlyList<IServerInfo>> UpdateServerInfoAsync(IServerInfo serverInfo)
    {
        Servers[serverInfo.ServerId] = new ServerInfoEntry(serverInfo, DateTimeOffset.UtcNow);

        // 만료된 서버 정리
        var expiredTime = DateTimeOffset.UtcNow - _ttl;
        var expired = Servers
            .Where(kvp => kvp.Value.UpdatedAt < expiredTime)
            .Select(kvp => kvp.Key)
            .ToList();

        foreach (var key in expired)
        {
            Servers.TryRemove(key, out _);
        }

        var result = Servers.Values
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
