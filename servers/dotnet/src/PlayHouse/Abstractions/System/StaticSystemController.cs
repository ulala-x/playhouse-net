#nullable enable

using PlayHouse.Runtime.ServerMesh.Discovery;

namespace PlayHouse.Abstractions.System;

/// <summary>
/// CLI 옵션으로 전달된 정적 서버 목록을 사용하는 SystemController 구현.
/// </summary>
/// <remarks>
/// 서비스 디스커버리 없이 고정된 서버 목록으로 운영하는 환경에 적합합니다.
/// ServerId 패턴을 기반으로 ServerType을 자동 추론합니다:
/// - "play-*" -> ServerType.Play
/// - "api-*" -> ServerType.Api
/// - 그 외 -> 기본값 ServerType.Play
/// </remarks>
public sealed class StaticSystemController : ISystemController
{
    private readonly Dictionary<string, string> _peers;

    /// <summary>
    /// 새 StaticSystemController 인스턴스를 생성합니다.
    /// </summary>
    /// <param name="peers">서버 목록 (serverId -> address).</param>
    public StaticSystemController(Dictionary<string, string> peers)
    {
        _peers = peers ?? throw new ArgumentNullException(nameof(peers));
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<IServerInfo>> UpdateServerInfoAsync(IServerInfo serverInfo)
    {
        var servers = new List<IServerInfo>();

        // peers의 주소를 사용 (connect 가능한 주소)
        // 자기 자신도 포함해야 같은 서버 내 Stage간 통신 가능
        foreach (var kvp in _peers)
        {
            var serverId = kvp.Key;
            var address = kvp.Value;
            var (serverType, serviceId) = InferServerTypeAndServiceId(serverId);
            servers.Add(new XServerInfo(serverType, serviceId, serverId, address));
        }

        // peers에 자기 자신이 없으면 serverInfo 추가
        if (!_peers.ContainsKey(serverInfo.ServerId))
        {
            servers.Insert(0, serverInfo);
        }

        return Task.FromResult<IReadOnlyList<IServerInfo>>(servers);
    }

    /// <summary>
    /// 문자열에서 정적 서버 목록을 파싱합니다.
    /// </summary>
    /// <param name="peersString">서버 목록 문자열 (형식: "play-1=tcp://127.0.0.1:16100,api-1=tcp://127.0.0.1:16201").</param>
    /// <returns>파싱된 StaticSystemController 인스턴스.</returns>
    /// <exception cref="ArgumentException">잘못된 형식의 문자열.</exception>
    public static StaticSystemController Parse(string peersString)
    {
        if (string.IsNullOrWhiteSpace(peersString))
        {
            return new StaticSystemController(new Dictionary<string, string>());
        }

        var peers = new Dictionary<string, string>();
        var entries = peersString.Split(',', StringSplitOptions.RemoveEmptyEntries);

        foreach (var entry in entries)
        {
            var parts = entry.Split('=', 2);
            if (parts.Length != 2)
            {
                throw new ArgumentException($"Invalid peer entry format: '{entry}'. Expected format: 'serverId=address'");
            }

            var serverId = parts[0].Trim();
            var address = parts[1].Trim();

            if (string.IsNullOrEmpty(serverId) || string.IsNullOrEmpty(address))
            {
                throw new ArgumentException($"Invalid peer entry: '{entry}'. ServerId and address cannot be empty");
            }

            peers[serverId] = address;
        }

        return new StaticSystemController(peers);
    }

    /// <summary>
    /// 현재 등록된 서버 목록을 반환합니다 (디버깅용).
    /// </summary>
    /// <returns>서버 목록.</returns>
    public IReadOnlyDictionary<string, string> GetServerList()
    {
        return _peers;
    }

    /// <summary>
    /// ServerId 패턴으로부터 ServerType과 ServiceId를 추론합니다.
    /// </summary>
    /// <param name="serverId">서버 ID.</param>
    /// <returns>추론된 ServerType과 기본 ServiceId (1).</returns>
    private static (ServerType serverType, ushort serviceId) InferServerTypeAndServiceId(string serverId)
    {
        if (serverId.StartsWith("play-", StringComparison.OrdinalIgnoreCase))
        {
            return (ServerType.Play, ServiceIdDefaults.Default);
        }

        if (serverId.StartsWith("api-", StringComparison.OrdinalIgnoreCase))
        {
            return (ServerType.Api, ServiceIdDefaults.Default);
        }

        // 기본값
        return (ServerType.Play, ServiceIdDefaults.Default);
    }
}
