﻿using PlayHouse.Communicator;

namespace PlayHouse.Service
{
    public class XSystemPanel : ISystemPanel
    {
        private readonly IServerInfoCenter _serverInfoCenter;
        private readonly IClientCommunicator _clientCommunicator;
        private readonly UniqueIdGenerator _uniqueIdGenerator;

        public Communicator.Communicator? Communicator { get; set; }

        public XSystemPanel(IServerInfoCenter serverInfoCenter, IClientCommunicator clientCommunicator,int NodeId)
        {
            this._serverInfoCenter = serverInfoCenter;
            this._clientCommunicator = clientCommunicator;
            this._uniqueIdGenerator = new UniqueIdGenerator(NodeId);
        }

        public IServerInfo RandomServerInfo(short serviceId)
        {
            return _serverInfoCenter.FindRoundRobinServer(serviceId);
        }

        public IServerInfo ServerInfo(string endpoint)
        {
            return _serverInfoCenter.FindServer(endpoint);
        }

        public IList<IServerInfo> ServerList()
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

        public void Shutdown()
        {
            Communicator!.Stop();
        }

        public ServerState ServerState()
        {
            return Communicator!.GetServerState();
        }

        public long GenerateUUID()
        {
            return _uniqueIdGenerator.NextId();
        }
    }
}