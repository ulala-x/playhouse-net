﻿using PlayHouse.Communicator;
using PlayHouse.Production.Shared;

namespace PlayHouse.Service.Shared
{
    internal class XSystemPanel : ISystemPanel
    {
        private readonly IServerInfoCenter _serverInfoCenter;
        private readonly IClientCommunicator _clientCommunicator;
        private readonly UniqueIdGenerator _uniqueIdGenerator;

        public Communicator.Communicator? Communicator { get; set; }

        public XSystemPanel(IServerInfoCenter serverInfoCenter, IClientCommunicator clientCommunicator, int NodeId)
        {
            _serverInfoCenter = serverInfoCenter;
            _clientCommunicator = clientCommunicator;
            _uniqueIdGenerator = new UniqueIdGenerator(NodeId);
        }

        public IServerInfo GetServerInfoBy(ushort serviceId)
        {
            return _serverInfoCenter.FindRoundRobinServer(serviceId);
        }
        public IServerInfo GetServerInfoBy(ushort serviceId, string accountId)
        {
            return _serverInfoCenter.FindServerByAccountId(serviceId, accountId);
        }

        public IServerInfo GetServerInfoByEndpoint(string endpoint)
        {
            return _serverInfoCenter.FindServer(endpoint);
        }

        public IList<IServerInfo> GetServers()
        {
            return _serverInfoCenter.GetServerList().Cast<IServerInfo>().ToList();
        }

        public void Pause()
        {
            Communicator!.Pause();
        }

        public void Resume()
        {
            Communicator!.Resume();
        }

        public async Task ShutdownASync()
        {
            await Communicator!.StopAsync();
        }

        public ServerState GetServerState()
        {
            return Communicator!.GetServerState();
        }

        public long GenerateUUID()
        {
            return _uniqueIdGenerator.NextId();
        }

        
    }
}
