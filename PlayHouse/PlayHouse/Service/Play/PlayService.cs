using PlayHouse.Communicator;
using PlayHouse.Communicator.Message;
using PlayHouse.Production.Play;
using PlayHouse.Production.Shared;
using PlayHouse.Utils;

namespace PlayHouse.Service.Play;

internal class PlayService(
    ushort serviceId,
    int serverId,
    string nid,
    PlayOption playOption,
    IClientCommunicator clientCommunicator,
    RequestCache requestCache,
    IServerInfoCenter serverInfoCenter)
    : IService
{
    private readonly LOG<PlayService> _log = new();

    private readonly PlayDispatcher _playDispatcher = new(serviceId, clientCommunicator, requestCache, serverInfoCenter,
        nid, playOption);

    private readonly AtomicEnum<ServerState> _state = new(ServerState.DISABLE);
    public ushort ServiceId { get; } = serviceId;
    public int ServerId { get; } = serverId;
    public string Nid { get; } = nid;

    public void OnStart()
    {
        _state.Set(ServerState.RUNNING);
        _playDispatcher.Start();
    }


    public void OnStop()
    {
        _state.Set(ServerState.DISABLE);
        _playDispatcher.Stop();
    }

    public ServerState GetServerState()
    {
        return _state.Get();
    }

    public ServiceType GetServiceType()
    {
        return ServiceType.Play;
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
        return _playDispatcher.GetActorCount();
    }

    public void OnPost(RoutePacket routePacket)
    {
        _playDispatcher.OnPost(routePacket);
    }
}