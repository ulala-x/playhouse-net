using System.Text;
using FluentAssertions;
using PlayHouse;
using Xunit;

namespace PlayHouseTests.Service.Session;

public class RingBufferSpec
{
    public RingBufferSpec()
    {
        PooledBuffer.Init();
    }

    [Fact]
    public void Enqueue_ShouldIncreaseCount()
    {
        var buffer = new RingBuffer(10);

        buffer.Enqueue(1);
        buffer.Count.Should().Be(1);

        buffer.Enqueue(2);
        buffer.Count.Should().Be(2);
    }

    [Fact]
    public void Dequeue_ShouldDecreaseCount()
    {
        var buffer = new RingBuffer(10);
        buffer.Enqueue(1);
        buffer.Enqueue(2);

        buffer.Dequeue();
        buffer.Count.Should().Be(1);

        buffer.Dequeue();
        buffer.Count.Should().Be(0);
    }

    [Fact]
    public void Dequeue_ShouldReturnCorrectValues()
    {
        var buffer = new RingBuffer(10);
        buffer.Enqueue(1);
        buffer.Enqueue(2);

        var value1 = buffer.Dequeue();
        value1.Should().Be(1);

        var value2 = buffer.Dequeue();
        value2.Should().Be(2);
    }

    [Fact]
    public void Peek_ShouldReturnCorrectValueWithoutChangingCount()
    {
        var buffer = new RingBuffer(10);
        buffer.Enqueue(1);
        buffer.Enqueue(2);

        var value = buffer.Peek();
        value.Should().Be(1);
        buffer.Count.Should().Be(2);
    }

    [Fact]
    public void Clear_ShouldResetBuffer()
    {
        var buffer = new RingBuffer(10);
        buffer.Enqueue(1);
        buffer.Enqueue(2);

        buffer.Clear();
        buffer.Count.Should().Be(0);
        buffer.Invoking(b => b.Dequeue()).Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Enqueue_ExceedingCapacity_ShouldResizeBuffer()
    {
        var buffer = new RingBuffer(2, 4);
        buffer.Enqueue(1);
        buffer.Enqueue(2);

        buffer.Count.Should().Be(2);
        buffer.Capacity.Should().Be(2);

        buffer.Enqueue(3);
        buffer.Count.Should().Be(3);
        buffer.Capacity.Should().Be(4); // Buffer should resize

        buffer.Dequeue().Should().Be(1);
        buffer.Dequeue().Should().Be(2);
        buffer.Dequeue().Should().Be(3);
    }

    [Fact]
    public void WriteAndReadInt16_ShouldHandleNetworkOrder()
    {
        var buffer = new RingBuffer(10);
        ushort value = 0x1234;

        buffer.WriteInt16(value);
        buffer.Count.Should().Be(2);

        var readValue = buffer.ReadInt16();
        readValue.Should().Be(value);
    }

    [Fact]
    public void WriteAndReadInt32_ShouldHandleNetworkOrder()
    {
        var buffer = new RingBuffer(10);
        var value = 0x12345678;

        buffer.WriteInt32(value);
        buffer.Count.Should().Be(4);

        var readValue = buffer.ReadInt32();
        readValue.Should().Be(value);
    }

    [Fact]
    public void WriteAndReadInt64_ShouldHandleNetworkOrder()
    {
        var buffer = new RingBuffer(20);
        var value = 0x123456789ABCDEF0;

        buffer.WriteInt64(value);
        buffer.Count.Should().Be(8);

        var readValue = buffer.ReadInt64();
        readValue.Should().Be(value);
    }

    [Fact]
    public void WriteAndReadString_ShouldHandleUtf8Encoding()
    {
        var buffer = new RingBuffer(50);
        var value = "Hello, World!";

        buffer.Write(value);

        var count = Encoding.UTF8.GetByteCount(value) + 1;
        buffer.Count.Should().Be(count);

        int length = buffer.ReadByte();
        var readValue = buffer.ReadString(length);
        readValue.Should().Be(value);
    }

    [Fact]
    public void Enqueue_Dequeue_MultipleTypes()
    {
        var buffer = new RingBuffer(20);
        ushort value16 = 0x1234;
        var value32 = 0x12345678;
        var value64 = 0x123456789ABCDEF0;

        buffer.WriteInt16(value16);
        buffer.WriteInt32(value32);
        buffer.WriteInt64(value64);

        buffer.Count.Should().Be(2 + 4 + 8);

        buffer.ReadInt16().Should().Be(value16);
        buffer.ReadInt32().Should().Be(value32);
        buffer.ReadInt64().Should().Be(value64);
    }

    [Fact]
    public void Buffer_ShouldWrapAroundCorrectly()
    {
        var buffer = new RingBuffer(5);
        buffer.Enqueue(1);
        buffer.Enqueue(2);
        buffer.Enqueue(3);
        buffer.Enqueue(4);
        buffer.Enqueue(5);

        buffer.Dequeue();
        buffer.Dequeue();

        buffer.Enqueue(6);
        buffer.Enqueue(7);

        buffer.Dequeue().Should().Be(3);
        buffer.Dequeue().Should().Be(4);
        buffer.Dequeue().Should().Be(5);
        buffer.Dequeue().Should().Be(6);
        buffer.Dequeue().Should().Be(7);
    }

    [Fact]
    public void WriteAndRead_ShouldHandleEdgeCases()
    {
        var buffer = new RingBuffer(5, 10);

        // Enqueue to capacity
        buffer.Enqueue(1);
        buffer.Enqueue(2);
        buffer.Enqueue(3);
        buffer.Enqueue(4);
        buffer.Enqueue(5);

        // Buffer is full, next enqueue should resize
        buffer.Enqueue(6);
        buffer.Count.Should().Be(6);
        buffer.Capacity.Should().Be(10); // Capacity should double

        // Read back values
        buffer.Dequeue().Should().Be(1);
        buffer.Dequeue().Should().Be(2);
        buffer.Dequeue().Should().Be(3);
        buffer.Dequeue().Should().Be(4);
        buffer.Dequeue().Should().Be(5);
        buffer.Dequeue().Should().Be(6);
    }
}