#nullable enable

using Google.Protobuf;
using PlayHouse.Abstractions;
using PlayHouse.Core.Shared;

namespace PlayHouse.Extensions.Proto;

/// <summary>
/// Extension methods for creating CPacket instances from Protobuf messages.
/// </summary>
public static class ProtoCPacketExtensions
{
    /// <summary>
    /// Creates a CPacket from a Protobuf message.
    /// </summary>
    /// <typeparam name="T">The Protobuf message type.</typeparam>
    /// <param name="message">The Protobuf message to wrap.</param>
    /// <returns>A new CPacket containing the message.</returns>
    /// <example>
    /// <code>
    /// var chatMsg = new ChatMessage { Content = "Hello" };
    /// var packet = ProtoCPacketExtensions.OfProto(chatMsg);
    /// sender.Reply(packet);
    /// </code>
    /// </example>
    public static CPacket OfProto<T>(T message) where T : IMessage
    {
        var msgId = typeof(T).Name;
        return CPacket.Of(msgId, new ProtoPayload(message));
    }
}
