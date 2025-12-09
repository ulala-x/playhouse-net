namespace PlayHouse.Connector.Protocol;

using System.Buffers;
using Google.Protobuf;
using PlayHouse.Connector.Packet;

/// <summary>
/// Encodes messages into binary packets for transmission.
/// </summary>
internal sealed class PacketEncoder
{
    /// <summary>
    /// Encodes a request message into a binary packet.
    /// </summary>
    /// <typeparam name="T">Message type</typeparam>
    /// <param name="message">Message to encode</param>
    /// <param name="msgSeq">Message sequence number</param>
    /// <param name="msgId">Message ID (type identifier)</param>
    /// <returns>Encoded packet bytes</returns>
    public byte[] EncodeRequest<T>(T message, ushort msgSeq, ushort msgId) where T : IMessage
    {
        if (message == null)
        {
            throw new ArgumentNullException(nameof(message));
        }

        // Serialize the protobuf message
        var payload = message.ToByteArray();

        // Create packet header
        var packet = new ClientPacket
        {
            MsgSeq = msgSeq,
            MsgId = msgId,
            Payload = Google.Protobuf.ByteString.CopyFrom(payload)
        };

        // Serialize the packet
        var packetBytes = packet.ToByteArray();

        // Prepend packet size (4 bytes, big-endian)
        var totalSize = packetBytes.Length;
        var buffer = ArrayPool<byte>.Shared.Rent(totalSize + 4);

        try
        {
            // Write size header (big-endian)
            buffer[0] = (byte)(totalSize >> 24);
            buffer[1] = (byte)(totalSize >> 16);
            buffer[2] = (byte)(totalSize >> 8);
            buffer[3] = (byte)totalSize;

            // Write packet data
            Array.Copy(packetBytes, 0, buffer, 4, packetBytes.Length);

            // Return final packet
            var result = new byte[totalSize + 4];
            Array.Copy(buffer, 0, result, 0, totalSize + 4);

            return result;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    /// <summary>
    /// Encodes a one-way message (no response expected).
    /// </summary>
    /// <typeparam name="T">Message type</typeparam>
    /// <param name="message">Message to encode</param>
    /// <param name="msgId">Message ID (type identifier)</param>
    /// <returns>Encoded packet bytes</returns>
    public byte[] EncodeMessage<T>(T message, ushort msgId) where T : IMessage
    {
        // Use MsgSeq = 0 for one-way messages
        return EncodeRequest(message, msgSeq: 0, msgId);
    }
}
