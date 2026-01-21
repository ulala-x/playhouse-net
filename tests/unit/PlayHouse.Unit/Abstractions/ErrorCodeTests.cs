#nullable enable

using FluentAssertions;
using PlayHouse.Abstractions;
using Xunit;

namespace PlayHouse.Unit.Abstractions;

/// <summary>
/// ErrorCode enum 단위 테스트
/// </summary>
public class ErrorCodeTests
{
    #region 값 검증

    [Theory(DisplayName = "모든 ErrorCode는 올바른 값을 가진다")]
    [InlineData(ErrorCode.Success, 0)]
    [InlineData(ErrorCode.RequestTimeout, 1)]
    [InlineData(ErrorCode.ServerNotFound, 2)]
    [InlineData(ErrorCode.StageNotFound, 3)]
    [InlineData(ErrorCode.ActorNotFound, 4)]
    [InlineData(ErrorCode.AuthenticationFailed, 5)]
    [InlineData(ErrorCode.NotAuthenticated, 6)]
    [InlineData(ErrorCode.AlreadyAuthenticated, 7)]
    [InlineData(ErrorCode.StageAlreadyExists, 8)]
    [InlineData(ErrorCode.StageCreationFailed, 9)]
    [InlineData(ErrorCode.JoinStageFailed, 10)]
    [InlineData(ErrorCode.InvalidMessage, 11)]
    [InlineData(ErrorCode.HandlerNotFound, 12)]
    [InlineData(ErrorCode.InvalidStageType, 13)]
    [InlineData(ErrorCode.SystemError, 14)]
    [InlineData(ErrorCode.UncheckedContentsError, 15)]
    [InlineData(ErrorCode.InvalidAccountId, 16)]
    [InlineData(ErrorCode.JoinStageRejected, 17)]
    [InlineData(ErrorCode.InternalError, 99)]
    [InlineData(ErrorCode.ApplicationBase, 1000)]
    public void ErrorCode_HasCorrectValue(ErrorCode errorCode, ushort expectedValue)
    {
        ((ushort)errorCode).Should().Be(expectedValue);
    }

    #endregion

    #region Description 검증

    [Theory(DisplayName = "모든 ErrorCode는 Description을 가진다")]
    [InlineData(ErrorCode.Success, "Operation completed successfully")]
    [InlineData(ErrorCode.RequestTimeout, "Request timed out waiting for reply")]
    [InlineData(ErrorCode.ServerNotFound, "Target server not found or not connected")]
    [InlineData(ErrorCode.StageNotFound, "Target stage not found")]
    [InlineData(ErrorCode.ActorNotFound, "Target actor not found")]
    [InlineData(ErrorCode.AuthenticationFailed, "Authentication failed")]
    [InlineData(ErrorCode.NotAuthenticated, "Not authenticated - authentication required")]
    [InlineData(ErrorCode.AlreadyAuthenticated, "Already authenticated")]
    [InlineData(ErrorCode.StageAlreadyExists, "Stage already exists")]
    [InlineData(ErrorCode.StageCreationFailed, "Stage creation failed")]
    [InlineData(ErrorCode.JoinStageFailed, "Join stage failed")]
    [InlineData(ErrorCode.InvalidMessage, "Invalid message format or content")]
    [InlineData(ErrorCode.HandlerNotFound, "Handler not found for the message ID")]
    [InlineData(ErrorCode.InvalidStageType, "Invalid stage type")]
    [InlineData(ErrorCode.SystemError, "System error")]
    [InlineData(ErrorCode.UncheckedContentsError, "Unchecked contents error")]
    [InlineData(ErrorCode.InvalidAccountId, "Invalid account ID - not set after authentication")]
    [InlineData(ErrorCode.JoinStageRejected, "Join stage rejected by stage")]
    [InlineData(ErrorCode.InternalError, "Internal server error")]
    [InlineData(ErrorCode.ApplicationBase, "First available code for application use")]
    public void ErrorCode_HasCorrectDescription(ErrorCode errorCode, string expectedDescription)
    {
        errorCode.GetDescription().Should().Be(expectedDescription);
    }

    #endregion

    #region 확장 메서드

