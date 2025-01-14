using FluentAssertions;
using PlayHouse;
using PlayHouse.Service.Session.Network;
using Xunit;

namespace PlayHouseTests.Service.Session;

public class PacketParserTests
{
    private readonly PacketParser _parser = new();

    public PacketParserTests()
    {
        PooledBuffer.Init();
    }

    [Fact]
    public void Parse_ValidSinglePacket_ShouldReturnOnePacket()
    {
        var buffer = new RingBuffer(1024);
        var serviceId = (ushort)1;
        var msgId = "12345";
        var msgSeq = (ushort)1;
        var stageId = 67890L;
        var body = new byte[] { 1, 2, 3, 4, 5 };

        buffer.WriteInt32(body.Length);
        buffer.WriteInt16(serviceId);
        buffer.Write(msgId);
        buffer.WriteInt16(msgSeq);
        buffer.WriteInt64(stageId);
        buffer.Write(body);

        var packets = _parser.Parse(buffer);

        packets.Should().HaveCount(1);
        var packet = packets[0];
        packet.Header.ServiceId.Should().Be(serviceId);
        packet.Header.MsgId.Should().Be(msgId);
        packet.Header.MsgSeq.Should().Be(msgSeq);
        packet.Header.StageId.Should().Be(stageId);
        packet.Payload.Data.ToArray().Should().Equal(body);
    }

    [Fact]
    public void Parse_IncompletePacket_ShouldReturnNoPackets()
    {
        var buffer = new RingBuffer(1024);
        var bodySize = 5;

        buffer.WriteInt32(bodySize); // Write only body size, not the full packet

        var packets = _parser.Parse(buffer);

        packets.Should().BeEmpty();
    }

    [Fact]
    public void Parse_MultiplePackets_ShouldReturnAllPackets()
    {
        var buffer = new RingBuffer(1024);
        var packetCount = 3;
        var serviceId = (ushort)1;
        var msgId = "12345";
        var msgSeq = (ushort)1;
        var stageId = 67890L;
        var body = new byte[] { 1, 2, 3, 4, 5 };

        for (var i = 0; i < packetCount; i++)
        {
            buffer.WriteInt32(body.Length);
            buffer.WriteInt16(serviceId);
            buffer.Write(msgId);
            buffer.WriteInt16(msgSeq);
            buffer.WriteInt64(stageId);
            buffer.Write(body);
        }

        var packets = _parser.Parse(buffer);

        packets.Should().HaveCount(packetCount);
        foreach (var packet in packets)
        {
            packet.Header.ServiceId.Should().Be(serviceId);
            packet.Header.MsgId.Should().Be(msgId);
            packet.Header.MsgSeq.Should().Be(msgSeq);
            packet.Header.StageId.Should().Be(stageId);
            packet.Payload.Data.ToArray().Should().Equal(body);
        }
    }

    [Fact]
    public void Parse_PartialPackets_ShouldHandleProperly()
    {
        var buffer = new RingBuffer(1024);
        var serviceId = (ushort)1;
        var msgId = "12345";
        var msgSeq = (ushort)1;
        var stageId = 67890L;
        var body = new byte[] { 1, 2, 3, 4, 5 };

        // Write a full packet
        buffer.WriteInt32(body.Length);
        buffer.WriteInt16(serviceId);
        buffer.Write(msgId);
        buffer.WriteInt16(msgSeq);
        buffer.WriteInt64(stageId);
        buffer.Write(body);

        // Write a partial packet
        buffer.WriteInt32(body.Length);
        buffer.WriteInt16(serviceId);
        buffer.Write((byte)msgId.Length);
        buffer.Write(msgId);

        var packets = _parser.Parse(buffer);

        packets.Should().HaveCount(1);
        var packet = packets[0];
        packet.Header.ServiceId.Should().Be(serviceId);
        packet.Header.MsgId.Should().Be(msgId);
        packet.Header.MsgSeq.Should().Be(msgSeq);
        packet.Header.StageId.Should().Be(stageId);
        packet.Payload.Data.ToArray().Should().Equal(body);
    }

    [Fact]
    public void Parse_LargeNumberOfPackets_ShouldHandleProperly()
    {
        var buffer = new RingBuffer(1024 * 1024);
        var packetCount = 1000;
        var serviceId = (ushort)1;
        var msgId = "12345";
        var msgSeq = (ushort)1;
        var stageId = 67890L;
        var body = new byte[] { 1, 2, 3, 4, 5 };

        for (var i = 0; i < packetCount; i++)
        {
            buffer.WriteInt32(body.Length);
            buffer.WriteInt16(serviceId);
            buffer.Write(msgId);
            buffer.WriteInt16(msgSeq);
            buffer.WriteInt64(stageId);
            buffer.Write(body);
        }

        var packets = _parser.Parse(buffer);

        packets.Should().HaveCount(packetCount);
        foreach (var packet in packets)
        {
            packet.Header.ServiceId.Should().Be(serviceId);
            packet.Header.MsgId.Should().Be(msgId);
            packet.Header.MsgSeq.Should().Be(msgSeq);
            packet.Header.StageId.Should().Be(stageId);
            packet.Payload.Data.ToArray().Should().Equal(body);
        }
    }

    [Fact]
    public void Parse_LargeNumberOfPackets_ShouldHandleProperly_oneByone()
    {
        var buffer = new RingBuffer(1024 * 1);
        var packetCount = 1000;
        var serviceId = (ushort)1;
        var msgId = "12345asdfsadfasdfasfrewqrfd";
        var msgSeq = (ushort)1;
        var stageId = 67890L;
        var body = new byte[] { 1, 2, 3, 4, 5 };

        for (var i = 0; i < packetCount; i++)
        {
            buffer.WriteInt32(body.Length);
            buffer.WriteInt16(serviceId);
            buffer.Write(msgId);
            buffer.WriteInt16(msgSeq);
            buffer.WriteInt64(stageId);
            buffer.Write(body);

            var packets = _parser.Parse(buffer);

            packets.Should().HaveCount(1);
            foreach (var packet in packets)
            {
                packet.Header.ServiceId.Should().Be(serviceId);
                packet.Header.MsgId.Should().Be(msgId);
                packet.Header.MsgSeq.Should().Be(msgSeq);
                packet.Header.StageId.Should().Be(stageId);
                packet.Payload.Data.ToArray().Should().Equal(body);
            }
        }
    }
}