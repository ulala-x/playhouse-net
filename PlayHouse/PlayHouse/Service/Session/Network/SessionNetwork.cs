using PlayHouse.Production.Session;
using PlayHouse.Service.Session.Network.Tcp;
using PlayHouse.Service.Session.Network.Websocket;

namespace PlayHouse.Service.Session.Network;

internal class SessionNetwork
{
    private readonly ISessionNetwork _sessionNetwork;

    public SessionNetwork(SessionOption sessionOption, ISessionDispatcher sessionDispatcher)
    {
        if (sessionOption.UseWebSocket)
        {
            _sessionNetwork = new WsSessionNetwork(sessionOption, sessionDispatcher);
        }
        else
        {
            _sessionNetwork = new TcpSessionNetwork(sessionOption, sessionDispatcher);
        }
    }

    public void Start()
    {
        _sessionNetwork.Start();
    }

    public void Stop()
    {
        _sessionNetwork.Stop();
    }

    public void Await()
    {
        //_sessionThread!.Join();
    }
}