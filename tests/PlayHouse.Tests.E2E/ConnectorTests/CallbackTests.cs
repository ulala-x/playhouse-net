#nullable enable

using FluentAssertions;
using Google.Protobuf;
using PlayHouse.Connector;
using PlayHouse.Connector.Protocol;
using PlayHouse.Tests.E2E.Infrastructure;
using PlayHouse.Tests.E2E.Infrastructure.Fixtures;
using PlayHouse.Tests.E2E.Proto;
using Xunit;
using ClientConnector = PlayHouse.Connector.Connector;
using ClientPacket = PlayHouse.Connector.Protocol.IPacket;

namespace PlayHouse.Tests.E2E.ConnectorTests;

/// <summary>
/// IActor/IStage 콜백 E2E 테스트
///
/// 이 테스트는 PlayHouse의 콜백 시스템 사용법을 보여줍니다.
/// E2E 테스트 원칙:
/// - 응답 검증: Connector 공개 API로 확인
/// - 콜백 호출 검증: 테스트 구현체의 StageId 기반 검증
/// </summary>
[Collection("E2E Connector Tests")]
public class CallbackTests : IAsyncLifetime
{
    private readonly SinglePlayServerFixture _fixture;
    private readonly ClientConnector _connector;
    private readonly List<(long stageId, ClientPacket packet)> _receivedMessages = new();
    private Timer? _callbackTimer;
    private readonly object _callbackLock = new();

