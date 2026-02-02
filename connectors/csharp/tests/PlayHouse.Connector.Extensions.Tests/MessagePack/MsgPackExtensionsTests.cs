using FluentAssertions;
using MessagePack;
using PlayHouse.Connector.Extensions.MessagePack;
using PlayHouse.Connector.Protocol;
using Xunit;

namespace PlayHouse.Connector.Extensions.Tests.MessagePack;

public class MsgPackExtensionsTests
{
    [MessagePackObject]
    public class TestMessage
    {
        [Key(0)]
        public string Name { get; set; } = string.Empty;

        [Key(1)]
        public int Value { get; set; }
    }

    [Fact]
    public void Of_CreatesPacketWithCorrectMsgId()
    {
        // Arrange
        var message = new TestMessage { Name = "Test", Value = 42 };

        // Act
        var packet = MsgPackConnectorExtensions.Of(message);

        // Assert
        packet.MsgId.Should().Be("TestMessage");
    }

    [Fact]
    public void Parse_DeserializesMessagePackPayload()
    {
        // Arrange
        var original = new TestMessage { Name = "Test", Value = 42 };
        var packet = MsgPackConnectorExtensions.Of(original);

        // Act
        var deserialized = packet.Parse<TestMessage>();

        // Assert
        deserialized.Should().NotBeNull();
        deserialized.Name.Should().Be(original.Name);
        deserialized.Value.Should().Be(original.Value);
    }

    [Fact]
    public void TryParse_SucceedsWithValidPayload()
    {
        // Arrange
        var original = new TestMessage { Name = "Test", Value = 42 };
        var packet = MsgPackConnectorExtensions.Of(original);

        // Act
        var success = packet.TryParse<TestMessage>(out var deserialized);

        // Assert
        success.Should().BeTrue();
        deserialized.Should().NotBeNull();
        deserialized!.Name.Should().Be(original.Name);
        deserialized.Value.Should().Be(original.Value);
    }

    [Fact]
    public void TryParse_FailsWithEmptyPayload()
    {
        // Arrange
        var packet = Packet.Empty("TestMessage");

        // Act
        var success = packet.TryParse<TestMessage>(out var deserialized);

        // Assert
        success.Should().BeFalse();
        deserialized.Should().BeNull();
    }

    [Fact]
    public void Parse_ThrowsWithEmptyPayload()
    {
        // Arrange
        var packet = Packet.Empty("TestMessage");

        // Act
        var act = () => packet.Parse<TestMessage>();

        // Assert
        act.Should().Throw<Exception>();
    }

    [Fact]
    public void RoundTrip_PreservesData()
    {
        // Arrange
        var original = new TestMessage { Name = "Hello World", Value = 12345 };

        // Act
        var packet = MsgPackConnectorExtensions.Of(original);
        var deserialized = packet.Parse<TestMessage>();

        // Assert
        deserialized.Should().BeEquivalentTo(original);
    }

    [Fact]
    public void Of_CreatesCompactPayload()
    {
        // Arrange
        var message = new TestMessage { Name = "Test", Value = 42 };

        // Act
        using var packet = MsgPackConnectorExtensions.Of(message);

        // Assert - MessagePack is typically more compact than JSON
        packet.MsgId.Should().Be("TestMessage");
        packet.Payload.DataSpan.Length.Should().BeGreaterThan(0);
    }

    [Fact]
    public void Parse_HandlesComplexObjects()
    {
        // Arrange
        var original = new TestMessage
        {
            Name = "Complex Test with Unicode 한글 🎮",
            Value = int.MaxValue
        };

        // Act
        var packet = MsgPackConnectorExtensions.Of(original);
        var deserialized = packet.Parse<TestMessage>();

        // Assert
        deserialized.Name.Should().Be(original.Name);
        deserialized.Value.Should().Be(original.Value);
    }
}
