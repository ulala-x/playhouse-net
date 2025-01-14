using Google.Protobuf;
using PlayHouse.Production.Shared;
using Playhouse.Protocol;

namespace PlayHouse.Communicator;

internal class XServerInfo(
    string bindEndpoint,
    ushort serviceId,
    int serverId,
    string nid,
    ServiceType serviceType,
    ServerState serverState,
    int actorCount,
    long lastUpdate)
    : IServerInfo
{
    public string GetBindEndpoint()
    {
        return bindEndpoint;
    }

    public string GetNid()
    {
        return nid;
    }

    public int GetServerId()
    {
        return serverId;
    }

    public ServiceType GetServiceType()
    {
        return serviceType;
    }

    public ushort GetServiceId()
    {
        return serviceId;
    }

    public ServerState GetState()
    {
        return serverState;
    }

    public long GetLastUpdate()
    {
        return lastUpdate;
    }

    public int GetActorCount()
    {
        return actorCount;
    }


    public static XServerInfo Of(string bindEndpoint, IService service)
    {
        return new XServerInfo(
            bindEndpoint,
            service.ServiceId,
            service.ServerId,
            service.Nid,
            service.GetServiceType(),
            service.GetServerState(),
            service.GetActorCount(),
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        );
    }

    public static XServerInfo Of(ServerInfoMsg infoMsg)
    {
        return new XServerInfo(
            infoMsg.Endpoint,
            (ushort)infoMsg.ServiceId,
            infoMsg.ServerId,
            infoMsg.Nid,
            Enum.Parse<ServiceType>(infoMsg.ServiceType),
            Enum.Parse<ServerState>(infoMsg.ServerState),
            infoMsg.ActorCount,
            infoMsg.Timestamp
        );
    }

    public static XServerInfo Of(
        string bindEndpoint,
        ushort serviceId,
        int serverId,
        string nid,
        ServiceType serviceType,
        ServerState state,
        int actorCount,
        long timeStamp)
    {
        return new XServerInfo(
            bindEndpoint,
            serviceId,
            serverId,
            nid,
            serviceType,
            state,
            actorCount,
            timeStamp);
    }

    public static XServerInfo Of(IServerInfo serverInfo)
    {
        return new XServerInfo(
            serverInfo.GetBindEndpoint(),
            serverInfo.GetServiceId(),
            serverInfo.GetServerId(),
            serverInfo.GetNid(),
            serverInfo.GetServiceType(),
            serverInfo.GetState(),
            serverInfo.GetActorCount(),
            serverInfo.GetLastUpdate());
    }

    public ServerInfoMsg ToMsg()
    {
        return new ServerInfoMsg
        {
            ServiceType = serviceType.ToString(),
            ServiceId = serviceId,
            Endpoint = bindEndpoint,
            ServerState = serverState.ToString(),
            Timestamp = lastUpdate,
            ActorCount = actorCount
        };
    }

    public bool TimeOver()
    {
        if (ConstOption.ServerTimeLimitMs == 0) return false;

        return DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - lastUpdate > ConstOption.ServerTimeLimitMs;
    }

    public void Update(XServerInfo serverInfo)
    {
        serverState = serverInfo.GetState();
        lastUpdate = serverInfo.GetLastUpdate();
        actorCount = serverInfo.GetActorCount();

        serviceId = serverInfo.GetServiceId();
        serverId = serverInfo.GetServerId();
        bindEndpoint = serverInfo.GetBindEndpoint();
    }

    public bool IsValid()
    {
        return serverState == ServerState.RUNNING;
    }

    public byte[] ToByteArray()
    {
        return ToMsg().ToByteArray();
    }

    public bool CheckTimeout()
    {
        if (TimeOver())
        {
            serverState = ServerState.DISABLE;
            return true;
        }

        return false;
    }


    public override string ToString()
    {
        return
            $"[endpoint: {GetBindEndpoint}, service type: {GetServiceType}, serviceId: {GetServiceId}, state: {GetState}, actor count: {GetActorCount}, GetLastUpdate: {GetLastUpdate}]";
    }

    internal void SetState(ServerState state)
    {
        serverState = state;
    }

    internal void SetLastUpdate(long updatTime)
    {
        lastUpdate = updatTime;
    }
}