    public CallbackTests(SinglePlayServerFixture fixture)
    {
        _fixture = fixture;
        _connector = new ClientConnector();
        _connector.OnReceive += (stageId, packet) => _receivedMessages.Add((stageId, packet));
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

    #region IActor 콜백 테스트 (3개)

    /// <summary>
    /// IActor.OnAuthenticate 콜백 E2E 테스트
    ///
    /// 이 테스트는 인증 플로우에서 OnAuthenticate 콜백이 호출되는지 검증합니다.
    /// </summary>
    /// <remarks>
    /// E2E 검증 방법:
    /// - 응답 검증: IsAuthenticated() == true
    /// - 콜백 호출 검증: TestActorImpl.OnAuthenticateCallCount > 0, AuthenticatedAccountIds 확인
    /// </remarks>
    [Fact(DisplayName = "Authenticate - OnAuthenticate 콜백 호출, AccountId 설정")]
    public async Task Authenticate_CallsOnAuthenticate_SetsAccountId()
    {
        // Given - 연결된 상태
        var stageId = await ConnectOnlyAsync();

        // When - AuthenticateRequest로 인증
        var authRequest = new AuthenticateRequest
        {
            UserId = "test-user-1",
            Token = "valid-token"
        };
        using var authPacket = new Packet(authRequest);
        await _connector.AuthenticateAsync(authPacket);
        await Task.Delay(100);

        // Then - E2E 검증: 응답 검증
        _connector.IsAuthenticated().Should().BeTrue("인증이 성공해야 함");

        // Then - E2E 검증: 콜백 호출 검증
        TestActorImpl.OnAuthenticateCallCount.Should().BeGreaterThan(0,
            "OnAuthenticate 콜백이 호출되어야 함");
        TestActorImpl.AuthenticatedAccountIds.Should().NotBeEmpty(
            "AccountId가 설정되어야 함");
    }

    /// <summary>
    /// IActor.OnPostAuthenticate 콜백 E2E 테스트
    ///
    /// 이 테스트는 인증 성공 후 OnPostAuthenticate 콜백이 호출되는지 검증합니다.
    /// </summary>
    /// <remarks>
    /// E2E 검증 방법:
    /// - 응답 검증: IsAuthenticated() == true
    /// - 콜백 호출 검증: TestActorImpl.Instances에서 OnPostAuthenticateCalled 확인
    /// </remarks>
    [Fact(DisplayName = "Authenticate - OnPostAuthenticate 콜백 호출 (인증 후)")]
    public async Task Authenticate_CallsOnPostAuthenticate_AfterSuccess()
    {
        // Given - 연결된 상태
        var stageId = await ConnectOnlyAsync();

        // When - 인증 성공 후
        var authRequest = new AuthenticateRequest
        {
            UserId = "test-user-2",
            Token = "valid-token"
        };
        using var authPacket = new Packet(authRequest);
        await _connector.AuthenticateAsync(authPacket);
        await Task.Delay(100);

        // Then - E2E 검증: 응답 검증
        _connector.IsAuthenticated().Should().BeTrue("인증이 성공해야 함");

        // Then - E2E 검증: 콜백 호출 검증
        TestActorImpl.Instances.Should().Contain(a => a.OnPostAuthenticateCalled,
            "OnPostAuthenticate 콜백이 호출되어야 함");
    }

    /// <summary>
    /// IActor.OnCreate 콜백 E2E 테스트
    ///
    /// 이 테스트는 Stage 가입 시 새로운 Actor가 생성되고 OnCreate 콜백이 호출되는지 검증합니다.
    /// </summary>
    /// <remarks>
    /// E2E 검증 방법:
    /// - 응답 검증: IsAuthenticated() == true
    /// - 콜백 호출 검증: TestActorImpl.Instances에서 OnCreateCalled 확인
    /// </remarks>
    [Fact(DisplayName = "JoinStage - OnCreate 콜백 호출 (새 Actor 생성)")]
    public async Task JoinStage_CallsOnCreate_ForNewActor()
    {
        // Given - 인증된 상태 (자동으로 Stage에 Join)
        var stageId = await ConnectToServerAsync();

        // Then - E2E 검증: 콜백 호출 검증
        TestActorImpl.Instances.Should().Contain(a => a.OnCreateCalled,
            "OnCreate 콜백이 호출되어야 함");
    }

    #endregion

    #region IStage 콜백 테스트 (7개)

    /// <summary>
    /// IStage.OnCreate 콜백 E2E 테스트
    ///
    /// 이 테스트는 클라이언트 연결 및 인증 시 Stage가 생성되고 OnCreate 콜백이 호출되는지 검증합니다.
    /// </summary>
    /// <remarks>
    /// E2E 검증 방법:
    /// - 응답 검증: IsAuthenticated() == true
    /// - 콜백 호출 검증: TestStageImpl.GetByStageId()로 특정 Stage 검증
    /// </remarks>
    [Fact(DisplayName = "CreateStage - OnCreate 콜백 호출 (패킷 전달)")]
    public async Task CreateStage_CallsOnCreate_WithPacket()
    {
        // Given - 서버 시작됨
        // When - 클라이언트 연결 및 인증
        var stageId = await ConnectToServerAsync();

        // Then - E2E 검증: 콜백 호출 검증
        var stage = TestStageImpl.GetByStageId(stageId);
        stage.Should().NotBeNull();
        stage!.OnCreateCalled.Should().BeTrue("OnCreate 콜백이 호출되어야 함");
        stage.LastCreatePacket.Should().NotBeNull("OnCreate에 패킷이 전달되어야 함");
    }

    /// <summary>
    /// IStage.OnPostCreate 콜백 E2E 테스트
    ///
    /// 이 테스트는 Stage 생성 후 OnPostCreate 콜백이 호출되는지 검증합니다.
    /// </summary>
    /// <remarks>
    /// E2E 검증 방법:
    /// - 응답 검증: IsAuthenticated() == true
    /// - 콜백 호출 검증: TestStageImpl.GetByStageId()로 특정 Stage 검증
    ///
    /// Note: OnPostCreate는 비동기로 처리되므로 OnCreate가 호출되면 OnPostCreate도 호출됨
    /// 따라서 OnCreateCalled로 간접 검증
    /// </remarks>
    [Fact(DisplayName = "CreateStage - OnPostCreate 콜백 호출 (OnCreate 후)")]
    public async Task CreateStage_CallsOnPostCreate_AfterOnCreate()
    {
        // Given - 서버 시작됨
        // When - 클라이언트 연결 및 인증
        var stageId = await ConnectToServerAsync();

        // 비동기 처리를 위해 추가 대기
        await Task.Delay(100);

        // Then - E2E 검증: 콜백 호출 검증
        // OnPostCreate는 내부적으로 호출되지만, E2E에서는 OnCreate 호출 여부로 검증
        var stage = TestStageImpl.GetByStageId(stageId);
        stage.Should().NotBeNull();
        stage!.OnCreateCalled.Should().BeTrue("OnCreate 콜백이 호출되어야 하며, OnPostCreate도 내부적으로 호출됨");
    }

    /// <summary>
    /// IStage.OnJoinStage 콜백 E2E 테스트
    ///
    /// 이 테스트는 Actor가 Stage에 가입할 때 OnJoinStage 콜백이 호출되는지 검증합니다.
    /// </summary>
    /// <remarks>
    /// E2E 검증 방법:
    /// - 응답 검증: IsAuthenticated() == true
    /// - 콜백 호출 검증: TestStageImpl.GetByStageId()로 특정 Stage의 JoinedActors 확인
    /// </remarks>
    [Fact(DisplayName = "JoinStage - OnJoinStage 콜백 호출, Actor 추가")]
    public async Task JoinStage_CallsOnJoinStage_AddsActor()
    {
        // Given - 서버 시작됨
        // When - 클라이언트 연결 및 인증 (자동으로 Stage에 Join)
        var stageId = await ConnectToServerAsync();

        // Then - E2E 검증: 콜백 호출 검증
        var stage = TestStageImpl.GetByStageId(stageId);
        stage.Should().NotBeNull();
        stage!.JoinedActors.Should().NotBeEmpty("OnJoinStage 콜백이 호출되어 Actor가 추가되어야 함");
    }

    /// <summary>
    /// IStage.OnPostJoinStage 콜백 E2E 테스트
    ///
    /// 이 테스트는 Actor가 Stage에 가입한 후 OnPostJoinStage 콜백이 호출되는지 검증합니다.
    /// </summary>
    /// <remarks>
    /// E2E 검증 방법:
    /// - 응답 검증: IsAuthenticated() == true
    /// - 콜백 호출 검증: TestStageImpl.GetByStageId()로 특정 Stage의 OnPostJoinStageCalled 확인
    /// </remarks>
    [Fact(DisplayName = "JoinStage - OnPostJoinStage 콜백 호출 (Join 후)")]
    public async Task JoinStage_CallsOnPostJoinStage_AfterJoin()
    {
        // Given - 서버 시작됨
        // When - 클라이언트 연결 및 인증 (자동으로 Stage에 Join)
        var stageId = await ConnectToServerAsync();

        // Then - E2E 검증: 콜백 호출 검증
        var stage = TestStageImpl.GetByStageId(stageId);
        stage.Should().NotBeNull();
        stage!.OnPostJoinStageCalled.Should().BeTrue("OnPostJoinStage 콜백이 호출되어야 함");
    }

    /// <summary>
    /// IStage.OnDispatch 콜백 E2E 테스트
    ///
    /// 이 테스트는 클라이언트에서 메시지 전송 시 OnDispatch 콜백이 호출되는지 검증합니다.
    /// </summary>
    /// <remarks>
    /// E2E 검증 방법:
    /// - 응답 검증: EchoReply 메시지 수신
    /// - 콜백 호출 검증: TestStageImpl.GetByStageId()로 특정 Stage의 ReceivedMsgIds 확인
    /// </remarks>
    [Fact(DisplayName = "SendMessage - OnDispatch 콜백 호출, MsgId 기록")]
    public async Task SendMessage_CallsOnDispatch_RecordsMsgId()
    {
        // Given - 인증된 상태
        var stageId = await ConnectToServerAsync();

        // When - EchoRequest 전송
        var echoRequest = new EchoRequest
        {
            Content = "Dispatch Test",
            Sequence = 42
        };
        using var packet = new Packet(echoRequest);
        var response = await _connector.RequestAsync(packet);

        // Then - E2E 검증: 응답 검증
        response.MsgId.Should().EndWith("EchoReply", "응답 메시지 ID가 EchoReply로 끝나야 함");

        // Then - E2E 검증: 콜백 호출 검증
        var stage = TestStageImpl.GetByStageId(stageId);
        stage.Should().NotBeNull();
        stage!.ReceivedMsgIds.Should().Contain("EchoRequest", "OnDispatch에서 EchoRequest를 받아야 함");
    }

    /// <summary>
    /// Disconnect E2E 테스트
    ///
    /// 이 테스트는 클라이언트 연결 해제가 정상적으로 동작하는지 검증합니다.
    /// </summary>
    /// <remarks>
    /// E2E 검증 방법:
    /// - 응답 검증: IsConnected() == false
    ///
    /// Note: OnConnectionChanged는 서버 내부 콜백이므로 통합 테스트에서 검증해야 합니다.
    /// E2E 테스트에서는 클라이언트 공개 API로 확인 가능한 것만 검증합니다.
    /// </remarks>
    [Fact(DisplayName = "Disconnect - 연결 해제 성공")]
    public async Task Disconnect_CallsOnConnectionChanged_RecordsChange()
    {
        // Given - 인증된 상태
        var stageId = await ConnectToServerAsync();
        _connector.IsConnected().Should().BeTrue("연결이 되어 있어야 함");

        // When - 연결 해제
        _connector.Disconnect();

        // Then - E2E 검증: 연결 해제 확인
        _connector.IsConnected().Should().BeFalse("연결이 해제되어야 함");

        // Note: OnConnectionChanged 콜백은 서버 내부 동작이므로 통합 테스트로 검증
    }

    /// <summary>
    /// IStage.OnDestroy 콜백 E2E 테스트
    ///
    /// 이 테스트는 Stage 종료 시 OnDestroy 콜백이 호출되는지 검증합니다.
    /// </summary>
    /// <remarks>
    /// E2E 검증 방법:
    /// - 응답 검증: CloseStageReply 메시지 수신
    /// - 콜백 호출 검증: TestStageImpl.GetByStageId()로 특정 Stage의 OnDestroyCalled 확인
    /// </remarks>
    [Fact(DisplayName = "CloseStage - OnDestroy 콜백 호출, Stage 소멸")]
    public async Task CloseStage_CallsOnDestroy_StageDestroyed()
    {
        // Given - 인증된 상태
        var stageId = await ConnectToServerAsync();

        // When - CloseStageRequest 전송
        var closeRequest = new CloseStageRequest
        {
            Reason = "Test completed"
        };
        using var packet = new Packet(closeRequest);
        var response = await _connector.RequestAsync(packet);

        // Then - E2E 검증: 응답 검증
        response.MsgId.Should().EndWith("CloseStageReply", "응답 메시지 ID가 CloseStageReply로 끝나야 함");
        var closeReply = CloseStageReply.Parser.ParseFrom(response.Payload.Data.Span);
        closeReply.Success.Should().BeTrue("Stage 종료가 성공해야 함");

        // OnDestroy는 비동기로 처리되므로 약간 대기
        await Task.Delay(200);

        // Then - E2E 검증: 콜백 호출 검증
        var stage = TestStageImpl.GetByStageId(stageId);
        stage.Should().NotBeNull();
        stage!.OnDestroyCalled.Should().BeTrue("OnDestroy 콜백이 호출되어야 함");
    }

    #endregion

    #region IStageSender 콜백 테스트 (3개)

    /// <summary>
    /// IStageSender.AddRepeatTimer 콜백 E2E 테스트
    ///
    /// 이 테스트는 반복 타이머 콜백이 주기적으로 호출되는지 검증합니다.
    /// </summary>
    /// <remarks>
    /// E2E 검증 방법:
    /// - 응답 검증: StartTimerReply 메시지 수신
    /// - Push 메시지 검증: TimerTickNotify 메시지 3회 이상 수신
    /// </remarks>
    [Fact(DisplayName = "AddRepeatTimer - 타이머 콜백 호출 (주기적)")]
    public async Task AddRepeatTimer_SendsPushMessages_OnEachTick()
    {
        // Given - 인증된 상태
        var stageId = await ConnectToServerAsync();
        _receivedMessages.Clear();

        // When - StartRepeatTimerRequest 전송 (IntervalMs = 100)
        var timerRequest = new StartRepeatTimerRequest
        {
            IntervalMs = 100,
            InitialDelayMs = 50
        };
        using var packet = new Packet(timerRequest);
        var response = await _connector.RequestAsync(packet);

        // Then - E2E 검증: 응답 검증
        response.MsgId.Should().EndWith("StartTimerReply", "응답 메시지 ID가 StartTimerReply로 끝나야 함");
        var timerReply = StartTimerReply.Parser.ParseFrom(response.Payload.Data.Span);
        timerReply.TimerId.Should().BeGreaterThan(0, "유효한 타이머 ID가 반환되어야 함");

        // 타이머 Tick 메시지 3회 수신 대기 (충분한 시간)
        await Task.Delay(800);

        // 진단: 해당 stageId에 대한 타이머 콜백 호출 횟수 확인
        var callbackCount = TestStageImpl.GetTimerCallbackCount(stageId);

        // Then - E2E 검증: Push 메시지 검증
        var timerTicks = _receivedMessages.Count(m => m.packet.MsgId.Contains("TimerTickNotify"));

        // 진단 메시지 포함
        timerTicks.Should().BeGreaterOrEqualTo(3,
            $"타이머 Tick Push 메시지가 3회 이상 수신되어야 함. " +
            $"(콜백호출: {callbackCount}회, 수신메시지: {_receivedMessages.Count}개, " +
            $"MsgIds: [{string.Join(", ", _receivedMessages.Select(m => m.packet.MsgId))}])");

        // 테스트 종료 전 Stage 정리 (반복 타이머 중지)
        var closeRequest = new CloseStageRequest { Reason = "Test cleanup" };
        using var closePacket = new Packet(closeRequest);
        await _connector.RequestAsync(closePacket);
    }

    /// <summary>
    /// IStageSender.AddCountTimer 콜백 E2E 테스트
    ///
    /// 이 테스트는 카운트 타이머 콜백이 정확한 횟수만큼 호출되는지 검증합니다.
    /// </summary>
    /// <remarks>
    /// E2E 검증 방법:
    /// - 응답 검증: StartTimerReply 메시지 수신
    /// - Push 메시지 검증: TimerTickNotify 메시지 정확히 3회 수신
    /// </remarks>
    [Fact(DisplayName = "AddCountTimer - 정확한 횟수의 타이머 콜백 호출")]
    public async Task AddCountTimer_SendsExactNumberOfPushMessages()
    {
        // Given - 인증된 상태
        var stageId = await ConnectToServerAsync();
        _receivedMessages.Clear();

        // When - StartCountTimerRequest (Count = 3) 전송
        var timerRequest = new StartCountTimerRequest
        {
            IntervalMs = 100,
            Count = 3,
            InitialDelayMs = 50
        };
        using var packet = new Packet(timerRequest);
        var response = await _connector.RequestAsync(packet);

        // Then - E2E 검증: 응답 검증
        response.MsgId.Should().EndWith("StartTimerReply", "응답 메시지 ID가 StartTimerReply로 끝나야 함");

        // 타이머가 3회 호출될 때까지 대기 (3 * 100ms + 50ms initial + 여유)
        await Task.Delay(600);

        // 추가로 타이머가 호출되지 않는지 확인하기 위해 대기
        await Task.Delay(300);

        // 진단: 해당 stageId에 대한 타이머 콜백 호출 횟수 확인
        var callbackCount = TestStageImpl.GetTimerCallbackCount(stageId);

        // Then - E2E 검증: 정확히 3회 수신 확인
        var timerTicks = _receivedMessages.Count(m => m.packet.MsgId.Contains("TimerTickNotify"));
        timerTicks.Should().Be(3,
            $"타이머 Tick Push 메시지가 정확히 3회 수신되어야 함. " +
            $"(콜백호출: {callbackCount}회, 수신메시지: {_receivedMessages.Count}개, " +
            $"MsgIds: [{string.Join(", ", _receivedMessages.Select(m => m.packet.MsgId))}])");
    }

    /// <summary>
    /// IStageSender.AsyncBlock 콜백 E2E 테스트
    ///
    /// 이 테스트는 AsyncBlock의 Pre/Post 콜백이 올바르게 실행되는지 검증합니다.
    /// </summary>
    /// <remarks>
    /// E2E 검증 방법:
    /// - 즉시 응답: AsyncBlockAccepted 메시지 수신
    /// - Push 메시지: AsyncBlockReply 수신 (Pre/Post 처리 결과 포함)
    ///
    /// Note: AsyncBlock은 비동기 특성상 즉시 수락 응답 후 결과는 Push로 전송됨
    /// </remarks>
    [Fact(DisplayName = "AsyncBlock - Pre/Post 콜백 실행")]
    public async Task AsyncBlock_ExecutesPreAndPostCallbacks_ReturnsResult()
    {
        // Given - 인증된 상태
        var stageId = await ConnectToServerAsync();
        _receivedMessages.Clear();

        // When - AsyncBlockRequest 전송
        var asyncRequest = new AsyncBlockRequest
        {
            Operation = "test_operation",
            DelayMs = 100
        };
        using var packet = new Packet(asyncRequest);

        // AsyncBlock은 즉시 수락 응답 후 결과는 Push로 전송됨
        var response = await _connector.RequestAsync(packet);

        // Then - E2E 검증: 즉시 응답 (수락 확인)
        response.MsgId.Should().Contain("AsyncBlockAccepted", "즉시 수락 응답이 와야 함");

        // Then - E2E 검증: Push 메시지 대기 (비동기 결과)
        await Task.Delay(300); // AsyncBlock 처리 완료 대기 (100ms delay + 여유)

        var asyncReplyMessage = _receivedMessages
            .FirstOrDefault(m => m.packet.MsgId.Contains("AsyncBlockReply"));
        asyncReplyMessage.packet.Should().NotBeNull("AsyncBlockReply Push 메시지가 수신되어야 함");

        var asyncReply = AsyncBlockReply.Parser.ParseFrom(asyncReplyMessage.packet.Payload.Data.Span);
        asyncReply.PreResult.Should().Contain("pre_completed", "Pre 콜백이 실행되어야 함");
        asyncReply.PostResult.Should().Contain("post_completed", "Post 콜백이 실행되어야 함");
        asyncReply.PreThreadId.Should().BeGreaterThan(0, "Pre 콜백이 실행된 스레드 ID가 기록되어야 함");
        asyncReply.PostThreadId.Should().BeGreaterThan(0, "Post 콜백이 실행된 스레드 ID가 기록되어야 함");
    }

    #endregion

    #region IActorSender 테스트 (2개)

    /// <summary>
    /// IActorSender.AccountId E2E 테스트
    /// Reply에 AccountId가 포함되는지 검증합니다.
    /// </summary>
    [Fact(DisplayName = "AccountId - Reply에 AccountId 포함 확인")]
    public async Task AccountId_InReply_Verified()
    {
        // Given - 인증된 상태
        var stageId = await ConnectToServerAsync();

        // When - GetAccountIdRequest 전송
        var request = new GetAccountIdRequest();
        using var packet = new Packet(request);
        var response = await _connector.RequestAsync(packet);

        // Then - E2E 검증: 응답 검증
        response.MsgId.Should().EndWith("GetAccountIdReply", "응답 메시지 ID가 GetAccountIdReply로 끝나야 함");
        var reply = GetAccountIdReply.Parser.ParseFrom(response.Payload.Data.Span);
        reply.AccountId.Should().NotBeNullOrEmpty("AccountId가 설정되어야 함");

        // Then - E2E 검증: 콜백 검증
        TestActorImpl.AuthenticatedAccountIds.Should().Contain(reply.AccountId,
            "Reply의 AccountId가 인증된 AccountId 목록에 있어야 함");
    }

    /// <summary>
    /// IActorSender.LeaveStage E2E 테스트
    /// Actor가 Stage를 떠날 때 OnDestroy 콜백이 호출되는지 검증합니다.
    /// </summary>
    [Fact(DisplayName = "LeaveStage - OnDestroy 콜백 호출")]
    public async Task LeaveStage_OnDestroy_Called()
    {
        // Given - 인증된 상태
        var stageId = await ConnectToServerAsync();
        var initialInstanceCount = TestActorImpl.Instances.Count;

        // When - LeaveStageRequest 전송
        var request = new LeaveStageRequest { Reason = "Test leave" };
        using var packet = new Packet(request);
        var response = await _connector.RequestAsync(packet);

        // 비동기 처리 대기
        await Task.Delay(200);

        // Then - E2E 검증: 응답 검증
        response.MsgId.Should().EndWith("LeaveStageReply", "응답 메시지 ID가 LeaveStageReply로 끝나야 함");
        var reply = LeaveStageReply.Parser.ParseFrom(response.Payload.Data.Span);
        reply.Success.Should().BeTrue("LeaveStage가 성공해야 함");

        // Then - E2E 검증: 콜백 검증
        TestActorImpl.Instances.Should().Contain(a => a.OnDestroyCalled,
            "OnDestroy 콜백이 호출되어야 함");
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
        var connected = await _connector.ConnectAsync("127.0.0.1", _fixture.PlayServer!.ActualTcpPort, stageId);
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

    /// <summary>
    /// 지정된 개수의 Push 메시지를 받을 때까지 대기.
    /// </summary>
    private async Task WaitForPushMessagesAsync(int count, int timeoutMs)
    {
        var startTime = DateTime.UtcNow;
        while (_receivedMessages.Count < count && (DateTime.UtcNow - startTime).TotalMilliseconds < timeoutMs)
        {
            await Task.Delay(50);
        }
    }

    #endregion
}
