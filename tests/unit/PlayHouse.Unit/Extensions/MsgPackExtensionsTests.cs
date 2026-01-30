#nullable enable

using FluentAssertions;
using MessagePack;
using PlayHouse.Core.Shared;
using PlayHouse.Extensions.MessagePack;
using Xunit;

namespace PlayHouse.Unit.Extensions;

/// <summary>
/// MessagePack 테스트용 간단한 DTO
/// </summary>
[MessagePackObject]
public class TestMsgPackMessage
{
    [Key(0)]
    public string Content { get; set; } = string.Empty;
    [Key(1)]
    public int Value { get; set; }
}

/// <summary>
/// 복잡한 MessagePack 메시지 테스트용 DTO
/// </summary>
[MessagePackObject]
public class ComplexMsgPackMessage
{
    [Key(0)]
    public string Name { get; set; } = string.Empty;
    [Key(1)]
    public int Age { get; set; }
    [Key(2)]
    public List<string> Tags { get; set; } = new();
    [Key(3)]
    public NestedMsgPackData? Data { get; set; }
}

/// <summary>
/// 중첩된 데이터 구조
/// </summary>
[MessagePackObject]
public class NestedMsgPackData
{
    [Key(0)]
    public string Key { get; set; } = string.Empty;
    [Key(1)]
    public double Score { get; set; }
}

/// <summary>
/// PlayHouse.Extensions.MessagePack의 확장 메서드 테스트
/// </summary>
public class MsgPackExtensionsTests
{
    /// <summary>
    /// MsgPackCPacketExtensions.Of이 올바른 MsgId를 설정하는지 확인
    /// </summary>
    [Fact]
    public void Of_ShouldSetCorrectMsgId()
    {
        // Arrange
        var message = new TestMsgPackMessage
        {
            Content = "Hello",
            Value = 42
        };

        // Act
        using var packet = MsgPackCPacketExtensions.Of(message);

        // Assert
        packet.MsgId.Should().Be("TestMsgPackMessage", "MsgId는 typeof(T).Name이어야 함");
    }

    /// <summary>
    /// MsgPackCPacketExtensions.Of이 Payload를 올바르게 MessagePack 직렬화하는지 확인
    /// </summary>
    [Fact]
    public void Of_ShouldSerializePayloadCorrectly()
    {
        // Arrange
        var message = new TestMsgPackMessage
        {
            Content = "Test Content",
            Value = 123
        };

        // Act
        using var packet = MsgPackCPacketExtensions.Of(message);

        // Assert
        var parsed = packet.Parse<TestMsgPackMessage>();
        parsed.Content.Should().Be("Test Content");
        parsed.Value.Should().Be(123);
    }

    /// <summary>
    /// MsgPackPacketExtensions.Parse이 정상적으로 파싱하는지 확인
    /// </summary>
    [Fact]
    public void Parse_WithValidData_ShouldDeserialize()
    {
        // Arrange
        var original = new TestMsgPackMessage
        {
            Content = "Parse Test",
            Value = 999
        };
        using var packet = MsgPackCPacketExtensions.Of(original);

        // Act
        var parsed = packet.Parse<TestMsgPackMessage>();

        // Assert
        parsed.Should().NotBeNull();
        parsed.Content.Should().Be("Parse Test");
        parsed.Value.Should().Be(999);
    }

    /// <summary>
    /// MsgPackPacketExtensions.TryParse이 성공 케이스를 처리하는지 확인
    /// </summary>
    [Fact]
    public void TryParse_WithValidData_ShouldReturnTrue()
    {
        // Arrange
        var message = new TestMsgPackMessage
        {
            Content = "Success",
            Value = 200
        };
        using var packet = MsgPackCPacketExtensions.Of(message);

        // Act
        var result = packet.TryParse<TestMsgPackMessage>(out var parsed);

        // Assert
        result.Should().BeTrue();
        parsed.Should().NotBeNull();
        parsed!.Content.Should().Be("Success");
        parsed.Value.Should().Be(200);
    }

    /// <summary>
    /// MsgPackPacketExtensions.TryParse이 실패 케이스를 처리하는지 확인
    /// </summary>
    [Fact]
    public void TryParse_WithInvalidData_ShouldReturnFalse()
    {
        // Arrange - 빈 패킷은 역직렬화 실패
        using var packet = CPacket.Empty("InvalidMsgPack");

        // Act
        var result = packet.TryParse<TestMsgPackMessage>(out var parsed);

        // Assert
        result.Should().BeFalse();
        parsed.Should().BeNull();
    }

    /// <summary>
    /// 복잡한 MessagePack 구조도 올바르게 직렬화/역직렬화되는지 확인
    /// </summary>
    [Fact]
    public void Of_WithComplexMessage_ShouldPreserveAllFields()
    {
        // Arrange
        var message = new ComplexMsgPackMessage
        {
            Name = "Alice",
            Age = 30,
            Tags = new List<string> { "developer", "gamer" },
            Data = new NestedMsgPackData
            {
                Key = "score-key",
                Score = 95.5
            }
        };

        // Act
        using var packet = MsgPackCPacketExtensions.Of(message);
        var parsed = packet.Parse<ComplexMsgPackMessage>();

        // Assert
        parsed.Name.Should().Be("Alice");
        parsed.Age.Should().Be(30);
        parsed.Tags.Should().HaveCount(2);
        parsed.Tags.Should().Contain(new[] { "developer", "gamer" });
        parsed.Data.Should().NotBeNull();
        parsed.Data!.Key.Should().Be("score-key");
        parsed.Data.Score.Should().Be(95.5);
    }

