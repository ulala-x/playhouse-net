#nullable enable

using FluentAssertions;
using PlayHouse.Connector;
using PlayHouse.Connector.Protocol;
using Xunit;

namespace PlayHouse.Tests.Unit.Connector;

/// <summary>
/// 단위 테스트: ConnectorException의 속성 및 메시지 검증
/// </summary>
public class ConnectorExceptionTests
{
    [Fact(DisplayName = "생성자 - 모든 속성이 설정된다")]
    public void Constructor_SetsAllProperties()
    {
        // Given (전제조건)
        const long stageId = 12345L;
        const ushort errorCode = (ushort)ConnectorErrorCode.RequestTimeout;
        using var request = Packet.Empty("Test.Request");
        const ushort msgSeq = 100;

        // When (행동)
        var exception = new ConnectorException(stageId, errorCode, request, msgSeq);

        // Then (결과)
        exception.StageId.Should().Be(stageId, "StageId가 설정되어야 함");
        exception.ErrorCode.Should().Be(errorCode, "ErrorCode가 설정되어야 함");
        exception.Request.Should().BeSameAs(request, "Request가 설정되어야 함");
        exception.MsgSeq.Should().Be(msgSeq, "MsgSeq가 설정되어야 함");
    }

    [Fact(DisplayName = "Message - 의미 있는 에러 메시지를 반환한다")]
    public void Message_ReturnsDescriptiveMessage()
    {
        // Given (전제조건)
        using var request = Packet.Empty("Game.Action");
        var exception = new ConnectorException(
            stageId: 1,
            errorCode: (ushort)ConnectorErrorCode.Disconnected,
            request: request,
            msgSeq: 50);

        // When (행동)
        var message = exception.Message;

        // Then (결과)
        message.Should().Contain("60201", "에러 코드가 포함되어야 함");
        message.Should().Contain("Game.Action", "메시지 ID가 포함되어야 함");
    }

    [Fact(DisplayName = "ConnectorErrorCode.Disconnected - 올바른 값")]
    public void ConnectorErrorCode_Disconnected_HasCorrectValue()
    {
        // Given (전제조건)
        // When (행동)
        var errorCode = ConnectorErrorCode.Disconnected;

        // Then (결과)
        ((ushort)errorCode).Should().Be(60201, "Disconnected 에러 코드는 60201");
    }

    [Fact(DisplayName = "ConnectorErrorCode.RequestTimeout - 올바른 값")]
    public void ConnectorErrorCode_RequestTimeout_HasCorrectValue()
    {
        // Given (전제조건)
        // When (행동)
        var errorCode = ConnectorErrorCode.RequestTimeout;

        // Then (결과)
        ((ushort)errorCode).Should().Be(60202, "RequestTimeout 에러 코드는 60202");
    }

    [Fact(DisplayName = "ConnectorErrorCode.Unauthenticated - 올바른 값")]
    public void ConnectorErrorCode_Unauthenticated_HasCorrectValue()
    {
        // Given (전제조건)
        // When (행동)
        var errorCode = ConnectorErrorCode.Unauthenticated;

        // Then (결과)
        ((ushort)errorCode).Should().Be(60203, "Unauthenticated 에러 코드는 60203");
    }

    [Fact(DisplayName = "Exception은 상속 체인을 따른다")]
    public void Exception_InheritsFromException()
    {
        // Given (전제조건)
        using var request = Packet.Empty("Test");
        var exception = new ConnectorException(0, 0, request, 0);

        // When (행동)
        var isException = exception is Exception;

        // Then (결과)
        isException.Should().BeTrue("ConnectorException은 Exception을 상속해야 함");
    }

    [Fact(DisplayName = "StageId가 0인 경우 - Stage 없는 요청")]
    public void StageId_Zero_IndicatesNoStage()
    {
        // Given (전제조건)
        using var request = Packet.Empty("Auth.Login");
        var exception = new ConnectorException(0, (ushort)ConnectorErrorCode.RequestTimeout, request, 1);

        // When (행동)
        var stageId = exception.StageId;

        // Then (결과)
        stageId.Should().Be(0, "StageId 0은 Stage 없는 요청을 의미");
    }

    [Fact(DisplayName = "MsgSeq가 0인 경우 - Push 메시지")]
    public void MsgSeq_Zero_IndicatesPushMessage()
    {
        // Given (전제조건)
        using var request = Packet.Empty("System.Push");
        var exception = new ConnectorException(1, 500, request, 0);

        // When (행동)
        var msgSeq = exception.MsgSeq;

        // Then (결과)
        msgSeq.Should().Be(0, "MsgSeq 0은 Push 메시지를 의미");
    }
}
