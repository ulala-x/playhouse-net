#nullable enable

namespace PlayHouse.Abstractions;

/// <summary>
/// Represents a message packet in the PlayHouse framework.
/// </summary>
/// <remarks>
/// Packets are the fundamental unit of communication between clients, stages, and actors.
/// Each packet contains a message identifier and a payload. Packets must be disposed to
/// prevent resource leaks.
/// </remarks>
public interface IPacket : IDisposable
{
    /// <summary>
    /// Gets the message identifier that determines the message type and handler.
    /// </summary>
    string MsgId { get; }

    /// <summary>
    /// Gets the payload data of the packet.
    /// </summary>
    IPayload Payload { get; }
}
