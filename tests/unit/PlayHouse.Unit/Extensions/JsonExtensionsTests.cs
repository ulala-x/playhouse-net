#nullable enable

using FluentAssertions;
using PlayHouse.Core.Shared;
using PlayHouse.Extensions.Json;
using Xunit;

namespace PlayHouse.Unit.Extensions;

/// <summary>
/// JSON 테스트용 간단한 DTO
/// </summary>
public class TestJsonMessage
{
    public string Content { get; set; } = string.Empty;
    public int Value { get; set; }
}

/// <summary>
/// 복잡한 JSON 메시지 테스트용 DTO
/// </summary>
public class ComplexJsonMessage
{
    public string Name { get; set; } = string.Empty;
    public int Age { get; set; }
    public List<string> Tags { get; set; } = new();
    public NestedData? Data { get; set; }
}

/// <summary>
/// 중첩된 데이터 구조
/// </summary>
public class NestedData
{
    public string Key { get; set; } = string.Empty;
    public double Score { get; set; }
}

/// <summary>
/// PlayHouse.Extensions.Json의 확장 메서드 테스트
/// </summary>
public class JsonExtensionsTests
{
    /// <summary>
    /// JsonCPacketExtensions.Of이 올바른 MsgId를 설정하는지 확인
    /// </summary>
    [Fact]
    public void Of_ShouldSetCorrectMsgId()
    {
        // Arrange
        var message = new TestJsonMessage
        {
            Content = "Hello",
            Value = 42
        };

        // Act
        using var packet = JsonCPacketExtensions.Of(message);

        // Assert
        packet.MsgId.Should().Be("TestJsonMessage", "MsgId는 typeof(T).Name이어야 함");
    }

    /// <summary>
    /// JsonCPacketExtensions.Of이 Payload를 올바르게 JSON 직렬화하는지 확인
    /// </summary>
    [Fact]
    public void Of_ShouldSerializePayloadCorrectly()
    {
        // Arrange
        var message = new TestJsonMessage
        {
            Content = "Test Content",
            Value = 123
        };

        // Act
        using var packet = JsonCPacketExtensions.Of(message);

        // Assert
        var parsed = packet.Parse<TestJsonMessage>();
        parsed.Content.Should().Be("Test Content");
        parsed.Value.Should().Be(123);
    }

    /// <summary>
    /// JsonPacketExtensions.Parse이 정상적으로 파싱하는지 확인
    /// </summary>
    [Fact]
    public void Parse_WithValidData_ShouldDeserialize()
    {
        // Arrange
        var original = new TestJsonMessage
        {
            Content = "Parse Test",
            Value = 999
        };
        using var packet = JsonCPacketExtensions.Of(original);

        // Act
        var parsed = packet.Parse<TestJsonMessage>();

        // Assert
        parsed.Should().NotBeNull();
        parsed.Content.Should().Be("Parse Test");
        parsed.Value.Should().Be(999);
    }

    /// <summary>
    /// JsonPacketExtensions.TryParse이 성공 케이스를 처리하는지 확인
    /// </summary>
    [Fact]
    public void TryParse_WithValidData_ShouldReturnTrue()
    {
        // Arrange
        var message = new TestJsonMessage
        {
            Content = "Success",
            Value = 200
        };
        using var packet = JsonCPacketExtensions.Of(message);

        // Act
        var result = packet.TryParse<TestJsonMessage>(out var parsed);

        // Assert
        result.Should().BeTrue();
        parsed.Should().NotBeNull();
        parsed!.Content.Should().Be("Success");
        parsed.Value.Should().Be(200);
    }

    /// <summary>
    /// JsonPacketExtensions.TryParse이 실패 케이스를 처리하는지 확인
    /// </summary>
    [Fact]
    public void TryParse_WithInvalidData_ShouldReturnFalse()
    {
        // Arrange - 빈 패킷은 역직렬화 실패
        using var packet = CPacket.Empty("InvalidJson");

        // Act
        var result = packet.TryParse<TestJsonMessage>(out var parsed);

        // Assert
        result.Should().BeFalse();
        parsed.Should().BeNull();
    }

