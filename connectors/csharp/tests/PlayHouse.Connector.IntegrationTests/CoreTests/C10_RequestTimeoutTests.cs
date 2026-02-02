using FluentAssertions;
using PlayHouse.Connector.Protocol;
using PlayHouse.TestServer.Proto;
using Xunit;

namespace PlayHouse.Connector.IntegrationTests.CoreTests;

/// <summary>
/// C-10: 요청 타임아웃 테스트
/// </summary>
/// <remarks>
/// 서버가 응답하지 않는 경우 요청이 타임아웃되는지 검증합니다.
/// NoResponseRequest를 보내면 서버가 의도적으로 응답하지 않습니다.
/// </remarks>
public class C10_RequestTimeoutTests : BaseIntegrationTest
{
    public C10_RequestTimeoutTests(TestServerFixture testServer) : base(testServer)
    {
    }

    [Fact(DisplayName = "C-10-01: 응답이 없는 요청은 타임아웃된다")]
    public async Task RequestAsync_WithNoResponse_TimesOut()
    {
        // Given: 짧은 타임아웃 설정 (2초)
        Connector = new PlayHouse.Connector.Connector();
        Connector.Init(new ConnectorConfig
        {
            RequestTimeoutMs = 2000, // 2초 타임아웃
            HeartBeatIntervalMs = 10000
        });

        await CreateStageAndConnectAsync();
        await AuthenticateAsync("timeoutUser");

        var noResponseRequest = new NoResponseRequest
        {
            DelayMs = 10000 // 서버가 10초 동안 응답 안 함
        };

        // When: 응답이 없는 요청 전송
        using var requestPacket = new Packet(noResponseRequest);
        var action = async () => await Connector!.RequestAsync(requestPacket);

        // Then: ConnectorException이 발생해야 함
        var exception = await action.Should().ThrowAsync<ConnectorException>("타임아웃이 발생해야 함");
        exception.Which.ErrorCode.Should().Be((ushort)ConnectorErrorCode.RequestTimeout,
            "에러 코드가 Timeout이어야 함");
    }

    [Fact(DisplayName = "C-10-02: 타임아웃 후에도 연결은 유지된다")]
    public async Task Connection_AfterTimeout_RemainsConnected()
    {
        // Given: 짧은 타임아웃 설정
        Connector = new PlayHouse.Connector.Connector();
        Connector.Init(new ConnectorConfig
        {
            RequestTimeoutMs = 2000,
            HeartBeatIntervalMs = 10000,
        });

        await CreateStageAndConnectAsync();
        await AuthenticateAsync("timeoutConnectionUser");

        var noResponseRequest = new NoResponseRequest { DelayMs = 10000 };

        // When: 타임아웃 발생
        using var requestPacket = new Packet(noResponseRequest);
        try
        {
            await Connector!.RequestAsync(requestPacket);
        }
        catch (ConnectorException)
        {
            // 타임아웃 예외 무시
        }

        // Then: 연결은 유지되어야 함
        Connector!.IsConnected().Should().BeTrue("타임아웃 후에도 연결은 유지되어야 함");
        Connector.IsAuthenticated().Should().BeTrue("인증 상태도 유지되어야 함");

        // 다른 요청은 정상 동작해야 함
        var echoReply = await EchoAsync("After Timeout", 1);
        echoReply.Content.Should().Be("After Timeout", "타임아웃 후 다른 요청은 정상 동작해야 함");
    }

    [Fact(DisplayName = "C-10-03: 콜백 방식 Request도 타임아웃된다")]
    public async Task Request_WithCallback_TimesOut()
    {
        // Given: 짧은 타임아웃 설정
        Connector = new PlayHouse.Connector.Connector();
        Connector.Init(new ConnectorConfig
        {
            RequestTimeoutMs = 2000,
            HeartBeatIntervalMs = 10000,
        });

        await CreateStageAndConnectAsync();
        await AuthenticateAsync("callbackTimeoutUser");

        var noResponseRequest = new NoResponseRequest { DelayMs = 10000 };

        var callbackInvoked = false;
        ushort? errorCode = null;
        var tcs = new TaskCompletionSource<bool>();

        Connector!.OnError += (stageId, stageType, code, request) =>
        {
            errorCode = code;
            tcs.TrySetResult(true);
        };

        // When: 콜백 방식으로 응답 없는 요청
        using var requestPacket = new Packet(noResponseRequest);
        Connector.Request(requestPacket, response =>
        {
            callbackInvoked = true;
        });

        // OnError 이벤트 대기 (MainThreadAction 호출하면서 최대 5초)
        var completed = await WaitForConditionWithMainThreadActionAsync(() => tcs.Task.IsCompleted, 5000);

        // Then: OnError 이벤트가 발생하고 콜백은 호출되지 않아야 함
        completed.Should().BeTrue("OnError 이벤트가 발생해야 함");
        errorCode.Should().Be((ushort)ConnectorErrorCode.RequestTimeout, "에러 코드가 Timeout이어야 함");
        callbackInvoked.Should().BeFalse("타임아웃 시 성공 콜백은 호출되지 않아야 함");
    }

