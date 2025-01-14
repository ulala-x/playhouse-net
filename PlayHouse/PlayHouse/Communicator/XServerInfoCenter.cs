using System.Collections.Immutable;
using PlayHouse.Production.Shared;
using PlayHouse.Utils;

namespace PlayHouse.Communicator;

internal class XServerInfoCenter(bool debugMode) : IServerInfoCenter
{
    private LOG<XServerCommunicator> _log = new();
    private int _offset;
    private ImmutableList<XServerInfo> _serverInfoList = ImmutableList<XServerInfo>.Empty;

    public IReadOnlyList<XServerInfo> Update(IReadOnlyList<XServerInfo> serverList)
    {
        if (serverList.Count == 0)
        {
            return _serverInfoList;
        }

        // 기존 리스트를 복사 (수정 가능한 List로 변환)
        var currentList = _serverInfoList.ToList();

        List<XServerInfo> updateList = [];

        foreach (var incomingServer in serverList)
        {
            // 기존 서버 정보와 동일한 Endpoint를 가진 서버 검색
            var existingServer = currentList.FirstOrDefault(x => x.GetNid() == incomingServer.GetNid());

            if (existingServer != null)
            {
                // 기존 서버 정보 업데이트
                if (existingServer.GetBindEndpoint() != incomingServer.GetBindEndpoint())
                {
                    //기존 주소는 disconnect 해야줘야함
                    var toDisconnectServer = XServerInfo.Of(existingServer);
                    toDisconnectServer.SetState(ServerState.DISABLE);
                    updateList.Add(toDisconnectServer);
                }

                existingServer.Update(incomingServer);
            }
            else
            {
                // 새 서버 추가
                currentList.Add(incomingServer);
            }
        }

        if (!debugMode)
        {
            foreach (var server in currentList)
            {
                server.CheckTimeout();
            }
        }

        // 정렬 후 ImmutableList로 변환
        var newList = currentList.OrderBy(x => x.GetNid()).ToImmutableList();

        // 기존 리스트를 원자적으로 교체
        _serverInfoList = newList;

        updateList.AddRange(_serverInfoList);

        return updateList;
    }


    public XServerInfo FindServer(string nid)
    {
        // 최신 _serverInfoList를 직접 읽어 사용
        var serverInfo = _serverInfoList
            .FirstOrDefault(e => e.IsValid() && e.GetNid() == nid);

        if (serverInfo == null)
        {
            throw new CommunicatorException.NotExistServerInfo($"target nid:{nid}, ServerInfo is not exist");
        }

        return serverInfo;
    }

    public XServerInfo FindRoundRobinServer(ushort serviceId)
    {
        // 최신 _serverInfoList를 직접 읽어 사용
        var list = _serverInfoList
            .Where(x => x.IsValid() && x.GetServiceId() == serviceId)
            .ToList();

        if (!list.Any())
        {
            throw new CommunicatorException.NotExistServerInfo($"serviceId:{serviceId}, ServerInfo is not exist");
        }

        // Round-robin 방식으로 다음 서버 선택
        var next = Interlocked.Increment(ref _offset);
        var index = Math.Abs(next) % list.Count; // 음수 방지
        return list[index];
    }

    public IReadOnlyList<XServerInfo> GetServerList()
    {
        // 최신 _serverInfoList를 직접 반환
        return _serverInfoList;
    }

    public XServerInfo FindServerByAccountId(ushort serviceId, long accountId)
    {
        // 최신 _serverInfoList를 직접 읽어 사용
        var list = _serverInfoList
            .Where(e => e.IsValid() && e.GetServiceId() == serviceId)
            .ToList();

        if (list.Count == 0)
        {
            throw new CommunicatorException.NotExistServerInfo($"serviceId:{serviceId}, ServerInfo is not exist");
        }

        // Account ID를 기준으로 리스트에서 서버 선택
        var index = (int)(accountId % list.Count);
        return list[index];
    }

    public ServiceType FindServerType(ushort serviceId)
    {
        // 최신 _serverInfoList를 직접 읽어 사용
        var list = _serverInfoList
            .Where(info => info.GetServiceId() == serviceId)
            .ToList();

        if (list.Count == 0)
        {
            throw new CommunicatorException.NotExistServerInfo($"serviceId:{serviceId}, ServerInfo is not exist");
        }

        return list.First().GetServiceType();
    }
}