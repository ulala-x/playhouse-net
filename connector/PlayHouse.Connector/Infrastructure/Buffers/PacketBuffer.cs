using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Text;

namespace PlayHouse.Connector.Infrastructure.Buffers;

/// <summary>
/// Java ByteBuffer style packet serialization buffer
/// - Position/Limit/Flip pattern
/// - Little-Endian byte order
/// - Zero-copy with Span/Memory
/// - ArrayPool for memory management
/// </summary>
public sealed class PacketBuffer : IDisposable
{
    private static readonly ArrayPool<byte> Pool = ArrayPool<byte>.Shared;

    private byte[] _buffer;
    private int _position;
    private int _limit;
    private int _capacity;
    private bool _isPooled;

    /// <summary>
    /// Create a new PacketBuffer with specified capacity
    /// </summary>
    public PacketBuffer(int capacity = 256)
    {
        if (capacity <= 0)
        {
            throw new ArgumentException("Capacity must be positive", nameof(capacity));
        }

        _buffer = Pool.Rent(capacity);
        _capacity = _buffer.Length;
        _position = 0;
        _limit = _capacity;
        _isPooled = true;
    }

    /// <summary>
    /// Private constructor for Wrap (no allocation)
    /// </summary>
    private PacketBuffer(byte[] data, int offset, int length, bool isPooled)
    {
        _buffer = data;
        _capacity = data.Length;
        _position = offset;
        _limit = offset + length;
        _isPooled = isPooled;
    }

    /// <summary>
    /// Wrap existing byte array without copying (Zero-copy)
    /// </summary>
    public static PacketBuffer Wrap(byte[] data, int offset, int length)
    {
        if (data == null)
        {
            throw new ArgumentNullException(nameof(data));
        }
        if (offset < 0 || length < 0 || offset + length > data.Length)
        {
            throw new ArgumentException("Invalid offset or length");
        }

        return new PacketBuffer(data, offset, length, isPooled: false);
    }

    /// <summary>
    /// Current read/write position
    /// </summary>
    public int Position
    {
        get => _position;
        set
        {
            if (value < 0 || value > _limit)
            {
                throw new ArgumentOutOfRangeException(nameof(value));
            }
            _position = value;
        }
    }

    /// <summary>
    /// Valid data limit
    /// </summary>
    public int Limit => _limit;

    /// <summary>
    /// Buffer capacity
    /// </summary>
    public int Capacity => _capacity;

    /// <summary>
    /// Remaining bytes from position to limit
    /// </summary>
    public int Remaining => _limit - _position;

    #region Write Methods (Little-Endian, Block Write)

    /// <summary>
    /// Write a single byte
    /// </summary>
    public void WriteByte(byte value)
    {
        EnsureCapacity(1);
        _buffer[_position++] = value;
    }

    /// <summary>
    /// Write Int16 (Little-Endian)
    /// </summary>
    public void WriteInt16(short value)
    {
        EnsureCapacity(sizeof(short));
        BinaryPrimitives.WriteInt16LittleEndian(_buffer.AsSpan(_position), value);
        _position += sizeof(short);
    }

    /// <summary>
    /// Write UInt16 (Little-Endian)
    /// </summary>
    public void WriteUInt16(ushort value)
    {
        EnsureCapacity(sizeof(ushort));
        BinaryPrimitives.WriteUInt16LittleEndian(_buffer.AsSpan(_position), value);
        _position += sizeof(ushort);
    }

    /// <summary>
    /// Write Int32 (Little-Endian)
    /// </summary>
    public void WriteInt32(int value)
    {
        EnsureCapacity(sizeof(int));
        BinaryPrimitives.WriteInt32LittleEndian(_buffer.AsSpan(_position), value);
        _position += sizeof(int);
    }

    /// <summary>
    /// Write Int64 (Little-Endian)
    /// </summary>
    public void WriteInt64(long value)
    {
        EnsureCapacity(sizeof(long));
        BinaryPrimitives.WriteInt64LittleEndian(_buffer.AsSpan(_position), value);
        _position += sizeof(long);
    }

    /// <summary>
    /// Write byte array
    /// </summary>
    public void WriteBytes(ReadOnlySpan<byte> data)
    {
        if (data.Length == 0)
        {
            return;
        }

        EnsureCapacity(data.Length);
        data.CopyTo(_buffer.AsSpan(_position));
        _position += data.Length;
    }

    /// <summary>
    /// Write string with length prefix [1byte len][UTF-8 bytes]
    /// </summary>
    public void WriteString(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            WriteByte(0);
            return;
        }

        var maxByteCount = Encoding.UTF8.GetMaxByteCount(value.Length);

        if (maxByteCount > 255)
        {
            throw new ArgumentException("String too large (max 255 bytes in UTF-8)", nameof(value));
        }

        // Use stack allocation for small strings
        Span<byte> tempBuffer = maxByteCount <= 128
            ? stackalloc byte[maxByteCount]
            : new byte[maxByteCount];

        var bytesWritten = Encoding.UTF8.GetBytes(value, tempBuffer);

