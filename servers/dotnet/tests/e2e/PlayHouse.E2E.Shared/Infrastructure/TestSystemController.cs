#nullable enable

using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using PlayHouse.Abstractions;
using PlayHouse.Abstractions.System;
using PlayHouse.E2E.Shared.Proto;
using PlayHouse.Runtime.ServerMesh.Discovery;

namespace PlayHouse.E2E.Shared.Infrastructure;

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
    private readonly ILogger<TestSystemController> _logger;
    private static readonly ConcurrentDictionary<string, ServerInfoEntry> _servers = new();
    private readonly TimeSpan _ttl;

    // 시스템 메시지 수신 기록 (E2E 테스트 검증용)
    private static readonly ConcurrentBag<SystemEchoRequest> _receivedSystemMessages = new();
    private readonly string _serverId;

    /// <summary>
    /// 수신된 시스템 메시지 목록을 반환합니다 (테스트용).
    /// </summary>
    public static IReadOnlyList<SystemEchoRequest> ReceivedSystemMessages =>
        _receivedSystemMessages.ToList();

    /// <summary>
    /// 수신된 시스템 메시지 기록을 초기화합니다 (테스트용).
    /// </summary>
    public static void ResetSystemMessages() => _receivedSystemMessages.Clear();

    /// <summary>
    /// 새 TestSystemController 인스턴스를 생성합니다.
    /// </summary>
    /// <param name="logger">로거 인스턴스 (DI를 통해 주입).</param>
    public TestSystemController(ILogger<TestSystemController> logger)
    {
        _logger = logger;
        _serverId = "unknown";
        _ttl = TimeSpan.FromSeconds(30);
        _logger.LogDebug("TestSystemController created with ServerId={ServerId}, TTL={Ttl}", _serverId, _ttl);
    }

    /// <inheritdoc/>
    public void Handles(ISystemHandlerRegister register)
    {
        
        register.Add(SystemEchoRequest.Descriptor.Name, async (packet, sender) =>
        {
            var msg = SystemEchoRequest.Parser.ParseFrom(packet.Payload.DataSpan);
            _receivedSystemMessages.Add(msg);
            _logger.LogDebug("[System] Received SystemEchoRequest: Content={Content}, From={From}, HandledBy={HandledBy}",
                msg.Content, msg.FromServerId, _serverId);
            await Task.CompletedTask;
        });
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