    /// <summary>
    /// 복잡한 JSON 구조도 올바르게 직렬화/역직렬화되는지 확인
    /// </summary>
    [Fact]
    public void Of_WithComplexMessage_ShouldPreserveAllFields()
    {
        // Arrange
        var message = new ComplexJsonMessage
        {
            Name = "Alice",
            Age = 30,
            Tags = new List<string> { "developer", "gamer" },
            Data = new NestedData
            {
                Key = "score-key",
                Score = 95.5
            }
        };

        // Act
        using var packet = JsonCPacketExtensions.Of(message);
        var parsed = packet.Parse<ComplexJsonMessage>();

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
        var message = new TestJsonMessage
        {
            Content = "",
            Value = 0
        };

        // Act
        using var packet = JsonCPacketExtensions.Of(message);
        var parsed = packet.Parse<TestJsonMessage>();

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
        var message = new ComplexJsonMessage
        {
            Name = "Bob",
            Age = 25,
            Tags = new List<string>(),
            Data = null
        };

        // Act
        using var packet = JsonCPacketExtensions.Of(message);
        var parsed = packet.Parse<ComplexJsonMessage>();

        // Assert
        parsed.Name.Should().Be("Bob");
        parsed.Age.Should().Be(25);
        parsed.Tags.Should().BeEmpty();
        parsed.Data.Should().BeNull();
    }

    /// <summary>
    /// Parse이 잘못된 JSON에 대해 예외를 던지는지 확인
    /// </summary>
    [Fact]
    public void Parse_WithInvalidJson_ShouldThrowException()
    {
        // Arrange
        var invalidJsonBytes = new byte[] { 0xFF, 0xFE, 0xFD }; // Invalid JSON
        using var packet = CPacket.Of("InvalidJson", invalidJsonBytes);

        // Act
        var act = () => packet.Parse<TestJsonMessage>();

        // Assert
        act.Should().Throw<Exception>(); // JsonException or InvalidOperationException
    }

    /// <summary>
    /// CPacket이 IDisposable을 구현하는지 확인
    /// </summary>
    [Fact]
    public void CPacket_ImplementsIDisposable()
    {
        // Arrange
        var message = new TestJsonMessage { Content = "Test", Value = 1 };
        var packet = JsonCPacketExtensions.Of(message);

        // Act & Assert - using으로 정상적으로 Dispose 가능
        using (packet)
        {
            packet.MsgId.Should().Be("TestJsonMessage");
        }

        // Dispose 후에도 속성 접근은 가능 (내부 Payload는 Dispose됨)
        packet.MsgId.Should().Be("TestJsonMessage");
    }

    /// <summary>
    /// 작은 크기 메시지도 올바르게 처리되는지 확인 (ArrayPool 버퍼 256바이트 이내)
    /// </summary>
    [Fact]
    public void Of_WithSmallMessage_ShouldSerializeCorrectly()
    {
        // Arrange - 256바이트 이내의 작은 메시지
        var message = new ComplexJsonMessage
        {
            Name = "Small Test",
            Age = 25,
            Tags = new List<string> { "tag1", "tag2", "tag3" },
            Data = new NestedData
            {
                Key = "TestKey",
                Score = 95.5
            }
        };

        // Act
        using var packet = JsonCPacketExtensions.Of(message);
        var parsed = packet.Parse<ComplexJsonMessage>();

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
        var original = new TestJsonMessage
        {
            Content = "Round Trip",
            Value = 777
        };

        // Act & Assert - 3번 반복
        using var packet1 = JsonCPacketExtensions.Of(original);
        var parsed1 = packet1.Parse<TestJsonMessage>();
        parsed1.Content.Should().Be("Round Trip");
        parsed1.Value.Should().Be(777);

        using var packet2 = JsonCPacketExtensions.Of(parsed1);
        var parsed2 = packet2.Parse<TestJsonMessage>();
        parsed2.Content.Should().Be("Round Trip");
        parsed2.Value.Should().Be(777);

        using var packet3 = JsonCPacketExtensions.Of(parsed2);
        var parsed3 = packet3.Parse<TestJsonMessage>();
        parsed3.Content.Should().Be("Round Trip");
        parsed3.Value.Should().Be(777);
    }
}
