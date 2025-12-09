#nullable enable

using System.Buffers.Binary;
using System.Text;
using PlayHouse.Abstractions;

namespace PlayHouse.Infrastructure.Serialization;

/// <summary>
/// Binary serializer for IPacket instances.
///
/// Client → Server format: ServiceId(2) + MsgIdLen(1) + MsgId(N) + MsgSeq(2) + StageId(8) + Body
/// - Client messages are never compressed
/// - Body is raw Protobuf data
/// - ServiceId is currently unused but required for protocol compatibility
///
/// Server → Client format: ServiceId(2) + MsgIdLen(1) + MsgId(N) + MsgSeq(2) + StageId(8) + ErrorCode(2) + OriginalSize(4) + Body
/// - Supports LZ4 compression for payloads larger than the compression threshold
/// - OriginalSize > 0 indicates compression
/// - ServiceId is set to 0 for server responses (reserved for future use)
/// </summary>
public sealed class PacketSerializer
{
    private const int CompressionThreshold = 512;
    private const double MinCompressionRatio = 0.9;

    /// <summary>
    /// Serializes a packet to binary format.
    /// </summary>
    /// <param name="packet">The packet to serialize.</param>
    /// <param name="compress">Whether to attempt LZ4 compression for large payloads.</param>
    /// <returns>The serialized packet data.</returns>
    public byte[] Serialize(IPacket packet, bool compress = true)
    {
        // ServiceId(2) + MsgIdLen(1) + MsgId(N) + MsgSeq(2) + StageId(8) + ErrorCode(2) + OriginalSize(4) + Body
        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms);

        // Write ServiceId (2 bytes) - set to 0 for server responses
        writer.Write((short)0);

        // Write MsgId with length prefix
        var msgIdBytes = Encoding.UTF8.GetBytes(packet.MsgId);
        writer.Write((byte)msgIdBytes.Length);
        writer.Write(msgIdBytes);

        // Write header fields
        writer.Write(packet.MsgSeq);
        writer.Write((long)packet.StageId); // Write as Int64 (8 bytes)
        writer.Write(packet.ErrorCode);

        // Process body with optional compression
        var bodyData = packet.Payload.Data.ToArray();
        if (compress && bodyData.Length > CompressionThreshold)
        {
            var compressed = CompressLz4(bodyData);
            if (compressed.Length < bodyData.Length * MinCompressionRatio)
            {
                writer.Write(bodyData.Length); // OriginalSize (indicates compression)
                writer.Write(compressed);
                return ms.ToArray();
            }
        }

        // No compression
        writer.Write(0); // OriginalSize = 0 means not compressed
        writer.Write(bodyData);
        return ms.ToArray();
    }

    /// <summary>
    /// Deserializes binary data into a SimplePacket.
    /// Parses client messages in the format: ServiceId(2) + MsgIdLen(1) + MsgId(N) + MsgSeq(2) + StageId(8) + Body
    /// Client messages are never compressed - the body is always raw Protobuf data.
    /// </summary>
    /// <param name="data">The serialized packet data.</param>
    /// <returns>The deserialized packet.</returns>
    /// <exception cref="InvalidDataException">Thrown when the packet format is invalid.</exception>
    public SimplePacket Deserialize(ReadOnlySpan<byte> data)
    {
        // Minimum packet size: ServiceId(2) + MsgIdLen(1) + MsgId(1+) + MsgSeq(2) + StageId(8) = 14 bytes
        if (data.Length < 14)
        {
            throw new InvalidDataException("Packet data is too short");
        }

        var offset = 0;

        // Parse ServiceId (2 bytes) - currently not used but must be read
        var serviceId = BinaryPrimitives.ReadInt16LittleEndian(data.Slice(offset, 2));
        offset += 2;

        // Parse MsgId with length prefix
        var msgIdLen = data[offset++];
        if (data.Length < offset + msgIdLen)
        {
            throw new InvalidDataException("Invalid MsgId length");
        }

        var msgId = Encoding.UTF8.GetString(data.Slice(offset, msgIdLen));
        offset += msgIdLen;

        // Parse header fields
        // Client → Server format: MsgSeq(2) + StageId(8) + Body
        // Note: StageId is 8 bytes (Int64), not 4 bytes as in server responses
        if (data.Length < offset + 2 + 8)
        {
            throw new InvalidDataException("Packet header is incomplete");
        }

        var msgSeq = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(offset, 2));
        offset += 2;

        var stageId = BinaryPrimitives.ReadInt64LittleEndian(data.Slice(offset, 8));
        offset += 8;

        // Client messages don't have ErrorCode or OriginalSize fields
        ushort errorCode = 0;

        // Body is raw Protobuf data (never compressed in client messages)
        var bodyData = data.Slice(offset).ToArray();

        return new SimplePacket(msgId, new BinaryPayload(bodyData), msgSeq, (int)stageId, errorCode);
    }

    /// <summary>
    /// Compresses data using LZ4.
    /// </summary>
    /// <param name="data">The data to compress.</param>
    /// <returns>The compressed data.</returns>
    private byte[] CompressLz4(byte[] data)
    {
        return K4os.Compression.LZ4.LZ4Pickler.Pickle(data);
    }

    /// <summary>
    /// Decompresses LZ4 data.
    /// </summary>
    /// <param name="compressed">The compressed data.</param>
    /// <param name="originalSize">The expected original size (for validation).</param>
    /// <returns>The decompressed data.</returns>
    private byte[] DecompressLz4(byte[] compressed, int originalSize)
    {
        return K4os.Compression.LZ4.LZ4Pickler.Unpickle(compressed);
    }
}
