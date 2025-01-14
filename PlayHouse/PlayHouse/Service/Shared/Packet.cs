using Google.Protobuf;
using PlayHouse.Communicator.Message;
using PlayHouse.Production.Shared;

namespace PlayHouse.Service.Shared;

public delegate void ReplyCallback(ushort errorCode, IPacket reply);

internal class CPacket
{
    public static IPacket Of(string msgId, ByteString message)
    {
        return PacketProducer.CreatePacket(msgId, new ByteStringPayload(message), 0);
    }

    public static IPacket Of(IMessage message)
    {
        return PacketProducer.CreatePacket(message.Descriptor.Name, new ProtoPayload(message), 0);
    }

    public static IPacket Of(string msgId, IPayload payload)
    {
        return PacketProducer.CreatePacket(msgId, payload, 0);
    }

    //public static IPacket Of(ReplyPacket replyPacket)
    //{
    //    return PacketProducer.CreatePacket(replyPacket.MsgId, replyPacket.Payload, 0);
    //}

    public static IPacket Of(RoutePacket packet)
    {
        return PacketProducer.CreatePacket(packet.MsgId, packet.Payload, 0);
    }
}

internal class XPacket : IPacket
{
    private XPacket(string msgId, IPayload payload, int msgSeq)
    {
        MsgId = msgId;
        Payload = payload;
        MsgSeq = msgSeq;
    }

    public int MsgSeq { get; set; }

    public string MsgId { get; }

    public IPayload Payload { get; }

    public void Dispose()
    {
        Payload.Dispose();
    }

    public static XPacket Of(IMessage message)
    {
        return new XPacket(message.Descriptor.Name, new ProtoPayload(message), 0);
    }

    //public IPacket Copy()
    //{
    //    throw new NotImplementedException();
    //}
    //public T Parse<T>()
    //{
    //    throw new NotImplementedException();
    //}
}

internal class EmptyPacket : IPacket
{
    private int _msgSeq;

    public int MsgSeq
    {
        get => 0;
        set => _msgSeq = value;
    }

    public string MsgId => string.Empty;
    public IPayload Payload { get; } = new EmptyPayload();

    public void Dispose()
    {
        Payload.Dispose();
    }

    //public IPacket Copy()
    //{
    //    throw new NotImplementedException();
    //}

    //public T Parse<T>()
    //{
    //    throw new NotImplementedException();
    //}
}