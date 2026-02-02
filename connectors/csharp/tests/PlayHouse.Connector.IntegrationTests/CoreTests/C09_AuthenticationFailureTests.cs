using FluentAssertions;
using PlayHouse.Connector.Protocol;
using PlayHouse.TestServer.Proto;
using Xunit;

namespace PlayHouse.Connector.IntegrationTests.CoreTests;

/// <summary>
/// C-09: 인증 실패 테스트
/// </summary>
/// <remarks>
/// 잘못된 토큰이나 유효하지 않은 인증 정보로 인증이 실패하는 경우를 검증합니다.
/// 인증 실패 시 서버는 에러 코드(AuthenticationFailed=5)를 반환하고,
/// 커넥터는 ConnectorException을 throw합니다.
/// </remarks>
public class C09_AuthenticationFailureTests : BaseIntegrationTest
{
    // 서버에서 정의된 AuthenticationFailed 에러 코드
    private const ushort AuthenticationFailedErrorCode = 5;

    public C09_AuthenticationFailureTests(TestServerFixture testServer) : base(testServer)
    {
    }

    [Fact(DisplayName = "C-09-01: 잘못된 토큰으로 인증하면 실패한다")]
    public async Task Authenticate_WithInvalidToken_Fails()
    {
        // Given: 연결된 상태
        await CreateStageAndConnectAsync();

        var authRequest = new AuthenticateRequest
        {
            UserId = "testUser",
            Token = "invalid_token" // 잘못된 토큰
        };

        // When: 잘못된 토큰으로 인증 시도
        using var requestPacket = new Packet(authRequest);
        var action = async () => await Connector!.AuthenticateAsync(requestPacket);

        // Then: 인증 실패 예외가 발생해야 함
        var exception = await action.Should().ThrowAsync<ConnectorException>();
        exception.Which.ErrorCode.Should().Be(AuthenticationFailedErrorCode, "인증 실패 에러 코드가 반환되어야 함");

        // IsAuthenticated도 false여야 함
        Connector!.IsAuthenticated().Should().BeFalse("인증 실패 시 IsAuthenticated는 false여야 함");
    }

    [Fact(DisplayName = "C-09-02: 빈 UserId로 인증하면 실패한다")]
    public async Task Authenticate_WithEmptyUserId_Fails()
    {
        // Given: 연결된 상태
        await CreateStageAndConnectAsync();

        var authRequest = new AuthenticateRequest
        {
            UserId = "", // 빈 UserId
            Token = "valid_token"
        };

        // When: 빈 UserId로 인증 시도
        using var requestPacket = new Packet(authRequest);
        var action = async () => await Connector!.AuthenticateAsync(requestPacket);

        // Then: 인증 실패 예외가 발생해야 함
        var exception = await action.Should().ThrowAsync<ConnectorException>();
        exception.Which.ErrorCode.Should().Be(AuthenticationFailedErrorCode, "인증 실패 에러 코드가 반환되어야 함");
        Connector!.IsAuthenticated().Should().BeFalse();
    }

    [Fact(DisplayName = "C-09-03: 빈 토큰으로 인증하면 실패한다")]
    public async Task Authenticate_WithEmptyToken_Fails()
    {
        // Given: 연결된 상태
        await CreateStageAndConnectAsync();

        var authRequest = new AuthenticateRequest
        {
            UserId = "testUser",
            Token = "" // 빈 토큰
        };

        // When: 빈 토큰으로 인증 시도
        using var requestPacket = new Packet(authRequest);
        var action = async () => await Connector!.AuthenticateAsync(requestPacket);

        // Then: 인증 실패 예외가 발생해야 함
        var exception = await action.Should().ThrowAsync<ConnectorException>();
        exception.Which.ErrorCode.Should().Be(AuthenticationFailedErrorCode, "인증 실패 에러 코드가 반환되어야 함");
        Connector!.IsAuthenticated().Should().BeFalse();
    }

    [Fact(DisplayName = "C-09-04: 인증 실패 후에도 연결은 유지된다")]
    public async Task Connection_AfterAuthenticationFailure_RemainsConnected()
    {
        // Given: 연결된 상태
        await CreateStageAndConnectAsync();

        Connector!.IsConnected().Should().BeTrue("초기 연결 상태");

        // When: 잘못된 토큰으로 인증 실패
        var authRequest = new AuthenticateRequest
        {
            UserId = "testUser",
            Token = "invalid_token"
        };
        using var requestPacket = new Packet(authRequest);

        try
        {
            await Connector.AuthenticateAsync(requestPacket);
        }
        catch (ConnectorException ex)
        {
            // 예상된 예외 - 인증 실패
            ex.ErrorCode.Should().Be(AuthenticationFailedErrorCode);
        }

        // Then: 인증은 실패했지만 연결은 유지되어야 함
        Connector.IsConnected().Should().BeTrue("인증 실패 후에도 연결은 유지되어야 함");
        Connector.IsAuthenticated().Should().BeFalse("인증은 실패 상태");
    }