    [Fact(DisplayName = "ToUInt16 - ErrorCode를 ushort로 변환한다")]
    public void ToUInt16_ConvertsToUshort()
    {
        ErrorCode.HandlerNotFound.ToUInt16().Should().Be(12);
        ErrorCode.InternalError.ToUInt16().Should().Be(99);
        ErrorCode.ApplicationBase.ToUInt16().Should().Be(1000);
    }

    [Fact(DisplayName = "GetDescription - 정의되지 않은 값은 enum 이름을 반환한다")]
    public void GetDescription_UndefinedValue_ReturnsEnumName()
    {
        var undefinedCode = (ErrorCode)9999;
        undefinedCode.GetDescription().Should().Be("9999");
    }

    #endregion

    #region 범위 검증

    [Fact(DisplayName = "프레임워크 에러 코드는 0-999 범위이다")]
    public void FrameworkErrorCodes_AreInRange()
    {
        var frameworkCodes = new[]
        {
            ErrorCode.Success,
            ErrorCode.RequestTimeout,
            ErrorCode.ServerNotFound,
            ErrorCode.StageNotFound,
            ErrorCode.ActorNotFound,
            ErrorCode.AuthenticationFailed,
            ErrorCode.NotAuthenticated,
            ErrorCode.AlreadyAuthenticated,
            ErrorCode.StageAlreadyExists,
            ErrorCode.StageCreationFailed,
            ErrorCode.JoinStageFailed,
            ErrorCode.InvalidMessage,
            ErrorCode.HandlerNotFound,
            ErrorCode.InvalidStageType,
            ErrorCode.SystemError,
            ErrorCode.UncheckedContentsError,
            ErrorCode.InvalidAccountId,
            ErrorCode.JoinStageRejected,
            ErrorCode.InternalError,
        };

        foreach (var code in frameworkCodes)
        {
            ((ushort)code).Should().BeLessThan(1000, $"{code} should be a framework error code");
        }
    }

    [Fact(DisplayName = "ApplicationBase는 애플리케이션 에러 코드의 시작점이다")]
    public void ApplicationBase_IsStartOfApplicationCodes()
    {
        ((ushort)ErrorCode.ApplicationBase).Should().Be(1000);
    }

    #endregion
}

/// <summary>
/// PlayException 단위 테스트
/// </summary>
public class PlayExceptionTests
{
    [Fact(DisplayName = "생성자 - ErrorCode를 설정한다")]
    public void Constructor_SetsErrorCode()
    {
        var exception = new PlayException(ErrorCode.HandlerNotFound);

        exception.ErrorCode.Should().Be(ErrorCode.HandlerNotFound);
    }

    [Fact(DisplayName = "생성자 - 메시지에 ErrorCode와 Description을 포함한다")]
    public void Constructor_IncludesErrorCodeAndDescriptionInMessage()
    {
        var exception = new PlayException(ErrorCode.HandlerNotFound);

        exception.Message.Should().Contain("HandlerNotFound");
        exception.Message.Should().Contain("12");
        exception.Message.Should().Contain("Handler not found for the message ID");
    }

    [Fact(DisplayName = "생성자 - InnerException을 설정한다")]
    public void Constructor_WithInnerException_SetsInnerException()
    {
        var inner = new InvalidOperationException("inner error");
        var exception = new PlayException(ErrorCode.SystemError, inner);

        exception.InnerException.Should().BeSameAs(inner);
        exception.ErrorCode.Should().Be(ErrorCode.SystemError);
    }

    [Theory(DisplayName = "메시지 포맷 - [ErrorCode(값)] Description 형식이다")]
    [InlineData(ErrorCode.Success, "[Success(0)] Operation completed successfully")]
    [InlineData(ErrorCode.RequestTimeout, "[RequestTimeout(1)] Request timed out waiting for reply")]
    [InlineData(ErrorCode.InternalError, "[InternalError(99)] Internal server error")]
    public void Message_HasCorrectFormat(ErrorCode errorCode, string expectedMessage)
    {
        var exception = new PlayException(errorCode);

        exception.Message.Should().Be(expectedMessage);
    }

    [Fact(DisplayName = "PlayException은 Exception을 상속한다")]
    public void PlayException_InheritsFromException()
    {
        var exception = new PlayException(ErrorCode.SystemError);

        exception.Should().BeAssignableTo<Exception>();
    }
}
