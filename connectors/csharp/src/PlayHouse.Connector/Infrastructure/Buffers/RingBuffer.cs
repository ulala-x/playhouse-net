using System;
using System.Buffers;

namespace PlayHouse.Connector.Infrastructure.Buffers;

/// <summary>
/// Pure ring buffer implementation for TCP receive buffering.
/// </summary>
/// <remarks>
/// ⚠️ WARNING: This class is NOT thread-safe.
/// It must be used from a single thread only, or external synchronization must be provided.
/// In this connector, it is used exclusively from the receive loop thread.
/// </remarks>
public sealed class RingBuffer : IDisposable
{
    private static readonly ArrayPool<byte> Pool = ArrayPool<byte>.Shared;

    private byte[] _buffer;
    private int _head;     // Write position
    private int _tail;     // Read position
    private int _count;    // Current data size
    private int _capacity;
    private bool _isDisposed;

    /// <summary>
    /// Create a new RingBuffer with specified capacity
    /// </summary>
    public RingBuffer(int capacity)
    {
        if (capacity <= 0)
        {
            throw new ArgumentException("Capacity must be positive", nameof(capacity));
        }

        _buffer = Pool.Rent(capacity);
        _capacity = _buffer.Length;
        _head = 0;
        _tail = 0;
        _count = 0;
    }

    /// <summary>
    /// Current data size in buffer
    /// </summary>
    public int Count => _count;

    /// <summary>
    /// Buffer capacity
    /// </summary>
    public int Capacity => _capacity;

    /// <summary>
    /// Available free space
    /// </summary>
    public int FreeSpace => _capacity - _count;

    #region Write Operations (Zero-copy)

    /// <summary>
    /// Get writable span for direct writing (Zero-copy)
    /// Returns the maximum contiguous writable space
    /// </summary>
    public Span<byte> GetWriteSpan(int requestedSize)
    {
        if (requestedSize <= 0)
        {
            throw new ArgumentException("Size must be positive", nameof(requestedSize));
        }

        if (requestedSize > FreeSpace)
        {
            throw new InvalidOperationException($"Not enough free space. Requested: {requestedSize}, Available: {FreeSpace}");
        }

        // Calculate contiguous writable size
        int contiguousSize;
        if (_head >= _tail || _count == 0)
        {
            // Case 1: [___TTTT____HH___] or empty buffer
            // Can write from head to end of buffer
            contiguousSize = _capacity - _head;

            // If wrapping is needed and requested size is larger than tail space
            if (contiguousSize < requestedSize && _tail > 0)
            {
                // Return only the space until end, caller should call again for wrapped part
                contiguousSize = Math.Min(contiguousSize, requestedSize);
            }
            else
            {
                contiguousSize = Math.Min(contiguousSize, requestedSize);
            }
        }
        else
        {
            // Case 2: [HH____TTTT]
            // Can write from head to tail
            contiguousSize = Math.Min(_tail - _head, requestedSize);
        }

        return _buffer.AsSpan(_head, contiguousSize);
    }

    /// <summary>
    /// Advance write position after writing data
    /// </summary>
    public void Advance(int count)
    {
        if (count < 0)
        {
            throw new ArgumentException("Count must be non-negative", nameof(count));
        }

        if (count > FreeSpace)
        {
            throw new InvalidOperationException($"Cannot advance by {count}, only {FreeSpace} free space available");
        }

        _head = (_head + count) % _capacity;
        _count += count;
    }

    /// <summary>
    /// Write single byte
    /// </summary>
    public void WriteByte(byte value)
    {
        if (FreeSpace < 1)
        {
            throw new InvalidOperationException("Buffer is full");
        }

        _buffer[_head] = value;
        _head = (_head + 1) % _capacity;
        _count++;
    }

    /// <summary>
    /// Write bytes to buffer
    /// </summary>
    public void WriteBytes(ReadOnlySpan<byte> data)
    {
        if (data.Length > FreeSpace)
        {
            throw new InvalidOperationException($"Not enough space to write {data.Length} bytes");
        }

        var remaining = data.Length;
        var offset = 0;

        while (remaining > 0)
        {
            var writeSpan = GetWriteSpan(remaining);
            var writeCount = Math.Min(writeSpan.Length, remaining);

            data.Slice(offset, writeCount).CopyTo(writeSpan);
            Advance(writeCount);

            offset += writeCount;
            remaining -= writeCount;
        }
    }

    #endregion

    #region Read Operations (Zero-copy)

