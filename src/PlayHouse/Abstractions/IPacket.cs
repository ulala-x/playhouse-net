#nullable enable

namespace PlayHouse.Abstractions;

/// <summary>
/// Represents a message packet in the PlayHouse framework.
/// </summary>
/// <remarks>
/// Packets are the fundamental unit of communication between clients, stages, and actors.
/// Each packet contains header information and a payload. Packets must be disposed to
/// prevent resource leaks.
/// </remarks>
public interface IPacket : IDisposable
{
    /// <summary>
    /// Gets the message identifier that determines the message type and handler.
    /// </summary>
    string MsgId { get; }

    /// <summary>
    /// Gets the message sequence number. Non-zero values indicate this is a request packet
    /// that expects a reply with the same sequence number.
    /// </summary>
    ushort MsgSeq { get; }

    /// <summary>
    /// Gets the target stage identifier.
    /// </summary>
    int StageId { get; }

    /// <summary>
    /// Gets the error code. Zero indicates success.
    /// </summary>
    ushort ErrorCode { get; }

    /// <summary>
    /// Gets the payload data of the packet.
    /// </summary>
    IPayload Payload { get; }

    /// <summary>
    /// Gets the packet header as a value type for efficient passing.
    /// </summary>
    PacketHeader Header => new(MsgId, MsgSeq, StageId, ErrorCode);

    /// <summary>
    /// Gets a value indicating whether this packet is a request that expects a reply.
    /// </summary>
    bool IsRequest => MsgSeq > 0;
}
