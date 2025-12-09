#nullable enable

using System.Buffers.Binary;
using System.Text;
using PlayHouse.Abstractions;

namespace PlayHouse.Infrastructure.Serialization;

/// <summary>
/// Binary serializer for IPacket instances.
/// Implements the packet format: MsgIdLen(1) + MsgId(N) + MsgSeq(2) + StageId(4) + ErrorCode(2) + OriginalSize(4) + Body
/// Supports LZ4 compression for payloads larger than the compression threshold.
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
        // MsgIdLen(1) + MsgId(N) + MsgSeq(2) + StageId(4) + ErrorCode(2) + OriginalSize(4) + Body
        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms);

        // Write MsgId with length prefix
        var msgIdBytes = Encoding.UTF8.GetBytes(packet.MsgId);
        writer.Write((byte)msgIdBytes.Length);
        writer.Write(msgIdBytes);

        // Write header fields
        writer.Write(packet.MsgSeq);
        writer.Write(packet.StageId);
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
    /// </summary>
    /// <param name="data">The serialized packet data.</param>
    /// <returns>The deserialized packet.</returns>
    /// <exception cref="InvalidDataException">Thrown when the packet format is invalid.</exception>
    public SimplePacket Deserialize(ReadOnlySpan<byte> data)
    {
        if (data.Length < 1)
        {
            throw new InvalidDataException("Packet data is too short");
        }

        // Parse MsgId
        var msgIdLen = data[0];
        if (data.Length < 1 + msgIdLen)
        {
            throw new InvalidDataException("Invalid MsgId length");
        }

        var msgId = Encoding.UTF8.GetString(data.Slice(1, msgIdLen));
        var offset = 1 + msgIdLen;

        // Parse header fields
        if (data.Length < offset + 2 + 4 + 2 + 4)
        {
            throw new InvalidDataException("Packet header is incomplete");
        }

        var msgSeq = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(offset, 2));
        offset += 2;

        var stageId = BinaryPrimitives.ReadInt32LittleEndian(data.Slice(offset, 4));
        offset += 4;

        var errorCode = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(offset, 2));
        offset += 2;

        var originalSize = BinaryPrimitives.ReadInt32LittleEndian(data.Slice(offset, 4));
        offset += 4;

        // Parse body with optional decompression
        byte[] bodyData;
        if (originalSize > 0)
        {
            bodyData = DecompressLz4(data.Slice(offset).ToArray(), originalSize);
        }
        else
        {
            bodyData = data.Slice(offset).ToArray();
        }

        return new SimplePacket(msgId, new BinaryPayload(bodyData), msgSeq, stageId, errorCode);
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
