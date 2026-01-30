#nullable enable

using MessagePack;
using PlayHouse.Core.Shared;

namespace PlayHouse.Extensions.MessagePack;

public static class MsgPackCPacketExtensions
{
    public static CPacket Of<T>(T obj) where T : class
    {
        var msgId = typeof(T).Name;
        var bytes = MessagePackSerializer.Serialize(obj);
        return CPacket.Of(msgId, bytes);
    }
}
