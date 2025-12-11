#nullable enable

using System.Collections.Concurrent;

namespace PlayHouse.Runtime.ServerMesh.Discovery;

/// <summary>
/// 서버 정보 캐시 및 관리.
/// </summary>
/// <remarks>
/// 모든 서버 정보를 캐시하고, 서비스 타입별로 조회할 수 있습니다.
/// Round-robin 방식으로 로드밸런싱을 지원합니다.
/// </remarks>
public sealed class XServerInfoCenter
{
    private readonly ConcurrentDictionary<string, XServerInfo> _servers = new();
    private readonly ConcurrentDictionary<ushort, int> _roundRobinIndex = new();
    private readonly object _updateLock = new();

    /// <summary>
    /// 현재 등록된 서버 수.
    /// </summary>
    public int Count => _servers.Count;

    /// <summary>
    /// 서버 목록을 갱신합니다.
    /// </summary>
    /// <param name="serverList">새 서버 목록.</param>
    /// <returns>상태가 변경된 서버 목록 (추가, 제거, 상태변경).</returns>
    public List<ServerChange> Update(IEnumerable<IServerInfo> serverList)
    {
        var changes = new List<ServerChange>();
        var newServers = new HashSet<string>();

        lock (_updateLock)
        {
            foreach (var server in serverList)
            {
                var info = server as XServerInfo ?? new XServerInfo(
                    server.ServiceId,
                    server.ServerId,
                    server.Address,
                    server.State,
                    server.Weight);

                newServers.Add(info.Nid);

                if (_servers.TryGetValue(info.Nid, out var existing))
                {
                    // 상태 변경 확인
                    if (existing.State != info.State || existing.Address != info.Address)
                    {
                        _servers[info.Nid] = info;
                        changes.Add(new ServerChange(info, ChangeType.Updated));
                    }
                }
                else
                {
                    // 새 서버 추가
                    _servers[info.Nid] = info;
                    changes.Add(new ServerChange(info, ChangeType.Added));
                }
            }

            // 제거된 서버 확인
            var toRemove = _servers.Keys.Where(nid => !newServers.Contains(nid)).ToList();
            foreach (var nid in toRemove)
            {
                if (_servers.TryRemove(nid, out var removed))
                {
                    changes.Add(new ServerChange(removed, ChangeType.Removed));
                }
            }
        }

        return changes;
    }

    /// <summary>
    /// NID로 서버 정보를 조회합니다.
    /// </summary>
    /// <param name="nid">Node ID.</param>
    /// <returns>서버 정보 또는 null.</returns>
    public XServerInfo? GetServer(string nid)
    {
        _servers.TryGetValue(nid, out var server);
        return server;
    }

    /// <summary>
    /// 서비스 타입별로 서버를 조회합니다 (Round-robin).
    /// </summary>
    /// <param name="serviceId">서비스 ID.</param>
    /// <returns>다음 서버 정보 또는 null.</returns>
    public XServerInfo? GetServerByService(ushort serviceId)
    {
        var servers = GetServerListByService(serviceId)
            .Where(s => s.State == ServerState.Running)
            .ToList();

        if (servers.Count == 0)
            return null;

        var index = _roundRobinIndex.AddOrUpdate(serviceId, 0, (_, i) => (i + 1) % servers.Count);
        return servers[index % servers.Count];
    }

    /// <summary>
    /// 서비스 타입별 모든 서버 목록을 조회합니다.
    /// </summary>
    /// <param name="serviceId">서비스 ID.</param>
    /// <returns>해당 서비스의 모든 서버 목록.</returns>
    public IReadOnlyList<XServerInfo> GetServerListByService(ushort serviceId)
    {
        return _servers.Values
            .Where(s => s.ServiceId == serviceId)
            .ToList();
    }

    /// <summary>
    /// 모든 서버 목록을 조회합니다.
    /// </summary>
    /// <returns>모든 서버 목록.</returns>
    public IReadOnlyList<XServerInfo> GetAllServers()
    {
        return _servers.Values.ToList();
    }

    /// <summary>
    /// 활성화된 서버 목록을 조회합니다.
    /// </summary>
    /// <returns>Running 상태의 서버 목록.</returns>
    public IReadOnlyList<XServerInfo> GetActiveServers()
    {
        return _servers.Values
            .Where(s => s.State == ServerState.Running)
            .ToList();
    }

    /// <summary>
    /// 특정 서버를 제거합니다.
    /// </summary>
    /// <param name="nid">제거할 서버의 NID.</param>
    /// <returns>제거된 서버 정보 또는 null.</returns>
    public XServerInfo? Remove(string nid)
    {
        _servers.TryRemove(nid, out var removed);
        return removed;
    }

    /// <summary>
    /// 모든 서버 정보를 초기화합니다.
    /// </summary>
    public void Clear()
    {
        _servers.Clear();
        _roundRobinIndex.Clear();
    }
}

/// <summary>
/// 서버 변경 타입.
/// </summary>
public enum ChangeType
{
    /// <summary>
    /// 새 서버 추가됨.
    /// </summary>
    Added,

    /// <summary>
    /// 서버 정보 갱신됨.
    /// </summary>
    Updated,

    /// <summary>
    /// 서버 제거됨.
    /// </summary>
    Removed
}

/// <summary>
/// 서버 변경 정보.
/// </summary>
public readonly struct ServerChange
{
    /// <summary>
    /// 변경된 서버 정보.
    /// </summary>
    public XServerInfo Server { get; }

    /// <summary>
    /// 변경 타입.
    /// </summary>
    public ChangeType Type { get; }

    /// <summary>
    /// 새 ServerChange 인스턴스를 생성합니다.
    /// </summary>
    public ServerChange(XServerInfo server, ChangeType type)
    {
        Server = server;
        Type = type;
    }

    /// <inheritdoc/>
    public override string ToString() => $"{Type}: {Server}";
}
