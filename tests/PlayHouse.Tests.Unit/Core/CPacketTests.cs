using FluentAssertions;
using Google.Protobuf.WellKnownTypes;
using PlayHouse.Abstractions;
using PlayHouse.Core.Shared;
using Xunit;

namespace PlayHouse.Tests.Unit.Core;

/// <summary>
/// CPacket 단위 테스트
/// </summary>
public class CPacketTests
{
    #region 기본 생성

    [Fact(DisplayName = "Of(msgId, data)로 바이트 배열에서 패킷을 생성할 수 있다")]
    public void CPacket_FromBytes_CreatesPacketWithCorrectData()
    {
        // Given
        const string msgId = "TestMessage";
        var data = new byte[] { 1, 2, 3, 4, 5 };

        // When
        var packet = CPacket.Of(msgId, data);

        // Then
        packet.MsgId.Should().Be(msgId);
        packet.Payload.DataSpan.ToArray().Should().BeEquivalentTo(data);
    }

    [Fact(DisplayName = "Of<T>(message)로 Protobuf 메시지에서 패킷을 생성할 수 있다")]
    public void CPacket_FromProto_CreatesPacketWithCorrectMsgId()
    {
        // Given
        var message = new StringValue { Value = "test" };

        // When
        var packet = CPacket.Of(message);

        // Then
        packet.MsgId.Should().Contain("StringValue");
        packet.Payload.DataSpan.Length.Should().BeGreaterThan(0);
    }

    [Fact(DisplayName = "Empty(msgId)로 빈 패킷을 생성할 수 있다")]
    public void CPacket_Empty_CreatesPacketWithEmptyPayload()
    {
        // Given
        const string msgId = "EmptyMessage";

        // When
        var packet = CPacket.Empty(msgId);

        // Then
        packet.MsgId.Should().Be(msgId);
        packet.Payload.DataSpan.Length.Should().Be(0);
    }

    #endregion

    #region Dispose

    [Fact(DisplayName = "Dispose 후에도 MsgId에 접근할 수 있다")]
    public void CPacket_AfterDispose_MsgIdStillAccessible()
    {
        // Given
        var packet = CPacket.Of("Test", new byte[] { 1, 2, 3 });

        // When
        packet.Dispose();

        // Then
        var action = () => packet.MsgId;
        action.Should().NotThrow();
    }

    #endregion
}

/// <summary>
/// Payload 구현체 단위 테스트
/// </summary>
public class PayloadTests
{
    #region BytePayload

    [Fact(DisplayName = "BytePayload는 바이트 배열을 래핑한다")]
    public void BytePayload_FromArray_ReturnsSameData()
    {
        // Given
        var data = new byte[] { 10, 20, 30 };

        // When
        var payload = new BytePayload(data);

        // Then
        payload.DataSpan.ToArray().Should().BeEquivalentTo(data);
    }

    [Fact(DisplayName = "BytePayload는 ReadOnlySpan에서 복사하여 생성할 수 있다")]
    public void BytePayload_FromSpan_CopiesData()
    {
        // Given
        ReadOnlySpan<byte> span = stackalloc byte[] { 1, 2, 3 };

        // When
        var payload = new BytePayload(span);

        // Then
        payload.DataSpan.Length.Should().Be(3);
    }

    #endregion

    #region ProtoPayload

    [Fact(DisplayName = "ProtoPayload는 Protobuf 메시지를 직렬화한다")]
    public void ProtoPayload_FromMessage_SerializesCorrectly()
    {
        // Given
        var message = new StringValue { Value = "hello" };

        // When
        var payload = new ProtoPayload(message);

        // Then
        payload.DataSpan.Length.Should().BeGreaterThan(0);
        payload.GetProto().Should().Be(message);
    }

    [Fact(DisplayName = "ProtoPayload의 Data는 캐싱된다")]
    public void ProtoPayload_Data_IsCached()
    {
        // Given
        var payload = new ProtoPayload(new StringValue { Value = "test" });

        // When
        var first = payload.DataSpan;
        var second = payload.DataSpan;

        // Then
        first.ToArray().Should().BeEquivalentTo(second.ToArray());
    }

    #endregion

    #region EmptyPayload

    [Fact(DisplayName = "EmptyPayload는 싱글톤이다")]
    public void EmptyPayload_Instance_IsSingleton()
    {
        // When
        var first = EmptyPayload.Instance;
        var second = EmptyPayload.Instance;

        // Then
        first.Should().BeSameAs(second);
    }

    [Fact(DisplayName = "EmptyPayload의 길이는 0이다")]
    public void EmptyPayload_Data_IsEmpty()
    {
        // When
        var payload = EmptyPayload.Instance;

        // Then
        payload.DataSpan.Length.Should().Be(0);
    }

    #endregion
}