    [Fact(DisplayName = "C-09-05: 인증 실패 후 재시도할 수 있다")]
    public async Task Authenticate_AfterFailure_CanRetry()
    {
        // Given: 연결된 상태
        await CreateStageAndConnectAsync();

        // 첫 번째 인증 시도 (실패)
        var failRequest = new AuthenticateRequest
        {
            UserId = "testUser",
            Token = "invalid_token"
        };
        using var failPacket = new Packet(failRequest);

        try
        {
            await Connector!.AuthenticateAsync(failPacket);
            true.Should().BeFalse("첫 번째 인증은 예외가 발생해야 함");
        }
        catch (ConnectorException ex)
        {
            ex.ErrorCode.Should().Be(AuthenticationFailedErrorCode, "첫 번째 인증은 실패해야 함");
        }

        // When: 두 번째 인증 시도 (성공)
        var successRequest = new AuthenticateRequest
        {
            UserId = "testUser",
            Token = "valid_token"
        };
        using var successPacket = new Packet(successRequest);
        var successReply = await Connector!.AuthenticateAsync(successPacket);
        var successResult = ParsePayload<AuthenticateReply>(successReply.Payload);

        // Then: 두 번째 인증은 성공해야 함
        successResult.Success.Should().BeTrue("재시도한 인증은 성공해야 함");
        Connector.IsAuthenticated().Should().BeTrue("인증 상태가 true여야 함");
    }

    [Fact(DisplayName = "C-09-06: 인증 실패 시 예외에 에러 정보가 포함된다")]
    public async Task AuthenticationFailure_Exception_ContainsErrorInfo()
    {
        // Given: 연결된 상태
        await CreateStageAndConnectAsync();

        var authRequest = new AuthenticateRequest
        {
            UserId = "failUser",
            Token = "invalid_token"
        };

        // When: 인증 실패
        using var requestPacket = new Packet(authRequest);
        var action = async () => await Connector!.AuthenticateAsync(requestPacket);

        // Then: 예외에 에러 정보가 포함되어야 함
        var exception = await action.Should().ThrowAsync<ConnectorException>();
        exception.Which.ErrorCode.Should().Be(AuthenticationFailedErrorCode, "에러 코드가 AuthenticationFailed여야 함");
        exception.Which.MsgSeq.Should().BeGreaterThan((ushort)0, "메시지 시퀀스가 있어야 함");
    }

    [Fact(DisplayName = "C-09-07: 인증 없이 메시지를 보낼 수 없다")]
    public async Task SendMessage_WithoutAuthentication_Fails()
    {
        // Given: 연결만 된 상태 (인증 안 함)
        await CreateStageAndConnectAsync();

        Connector!.IsConnected().Should().BeTrue("연결은 되어 있음");
        Connector.IsAuthenticated().Should().BeFalse("인증은 안 되어 있음");

        // When: 인증 없이 Echo 요청 시도
        var echoRequest = new EchoRequest { Content = "Test", Sequence = 1 };
        using var requestPacket = new Packet(echoRequest);

        var action = async () => await Connector.RequestAsync(requestPacket);

        // Then: 예외가 발생하거나 에러 응답을 받아야 함
        // (서버 구현에 따라 다를 수 있으므로 예외 발생을 확인)
        var exception = await Record.ExceptionAsync(action);
        exception.Should().NotBeNull("인증 없이 메시지를 보내면 에러가 발생해야 함");
    }

    [Fact(DisplayName = "C-09-08: 연결 없이 인증 시도하면 예외가 발생한다")]
    public async Task Authenticate_WithoutConnection_ThrowsException()
    {
        // Given: 연결되지 않은 상태
        Connector!.IsConnected().Should().BeFalse("초기 상태는 연결 안 됨");

        var authRequest = new AuthenticateRequest
        {
            UserId = "testUser",
            Token = "valid_token"
        };

        // When: 연결 없이 인증 시도
        using var requestPacket = new Packet(authRequest);
        var action = async () => await Connector.AuthenticateAsync(requestPacket);

        // Then: ConnectorException이 발생해야 함
        await action.Should().ThrowAsync<ConnectorException>()
            .Where(ex => ex.ErrorCode == (ushort)ConnectorErrorCode.Disconnected,
                "연결 없이 인증하면 예외가 발생해야 함");
    }

    [Fact(DisplayName = "C-09-09: 여러 번 인증 실패해도 연결은 유지된다")]
    public async Task MultipleAuthenticationFailures_ConnectionRemains()
    {
        // Given: 연결된 상태
        await CreateStageAndConnectAsync();

        // When: 3번 연속 인증 실패
        for (int i = 1; i <= 3; i++)
        {
            var authRequest = new AuthenticateRequest
            {
                UserId = $"failUser{i}",
                Token = "invalid_token"
            };

            using var requestPacket = new Packet(authRequest);
            try
            {
                await Connector!.AuthenticateAsync(requestPacket);
                true.Should().BeFalse($"{i}번째 인증은 예외가 발생해야 함");
            }
            catch (ConnectorException ex)
            {
                ex.ErrorCode.Should().Be(AuthenticationFailedErrorCode, $"{i}번째 인증이 실패해야 함");
            }
        }

        // Then: 여러 번 실패해도 연결은 유지되어야 함
        Connector!.IsConnected().Should().BeTrue("여러 번 인증 실패 후에도 연결은 유지되어야 함");
        Connector.IsAuthenticated().Should().BeFalse("인증은 여전히 실패 상태");
    }
}
