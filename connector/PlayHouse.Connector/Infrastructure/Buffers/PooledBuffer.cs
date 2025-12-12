using System;
using System.Buffers;
using System.Diagnostics;
using System.Text;

namespace PlayHouse.Connector.Infrastructure.Buffers;

public class PooledBuffer : IDisposable
{
    private static ArrayPool<byte> _arrayPool = ArrayPool<byte>.Shared;

    private byte[]? _data;

    private readonly bool _isPooled = true;

    /// <summary>
    ///     Initialize a new expandable buffer with zero capacity
    /// </summary>
    public PooledBuffer()
    {
        _data = _arrayPool.Rent(0);
        Size = 0;
        Offset = 0;
    }

    /// <summary>
    ///     Initialize a new expandable buffer with the given capacity
    /// </summary>
    public PooledBuffer(long capacity)
    {
        _data = _arrayPool.Rent((int)capacity);
        Size = 0;
        Offset = 0;
    }

    /// <summary>
    ///     Initialize a new expandable buffer with the given data
    /// </summary>
    public PooledBuffer(byte[] data)
    {
        _data = data;
        Size = data.Length;
        Offset = 0;
        _isPooled = false;
    }

    /// <summary>
    ///     Is the buffer empty?
    /// </summary>
    public bool IsEmpty => _data == null || Size == 0;

    /// <summary>
    ///     Bytes memory buffer
    /// </summary>
    public byte[] Data => _data!;

    /// <summary>
    ///     Bytes memory buffer capacity
    /// </summary>
    public int Capacity => _data!.Length;

    /// <summary>
    ///     Bytes memory buffer size
    /// </summary>
    public int Size { get; private set; }

    /// <summary>
    ///     Bytes memory buffer offset
    /// </summary>
    public int Offset { get; private set; }

    /// <summary>
    ///     Buffer indexer operator
    /// </summary>
    public byte this[int index]
    {
        get => _data![index];
        set => _data![index] = value;
    }

    public static void Init(int maxBufferPoolSize = 1024 * 1024 * 100)
    {
        // ArrayPool.Shared is automatically initialized, no explicit init needed
        // This method is kept for API compatibility
    }

    #region Memory buffer methods

    /// <summary>
    ///     Get a span of bytes from the current buffer
    /// </summary>
    public Span<byte> AsSpan()
    {
        return new Span<byte>(_data, Offset, Size);
    }

    /// <summary>
    ///     Get a string from the current buffer
    /// </summary>
    public override string ToString()
    {
        return ExtractString(0, Size);
    }

    // Clear the current buffer and its offset
    public void Clear()
    {
        Size = 0;
        Offset = 0;
    }

    /// <summary>
    ///     Extract the string from buffer of the given offset and size
    /// </summary>
    public string ExtractString(long offset, long size)
    {
        Debug.Assert(offset + size <= Size, "Invalid offset & size!");
        if (offset + size > Size)
            throw new ArgumentException("Invalid offset & size!", nameof(offset));

        return Encoding.UTF8.GetString(_data!, (int)offset, (int)size);
    }

    /// <summary>
    ///     Remove the buffer of the given offset and size
    /// </summary>
    public void Remove(int offset, int size)
    {
        Debug.Assert(offset + size <= Size, "Invalid offset & size!");
        if (offset + size > Size)
            throw new ArgumentException("Invalid offset & size!", nameof(offset));

        Array.Copy(_data!, offset + size, _data!, offset, Size - size - offset);
        Size -= size;
        if (Offset >= offset + size)
            Offset -= size;
        else if (Offset >= offset)
        {
            Offset -= Offset - offset;
            if (Offset > Size)
                Offset = Size;
        }
    }

    /// <summary>
    ///     Reserve the buffer of the given capacity
    /// </summary>
    public void Reserve(int capacity)
    {
        Debug.Assert(capacity >= 0, "Invalid reserve capacity!");
        if (capacity < 0)
            throw new ArgumentException("Invalid reserve capacity!", nameof(capacity));

        if (capacity > Capacity)
        {
            var data = _arrayPool.Rent(Math.Max(capacity, 2 * Capacity));
            Array.Copy(_data!, 0, data, 0, Size);
            _arrayPool.Return(_data!);
            _data = data;
        }
    }

    // Resize the current buffer
    public void Resize(int size)
    {
        Reserve(size);
        Size = size;
        if (Offset > Size)
            Offset = Size;
    }

    // Shift the current buffer offset
    public void Shift(int offset)
    {
        Offset += offset;
    }

    // Unshift the current buffer offset
    public void Unshift(int offset)
    {
        Offset -= offset;
    }

    #endregion

    #region Buffer I/O methods

    /// <summary>
    ///     Append the single byte
    /// </summary>
    /// <param name="value">Byte value to append</param>
    /// <returns>Count of append bytes</returns>
    public long Append(byte value)
    {
        Reserve(Size + 1);
        _data![Size] = value;
        Size += 1;
        return 1;
    }

    /// <summary>
    ///     Append the given buffer
    /// </summary>
    /// <param name="buffer">Buffer to append</param>
    /// <returns>Count of append bytes</returns>
    public long Append(byte[] buffer)
    {
        Reserve(Size + buffer.Length);
        Array.Copy(buffer, 0, _data!, Size, buffer.Length);
        Size += buffer.Length;
        return buffer.Length;
    }

    /// <summary>
    ///     Append the given buffer fragment
    /// </summary>
    /// <param name="buffer">Buffer to append</param>
    /// <param name="offset">Buffer offset</param>
    /// <param name="size">Buffer size</param>
    /// <returns>Count of append bytes</returns>
    public long Append(byte[] buffer, int offset, int size)
    {
        Reserve(Size + size);
        Array.Copy(buffer, offset, _data!, Size, size);
        Size += size;
        return size;
    }

    /// <summary>
    ///     Append the given span of bytes
    /// </summary>
    /// <param name="buffer">Buffer to append as a span of bytes</param>
    /// <returns>Count of append bytes</returns>
    public long Append(ReadOnlySpan<byte> buffer)
    {
        Reserve(Size + buffer.Length);
        buffer.CopyTo(new Span<byte>(_data, Size, buffer.Length));
        Size += buffer.Length;
        return buffer.Length;
    }

    /// <summary>
    ///     Append the given buffer
    /// </summary>
    /// <param name="buffer">Buffer to append</param>
    /// <returns>Count of append bytes</returns>
    public long Append(PooledBuffer buffer)
    {
        return Append(buffer.AsSpan());
    }


    public void Dispose()
    {
        if (_data != null && _isPooled)
        {
            _arrayPool.Return(_data);
            _data = null;
        }
    }

    #endregion
}
