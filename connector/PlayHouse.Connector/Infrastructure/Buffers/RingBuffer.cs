namespace PlayHouse.Connector.Infrastructure.Buffers;

public class RingBuffer : PooledByteBuffer
{
    public RingBuffer(int capacity) : base(capacity)
    {
    }

    public RingBuffer(int capacity, int maxCapacity) : base(capacity, maxCapacity)
    {
    }

    public int PeekNextIndex(int offSet)
    {
        var readerIndex = ReaderIndex;
        for (var i = 0; i < offSet; i++)
        {
            readerIndex = NextIndex(readerIndex);
        }

        return readerIndex;
    }

    protected internal override int NextIndex(int index)
    {
        return (index + 1) % Capacity;
    }

    public bool ReadBool()
    {
        var data = ReadByte();
        return data != 0;
    }
}
