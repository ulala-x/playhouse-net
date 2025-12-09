#nullable enable

namespace PlayHouse.Core.Protocols;

/// <summary>
/// Represents a network packet containing message metadata and payload.
/// Packets are the fundamental unit of communication in PlayHouse.
/// </summary>
public interface IPacket : IDisposable
{
    /// <summary>
    /// Gets the message identifier (typically the Protobuf message type name).
    /// </summary>
    string MsgId { get; }

    /// <summary>
    /// Gets the message payload containing serialized data.
    /// </summary>
    IPayload Payload { get; }

    /// <summary>
    /// Gets the message sequence number for request/response matching.
    /// </summary>
    ushort MsgSeq { get; }

    /// <summary>
    /// Gets the target stage identifier.
    /// 0 indicates no specific stage (system message or broadcast).
    /// </summary>
    int StageId { get; }

    /// <summary>
    /// Gets the error code.
    /// 0 indicates success, non-zero values indicate specific error conditions.
    /// </summary>
    ushort ErrorCode { get; }
}
