using System;
using System.Text;
using PlayHouse.Connector.Infrastructure.Utils;

namespace PlayHouse.Connector.Infrastructure.Buffers;

public class PooledByteBuffer : IDisposable
{
    private readonly int _maxCapacity;
    private PooledBuffer _buffer;
    private int _headerIndex;
    private int _count;

    public PooledByteBuffer(int capacity, int maxCapacity)
    {
        if (capacity > maxCapacity)
        {
            throw new ArgumentException("capacity cannot be greater than maxCapacity");
        }

        _buffer = new PooledBuffer(capacity);
        ReaderIndex = 0;
        _headerIndex = 0;
        _count = 0;
        Capacity = capacity;
        _maxCapacity = maxCapacity;
    }

    public PooledByteBuffer(int capacity) : this(capacity, capacity)
    {
    }

    public int Capacity { get; set; }

    public int Count => _count;

    public int ReaderIndex { get; private set; }

    public void Dispose()
    {
        _buffer.Dispose();
    }

    public Span<byte> AsSpan()
    {
        return _buffer.AsSpan();
    }

    public Memory<byte> AsMemory()
    {
        return new Memory<byte>(_buffer.Data, ReaderIndex, _count);
    }

    protected internal virtual int NextIndex(int index)
    {
        return (index + 1) % Capacity;
    }

    public int MoveIndex(int index, int count)
    {
        for (var i = 0; i < count; i++)
        {
            index = NextIndex(index);
        }

        return index;
    }

    public void Enqueue(byte item)
    {
        if (_count == Capacity)
        {
            ResizeBuffer(Capacity * 2);
        }

        _buffer[_headerIndex] = item;
        _headerIndex = NextIndex(_headerIndex);
        _count++;
    }

    public void Enqueue(byte[] data)
    {
        foreach (var b in data)
        {
            Enqueue(b);
        }
    }

    public long Append(byte data)
    {
        return _buffer.Append(data);
    }

    private void ResizeBuffer(int newCapacity)
    {
        if (newCapacity > _maxCapacity)
        {
            throw new InvalidOperationException("Queue has reached maximum capacity");
        }

        var newBuffer = new PooledBuffer(newCapacity);

        int currentCount = Count; // 임시 변수에 현재 Count 저장

        for (int i = 0; i < currentCount; i++)
        {
            newBuffer.Append(Dequeue());
        }

        _buffer.Dispose();
        _buffer = newBuffer;
        _count = currentCount;
        _headerIndex = currentCount; // _count가 아닌 currentCount 사용
        ReaderIndex = 0;
        Capacity = newCapacity;
    }

    public byte Dequeue()
    {
        if (_count == 0)
        {
            throw new InvalidOperationException("Queue is empty");
        }

        var item = _buffer[ReaderIndex];
        _buffer[ReaderIndex] = default;
        ReaderIndex = NextIndex(ReaderIndex);
        _count--;
        return item;
    }

    public byte Peek()
    {
        if (_count == 0)
        {
            throw new InvalidOperationException("Queue is empty");
        }

        return _buffer[ReaderIndex];
    }

    public void Clear()
    {
        _buffer.Clear();
        ReaderIndex = 0;
        _headerIndex = 0;
        _count = 0;
    }

    public void WriteCount(int count)
    {
        if (_count + count > Capacity)
        {
            throw new InvalidOperationException("Queue has reached maximum capacity");
        }

        _count += count;
    }

    public void Clear(int count)
    {
        if (count > _count)
        {
            throw new ArgumentException(nameof(count));
        }

        for (var i = 0; i < count; ++i)
        {
            ReaderIndex = NextIndex(ReaderIndex);
        }

        _count -= count;
    }

    public int Read(byte[] buffer, int offset, int count)
    {
        var bytesRead = 0;

        while (bytesRead < count && _count > 0)
        {
            buffer[offset + bytesRead] = Dequeue();
            bytesRead++;
        }

        return bytesRead;
    }