    /// <summary>
    /// Peek at data without consuming (Zero-copy)
    /// </summary>
    public ReadOnlySpan<byte> Peek(int count)
    {
        if (count < 0)
        {
            throw new ArgumentException("Count must be non-negative", nameof(count));
        }

        if (count > _count)
        {
            throw new InvalidOperationException($"Cannot peek {count} bytes, only {_count} available");
        }

        if (count == 0)
        {
            return ReadOnlySpan<byte>.Empty;
        }

        // Calculate contiguous readable size
        int contiguousSize;
        if (_tail < _head || (_tail == _head && _count == 0))
        {
            // Case 1: [___TTTT____HH___]
            contiguousSize = Math.Min(_head - _tail, count);
        }
        else
        {
            // Case 2: [HH____TTTT] or buffer is full
            contiguousSize = Math.Min(_capacity - _tail, count);
        }

        return new ReadOnlySpan<byte>(_buffer, _tail, contiguousSize);
    }

    /// <summary>
    /// Peek byte at specific offset from current tail
    /// </summary>
    public byte PeekByte(int offset)
    {
        if (offset < 0 || offset >= _count)
        {
            throw new ArgumentOutOfRangeException(nameof(offset));
        }

        var index = (_tail + offset) % _capacity;
        return _buffer[index];
    }

    /// <summary>
    /// Peek multiple bytes at specific offset (handles wrap-around for bulk copy)
    /// Reduces function call overhead from N PeekByte() calls to 1-2 memory copies
    /// </summary>
    /// <param name="offset">Offset from current tail position</param>
    /// <param name="destination">Destination span to copy bytes into</param>
    public void PeekBytes(int offset, Span<byte> destination)
    {
        if (offset < 0 || offset + destination.Length > _count)
        {
            throw new ArgumentOutOfRangeException(nameof(offset),
                $"Cannot peek {destination.Length} bytes at offset {offset}, only {_count} bytes available");
        }

        if (destination.Length == 0)
        {
            return;
        }

        // Calculate read start position in circular buffer
        var readStart = (_tail + offset) % _capacity;

        // First chunk: from readStart to end of buffer or end of destination
        var firstChunkSize = Math.Min(_capacity - readStart, destination.Length);
        _buffer.AsSpan(readStart, firstChunkSize).CopyTo(destination);

        // Second chunk: if data wraps around to beginning of buffer
        if (firstChunkSize < destination.Length)
        {
            var secondChunkSize = destination.Length - firstChunkSize;
            _buffer.AsSpan(0, secondChunkSize).CopyTo(destination.Slice(firstChunkSize));
        }
    }

    /// <summary>
    /// Consume (remove) data from buffer after reading
    /// </summary>
    public void Consume(int count)
    {
        if (count < 0)
        {
            throw new ArgumentException("Count must be non-negative", nameof(count));
        }

        if (count > _count)
        {
            throw new InvalidOperationException($"Cannot consume {count} bytes, only {_count} available");
        }

        _tail = (_tail + count) % _capacity;
        _count -= count;
    }

    /// <summary>
    /// Read single byte
    /// </summary>
    public byte ReadByte()
    {
        if (_count == 0)
        {
            throw new InvalidOperationException("Buffer is empty");
        }

        var value = _buffer[_tail];
        _tail = (_tail + 1) % _capacity;
        _count--;
        return value;
    }

    /// <summary>
    /// Read bytes into destination buffer
    /// </summary>
    public int ReadBytes(Span<byte> destination)
    {
        var toRead = Math.Min(destination.Length, _count);
        var remaining = toRead;
        var offset = 0;

        while (remaining > 0)
        {
            var peekSpan = Peek(remaining);
            var readCount = Math.Min(peekSpan.Length, remaining);

            peekSpan.Slice(0, readCount).CopyTo(destination.Slice(offset));
            Consume(readCount);

            offset += readCount;
            remaining -= readCount;
        }

        return toRead;
    }

    #endregion

    #region Utility Methods

    /// <summary>
    /// Clear all data in buffer
    /// </summary>
    public void Clear()
    {
        _head = 0;
        _tail = 0;
        _count = 0;
    }

    /// <summary>
    /// Get next index in circular buffer
    /// </summary>
    private int NextIndex(int index)
    {
        return (index + 1) % _capacity;
    }

    #endregion

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        if (_buffer != null)
        {
            Pool.Return(_buffer);
            _buffer = null!;
        }

        _isDisposed = true;
    }
}
