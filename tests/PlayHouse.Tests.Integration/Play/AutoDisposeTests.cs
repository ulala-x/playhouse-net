#nullable enable

using FluentAssertions;
using PlayHouse.Connector;
using PlayHouse.Connector.Protocol;
using PlayHouse.Tests.Integration.Infrastructure;
using PlayHouse.Tests.Integration.Infrastructure.Fixtures;
using PlayHouse.Tests.Integration.Proto;
using Xunit;
using ClientConnector = PlayHouse.Connector.Connector;
using ClientPacket = PlayHouse.Connector.Protocol.IPacket;

namespace PlayHouse.Tests.Integration.Play;

/// <summary>
/// 자동 Dispose E2E 테스트
///
/// 이 테스트는 콜백 내에서 RequestToApi/RequestToStage 호출 시
/// 응답 패킷이 자동으로 dispose되는지 검증합니다.
/// E2E 테스트 원칙:
/// - 응답 검증: Connector 공개 API로 확인
/// - 기능 검증: 정상적으로 응답을 받고 처리 가능한지 확인
/// </summary>
[Collection("E2E ApiPlayServer")]
public class AutoDisposeTests : IAsyncLifetime
{
    private readonly ApiPlayServerFixture _fixture;
    private readonly ClientConnector _connector;
    private readonly List<(long stageId, string stageType, ClientPacket packet)> _receivedMessages = new();
    private Timer? _callbackTimer;
    private readonly object _callbackLock = new();

    public AutoDisposeTests(ApiPlayServerFixture fixture)
    {
        _fixture = fixture;
        _connector = new ClientConnector();
        _connector.OnReceive += (stageId, stageType, packet) => _receivedMessages.Add((stageId, stageType, packet));
    }

    public async Task InitializeAsync()
    {
        // 콜백 자동 처리 타이머 시작
        _callbackTimer = new Timer(_ =>
        {
            lock (_callbackLock)
            {
                _connector.MainThreadAction();
            }
        }, null, 0, 20); // 20ms 간격

        await Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        _callbackTimer?.Dispose();
        _callbackTimer = null;

        _connector.Disconnect();
        await Task.CompletedTask;
    }

    #region OnDispatch 내 RequestToApi 자동 Dispose 테스트

    /// <summary>
    /// OnDispatch 내에서 RequestToApi 호출 시 자동 Dispose 테스트
    ///
    /// 이 테스트는 OnDispatch 콜백 내에서 await RequestToApi를 호출했을 때
    /// 응답 패킷이 자동으로 dispose되는지 검증합니다.
    /// </summary>
    /// <remarks>
    /// E2E 검증 방법:
    /// - 응답 검증: TriggerAutoDisposeApiReply 메시지 수신
    /// - 기능 검증: API 서버로부터 받은 응답이 정상적으로 클라이언트에 전달됨
    /// </remarks>
    [Fact(DisplayName = "OnDispatch 내 RequestToApi - 자동 Dispose, 정상 응답")]
    public async Task OnDispatch_RequestToApi_AutoDispose_ReturnsResponse()
    {
        // Given - 서버에 연결 및 인증
        await ConnectToServerAsync();

        // When - OnDispatch 내 RequestToApi 트리거
        var request = new TriggerAutoDisposeApiRequest { Query = "test_query" };
        using var packet = new Packet(request);
        var response = await _connector.RequestAsync(packet);

        // Then - E2E 검증: 응답 검증
        response.MsgId.Should().EndWith("TriggerAutoDisposeApiReply", "응답 메시지를 받아야 함");
        var reply = TriggerAutoDisposeApiReply.Parser.ParseFrom(response.Payload.DataSpan);
        reply.ApiResponse.Should().Be("Echo: test_query", "API 서버 응답이 정상적으로 전달되어야 함");

        // 자동 dispose로 인한 에러가 없어야 함
        // 정상적으로 응답을 받았다는 것은 자동 dispose가 정상 동작했음을 의미
    }

    #endregion

    #region OnDispatch 내 RequestToStage 자동 Dispose 테스트

