#nullable enable

using Google.Protobuf;
using PlayHouse.Abstractions;

namespace PlayHouse.Core.Shared;

/// <summary>
/// Concrete implementation of IPacket for internal message handling.
/// </summary>
public sealed class CPacket : IPacket
{
    /// <inheritdoc/>
    public string MsgId { get; }

    /// <inheritdoc/>
    public IPayload Payload { get; }

    private bool _disposed;

    private CPacket(string msgId, IPayload payload)
    {
        MsgId = msgId;
        Payload = payload;
    }

    /// <summary>
    /// Creates a packet from raw bytes.
    /// </summary>
    /// <param name="msgId">Message identifier.</param>
    /// <param name="data">Payload data.</param>
    /// <returns>A new CPacket instance.</returns>
    public static CPacket Of(string msgId, byte[] data)
    {
        return new CPacket(msgId, new MemoryPayload(data));
    }

    /// <summary>
    /// Creates a packet from a Protobuf message.
    /// </summary>
    /// <typeparam name="T">Protobuf message type.</typeparam>
    /// <param name="message">The message to wrap.</param>
    /// <returns>A new CPacket instance.</returns>
    public static CPacket Of<T>(T message) where T : IMessage
    {
        var msgId = typeof(T).Name;
        return new CPacket(msgId, new ProtoPayload(message));
    }

    /// <summary>
    /// Creates a packet from an IPayload (zero-copy).
    /// </summary>
    /// <param name="msgId">Message identifier.</param>
    /// <param name="payload">Payload instance.</param>
    /// <returns>A new CPacket instance.</returns>
    public static CPacket Of(string msgId, IPayload payload)
    {
        return new CPacket(msgId, payload);
    }

    /// <summary>
    /// Creates an empty packet with only a message ID.
    /// </summary>
    /// <param name="msgId">Message identifier.</param>
    /// <returns>A new empty CPacket instance.</returns>
    public static CPacket Empty(string msgId)
    {
        return new CPacket(msgId, EmptyPayload.Instance);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Payload.Dispose();
    }
}

