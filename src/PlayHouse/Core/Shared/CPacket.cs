#nullable enable

using System.Buffers;
using Google.Protobuf;
using Net.Zmq;
using PlayHouse.Abstractions;
using RuntimePayload = PlayHouse.Runtime.ServerMesh.Message;

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
        var msgId = typeof(T).Name;
        return new CPacket(msgId, new ProtoPayload(message));
    }

    /// <summary>
    /// Creates a packet from a Runtime IPayload (zero-copy for server-to-server).
    /// </summary>
    /// <param name="msgId">Message identifier.</param>
    /// <param name="runtimePayload">Runtime payload instance.</param>
    /// <returns>A new CPacket instance.</returns>
    public static CPacket Of(string msgId, RuntimePayload.IPayload runtimePayload)
    {
        return new CPacket(msgId, new RuntimePayloadWrapper(runtimePayload));
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

    public ReadOnlySpan<byte> DataSpan => _data;

    public void Dispose() { }
}

/// <summary>
/// Payload implementation backed by a Protobuf message.
/// Uses ArrayPool for memory-efficient serialization.
/// </summary>
public sealed class ProtoPayload : IPayload
{
    private readonly IMessage _proto;
    private byte[]? _rentedBuffer;
    private Net.Zmq.Message? _zmqMessage;
    private int _actualSize;

    public ProtoPayload(IMessage proto)
    {
        _proto = proto;
    }

    public ReadOnlySpan<byte> DataSpan
    {
        get
        {
            EnsureArrayPoolBuffer();
            return new ReadOnlySpan<byte>(_rentedBuffer, 0, _actualSize);
        }
    }

    /// <summary>
    /// Gets a ZMQ Message for zero-copy sending.
    /// Uses MessagePool.Shared for efficient memory management.
    /// </summary>
    /// <returns>A pooled ZMQ Message containing the serialized protobuf data.</returns>
    public Net.Zmq.Message GetZmqMessage()
    {
        if (_zmqMessage == null)
        {
            _actualSize = _proto.CalculateSize();
            _zmqMessage = MessagePool.Shared.Rent(_actualSize);

            // Serialize to ArrayPool buffer, then copy to ZMQ Message
            var tempBuffer = ArrayPool<byte>.Shared.Rent(_actualSize);
            try
            {
                using var stream = new MemoryStream(tempBuffer, 0, _actualSize, writable: true);
                _proto.WriteTo(stream);
                tempBuffer.AsSpan(0, _actualSize).CopyTo(_zmqMessage.Data);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(tempBuffer);
            }
        }
        return _zmqMessage;
    }

    /// <summary>
    /// Gets the underlying Protobuf message.
    /// </summary>
    public IMessage GetProto() => _proto;

    private void EnsureArrayPoolBuffer()
    {
        if (_rentedBuffer == null)
        {
            _actualSize = _proto.CalculateSize();
            _rentedBuffer = ArrayPool<byte>.Shared.Rent(_actualSize);

            // Serialize directly to ArrayPool buffer (zero-copy)
            using var stream = new MemoryStream(_rentedBuffer, 0, _actualSize, writable: true);
            _proto.WriteTo(stream);
        }
    }

    public void Dispose()
    {
        if (_rentedBuffer != null)
        {
            ArrayPool<byte>.Shared.Return(_rentedBuffer);
            _rentedBuffer = null;
        }
        if (_zmqMessage != null)
        {
            _zmqMessage.Dispose(); // Return to MessagePool
            _zmqMessage = null;
        }
    }
}

/// <summary>
/// Singleton empty payload.
/// </summary>
public sealed class EmptyPayload : IPayload
{
    public static readonly EmptyPayload Instance = new();

    private EmptyPayload() { }

    public ReadOnlySpan<byte> DataSpan => ReadOnlySpan<byte>.Empty;

    public void Dispose() { }
}

/// <summary>
/// Payload wrapper that bridges Runtime.IPayload to Abstractions.IPayload (zero-copy).
/// </summary>
public sealed class RuntimePayloadWrapper : IPayload
{
    private readonly RuntimePayload.IPayload _runtimePayload;

    public RuntimePayloadWrapper(RuntimePayload.IPayload runtimePayload)
    {
        _runtimePayload = runtimePayload;
    }

    public ReadOnlySpan<byte> DataSpan => _runtimePayload.DataSpan;

    /// <summary>
    /// Gets the underlying runtime payload for conversion purposes.
    /// </summary>
    internal RuntimePayload.IPayload GetUnderlyingPayload() => _runtimePayload;

    public void Dispose()
    {
        _runtimePayload.Dispose();
    }
}