    public void Write(ReadOnlySpan<byte> buffer)
    {
        for (var i = 0; i < buffer.Length; i++)
        {
            Enqueue(buffer[i]);
        }
    }

    public void Write(byte[] buffer, long offset, long count)
    {
        for (var i = 0; i < count; i++)
        {
            Enqueue(buffer[offset + i]);
        }
    }

    /// <summary>
    /// Encodes a string to UTF-8 and writes it to the provided buffer.
    /// The string size must be 128 bytes or less when encoded in UTF-8.
    /// </summary>
    public void Write(string value)
    {
        var maxByteCount = Encoding.UTF8.GetMaxByteCount(value.Length);

        if (maxByteCount > 128)
        {
            throw new InvalidOperationException("String too large for stack allocation");
        }

        Span<byte> buffer = stackalloc byte[maxByteCount];
        var bytesUsed = Encoding.UTF8.GetBytes(value, buffer);

        Enqueue((byte)bytesUsed); // string size

        for (var i = 0; i < bytesUsed; i++)
        {
            Enqueue(buffer[i]);
        }
    }


    public void Write(byte b)
    {
        Enqueue(b);
    }

    private byte GetByte(int index)
    {
        if (index < 0 || index >= Capacity)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        return _buffer[index];
    }

    private ushort GetInt16(int index)
    {
        return XBitConverter.ToHostOrder((ushort)(GetByte(index) | (GetByte(NextIndex(index)) << 8)));
    }

    private int GetInt32(int index)
    {
        return XBitConverter.ToHostOrder(GetByte(index) |
                                         (GetByte(index = NextIndex(index)) << 8) |
                                         (GetByte(index = NextIndex(index)) << 16) |
                                         (GetByte(index = NextIndex(index)) << 24));
    }

    private long GetInt64(int index)
    {
        return XBitConverter.ToHostOrder((long)GetByte(index) |
                                         ((long)GetByte(index = NextIndex(index)) << 8) |
                                         ((long)GetByte(index = NextIndex(index)) << 16) |
                                         ((long)GetByte(index = NextIndex(index)) << 24) |
                                         ((long)GetByte(index = NextIndex(index)) << 32) |
                                         ((long)GetByte(index = NextIndex(index)) << 40) |
                                         ((long)GetByte(index = NextIndex(index)) << 48) |
                                         ((long)GetByte(index = NextIndex(index)) << 56));
    }


    public ushort PeekInt16(int index)
    {
        if (_count < sizeof(short))
        {
            throw new InvalidOperationException("Not enough data in the buffer to read Int16");
        }

        return GetInt16(index);
    }

    public void Read(PooledByteBuffer body, int count)
    {
        for (var i = 0; i < count; i++)
        {
            body.Enqueue(Dequeue());
        }
    }
    public byte PeekByte(int index)
    {
        return GetByte(index);
    }
    public int PeekInt32(int index)
    {
        if (_count < sizeof(int))
        {
            throw new InvalidOperationException("Not enough data in the buffer to read Int32");
        }

        return GetInt32(index);
    }

    public long PeekInt64(int index)
    {
        if (_count < sizeof(long))
        {
            throw new InvalidOperationException("Not enough data in the buffer to read Int64");
        }

        return GetInt64(index);
    }

    public ushort ReadInt16()
    {
        if (_count < sizeof(short))
        {
            throw new InvalidOperationException("Not enough data in the buffer to read Int16");
        }

        var data = PeekInt16(ReaderIndex);
        var count = sizeof(ushort);
        ReaderIndex = MoveIndex(ReaderIndex, count);
        _count -= count;
        return data;
    }

    public int ReadInt32()
    {
        if (_count < sizeof(int))
        {
            throw new InvalidOperationException("Not enough data in the buffer to read Int32");
        }

        var data = PeekInt32(ReaderIndex);
        var count = sizeof(int);
        ReaderIndex = MoveIndex(ReaderIndex, count);
        _count -= count;
        return data;
    }

