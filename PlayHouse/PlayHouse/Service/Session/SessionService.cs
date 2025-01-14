using PlayHouse.Communicator;
using PlayHouse.Communicator.Message;
using PlayHouse.Production.Session;
using PlayHouse.Production.Shared;
using PlayHouse.Utils;

namespace PlayHouse.Service.Session;

internal class SessionService(
    ushort serviceId,
    int serverId,
    string nid,
    SessionOption sessionOption,
    IServerInfoCenter serverInfoCenter,
    IClientCommunicator clientCommunicator,
    RequestCache requestCache,
    bool showQps)
    : IService
{
    private readonly LOG<SessionService> _log = new();
    private readonly PerformanceTester _performanceTester = new(showQps, "client");

    private readonly SessionDispatcher _sessionDispatcher =
        new(serviceId, sessionOption, serverInfoCenter, clientCommunicator, requestCache);

    private readonly AtomicEnum<ServerState> _state = new(ServerState.DISABLE);


    public ushort ServiceId { get; } = serviceId;
    public int ServerId { get; } = serverId;
    public string Nid { get; } = nid;


    public void OnStart()
    {
        _state.Set(ServerState.RUNNING);

        _sessionDispatcher.Start();
        _performanceTester.Start();
    }

    public void OnPost(RoutePacket routePacket)
    {
        _sessionDispatcher.OnPost(routePacket);
    }


    public void OnStop()
    {
        _performanceTester.Stop();
        _sessionDispatcher.Stop();

        _state.Set(ServerState.DISABLE);
    }

    public ServerState GetServerState()
    {
        return _state.Get();
    }

    public ServiceType GetServiceType()
    {
        return ServiceType.SESSION;
    }

    public void OnPause()
    {
        _state.Set(ServerState.PAUSE);
    }

    public void OnResume()
    {
        _state.Set(ServerState.RUNNING);
    }

    public int GetActorCount()
    {
        return _sessionDispatcher.GetActorCount();
    }

    public ushort GetServiceId()
    {
        return ServiceId;
    }
}