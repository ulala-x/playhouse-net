#nullable enable

using System.Buffers;
using MessagePack;
using PlayHouse.Abstractions;

namespace PlayHouse.Extensions.MessagePack;

public static class MsgPackPacketExtensions
{
    public static T Parse<T>(this IPacket packet) where T : class
    {
        var span = packet.Payload.DataSpan;
        var sequence = new ReadOnlySequence<byte>(span.ToArray());
        return MessagePackSerializer.Deserialize<T>(sequence)
            ?? throw new InvalidOperationException($"Failed to deserialize {typeof(T).Name}");
    }

    public static bool TryParse<T>(this IPacket packet, out T? result) where T : class
    {
        try
        {
            var span = packet.Payload.DataSpan;
            var sequence = new ReadOnlySequence<byte>(span.ToArray());
            result = MessagePackSerializer.Deserialize<T>(sequence);
            return result != null;
        }
        catch
        {
            result = default;
            return false;
        }
    }
}
