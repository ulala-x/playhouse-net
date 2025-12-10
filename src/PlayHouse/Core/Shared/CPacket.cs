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
        return new CPacket(msgId, new BytePayload(data));
    }

    /// <summary>
    /// Creates a packet from a Protobuf message.
    /// </summary>
    /// <typeparam name="T">Protobuf message type.</typeparam>
    /// <param name="message">The message to wrap.</param>
    /// <returns>A new CPacket instance.</returns>
    public static CPacket Of<T>(T message) where T : IMessage
    {
        var msgId = typeof(T).FullName ?? typeof(T).Name;
        return new CPacket(msgId, new ProtoPayload(message));
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

/// <summary>
/// Payload implementation backed by a byte array.
/// </summary>
public sealed class BytePayload : IPayload
{
    private readonly byte[] _data;

    public BytePayload(byte[] data)
    {
        _data = data;
    }

    public BytePayload(ReadOnlySpan<byte> data)
    {
        _data = data.ToArray();
    }

    public ReadOnlyMemory<byte> Data => _data;

    public void Dispose() { }
}

/// <summary>
/// Payload implementation backed by a Protobuf message.
/// </summary>
public sealed class ProtoPayload : IPayload
{
    private readonly IMessage _proto;
    private byte[]? _cachedData;

    public ProtoPayload(IMessage proto)
    {
        _proto = proto;
    }

    public ReadOnlyMemory<byte> Data => _cachedData ??= _proto.ToByteArray();

    /// <summary>
    /// Gets the underlying Protobuf message.
    /// </summary>
    public IMessage GetProto() => _proto;

    public void Dispose() { }
}

/// <summary>
/// Singleton empty payload.
/// </summary>
public sealed class EmptyPayload : IPayload
{
    public static readonly EmptyPayload Instance = new();

    private EmptyPayload() { }

    public ReadOnlyMemory<byte> Data => ReadOnlyMemory<byte>.Empty;

    public void Dispose() { }
}