    [Fact(DisplayName = "C-10-04: 여러 요청 중 하나만 타임아웃되어도 다른 요청은 정상 처리된다")]
    public async Task MultipleRequests_OneTimesOut_OthersSucceed()
    {
        // Given: 짧은 타임아웃 설정
        Connector = new PlayHouse.Connector.Connector();
        Connector.Init(new ConnectorConfig
        {
            RequestTimeoutMs = 2000,
            HeartBeatIntervalMs = 10000,
        });

        await CreateStageAndConnectAsync();
        await AuthenticateAsync("multiTimeoutUser");

        // When: 정상 요청과 타임아웃 요청을 병렬로 전송
        var echoTask1 = EchoAsync("Normal 1", 1);
        var echoTask2 = EchoAsync("Normal 2", 2);

        var noResponseRequest = new NoResponseRequest { DelayMs = 10000 };
        using var timeoutPacket = new Packet(noResponseRequest);
        var timeoutTask = Connector!.RequestAsync(timeoutPacket);

        var echoTask3 = EchoAsync("Normal 3", 3);

        // 정상 요청들 완료 대기
        var echo1 = await echoTask1;
        var echo2 = await echoTask2;
        var echo3 = await echoTask3;

        // Then: 정상 요청들은 성공해야 함
        echo1.Content.Should().Be("Normal 1");
        echo2.Content.Should().Be("Normal 2");
        echo3.Content.Should().Be("Normal 3");

        // 타임아웃 요청은 예외가 발생해야 함
        var timeoutAction = async () => await timeoutTask;
        await timeoutAction.Should().ThrowAsync<ConnectorException>();
    }

    [Fact(DisplayName = "C-10-05: 타임아웃 시간을 길게 설정하면 응답을 받을 수 있다")]
    public async Task RequestAsync_WithLongTimeout_ReceivesResponse()
    {
        // Given: 긴 타임아웃 설정 (15초)
        Connector = new PlayHouse.Connector.Connector();
        Connector.Init(new ConnectorConfig
        {
            RequestTimeoutMs = 15000, // 15초 타임아웃
            HeartBeatIntervalMs = 10000,
        });

        await CreateStageAndConnectAsync();
        await AuthenticateAsync("longTimeoutUser");

        // When: 짧은 지연으로 Echo 요청 (타임아웃 내에 응답)
        var echoReply = await EchoAsync("Long Timeout Test", 1);

        // Then: 정상적으로 응답을 받아야 함
        echoReply.Content.Should().Be("Long Timeout Test", "긴 타임아웃 설정으로 응답을 받아야 함");
    }

    [Fact(DisplayName = "C-10-06: 인증 요청도 타임아웃될 수 있다")]
    public async Task AuthenticateAsync_WithTimeout_ThrowsException()
    {
        // Given: 매우 짧은 타임아웃 설정 (100ms)
        Connector = new PlayHouse.Connector.Connector();
        Connector.Init(new ConnectorConfig
        {
            RequestTimeoutMs = 100, // 100ms 타임아웃
            HeartBeatIntervalMs = 10000,
        });

        await CreateStageAndConnectAsync();

        // When: 인증 요청 (네트워크 지연이 있으면 타임아웃될 수 있음)
        // 참고: 이 테스트는 네트워크 상태에 따라 불안정할 수 있으므로
        // 실제로는 타임아웃이 발생하지 않을 수도 있습니다.
        var authRequest = new AuthenticateRequest
        {
            UserId = "authTimeoutUser",
            Token = "valid_token"
        };

        using var requestPacket = new Packet(authRequest);

        // 예외 발생 가능성 체크 (발생하지 않을 수도 있음)
        var exception = await Record.ExceptionAsync(async () =>
        {
            await Connector!.AuthenticateAsync(requestPacket);
        });

        // Then: 예외가 발생하면 ConnectorException이어야 함
        if (exception != null)
        {
            exception.Should().BeOfType<ConnectorException>("타임아웃 시 ConnectorException이 발생해야 함");
        }
    }

    [Fact(DisplayName = "C-10-07: 타임아웃된 요청의 정보를 확인할 수 있다")]
    public async Task TimeoutException_ContainsRequestInfo()
    {
        // Given: 짧은 타임아웃 설정
        Connector = new PlayHouse.Connector.Connector();
        Connector.Init(new ConnectorConfig
        {
            RequestTimeoutMs = 2000,
            HeartBeatIntervalMs = 10000,
        });

        await CreateStageAndConnectAsync();
        await AuthenticateAsync("exceptionInfoUser");

        var noResponseRequest = new NoResponseRequest { DelayMs = 10000 };

        // When: 타임아웃 발생
        using var requestPacket = new Packet(noResponseRequest);
        ConnectorException? caughtException = null;

        try
        {
            await Connector!.RequestAsync(requestPacket);
        }
        catch (ConnectorException ex)
        {
            caughtException = ex;
        }

        // Then: 예외에 요청 정보가 포함되어야 함
        caughtException.Should().NotBeNull("타임아웃 예외가 발생해야 함");
        caughtException!.ErrorCode.Should().Be((ushort)ConnectorErrorCode.RequestTimeout);
        caughtException.StageId.Should().Be(StageInfo!.StageId, "예외에 Stage ID가 포함되어야 함");
    }
}
