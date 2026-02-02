#nullable enable

using FluentAssertions;
using PlayHouse.Core.Shared;
using PlayHouse.E2E.Shared.Proto;
using PlayHouse.Extensions.Proto;
using Xunit;

namespace PlayHouse.Unit.Extensions;

/// <summary>
/// PlayHouse.Extensions.Proto의 확장 메서드 테스트
/// </summary>
public class ProtoExtensionsTests
{
    /// <summary>
    /// ProtoCPacketExtensions.OfProto가 올바른 MsgId를 설정하는지 확인
    /// </summary>
    [Fact]
    public void OfProto_ShouldSetCorrectMsgId()
    {
        // Arrange
        var message = new EchoRequest
        {
            Content = "Hello",
            Sequence = 1
        };

        // Act
        using var packet = ProtoCPacketExtensions.OfProto(message);

        // Assert
        packet.MsgId.Should().Be("EchoRequest", "MsgId는 typeof(T).Name이어야 함");
    }

    /// <summary>
    /// ProtoCPacketExtensions.OfProto가 Payload를 올바르게 직렬화하는지 확인
    /// </summary>
    [Fact]
    public void OfProto_ShouldSerializePayloadCorrectly()
    {
        // Arrange
        var message = new EchoRequest
        {
            Content = "Hello World",
            Sequence = 42
        };

        // Act
        using var packet = ProtoCPacketExtensions.OfProto(message);

        // Assert
        var parsed = packet.Parse<EchoRequest>();
        parsed.Content.Should().Be("Hello World");
        parsed.Sequence.Should().Be(42);
    }

    /// <summary>
    /// ProtoPacketExtensions.Parse가 ProtoPayload에서 직접 캐스트하는지 확인 (zero-copy)
    /// </summary>
    [Fact]
    public void Parse_WithProtoPayload_ShouldDirectCast()
    {
        // Arrange
        var original = new EchoRequest
        {
            Content = "Direct Cast",
            Sequence = 123
        };
        using var packet = ProtoCPacketExtensions.OfProto(original);

        // Act
        var parsed = packet.Parse<EchoRequest>();

        // Assert
        parsed.Content.Should().Be("Direct Cast");
        parsed.Sequence.Should().Be(123);
    }

    /// <summary>
    /// ProtoPacketExtensions.Parse가 BytePayload에서 역직렬화하는지 확인
    /// </summary>
    [Fact]
    public void Parse_WithBytePayload_ShouldDeserialize()
    {
        // Arrange - 데이터가 있는 메시지 사용
        var message = new EchoRequest { Content = "BytePayload Test", Sequence = 999 };
        using var protoPacket = ProtoCPacketExtensions.OfProto(message);

        // BytePayload로 변환 (CPacket 내부 동작 모방)
        using var bytePacket = CPacket.Of(protoPacket.MsgId, protoPacket.Payload.DataSpan.ToArray());

        // Act
        var parsed = bytePacket.Parse<EchoRequest>();

        // Assert
        parsed.Should().NotBeNull();
        parsed.Content.Should().Be("BytePayload Test");
        parsed.Sequence.Should().Be(999);
    }

    /// <summary>
    /// ProtoPacketExtensions.Parse가 빈 Payload에 대해 예외를 던지는지 확인
    /// </summary>
    [Fact]
    public void Parse_WithEmptyPayload_ShouldThrowException()
    {
        // Arrange
        using var packet = CPacket.Empty("EmptyMessage");

        // Act
        var act = () => packet.Parse<EchoRequest>();

        // Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Cannot parse empty payload*");
    }

    /// <summary>
    /// ProtoPacketExtensions.TryParse가 성공 케이스를 처리하는지 확인
    /// </summary>
    [Fact]
    public void TryParse_WithValidData_ShouldReturnTrue()
    {
        // Arrange
        var message = new ChatMessage
        {
            SenderId = 1001,
            SenderName = "Alice",
            Message = "Hello",
            Timestamp = 123456789
        };
        using var packet = ProtoCPacketExtensions.OfProto(message);

        // Act
        var result = packet.TryParse<ChatMessage>(out var parsed);

        // Assert
        result.Should().BeTrue();
        parsed.Should().NotBeNull();
        parsed!.SenderId.Should().Be(1001);
        parsed.SenderName.Should().Be("Alice");
        parsed.Message.Should().Be("Hello");
        parsed.Timestamp.Should().Be(123456789);
    }

    /// <summary>
    /// ProtoPacketExtensions.TryParse가 실패 케이스를 처리하는지 확인
    /// </summary>
    [Fact]
    public void TryParse_WithInvalidData_ShouldReturnFalse()
    {
        // Arrange
        using var packet = CPacket.Empty("InvalidMessage");

        // Act
        var result = packet.TryParse<EchoRequest>(out var parsed);

        // Assert
        result.Should().BeFalse();
        parsed.Should().BeNull();
    }

    /// <summary>
    /// ProtoPacketExtensions.TryParse가 잘못된 데이터 파싱 시 실패하는지 확인
    /// </summary>
    [Fact]
    public void TryParse_WithCorruptedData_ShouldReturnFalse()
    {
        // Arrange - 잘못된 바이트 배열로 패킷 생성
        var corruptedData = new byte[] { 0xFF, 0xFF, 0xFF, 0xFF, 0xFF };
        using var packet = CPacket.Of("CorruptedMessage", corruptedData);

        // Act - 파싱 시도
        var result = packet.TryParse<EchoRequest>(out var parsed);

        // Assert
        result.Should().BeFalse();
        parsed.Should().BeNull();
    }

    /// <summary>
    /// 복잡한 Proto 메시지도 올바르게 직렬화/역직렬화되는지 확인
    /// </summary>
    [Fact]
    public void OfProto_WithComplexMessage_ShouldPreserveAllFields()
    {
        // Arrange
        var message = new StatusReply
        {
            ActorCount = 100,
            UptimeSeconds = 3600,
            StageType = "TestStage"
        };

        // Act
        using var packet = ProtoCPacketExtensions.OfProto(message);
        var parsed = packet.Parse<StatusReply>();

        // Assert
        parsed.ActorCount.Should().Be(100);
        parsed.UptimeSeconds.Should().Be(3600);
        parsed.StageType.Should().Be("TestStage");
    }

    /// <summary>
    /// CPacket이 IDisposable을 구현하는지 확인
    /// </summary>
    [Fact]
    public void CPacket_ImplementsIDisposable()
    {
        // Arrange
        var message = new EchoRequest { Content = "Test", Sequence = 1 };
        var packet = ProtoCPacketExtensions.OfProto(message);

        // Act & Assert - using으로 정상적으로 Dispose 가능
        using (packet)
        {
            packet.MsgId.Should().Be("EchoRequest");
        }

        // Dispose 후에도 속성 접근은 가능 (내부 Payload는 Dispose됨)
        packet.MsgId.Should().Be("EchoRequest");
    }
}
