using PlayHouse.Communicator.Message;
using PlayHouse.Production.Shared;

namespace PlayHouse.Service.Shared;

internal static class PacketProducer
{
    private static Func<string, IPayload, ushort, IPacket>? CreateFunc { get; set; } //msgId,


    public static void
        Init(Func<string, IPayload, ushort, IPacket> createFunc) //int msgId, payload,msgSeq return IPacket
    {
        CreateFunc = createFunc;
    }

    public static IPacket CreatePacket(string msgId, IPayload payload, ushort msgSeq)
    {
        return CreateFunc!.Invoke(msgId, payload, msgSeq);
    }
}