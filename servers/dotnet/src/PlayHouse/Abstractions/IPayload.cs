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

    /// <summary>
    /// Transfers ownership of the payload data to a new instance.
    /// After calling this method, the original payload becomes invalid and should not be used.
    /// </summary>
    /// <returns>A new IPayload instance with transferred ownership.</returns>
    IPayload Move();
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

    public ReadOnlySpan<byte> DataSpan => _disposed
        ? throw new ObjectDisposedException(nameof(ArrayPoolPayload))
        : _rentedBuffer.AsSpan(0, _actualSize);

    public int Length => _actualSize;

    public IPayload Move()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(ArrayPoolPayload));
        }

        var buffer = _rentedBuffer;
        var size = _actualSize;
        _rentedBuffer = null;
        _disposed = true;
        return new ArrayPoolPayload(buffer!, size);
    }

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
/// Payload that wraps an ArrayPool buffer with offset support.
/// Allows accessing a portion of a larger rented buffer without additional copying.
/// </summary>
/// <remarks>
/// This is useful when handling multi-segment packets where the entire packet is copied
/// into a single buffer. Instead of copying the payload portion again, this class provides
/// a view into the rented buffer at the specified offset.
/// </remarks>
public sealed class ArrayPoolPayloadWithOffset : IPayload
{
    private byte[]? _rentedBuffer;
    private readonly int _offset;
    private readonly int _length;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance from ArrayPool with offset support.
    /// </summary>
    /// <param name="rentedBuffer">Rented buffer from ArrayPool.</param>
    /// <param name="offset">Starting offset of the payload data in the buffer.</param>
    /// <param name="length">Length of the payload data.</param>
    /// <exception cref="ArgumentNullException">Thrown when rentedBuffer is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when offset or length is negative.</exception>
    public ArrayPoolPayloadWithOffset(byte[] rentedBuffer, int offset, int length)
    {
        _rentedBuffer = rentedBuffer ?? throw new ArgumentNullException(nameof(rentedBuffer));

        if (offset < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(offset), "Offset cannot be negative.");
        }

        if (length < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(length), "Length cannot be negative.");
        }

        _offset = offset;
        _length = length;
    }

    public ReadOnlySpan<byte> DataSpan => _disposed
        ? throw new ObjectDisposedException(nameof(ArrayPoolPayloadWithOffset))
        : _rentedBuffer.AsSpan(_offset, _length);

    public int Length => _length;

    public IPayload Move()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(ArrayPoolPayloadWithOffset));
        }

        var buffer = _rentedBuffer;
        var offset = _offset;
        var length = _length;
        _rentedBuffer = null;
        _disposed = true;
        return new ArrayPoolPayloadWithOffset(buffer!, offset, length);
    }

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

    public IPayload Move()
    {
        // MemoryPayload는 참조만 전달하므로 복사 불필요
        return this;
    }

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
    private bool _disposed;

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

    public IPayload Move()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(ProtoPayload));
        }

        // 이미 직렬화된 버퍼가 있으면 소유권 이전
        if (_rentedBuffer != null)
        {
            var buffer = _rentedBuffer;
            var size = _actualSize;
            _rentedBuffer = null;
            _disposed = true;
            return new ArrayPoolPayload(buffer, size);
        }

        // 아직 직렬화되지 않았으면 새 ProtoPayload 반환 (proto는 불변)
        _disposed = true;
        return new ProtoPayload(proto);
    }

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
/// Singleton empty payload.
/// </summary>
public sealed class EmptyPayload : IPayload
{
    public static readonly EmptyPayload Instance = new();

    private EmptyPayload() { }

    public ReadOnlySpan<byte> DataSpan => ReadOnlySpan<byte>.Empty;
    public int Length => DataSpan.Length;

    public IPayload Move()
    {
        // 싱글톤이므로 자기 자신 반환
        return Instance;
    }

    public void Dispose() { }
}
