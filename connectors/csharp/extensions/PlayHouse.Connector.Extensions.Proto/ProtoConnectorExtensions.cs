#nullable enable

using System;
using Google.Protobuf;
using PlayHouse.Connector.Protocol;

namespace PlayHouse.Connector.Extensions.Proto;

/// <summary>
/// Extension methods for creating Packet instances from Protobuf messages.
/// </summary>
public static class ProtoConnectorExtensions
{
    /// <summary>
    /// Creates a Packet from a Protobuf message.
    /// </summary>
    /// <typeparam name="T">The Protobuf message type.</typeparam>
    /// <param name="message">The Protobuf message to create a packet from.</param>
    /// <returns>A new Packet containing the Protobuf message.</returns>
    /// <example>
    /// <code>
    /// var request = new CreateRoomReq { RoomName = "MyRoom" };
    /// var packet = ProtoConnectorExtensions.Of(request);
    /// await connector.SendAsync(packet);
    /// </code>
    /// </example>
    public static Packet Of<T>(T message) where T : IMessage<T>
    {
        if (message == null)
        {
            throw new ArgumentNullException(nameof(message));
        }

        return new Packet(message);
    }
}