    public long ReadInt64()
    {
        if (_count < sizeof(long))
        {
            throw new InvalidOperationException("Not enough data in the buffer to read Int64");
        }

        var data = PeekInt64(ReaderIndex);
        var count = sizeof(long);
        ReaderIndex = MoveIndex(ReaderIndex, count);
        _count -= count;
        return data;
    }

    public void SetInt16(int index, short value)
    {
        var networkOrderValue = XBitConverter.ToNetworkOrder(value);
        SetByte(index, (byte)((networkOrderValue >> 8) & 0xFF));
        SetByte(NextIndex(index), (byte)(networkOrderValue & 0xFF));
    }

    public void SetByte(int index, byte value)
    {
        if (index < 0 || index >= Capacity)
        {
            throw new IndexOutOfRangeException();
        }

        _buffer[index] = value;
    }

    public int WriteInt16(ushort value)
    {
        var networkOrderValue = XBitConverter.ToNetworkOrder(value);
        var count = sizeof(ushort);

        if (_count + count > Capacity)
        {
            ResizeBuffer(Capacity * 2);
        }

        var startIndex = _headerIndex;

        Enqueue((byte)(networkOrderValue & 0xFF));          // 하위 바이트
        Enqueue((byte)((networkOrderValue >> 8) & 0xFF));   // 상위 바이트

        return startIndex;
    }

    public int WriteInt32(int value)
    {
        var networkOrderValue = XBitConverter.ToNetworkOrder(value);
        var count = sizeof(int);

        if (_count + count > Capacity)
        {
            ResizeBuffer(Capacity * 2);
        }

        var startIndex = _headerIndex;

        Enqueue((byte)(networkOrderValue & 0xFF));          // 하위 바이트
        Enqueue((byte)((networkOrderValue >> 8) & 0xFF));   // 2번째 바이트
        Enqueue((byte)((networkOrderValue >> 16) & 0xFF));  // 3번째 바이트
        Enqueue((byte)((networkOrderValue >> 24) & 0xFF));  // 상위 바이트

        return startIndex;
    }

    public int WriteInt64(long value)
    {
        var networkOrderValue = XBitConverter.ToNetworkOrder(value);
        var count = sizeof(long);

        if (_count + count > Capacity)
        {
            ResizeBuffer(Capacity * 2);
        }

        var startIndex = _headerIndex;

        Enqueue((byte)(networkOrderValue & 0xFF));          // 하위 바이트
        Enqueue((byte)((networkOrderValue >> 8) & 0xFF));   // 2번째 바이트
        Enqueue((byte)((networkOrderValue >> 16) & 0xFF));  // 3번째 바이트
        Enqueue((byte)((networkOrderValue >> 24) & 0xFF));  // 4번째 바이트
        Enqueue((byte)((networkOrderValue >> 32) & 0xFF));  // 5번째 바이트
        Enqueue((byte)((networkOrderValue >> 40) & 0xFF));  // 6번째 바이트
        Enqueue((byte)((networkOrderValue >> 48) & 0xFF));  // 7번째 바이트
        Enqueue((byte)((networkOrderValue >> 56) & 0xFF));  // 상위 바이트

        return startIndex;
    }

    public string ReadString(int msgSize, Encoding? encoding = null)
    {
        encoding ??= Encoding.UTF8;

        if (msgSize > 1024)
        {
            throw new ArgumentException("메시지 크기가 스택 할당에 너무 큽니다.");
        }

        Span<byte> stringBytes = stackalloc byte[msgSize];

        for (var i = 0; i < msgSize; i++)
        {
            if (_count == 0)
            {
                throw new InvalidOperationException("버퍼에 충분한 데이터가 없습니다.");
            }

            stringBytes[i] = Dequeue();
        }

        var result = encoding.GetString(stringBytes);
        return result;
    }

    public byte[] Buffer()
    {
        return _buffer.Data;
    }

    public byte ReadByte()
    {
        return Dequeue();
    }
}