    /// <summary>
    /// 빈 문자열 Content도 올바르게 처리되는지 확인
    /// </summary>
    [Fact]
    public void Of_WithEmptyString_ShouldPreserveEmptyString()
    {
        // Arrange
        var message = new TestMsgPackMessage
        {
            Content = "",
            Value = 0
        };

        // Act
        using var packet = MsgPackCPacketExtensions.Of(message);
        var parsed = packet.Parse<TestMsgPackMessage>();

        // Assert
        parsed.Content.Should().BeEmpty();
        parsed.Value.Should().Be(0);
    }

    /// <summary>
    /// null 값도 올바르게 처리되는지 확인
    /// </summary>
    [Fact]
    public void Of_WithNullNestedData_ShouldPreserveNull()
    {
        // Arrange
        var message = new ComplexMsgPackMessage
        {
            Name = "Bob",
            Age = 25,
            Tags = new List<string>(),
            Data = null
        };

        // Act
        using var packet = MsgPackCPacketExtensions.Of(message);
        var parsed = packet.Parse<ComplexMsgPackMessage>();

        // Assert
        parsed.Name.Should().Be("Bob");
        parsed.Age.Should().Be(25);
        parsed.Tags.Should().BeEmpty();
        parsed.Data.Should().BeNull();
    }

    /// <summary>
    /// Parse이 잘못된 MessagePack에 대해 예외를 던지는지 확인
    /// </summary>
    [Fact]
    public void Parse_WithInvalidMsgPack_ShouldThrowException()
    {
        // Arrange
        var invalidMsgPackBytes = new byte[] { 0xFF, 0xFE, 0xFD }; // Invalid MessagePack
        using var packet = CPacket.Of("InvalidMsgPack", invalidMsgPackBytes);

        // Act
        var act = () => packet.Parse<TestMsgPackMessage>();

        // Assert
        act.Should().Throw<Exception>(); // MessagePackSerializationException or InvalidOperationException
    }

    /// <summary>
    /// CPacket이 IDisposable을 구현하는지 확인
    /// </summary>
    [Fact]
    public void CPacket_ImplementsIDisposable()
    {
        // Arrange
        var message = new TestMsgPackMessage { Content = "Test", Value = 1 };
        var packet = MsgPackCPacketExtensions.Of(message);

        // Act & Assert - using으로 정상적으로 Dispose 가능
        using (packet)
        {
            packet.MsgId.Should().Be("TestMsgPackMessage");
        }

        // Dispose 후에도 속성 접근은 가능 (내부 Payload는 Dispose됨)
        packet.MsgId.Should().Be("TestMsgPackMessage");
    }

    /// <summary>
    /// 작은 크기 메시지도 올바르게 처리되는지 확인 (ArrayPool 버퍼 256바이트 이내)
    /// </summary>
    [Fact]
    public void Of_WithSmallMessage_ShouldSerializeCorrectly()
    {
        // Arrange - 256바이트 이내의 작은 메시지
        var message = new ComplexMsgPackMessage
        {
            Name = "Small Test",
            Age = 25,
            Tags = new List<string> { "tag1", "tag2", "tag3" },
            Data = new NestedMsgPackData
            {
                Key = "TestKey",
                Score = 95.5
            }
        };

        // Act
        using var packet = MsgPackCPacketExtensions.Of(message);
        var parsed = packet.Parse<ComplexMsgPackMessage>();

        // Assert
        parsed.Name.Should().Be("Small Test");
        parsed.Age.Should().Be(25);
        parsed.Tags.Should().HaveCount(3);
        parsed.Tags[0].Should().Be("tag1");
        parsed.Data!.Key.Should().Be("TestKey");
        parsed.Data.Score.Should().Be(95.5);
    }

    /// <summary>
    /// 여러 번 직렬화/역직렬화해도 데이터가 보존되는지 확인
    /// </summary>
    [Fact]
    public void Of_MultipleRoundTrips_ShouldPreserveData()
    {
        // Arrange
        var original = new TestMsgPackMessage
        {
            Content = "Round Trip",
            Value = 777
        };

        // Act & Assert - 3번 반복
        using var packet1 = MsgPackCPacketExtensions.Of(original);
        var parsed1 = packet1.Parse<TestMsgPackMessage>();
        parsed1.Content.Should().Be("Round Trip");
        parsed1.Value.Should().Be(777);

        using var packet2 = MsgPackCPacketExtensions.Of(parsed1);
        var parsed2 = packet2.Parse<TestMsgPackMessage>();
        parsed2.Content.Should().Be("Round Trip");
        parsed2.Value.Should().Be(777);

        using var packet3 = MsgPackCPacketExtensions.Of(parsed2);
        var parsed3 = packet3.Parse<TestMsgPackMessage>();
        parsed3.Content.Should().Be("Round Trip");
        parsed3.Value.Should().Be(777);
    }

    /// <summary>
    /// MessagePack이 JSON보다 작은 크기로 직렬화되는지 확인
    /// </summary>
    [Fact]
    public void Of_MessagePackShouldBeSmallerThanJson()
    {
        // Arrange
        var message = new TestMsgPackMessage
        {
            Content = "Size Comparison Test",
            Value = 12345
        };

        // Act
        using var msgPackPacket = MsgPackCPacketExtensions.Of(message);
        using var jsonPacket = PlayHouse.Extensions.Json.JsonCPacketExtensions.Of(message);

        // Assert - MessagePack이 JSON보다 작거나 같아야 함 (일반적으로 더 작음)
        msgPackPacket.Payload.DataSpan.Length.Should().BeLessThanOrEqualTo(jsonPacket.Payload.DataSpan.Length);
    }
}
