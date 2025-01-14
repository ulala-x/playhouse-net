using PlayHouse.Communicator;
using PlayHouse.Communicator.Message;
using PlayHouse.Production.Shared;
using PlayHouse.Service.Session.Network;
using PlayHouse.Service.Shared;

namespace PlayHouse.Service.Session;

internal class XSessionSender(
    ushort serviceId,
    IClientCommunicator clientCommunicator,
    RequestCache reqCache,
    ISession session,
    ISessionDispatcher sessionDispatcher)
    : XSender(serviceId, clientCommunicator, reqCache), ISessionSender
{
    private ushort _msgSeq;

    public void SendToClient(IPacket packet)
    {
        var routePacket = RoutePacket.Of(packet);
        routePacket.Header.ServiceId = ServiceId;

        sessionDispatcher.SendToClient(session, routePacket.ToClientPacket());
    }

    public void ReplyToClient(IPacket packet)
    {
        var routePacket = RoutePacket.Of(packet);

        routePacket.Header.MsgSeq = _msgSeq;
        routePacket.Header.ServiceId = ServiceId;

        sessionDispatcher.SendToClient(session, routePacket.ToClientPacket());
    }

    public void RelayToStage(string playNid, long stageId, long sid, long accountId, ClientPacket packet)
    {
        var routePacket = RoutePacket.ApiOf(packet.ToRoutePacket(), false, false);
        routePacket.RouteHeader.StageId = stageId;
        routePacket.RouteHeader.AccountId = accountId;
        routePacket.RouteHeader.Header.MsgSeq = packet.MsgSeq;
        routePacket.RouteHeader.Sid = sid;
        routePacket.RouteHeader.IsToClient = false;
        ClientCommunicator.Send(playNid, routePacket);
    }

    public void RelayToApi(string apiNid, long sid, long accountId, ClientPacket packet)
    {
        var routePacket = RoutePacket.ApiOf(packet.ToRoutePacket(), false, false);
        routePacket.RouteHeader.Sid = sid;
        routePacket.RouteHeader.Header.MsgSeq = packet.MsgSeq;
        routePacket.RouteHeader.IsToClient = false;
        routePacket.RouteHeader.AccountId = accountId;

        ClientCommunicator.Send(apiNid, routePacket);
    }

    public void SendToClient(ClientPacket packet)
    {
        sessionDispatcher.SendToClient(session, packet);
    }

    public void RelayToClient(ClientPacket packet)
    {
        session.Send(packet);
    }

    public void SetClientRequestMsgSeq(ushort headerMsgSeq)
    {
        _msgSeq = headerMsgSeq;
    }
}