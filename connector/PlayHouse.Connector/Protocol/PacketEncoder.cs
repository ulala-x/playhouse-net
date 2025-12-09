namespace PlayHouse.Connector.Protocol;

using System.Buffers.Binary;
using System.Text;
using Google.Protobuf;

/// <summary>
/// Encodes messages into binary packets using PlayHouse server protocol format.
/// Client → Server: ServiceId(2) + MsgIdLen(1) + MsgId(N) + MsgSeq(2) + StageId(8) + Body
/// Note: Length prefix (4 bytes) is added separately in EncodeWithLengthPrefix
/// </summary>
internal sealed class PacketEncoder
{
    /// <summary>
    /// Encodes a request message into binary format matching server protocol.
    /// </summary>
    /// <typeparam name="T">Message type</typeparam>
    /// <param name="message">Message to encode</param>
    /// <param name="msgSeq">Message sequence number (0 for one-way messages)</param>
    /// <param name="stageId">Target stage ID (0 if not yet joined)</param>
    /// <param name="serviceId">Service ID (0 for default service)</param>
    /// <returns>Encoded packet bytes (without length prefix)</returns>
    public byte[] EncodeMessage<T>(T message, ushort msgSeq = 0, long stageId = 0, short serviceId = 0) where T : IMessage
    {
        if (message == null)
        {
            throw new ArgumentNullException(nameof(message));
        }

        // Get message ID from protobuf descriptor
        var msgId = message.Descriptor.Name;
        var msgIdBytes = Encoding.UTF8.GetBytes(msgId);

        if (msgIdBytes.Length > 255)
        {
            throw new ArgumentException($"Message ID too long: {msgId}");
        }

        // Serialize protobuf payload
        var payloadBytes = message.ToByteArray();

        // Calculate total size
        // Client → Server: ServiceId(2) + MsgIdLen(1) + MsgId(N) + MsgSeq(2) + StageId(8) + Payload
        var totalSize = 2 + 1 + msgIdBytes.Length + 2 + 8 + payloadBytes.Length;
        var buffer = new byte[totalSize];

        int offset = 0;

        // ServiceId (2 bytes, little-endian)
        BinaryPrimitives.WriteInt16LittleEndian(buffer.AsSpan(offset), serviceId);
        offset += 2;

        // MsgIdLen (1 byte)
        buffer[offset++] = (byte)msgIdBytes.Length;

        // MsgId (N bytes)
        msgIdBytes.CopyTo(buffer, offset);
        offset += msgIdBytes.Length;

        // MsgSeq (2 bytes, little-endian)
        BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(offset), msgSeq);
        offset += 2;

        // StageId (8 bytes, little-endian)
        BinaryPrimitives.WriteInt64LittleEndian(buffer.AsSpan(offset), stageId);
        offset += 8;

        // Payload
        payloadBytes.CopyTo(buffer, offset);

        return buffer;
    }

    /// <summary>
    /// Encodes a message and returns it with length prefix (for TCP framing).
    /// Complete format: Length(4) + ServiceId(2) + MsgIdLen(1) + MsgId(N) + MsgSeq(2) + StageId(8) + Body
    /// </summary>
    /// <typeparam name="T">Message type</typeparam>
    /// <param name="message">Message to encode</param>
    /// <param name="msgSeq">Message sequence number</param>
    /// <param name="stageId">Target stage ID</param>
    /// <param name="serviceId">Service ID (0 for default service)</param>
    /// <returns>Length-prefixed packet bytes</returns>
    public byte[] EncodeWithLengthPrefix<T>(T message, ushort msgSeq = 0, long stageId = 0, short serviceId = 0) where T : IMessage
    {
        var packetBytes = EncodeMessage(message, msgSeq, stageId, serviceId);
        var totalLength = 4 + packetBytes.Length;
        var buffer = new byte[totalLength];

        // Write length prefix (4 bytes, little-endian)
        BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(0), packetBytes.Length);

        // Write packet data
        packetBytes.CopyTo(buffer, 4);

        return buffer;
    }
}