    /// <summary>
    /// OnDispatch 내에서 RequestToStage 호출 시 자동 Dispose 테스트
    ///
    /// 이 테스트는 OnDispatch 콜백 내에서 await RequestToStage를 호출했을 때
    /// 응답 패킷이 자동으로 dispose되는지 검증합니다.
    /// </summary>
    /// <remarks>
    /// E2E 검증 방법:
    /// - 응답 검증: TriggerAutoDisposeStageReply 메시지 수신
    /// - 기능 검증: 다른 Stage로부터 받은 응답이 정상적으로 클라이언트에 전달됨
    /// </remarks>
    [Fact(DisplayName = "OnDispatch 내 RequestToStage - 자동 Dispose, 정상 응답")]
    public async Task OnDispatch_RequestToStage_AutoDispose_ReturnsResponse()
    {
        // Given - 서버에 연결 및 인증
        var stageId1 = await ConnectToServerAsync();

        // 타겟 Stage 생성 (두 번째 클라이언트)
        var connector2 = new ClientConnector();
        try
        {
            var stageId2 = Random.Shared.NextInt64(100000, long.MaxValue);
            connector2.Init(new ConnectorConfig { RequestTimeoutMs = 30000 });
            await connector2.ConnectAsync("127.0.0.1", _fixture.PlayServer!.ActualTcpPort, stageId2, "TestStage");
            var authRequest2 = new AuthenticateRequest { UserId = "test-user-2", Token = "valid-token" };
            using var authPacket2 = new Packet(authRequest2);
            await connector2.AuthenticateAsync(authPacket2);
            await Task.Delay(100);

            // When - OnDispatch 내 RequestToStage 트리거
            var request = new TriggerAutoDisposeStageRequest
            {
                TargetNid = "play-1",
                TargetStageId = stageId2,
                Query = "inter_stage_query"
            };
            using var packet = new Packet(request);
            var response = await _connector.RequestAsync(packet);

            // Then - E2E 검증: 응답 검증
            response.MsgId.Should().EndWith("TriggerAutoDisposeStageReply", "응답 메시지를 받아야 함");
            var reply = TriggerAutoDisposeStageReply.Parser.ParseFrom(response.Payload.DataSpan);
            reply.Response.Should().Be("Echo: inter_stage_query", "Stage 간 응답이 정상적으로 전달되어야 함");

            // 자동 dispose로 인한 에러가 없어야 함
            // 정상적으로 응답을 받았다는 것은 자동 dispose가 정상 동작했음을 의미
        }
        finally
        {
            connector2.Disconnect();
        }
    }

    #endregion

    #region Timer 콜백 내 RequestAsync 자동 Dispose 테스트

    /// <summary>
    /// Timer 콜백 내에서 RequestAsync 호출 시 자동 Dispose 테스트
    ///
    /// 이 테스트는 Timer 콜백 내에서 await RequestToApi를 호출했을 때
    /// 응답 패킷이 자동으로 dispose되는지 검증합니다.
    /// </summary>
    /// <remarks>
    /// E2E 검증 방법:
    /// - 응답 검증: StartTimerWithRequestReply 메시지 수신
    /// - Push 메시지 검증: OnReceive 콜백으로 TimerRequestResultNotify 수신
    /// - 기능 검증: Timer 내에서 API 요청 후 결과를 정상적으로 받음
    /// </remarks>
    [Fact(DisplayName = "Timer 콜백 내 RequestAsync - 자동 Dispose, 정상 응답")]
    public async Task TimerCallback_RequestAsync_AutoDispose_ReturnsResponse()
    {
        // Given - 서버에 연결 및 인증
        await ConnectToServerAsync();
        _receivedMessages.Clear();

        // When - Timer에서 RequestAsync 실행
        var request = new StartTimerWithRequestRequest { DelayMs = 100 };
        using var packet = new Packet(request);
        var response = await _connector.RequestAsync(packet);

        // Then - E2E 검증: 응답 검증
        response.MsgId.Should().EndWith("StartTimerWithRequestReply", "응답 메시지를 받아야 함");
        var reply = StartTimerWithRequestReply.Parser.ParseFrom(response.Payload.DataSpan);
        reply.TimerId.Should().BeGreaterThan(0, "TimerId가 할당되어야 함");

        // Timer 실행 대기
        await Task.Delay(500);

        // Then - E2E 검증: Push 메시지 검증
        var timerResults = _receivedMessages
            .Where(m => m.packet.MsgId.EndsWith("TimerRequestResultNotify"))
            .Select(m => TimerRequestResultNotify.Parser.ParseFrom(m.packet.Payload.DataSpan))
            .ToList();

        timerResults.Should().HaveCount(1, "Timer가 1회 실행되어야 함");
        var result = timerResults[0];
        result.Success.Should().BeTrue("Timer 내 RequestAsync가 성공해야 함");
        result.Result.Should().Be("Timer API Response: timer_test", "API 응답이 정상적으로 전달되어야 함");

        // 자동 dispose로 인한 에러가 없어야 함
        // 정상적으로 응답을 받았다는 것은 자동 dispose가 정상 동작했음을 의미
    }

    #endregion

    #region Helper Methods

    /// <summary>
    /// 서버에 연결만 수행 (인증 X).
    /// </summary>
    private async Task<long> ConnectOnlyAsync()
    {
        var stageId = Random.Shared.NextInt64(100000, long.MaxValue);
        _connector.Init(new ConnectorConfig { RequestTimeoutMs = 30000 });
        var connected = await _connector.ConnectAsync("127.0.0.1", _fixture.PlayServer!.ActualTcpPort, stageId, "TestStage");
        connected.Should().BeTrue("서버에 연결되어야 함");
        await Task.Delay(100);
        return stageId;
    }

    /// <summary>
    /// 서버에 연결 및 인증 수행.
    /// </summary>
    private async Task<long> ConnectToServerAsync()
    {
        var stageId = await ConnectOnlyAsync();

        // Proto 메시지로 인증
        var authRequest = new AuthenticateRequest
        {
            UserId = "test-user",
            Token = "valid-token"
        };
        using var authPacket = new Packet(authRequest);
        await _connector.AuthenticateAsync(authPacket);
        await Task.Delay(100);
        return stageId;
    }

    #endregion
}
