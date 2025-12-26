#nullable enable

using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Text;

namespace PlayHouse.Runtime.ClientTransport;

/// <summary>
/// Common message encoding/decoding utilities for transport sessions.
/// </summary>
/// <remarks>
/// Message formats:
/// - Request: [MsgIdLen:1][MsgId:N][MsgSeq:2][StageId:8][Payload]
/// - Response: [MsgIdLen:1][MsgId:N][MsgSeq:2][StageId:8][ErrorCode:2][OriginalSize:4][Payload]
///
/// TCP adds a 4-byte length prefix, WebSocket uses native framing.
/// </remarks>
internal static class MessageCodec
{
    /// <summary>
    /// Minimum message size: MsgIdLen(1) + MsgSeq(2) + StageId(8) = 11 bytes
    /// </summary>
    public const int MinMessageSize = 11;

    /// <summary>
    /// Response header size after MsgId: MsgSeq(2) + StageId(8) + ErrorCode(2) + OriginalSize(4) = 16 bytes
    /// </summary>
    public const int ResponseHeaderSize = 16;

    /// <summary>
    /// Cache for UTF-8 encoded message IDs to avoid repeated allocations.
    /// </summary>
    private static readonly ConcurrentDictionary<string, byte[]> _msgIdCache = new();

    /// <summary>
    /// Gets the UTF-8 bytes for a message ID, using cache when possible.
    /// </summary>
    private static byte[] GetMsgIdBytes(string msgId)
    {
        return _msgIdCache.GetOrAdd(msgId, static id => Encoding.UTF8.GetBytes(id));
    }

    /// <summary>
    /// Parses a message body (without length prefix).
    /// </summary>
    public static bool TryParseMessage(
        ReadOnlySpan<byte> data,
        out string msgId,
        out ushort msgSeq,
        out long stageId,
        out int payloadOffset)
    {
        msgId = string.Empty;
        msgSeq = 0;
        stageId = 0;
        payloadOffset = 0;

        if (data.Length < MinMessageSize)
            return false;

        int offset = 0;

        // MsgIdLen (1 byte)
        var msgIdLen = data[offset++];

        // Validate we have enough data
        if (offset + msgIdLen + 10 > data.Length)
            return false;

        // MsgId
        msgId = Encoding.UTF8.GetString(data.Slice(offset, msgIdLen));
        offset += msgIdLen;

        // MsgSeq (2 bytes)
        msgSeq = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(offset));
        offset += 2;

        // StageId (8 bytes)
        stageId = BinaryPrimitives.ReadInt64LittleEndian(data.Slice(offset));
        offset += 8;

        payloadOffset = offset;
        return true;
    }

    /// <summary>
    /// Calculates the response packet size.
    /// </summary>
    public static int CalculateResponseSize(int msgIdLength, int payloadLength, bool includeLengthPrefix)
    {
        // MsgIdLen(1) + MsgId(N) + MsgSeq(2) + StageId(8) + ErrorCode(2) + OriginalSize(4) + Payload
        var bodySize = 1 + msgIdLength + ResponseHeaderSize + payloadLength;
        return includeLengthPrefix ? 4 + bodySize : bodySize;
    }

    /// <summary>
    /// Writes a response packet body (without length prefix).
    /// </summary>
    /// <returns>Number of bytes written.</returns>
    public static int WriteResponseBody(
        Span<byte> buffer,
        string msgId,
        ushort msgSeq,
        long stageId,
        ushort errorCode,
        ReadOnlySpan<byte> payload)
    {
        var msgIdBytes = GetMsgIdBytes(msgId);
        int offset = 0;

        // MsgIdLen (1 byte)
        buffer[offset++] = (byte)msgIdBytes.Length;

        // MsgId
        msgIdBytes.CopyTo(buffer.Slice(offset));
        offset += msgIdBytes.Length;

        // MsgSeq (2 bytes)
        BinaryPrimitives.WriteUInt16LittleEndian(buffer.Slice(offset), msgSeq);
        offset += 2;

        // StageId (8 bytes)
        BinaryPrimitives.WriteInt64LittleEndian(buffer.Slice(offset), stageId);
        offset += 8;

        // ErrorCode (2 bytes)
        BinaryPrimitives.WriteUInt16LittleEndian(buffer.Slice(offset), errorCode);
        offset += 2;

        // OriginalSize (4 bytes) - 0 = no compression
        BinaryPrimitives.WriteInt32LittleEndian(buffer.Slice(offset), 0);
        offset += 4;

        // Payload
        payload.CopyTo(buffer.Slice(offset));
        offset += payload.Length;

        return offset;
    }

    /// <summary>
    /// Creates a TCP response packet (with 4-byte length prefix).
    /// </summary>
    public static byte[] CreateTcpResponsePacket(
        string msgId,
        ushort msgSeq,
        long stageId,
        ushort errorCode,
        ReadOnlySpan<byte> payload)
    {
        var msgIdBytes = GetMsgIdBytes(msgId);
        var bodySize = 1 + msgIdBytes.Length + ResponseHeaderSize + payload.Length;
        var buffer = new byte[4 + bodySize];

        // Length prefix (4 bytes)
        BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(0), bodySize);

        // Body
        WriteResponseBody(buffer.AsSpan(4), msgId, msgSeq, stageId, errorCode, payload);

        return buffer;
    }

    /// <summary>
    /// Creates a WebSocket response packet (no length prefix).
    /// </summary>
    public static byte[] CreateWebSocketResponsePacket(
        string msgId,
        ushort msgSeq,
        long stageId,
        ushort errorCode,
        ReadOnlySpan<byte> payload)
    {
        var msgIdBytes = GetMsgIdBytes(msgId);
        var size = 1 + msgIdBytes.Length + ResponseHeaderSize + payload.Length;
        var buffer = new byte[size];

        WriteResponseBody(buffer.AsSpan(), msgId, msgSeq, stageId, errorCode, payload);

        return buffer;
    }
}
