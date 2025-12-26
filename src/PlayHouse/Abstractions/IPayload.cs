#nullable enable

using System.Buffers;
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
public sealed class ZmqPayload(Net.Zmq.Message message) : IPayload
{
    private bool _disposed;

    /// <summary>
    /// Gets the underlying ZMQ Message.
    /// </summary>
    public Net.Zmq.Message Message { get; } = message;

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
public sealed class MemoryPayload(ReadOnlyMemory<byte> data) : IPayload
{
    public ReadOnlySpan<byte> DataSpan => data.Span;

    public void Dispose() { }
}

/// <summary>
/// Payload implementation backed by a Protobuf message.
/// IMPORTANT: Serializes eagerly in constructor using ArrayPool for best performance.
/// This distributes serialization work across caller threads, avoiding ZMQ thread bottleneck.
/// </summary>
public sealed class ProtoPayload : IPayload
{
    private readonly IMessage _proto;
    private byte[]? _rentedBuffer;
    private readonly int _actualSize;

    /// <summary>
    /// Creates a ProtoPayload with eager serialization using ArrayPool.
    /// </summary>
    public ProtoPayload(IMessage proto)
    {
        _proto = proto;

        // IMPORTANT: Eager serialization on caller thread using ArrayPool.
        // ArrayPool is faster than MessagePool (ZMQ native memory) for allocation.
        // This distributes serialization work across multiple Stage/API threads.
        _actualSize = proto.CalculateSize();
        _rentedBuffer = ArrayPool<byte>.Shared.Rent(_actualSize);
        proto.WriteTo(_rentedBuffer.AsSpan(0, _actualSize));
    }

    public ReadOnlySpan<byte> DataSpan => _rentedBuffer.AsSpan(0, _actualSize);

    /// <summary>
    /// Gets the underlying Protobuf message.
    /// </summary>
    public IMessage GetProto() => _proto;

    public void Dispose()
    {
        if (_rentedBuffer != null)
        {
            ArrayPool<byte>.Shared.Return(_rentedBuffer);
            _rentedBuffer = null;
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
