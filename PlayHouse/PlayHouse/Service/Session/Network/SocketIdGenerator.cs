using PlayHouse.Service.Shared;

namespace PlayHouse.Service.Session.Network;

public static class SocketIdGenerator
{
    public static readonly UniqueIdGenerator IdGenerator = new(0);
}