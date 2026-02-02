#nullable enable

using System;
using MessagePack;
using PlayHouse.Connector.Protocol;

namespace PlayHouse.Connector.Extensions.MessagePack;

/// <summary>
/// Extension methods for creating Packet instances from MessagePack objects.
/// </summary>
public static class MsgPackConnectorExtensions
{
    /// <summary>
    /// Creates a Packet from a MessagePack-serializable object.
    /// </summary>
    /// <typeparam name="T">The object type to serialize.</typeparam>
    /// <param name="obj">The object to serialize as MessagePack.</param>
    /// <returns>A new Packet containing the MessagePack payload.</returns>
    /// <example>
    /// <code>
    /// var chatMsg = new ChatMessage { Content = "Hello" };
    /// var packet = MsgPackConnectorExtensions.Of(chatMsg);
    /// await connector.SendAsync(packet);
    /// </code>
    /// </example>
    public static Packet Of<T>(T obj) where T : class
    {
        var msgId = typeof(T).Name;
        var bytes = MessagePackSerializer.Serialize(obj);
        return new Packet(msgId, bytes);
    }
}
