namespace PlayHouse.Production.Shared;

public delegate Task SystemHandler(IPacket packet, ISystemPanel panel, ISender sender);

public interface ISystemHandlerRegister
{
    void Add(string msgId, SystemHandler handler);
}

public interface ISystemController
{
    void Handles(ISystemHandlerRegister handlerRegister);

    Task<IReadOnlyList<IServerInfo>> UpdateServerInfoAsync(IServerInfo serverInfo);
}