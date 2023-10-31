﻿using PlayHouse.Communicator;

namespace PlayHouse.Production
{
    public interface IServerInfoCenter
    {
        IList<XServerInfo> Update(IList<XServerInfo> serverList);
        XServerInfo FindServer(string endpoint);
        XServerInfo FindRoundRobinServer(ushort serviceId);
        IList<XServerInfo> GetServerList();
        XServerInfo FindServerByAccountId(ushort serviceId, Guid accountId);
        ServiceType FindServerType(ushort serviceId);
    }
}
