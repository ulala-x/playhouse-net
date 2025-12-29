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
    int Length { get; }
}

/// <summary>
/// Payload backed by ArrayPool buffer (receive path, owns buffer lifetime).
/// </summary>
public sealed class ArrayPoolPayload : IPayload
{
    private byte[]? _rentedBuffer;
    private readonly int _actualSize;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance from ArrayPool.
    /// </summary>
    /// <param name="rentedBuffer">Rented buffer from ArrayPool.</param>
    /// <param name="actualSize">Actual data size in the buffer.</param>
    public ArrayPoolPayload(byte[] rentedBuffer, int actualSize)
    {
        _rentedBuffer = rentedBuffer;
        _actualSize = actualSize;
    }

    public ReadOnlySpan<byte> DataSpan => _rentedBuffer.AsSpan(0, _actualSize);
    public int Length => _actualSize;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_rentedBuffer != null)
        {
            ArrayPool<byte>.Shared.Return(_rentedBuffer);
            _rentedBuffer = null;
        }
    }
}

/// <summary>
/// Payload backed by ReadOnlyMemory (zero-copy, no allocation).
/// </summary>
// public sealed class MemoryPayload(ReadOnlyMemory<byte> data) : IMessagePayload
// {
//     public ReadOnlySpan<byte> DataSpan => data.Span;
//     public int Length => data.Length;
//     
//     private Message? _message;
//     public Message Message => _message!;
//     public void MakeMessage()
//     {
//         _message ??= MessagePool.Shared.Rent(data.Span);
//     }
//     public void Dispose()
//     {
//         _message?.Dispose();
//     }
// }
public sealed class MemoryPayload(ReadOnlyMemory<byte> data) : IPayload
{
    public ReadOnlySpan<byte> DataSpan => data.Span;
    public int Length => data.Length;
    public void Dispose()
    {
    }
}

/// <summary>
/// Payload implementation backed by a Protobuf message.
/// Uses ArrayPool for serialization (lazy, on-demand).
/// </summary>
public sealed class ProtoPayload(IMessage proto) : IPayload
{
    private byte[]? _rentedBuffer;
    private int _actualSize = -1;

    /// <summary>
    /// Lazy serialization to ArrayPool buffer.
    /// </summary>
    public ReadOnlySpan<byte> DataSpan
    {
        get
        {
            if (_rentedBuffer == null)
            {
                _rentedBuffer = ArrayPool<byte>.Shared.Rent(Length);
                proto.WriteTo(_rentedBuffer.AsSpan(0, Length));
            }
            return _rentedBuffer.AsSpan(0, Length);
        }
    }

    public int Length
    {
        get
        {
            if (_actualSize < 0)
            {
                 _actualSize = proto.CalculateSize();
            }
            return _actualSize;
        }
    }

    /// <summary>
    /// Gets the underlying Protobuf message.
    /// </summary>
    public IMessage GetProto() => proto;

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
    public int Length => DataSpan.Length;
    public void Dispose() { }
}
