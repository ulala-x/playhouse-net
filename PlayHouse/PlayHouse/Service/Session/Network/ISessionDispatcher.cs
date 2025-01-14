using PlayHouse.Communicator.Message;

namespace PlayHouse.Service.Session.Network;

internal interface ISessionDispatcher
{
    void OnConnect(long sid, ISession session, string remoteIp);
    void OnReceive(long sid, ClientPacket clientPacket);
    void OnDisconnect(long sid);
    void SendToClient(ISession session, ClientPacket routePacket);
}