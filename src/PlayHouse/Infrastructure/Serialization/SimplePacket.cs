#nullable enable

using Google.Protobuf;
using PlayHouse.Abstractions;

namespace PlayHouse.Infrastructure.Serialization;

/// <summary>
/// Protobuf-based packet implementation.
/// Provides efficient serialization/deserialization with message caching.
/// </summary>
public sealed class SimplePacket : IPacket
{
    private IMessage? _parsedMessage;

    /// <summary>
    /// Initializes a new packet for sending (from a Protobuf message).
    /// </summary>
    /// <param name="message">The Protobuf message to send.</param>
    public SimplePacket(IMessage message)
    {
        MsgId = message.Descriptor.Name;
        Payload = new SimpleProtoPayload(message);
        _parsedMessage = message;
        MsgSeq = 0;
        StageId = 0;
        ErrorCode = 0;
    }

    /// <summary>
    /// Initializes a new packet for receiving (deserialization).
    /// </summary>
    /// <param name="msgId">The message identifier.</param>
    /// <param name="payload">The message payload.</param>
    /// <param name="msgSeq">The message sequence number.</param>
    /// <param name="stageId">The stage identifier (optional).</param>
    /// <param name="errorCode">The error code (optional).</param>
    public SimplePacket(string msgId, IPayload payload, ushort msgSeq, int stageId = 0, ushort errorCode = 0)
    {
        MsgId = msgId;
        Payload = new CopyPayload(payload);
        MsgSeq = msgSeq;
        StageId = stageId;
        ErrorCode = errorCode;
    }

    /// <inheritdoc />
    public string MsgId { get; }

    /// <inheritdoc />
    public IPayload Payload { get; }

    /// <inheritdoc />
    public ushort MsgSeq { get; init; }

    /// <inheritdoc />
    public int StageId { get; init; }

    /// <inheritdoc />
    public ushort ErrorCode { get; init; }

    /// <summary>
    /// Gets a value indicating whether this packet is a request (MsgSeq > 0).
    /// </summary>
    public bool IsRequest => MsgSeq > 0;

    /// <summary>
    /// Parses the payload as a Protobuf message of type T.
    /// Results are cached for subsequent calls.
    /// </summary>
    /// <typeparam name="T">The Protobuf message type.</typeparam>
    /// <returns>The parsed message instance.</returns>
    public T Parse<T>() where T : IMessage, new()
    {
        if (_parsedMessage is T cached)
        {
            return cached;
        }

        var message = new T();
        message.MergeFrom(Payload.Data.Span);
        _parsedMessage = message;
        return message;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        Payload?.Dispose();
    }
}

/// <summary>
/// Extension methods for packet parsing.
/// </summary>
public static class SimplePacketExtension
{
    /// <summary>
    /// Parses any IPacket as a Protobuf message of type T.
    /// </summary>
    /// <typeparam name="T">The Protobuf message type.</typeparam>
    /// <param name="packet">The packet to parse.</param>
    /// <returns>The parsed message instance.</returns>
    public static T Parse<T>(this IPacket packet) where T : IMessage, new()
    {
        if (packet is SimplePacket simplePacket)
        {
            return simplePacket.Parse<T>();
        }

        var message = new T();
        message.MergeFrom(packet.Payload.Data.Span);
        return message;
    }
}