        WriteByte((byte)bytesWritten);
        WriteBytes(tempBuffer.Slice(0, bytesWritten));
    }

    #endregion

    #region Read Methods (Little-Endian)

    /// <summary>
    /// Read a single byte
    /// </summary>
    public byte ReadByte()
    {
        if (Remaining < 1)
        {
            throw new InvalidOperationException("Not enough data in buffer to read byte");
        }
        return _buffer[_position++];
    }

    /// <summary>
    /// Read Int16 (Little-Endian)
    /// </summary>
    public short ReadInt16()
    {
        if (Remaining < sizeof(short))
        {
            throw new InvalidOperationException("Not enough data in buffer to read Int16");
        }
        var value = BinaryPrimitives.ReadInt16LittleEndian(_buffer.AsSpan(_position));
        _position += sizeof(short);
        return value;
    }

    /// <summary>
    /// Read UInt16 (Little-Endian)
    /// </summary>
    public ushort ReadUInt16()
    {
        if (Remaining < sizeof(ushort))
        {
            throw new InvalidOperationException("Not enough data in buffer to read UInt16");
        }
        var value = BinaryPrimitives.ReadUInt16LittleEndian(_buffer.AsSpan(_position));
        _position += sizeof(ushort);
        return value;
    }

    /// <summary>
    /// Read Int32 (Little-Endian)
    /// </summary>
    public int ReadInt32()
    {
        if (Remaining < sizeof(int))
        {
            throw new InvalidOperationException("Not enough data in buffer to read Int32");
        }
        var value = BinaryPrimitives.ReadInt32LittleEndian(_buffer.AsSpan(_position));
        _position += sizeof(int);
        return value;
    }

    /// <summary>
    /// Read Int64 (Little-Endian)
    /// </summary>
    public long ReadInt64()
    {
        if (Remaining < sizeof(long))
        {
            throw new InvalidOperationException("Not enough data in buffer to read Int64");
        }
        var value = BinaryPrimitives.ReadInt64LittleEndian(_buffer.AsSpan(_position));
        _position += sizeof(long);
        return value;
    }

    /// <summary>
    /// Read bytes without copying (Zero-copy)
    /// </summary>
    public ReadOnlySpan<byte> ReadBytes(int count)
    {
        if (count < 0)
        {
            throw new ArgumentException("Count must be non-negative", nameof(count));
        }
        if (Remaining < count)
        {
            throw new InvalidOperationException($"Not enough data in buffer to read {count} bytes");
        }

        var span = new ReadOnlySpan<byte>(_buffer, _position, count);
        _position += count;
        return span;
    }

    /// <summary>
    /// Read string with length prefix [1byte len][UTF-8 bytes]
    /// </summary>
    public string ReadString()
    {
        var length = ReadByte();

        if (length == 0)
        {
            return string.Empty;
        }

        if (Remaining < length)
        {
            throw new InvalidOperationException($"Not enough data in buffer to read string of length {length}");
        }

        var str = Encoding.UTF8.GetString(_buffer, _position, length);
        _position += length;
        return str;
    }

    #endregion

    #region Zero-copy Access

    /// <summary>
    /// Get read-only span of entire buffer content
    /// </summary>
    public ReadOnlySpan<byte> AsSpan()
    {
        return new ReadOnlySpan<byte>(_buffer, 0, _position);
    }

    /// <summary>
    /// Get read-only span from position to limit
    /// </summary>
    public ReadOnlySpan<byte> AsSpan(int offset, int length)
    {
        if (offset < 0 || length < 0 || offset + length > _position)
        {
            throw new ArgumentException("Invalid offset or length");
        }
        return new ReadOnlySpan<byte>(_buffer, offset, length);
    }

    /// <summary>
    /// Get read-only memory of entire buffer content
    /// </summary>
    public ReadOnlyMemory<byte> AsMemory()
    {
        return new ReadOnlyMemory<byte>(_buffer, 0, _position);
    }

    /// <summary>
    /// Get writable span for direct writing
    /// </summary>
    public Span<byte> GetWriteSpan(int size)
    {
        EnsureCapacity(size);
        return _buffer.AsSpan(_position, size);
    }

    /// <summary>
    /// Advance position after direct writing
    /// </summary>
    public void Advance(int count)
    {
        if (count < 0 || _position + count > _limit)
        {
            throw new ArgumentException("Invalid advance count", nameof(count));
        }
        _position += count;
    }

    #endregion

    #region State Management

    /// <summary>
    /// Flip: Write mode → Read mode (limit=position, position=0)
    /// </summary>
    public void Flip()
    {
        _limit = _position;
        _position = 0;
    }

    /// <summary>
    /// Clear: Reset to initial state (position=0, limit=capacity)
    /// </summary>
    public void Clear()
    {
        _position = 0;
        _limit = _capacity;
    }

    /// <summary>
    /// Compact: Move remaining data to beginning
    /// </summary>
    public void Compact()
    {
        var remaining = Remaining;
        if (remaining > 0 && _position > 0)
        {
            _buffer.AsSpan(_position, remaining).CopyTo(_buffer);
        }
        _position = remaining;
        _limit = _capacity;
    }

    #endregion

    #region Capacity Management

    private void EnsureCapacity(int additionalBytes)
    {
        var requiredCapacity = _position + additionalBytes;

        if (requiredCapacity <= _limit)
        {
            return;
        }

        if (!_isPooled)
        {
            throw new InvalidOperationException("Cannot expand wrapped buffer");
        }

        // Calculate new capacity (double until sufficient)
        var newCapacity = _capacity;
        while (newCapacity < requiredCapacity)
        {
            newCapacity *= 2;
        }

        // Rent new buffer and copy (exception-safe)
        var newBuffer = Pool.Rent(newCapacity);
        try
        {
            _buffer.AsSpan(0, _position).CopyTo(newBuffer);
        }
        catch
        {
            Pool.Return(newBuffer);
            throw;
        }

        // Return old buffer and swap
        Pool.Return(_buffer);
        _buffer = newBuffer;
        _capacity = newBuffer.Length;
        _limit = _capacity;
    }

    #endregion

    public void Dispose()
    {
        if (_buffer != null && _isPooled)
        {
            Pool.Return(_buffer);
            _buffer = null!;
        }
    }
}
