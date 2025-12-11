#nullable enable

using Google.Protobuf;
using PlayHouse.Core.Shared;

namespace PlayHouse.Abstractions;

/// <summary>
/// Extension methods for IPacket interface.
/// </summary>
public static class PacketExtensions
{
    /// <summary>
    /// Parses the packet payload as a Protobuf message.
    /// </summary>
    /// <typeparam name="T">The Protobuf message type to parse.</typeparam>
    /// <param name="packet">The packet to parse.</param>
    /// <returns>The parsed Protobuf message.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the payload cannot be parsed.</exception>
    /// <example>
    /// <code>
    /// // Parse a received packet
    /// var request = packet.Parse&lt;CreateRoomReq&gt;();
    ///
    /// // Use in message handler
    /// public void OnDispatch(IPacket packet, IStageSender sender)
    /// {
    ///     var message = packet.Parse&lt;GameMessage&gt;();
    ///     // Process message...
    /// }
    /// </code>
    /// </example>
    public static T Parse<T>(this IPacket packet) where T : IMessage<T>, new()
    {
        // If payload is already a ProtoPayload, try direct cast
        if (packet.Payload is ProtoPayload protoPayload)
        {
            var proto = protoPayload.GetProto();
            if (proto is T typedMessage)
            {
                return typedMessage;
            }
            // If types don't match, deserialize from bytes
            var parser = new MessageParser<T>(() => new T());
            return parser.ParseFrom(protoPayload.Data.Span);
        }

        // For BytePayload or other IPayload implementations, deserialize from bytes
        if (packet.Payload.Data.Length == 0)
        {
            throw new InvalidOperationException(
                $"Cannot parse empty payload as {typeof(T).Name}. " +
                "Ensure the packet contains valid Protobuf data.");
        }

        var messageParser = new MessageParser<T>(() => new T());
        return messageParser.ParseFrom(packet.Payload.Data.Span);
    }

    /// <summary>
    /// Tries to parse the packet payload as a Protobuf message.
    /// </summary>
    /// <typeparam name="T">The Protobuf message type to parse.</typeparam>
    /// <param name="packet">The packet to parse.</param>
    /// <param name="message">The parsed message if successful; otherwise, default.</param>
    /// <returns>True if parsing was successful; otherwise, false.</returns>
    /// <example>
    /// <code>
    /// if (packet.TryParse&lt;CreateRoomReq&gt;(out var request))
    /// {
    ///     // Handle request
    /// }
    /// else
    /// {
    ///     // Handle parse error
    /// }
    /// </code>
    /// </example>
    public static bool TryParse<T>(this IPacket packet, out T? message) where T : IMessage<T>, new()
    {
        try
        {
            message = packet.Parse<T>();
            return true;
        }
        catch
        {
            message = default;
            return false;
        }
    }
}
