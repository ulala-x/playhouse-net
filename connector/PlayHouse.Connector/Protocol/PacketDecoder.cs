namespace PlayHouse.Connector.Protocol;

using System.Buffers.Binary;
using System.Text;
using Google.Protobuf;
using Microsoft.Extensions.Logging;
using PlayHouse.Connector.Packet;

/// <summary>
/// Decodes binary packets received from the server using PlayHouse protocol format.
/// Server → Client: Length(4) + ServiceId(2) + MsgIdLen(1) + MsgId(N) + MsgSeq(2) + StageId(8) + ErrorCode(2) + OriginalSize(4) + Body
/// </summary>
internal sealed class PacketDecoder
{
    private readonly ILogger<PacketDecoder>? _logger;
    private readonly List<byte> _buffer = new();
    private int _expectedPacketSize = -1;

    public PacketDecoder(ILogger<PacketDecoder>? logger = null)
    {
        _logger = logger;
    }

    /// <summary>
    /// Processes incoming data and extracts complete packets.
    /// </summary>
    /// <param name="data">Raw bytes received from the server</param>
    /// <returns>Collection of decoded server packets</returns>
    public IEnumerable<ServerPacket> ProcessData(byte[] data)
    {
        if (data == null || data.Length == 0)
        {
            yield break;
        }

        // Add new data to buffer
        _buffer.AddRange(data);

        // Process all complete packets in buffer
        while (true)
        {
            // Read packet size if not already known
            if (_expectedPacketSize == -1)
            {
                if (_buffer.Count < 4)
                {
                    // Not enough data for size header
                    yield break;
                }

                // Read size (little-endian, 4 bytes)
                _expectedPacketSize = BinaryPrimitives.ReadInt32LittleEndian(_buffer.GetRange(0, 4).ToArray());

                if (_expectedPacketSize <= 0 || _expectedPacketSize > 10 * 1024 * 1024) // Max 10MB
                {
                    _logger?.LogError("Invalid packet size: {Size}", _expectedPacketSize);
                    throw new InvalidOperationException($"Invalid packet size: {_expectedPacketSize}");
                }

                // Remove size header from buffer
                _buffer.RemoveRange(0, 4);
            }

            // Check if we have a complete packet
            if (_buffer.Count < _expectedPacketSize)
            {
                // Not enough data yet
                yield break;
            }

            // Extract packet data
            var packetData = _buffer.GetRange(0, _expectedPacketSize).ToArray();
            _buffer.RemoveRange(0, _expectedPacketSize);
            _expectedPacketSize = -1;

            // Deserialize packet using PlayHouse protocol format
            ServerPacket? packet = null;
            try
            {
                packet = ParseServerPacket(packetData);
                _logger?.LogTrace(
                    "Decoded packet: MsgSeq={MsgSeq}, MsgId={MsgId}, ErrorCode={ErrorCode}, PayloadSize={Size}",
                    packet.MsgSeq,
                    packet.MsgId,
                    packet.ErrorCode,
                    packet.Payload.Length);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to parse ServerPacket");
                throw new InvalidOperationException("Failed to parse ServerPacket", ex);
            }

            if (packet != null)
            {
                yield return packet;
            }
        }
    }

    private ServerPacket ParseServerPacket(byte[] data)
    {
        if (data.Length < 3) // Minimum: ServiceId(2) + MsgIdLen(1)
        {
            throw new InvalidDataException("Packet data is too short");
        }

        int offset = 0;

        // Parse ServiceId (2 bytes) - currently not used but must be read
        var serviceId = BinaryPrimitives.ReadInt16LittleEndian(data.AsSpan(offset));
        offset += 2;

        // Parse MsgId
        var msgIdLen = data[offset++];
        if (data.Length < offset + msgIdLen)
        {
            throw new InvalidDataException("Invalid MsgId length");
        }

        var msgId = Encoding.UTF8.GetString(data, offset, msgIdLen);
        offset += msgIdLen;

        // Parse header fields: MsgSeq(2) + StageId(8) + ErrorCode(2) + OriginalSize(4)
        if (data.Length < offset + 2 + 8 + 2 + 4)
        {
            throw new InvalidDataException("Packet header is incomplete");
        }

        var msgSeq = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(offset));
        offset += 2;

        var stageId = BinaryPrimitives.ReadInt64LittleEndian(data.AsSpan(offset)); // Read as Int64 (8 bytes)
        offset += 8;

        var errorCode = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(offset));
        offset += 2;

        var originalSize = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(offset));
        offset += 4;

        // Parse body with optional decompression
        byte[] bodyData;
        if (originalSize > 0)
        {
            // Compressed data - need to decompress
            bodyData = DecompressLz4(data.AsSpan(offset).ToArray(), originalSize);
        }
        else
        {
            // Not compressed
            bodyData = data.AsSpan(offset).ToArray();
        }

        return new ServerPacket
        {
            MsgId = msgId, // Use string MsgId directly
            MsgSeq = msgSeq,
            ErrorCode = errorCode,
            Payload = Google.Protobuf.ByteString.CopyFrom(bodyData)
        };
    }

    private byte[] DecompressLz4(byte[] compressed, int originalSize)
    {
        return K4os.Compression.LZ4.LZ4Pickler.Unpickle(compressed);
    }

    /// <summary>
    /// Deserializes a typed message from packet payload.
    /// </summary>
    /// <typeparam name="T">Message type</typeparam>
    /// <param name="payload">Packet payload bytes</param>
    /// <returns>Deserialized message</returns>
    public T DecodeMessage<T>(ReadOnlyMemory<byte> payload) where T : IMessage<T>, new()
    {
        try
        {
            var parser = new MessageParser<T>(() => new T());
            return parser.ParseFrom(payload.ToArray());
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to decode message of type {Type}", typeof(T).Name);
            throw new InvalidOperationException($"Failed to decode message of type {typeof(T).Name}", ex);
        }
    }

    /// <summary>
    /// Deserializes a message using reflection when the type is only known at runtime.
    /// </summary>
    /// <param name="messageType">The type of the message</param>
    /// <param name="payload">Packet payload bytes</param>
    /// <returns>Deserialized message</returns>
    public IMessage DecodeMessage(Type messageType, ReadOnlyMemory<byte> payload)
    {
        try
        {
            // Use reflection to find the Parser property
            var parserProperty = messageType.GetProperty("Parser", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
            if (parserProperty == null)
            {
                throw new InvalidOperationException($"Type {messageType.Name} does not have a Parser property");
            }

            var parser = parserProperty.GetValue(null);
            if (parser == null)
            {
                throw new InvalidOperationException($"Parser for {messageType.Name} is null");
            }

            // Find the ParseFrom method
            var parseMethod = parser.GetType().GetMethod("ParseFrom", new[] { typeof(byte[]) });
            if (parseMethod == null)
            {
                throw new InvalidOperationException($"Parser for {messageType.Name} does not have ParseFrom method");
            }

            var result = parseMethod.Invoke(parser, new object[] { payload.ToArray() });
            return (IMessage)result!;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to decode message of type {Type}", messageType.Name);
            throw new InvalidOperationException($"Failed to decode message of type {messageType.Name}", ex);
        }
    }

    /// <summary>
    /// Resets the decoder state (clears internal buffer).
    /// </summary>
    public void Reset()
    {
        _buffer.Clear();
        _expectedPacketSize = -1;
        _logger?.LogDebug("PacketDecoder reset");
    }
}
