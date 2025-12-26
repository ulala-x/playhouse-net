#nullable enable

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
