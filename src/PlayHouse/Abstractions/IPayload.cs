#nullable enable

using System.Buffers;
using System.IO;
using Google.Protobuf;

namespace PlayHouse.Abstractions;

/// <summary>
/// Represents the payload data of a packet.
/// </summary>
/// <remarks>
/// Implementations must be thread-safe for read operations and properly handle disposal
/// to prevent memory leaks. The payload data is immutable once created.
/// </remarks>
public interface IPayload : IDisposable
{
    /// <summary>
    /// Gets the payload data as a read-only span.
    /// </summary>
    ReadOnlySpan<byte> DataSpan { get; }

    /// <summary>
    /// Gets the length of the payload.
    /// </summary>
    int Length => DataSpan.Length;
}

/// <summary>
/// Payload backed by ZMQ Message (zero-copy, owns message lifetime).
/// </summary>
public sealed class ZmqPayload : IPayload
{
    private bool _disposed;

    /// <summary>
    /// Gets the underlying ZMQ Message.
    /// </summary>
    public Net.Zmq.Message Message { get; }

    public ZmqPayload(Net.Zmq.Message message)
    {
        Message = message;
    }

    public ReadOnlySpan<byte> DataSpan => Message.Data;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Message.Dispose();
    }
}

/// <summary>
/// Payload backed by ReadOnlyMemory (zero-copy, no allocation).
/// </summary>
public sealed class MemoryPayload : IPayload
{
    private readonly ReadOnlyMemory<byte> _data;

    public MemoryPayload(ReadOnlyMemory<byte> data)
    {
        _data = data;
    }

    public ReadOnlySpan<byte> DataSpan => _data.Span;

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
    /// Gets a ZMQ Message for zero-copy sending (internal use only).
    /// Uses MessagePool.Shared for efficient memory management.
    /// </summary>
    /// <returns>A pooled ZMQ Message containing the serialized protobuf data.</returns>
    internal Net.Zmq.Message GetZmqMessage()
    {
        if (_zmqMessage == null)
        {
            _actualSize = _proto.CalculateSize();
            _zmqMessage = Net.Zmq.MessagePool.Shared.Rent(_actualSize);

            // Serialize directly to ZMQ Message buffer (zero-copy, no temp allocation)
            unsafe
            {
                fixed (byte* ptr = _zmqMessage.Data)
                {
                    using var stream = new UnmanagedMemoryStream(ptr, 0, _actualSize, FileAccess.Write);
                    _proto.WriteTo(stream);
                }
            }
        }
        return _zmqMessage;
    }

    /// <summary>
    /// Gets the actual payload data span for ZMQ sending.
    /// This returns only the valid data, not the full buffer.
    /// </summary>
    /// <returns>A ReadOnlySpan containing exactly _actualSize bytes of serialized data.</returns>
    internal ReadOnlySpan<byte> GetZmqPayloadSpan()
    {
        GetZmqMessage(); // Ensure _zmqMessage and _actualSize are set
        return _zmqMessage!.Data.Slice(0, _actualSize);
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
