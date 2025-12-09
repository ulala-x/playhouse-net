#nullable enable

namespace PlayHouse.Abstractions;

/// <summary>
/// Represents the header information of a packet.
/// </summary>
/// <param name="MsgId">The message identifier.</param>
/// <param name="MsgSeq">The message sequence number. Non-zero values indicate request packets.</param>
/// <param name="StageId">The target stage identifier.</param>
/// <param name="ErrorCode">The error code, or 0 for success.</param>
/// <remarks>
/// This is a lightweight value type that can be efficiently passed and compared.
/// The MsgSeq field determines if this is a request (>0) or notification (0) packet.
/// </remarks>
public readonly record struct PacketHeader(
    string MsgId,
    ushort MsgSeq,
    int StageId,
    ushort ErrorCode);
