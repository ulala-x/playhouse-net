using FluentAssertions;
using Google.Protobuf;
using PlayHouse.Connector.Extensions.Proto;
using PlayHouse.Connector.Protocol;
using PlayHouse.TestServer.Proto;
using Xunit;

namespace PlayHouse.Connector.Extensions.Tests.Proto;

public class ProtoExtensionsTests
{
    [Fact]
    public void Of_CreatesPacketWithCorrectMsgId()
    {
        // Arrange
        var message = new EchoRequest { Content ="Test" };

        // Act
        var packet = ProtoConnectorExtensions.Of(message);

        // Assert
        packet.MsgId.Should().Be("EchoRequest");
    }

    [Fact]
    public void Parse_DeserializesProtobufPayload()
    {
        // Arrange
        var original = new EchoRequest { Content ="Test" };
        var packet = ProtoConnectorExtensions.Of(original);

        // Act
        var deserialized = packet.Parse<EchoRequest>();

        // Assert
        deserialized.Should().NotBeNull();
        deserialized.Content.Should().Be(original.Content);
    }

    [Fact]
    public void TryParse_SucceedsWithValidPayload()
    {
        // Arrange
        var original = new EchoRequest { Content ="Test" };
        var packet = ProtoConnectorExtensions.Of(original);

        // Act
        var success = packet.TryParse<EchoRequest>(out var deserialized);

        // Assert
        success.Should().BeTrue();
        deserialized.Should().NotBeNull();
        deserialized!.Content.Should().Be(original.Content);
    }

    [Fact]
    public void TryParse_FailsWithEmptyPayload()
    {
        // Arrange
        var packet = Packet.Empty("EchoRequest");

        // Act
        var success = packet.TryParse<EchoRequest>(out var deserialized);

        // Assert
        success.Should().BeFalse();
        deserialized.Should().BeNull();
    }

    [Fact]
    public void Parse_ThrowsWithEmptyPayload()
    {
        // Arrange
        var packet = Packet.Empty("EchoRequest");

        // Act
        var act = () => packet.Parse<EchoRequest>();

        // Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*empty payload*");
    }

    [Fact]
    public void RoundTrip_PreservesData()
    {
        // Arrange
        var original = new EchoRequest { Content ="Hello World" };

        // Act
        var packet = ProtoConnectorExtensions.Of(original);
        var deserialized = packet.Parse<EchoRequest>();

        // Assert
        deserialized.Should().BeEquivalentTo(original);
    }

    [Fact]
    public void Parse_HandlesComplexProtobufMessage()
    {
        // Arrange
        var original = new CreateStagePayload
        {
            StageName = "TestStage",
            MaxPlayers = 10
        };

        // Act
        var packet = ProtoConnectorExtensions.Of(original);
        var deserialized = packet.Parse<CreateStagePayload>();

        // Assert
        deserialized.StageName.Should().Be(original.StageName);
        deserialized.MaxPlayers.Should().Be(original.MaxPlayers);
    }

    [Fact]
    public void Parse_FromBytePayload()
    {
        // Arrange
        var message = new EchoRequest { Content ="Test" };
        var bytes = message.ToByteArray();
        var packet = new Packet("EchoRequest", bytes);

        // Act
        var deserialized = packet.Parse<EchoRequest>();

        // Assert
        deserialized.Content.Should().Be(message.Content);
    }

    [Fact]
    public void Parse_FromProtoPayload()
    {
        // Arrange
        var message = new EchoRequest { Content ="Test" };
        var packet = new Packet(message);

        // Act
        var deserialized = packet.Parse<EchoRequest>();

        // Assert
        deserialized.Content.Should().Be(message.Content);
    }

    [Fact]
    public void Of_ThrowsWithNullMessage()
    {
        // Act
        var act = () => ProtoConnectorExtensions.Of<EchoRequest>(null!);

        // Assert
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Parse_HandlesUnicodeAndSpecialCharacters()
    {
        // Arrange
        var original = new EchoRequest
        {
            Content ="Hello 世界 🌍 مرحبا мир"
        };

        // Act
        var packet = ProtoConnectorExtensions.Of(original);
        var deserialized = packet.Parse<EchoRequest>();

        // Assert
        deserialized.Content.Should().Be(original.Content);
    }
}
