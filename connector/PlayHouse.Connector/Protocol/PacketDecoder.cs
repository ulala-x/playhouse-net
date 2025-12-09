namespace PlayHouse.Connector.Protocol;

using Google.Protobuf;
using Microsoft.Extensions.Logging;
using PlayHouse.Connector.Packet;

/// <summary>
/// Decodes binary packets received from the server.
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

                // Read size (big-endian, 4 bytes)
                _expectedPacketSize = (_buffer[0] << 24) | (_buffer[1] << 16) | (_buffer[2] << 8) | _buffer[3];

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

            // Deserialize packet
            ServerPacket? packet = null;
            try
            {
                packet = ServerPacket.Parser.ParseFrom(packetData);
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
