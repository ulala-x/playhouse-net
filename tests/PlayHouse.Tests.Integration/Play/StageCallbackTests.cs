#nullable enable

using FluentAssertions;
using PlayHouse.Connector;
using PlayHouse.Connector.Protocol;
using PlayHouse.Tests.Integration.Infrastructure;
using PlayHouse.Tests.Integration.Infrastructure.Fixtures;
using PlayHouse.Tests.Integration.Proto;
using Xunit;
using ClientConnector = PlayHouse.Connector.Connector;

namespace PlayHouse.Tests.Integration.Play;

/// <summary>
/// IStage 콜백 E2E 테스트
///
/// 이 테스트는 PlayHouse의 Stage 콜백 시스템 사용법을 보여줍니다.
/// E2E 테스트 원칙:
/// - 응답 검증: Connector 공개 API로 확인
/// - 콜백 호출 검증: 테스트 구현체(TestStageImpl)의 검증
/// </summary>
[Collection("E2E Connector Tests")]
public class StageCallbackTests : IAsyncLifetime
{
    private readonly SinglePlayServerFixture _fixture;
    private readonly ClientConnector _connector;
    private Timer? _callbackTimer;
    private readonly object _callbackLock = new();

    public StageCallbackTests(SinglePlayServerFixture fixture)
    {
        _fixture = fixture;
        _connector = new ClientConnector();
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

    #region IStage 콜백 테스트

    /// <summary>
    /// IStage.OnCreate 콜백 E2E 테스트
    ///
    /// 이 테스트는 Stage 생성 시 OnCreate 콜백이 호출되는지 검증합니다.
    /// </summary>
    /// <remarks>
    /// E2E 검증 방법:
    /// - 응답 검증: IsAuthenticated() == true
    /// - 콜백 호출 검증: TestStageImpl.Instances에서 OnCreateCalled 확인
    /// </remarks>
    [Fact(DisplayName = "Connect - OnCreate 콜백 호출 (Stage 생성)")]
    public async Task Connect_CallsOnCreate_WhenStageCreated()
    {
        // Given - 서버에 연결 및 인증
        var stageId = await ConnectToServerAsync();

        // Then - E2E 검증: 콜백 호출 검증
        var stage = TestStageImpl.GetByStageId(stageId);
        stage.Should().NotBeNull("Stage가 생성되어야 함");
        stage!.OnCreateCalled.Should().BeTrue("OnCreate 콜백이 호출되어야 함");
    }

    /// <summary>
    /// IStage.OnJoinStage 콜백 E2E 테스트
    ///
    /// 이 테스트는 Actor가 Stage에 Join할 때 OnJoinStage 콜백이 호출되는지 검증합니다.
    /// </summary>
    /// <remarks>
    /// E2E 검증 방법:
    /// - 응답 검증: IsAuthenticated() == true
    /// - 콜백 호출 검증: TestStageImpl.JoinedActors 확인
    /// </remarks>
    [Fact(DisplayName = "JoinStage - OnJoinStage 콜백 호출")]
    public async Task JoinStage_CallsOnJoinStage_WhenActorJoins()
    {
        // Given - 서버에 연결 및 인증
        var stageId = await ConnectToServerAsync();

        // Then - E2E 검증: 콜백 호출 검증
        var stage = TestStageImpl.GetByStageId(stageId);
        stage.Should().NotBeNull("Stage가 생성되어야 함");
        stage!.JoinedActors.Should().NotBeEmpty("OnJoinStage 콜백이 호출되어야 함");
    }

    /// <summary>
    /// IStage.OnPostJoinStage 콜백 E2E 테스트
    ///
    /// 이 테스트는 Actor가 Stage에 Join한 후 OnPostJoinStage 콜백이 호출되는지 검증합니다.
    /// </summary>
    /// <remarks>
    /// E2E 검증 방법:
    /// - 응답 검증: IsAuthenticated() == true
    /// - 콜백 호출 검증: TestStageImpl.OnPostJoinStageCalled 확인
    /// </remarks>
    [Fact(DisplayName = "JoinStage - OnPostJoinStage 콜백 호출 (Join 후)")]
    public async Task JoinStage_CallsOnPostJoinStage_AfterActorJoins()
    {
        // Given - 서버에 연결 및 인증
        var stageId = await ConnectToServerAsync();

        // Then - E2E 검증: 콜백 호출 검증
        var stage = TestStageImpl.GetByStageId(stageId);
        stage.Should().NotBeNull("Stage가 생성되어야 함");
        stage!.OnPostJoinStageCalled.Should().BeTrue("OnPostJoinStage 콜백이 호출되어야 함");
    }

    /// <summary>
    /// IStage.OnDispatch 콜백 E2E 테스트
    ///
    /// 이 테스트는 메시지 수신 시 OnDispatch 콜백이 호출되는지 검증합니다.
    /// </summary>
    /// <remarks>
    /// E2E 검증 방법:
    /// - 응답 검증: EchoReply 메시지 수신
    /// - 콜백 호출 검증: TestStageImpl.ReceivedMsgIds, OnDispatchCallCount 확인
    /// </remarks>
    [Fact(DisplayName = "Request - OnDispatch 콜백 호출 (메시지 수신)")]
    public async Task Request_CallsOnDispatch_WhenMessageReceived()
    {
        // Given - 서버에 연결 및 인증
        var stageId = await ConnectToServerAsync();
        var stage = TestStageImpl.GetByStageId(stageId);
        stage.Should().NotBeNull("Stage가 생성되어야 함");

        var initialDispatchCount = TestStageImpl.OnDispatchCallCount;

        // When - EchoRequest 전송
        var echoRequest = new EchoRequest { Content = "OnDispatch Test", Sequence = 1 };
        using var packet = new Packet(echoRequest);
        var response = await _connector.RequestAsync(packet);

        // Then - E2E 검증: 응답 검증
        response.MsgId.Should().EndWith("EchoReply", "응답 메시지를 받아야 함");

        // Then - E2E 검증: 콜백 호출 검증
        stage!.ReceivedMsgIds.Should().Contain("EchoRequest", "OnDispatch에서 메시지를 수신해야 함");
        TestStageImpl.OnDispatchCallCount.Should().BeGreaterThan(initialDispatchCount,
            "OnDispatch 콜백이 호출되어야 함");
    }

    /// <summary>
    /// IStage.OnDestroy 콜백 E2E 테스트
    ///
    /// 이 테스트는 Stage가 닫힐 때 OnDestroy 콜백이 호출되는지 검증합니다.
    /// </summary>
    /// <remarks>
    /// E2E 검증 방법:
    /// - 응답 검증: CloseStageReply 메시지 수신
    /// - 콜백 호출 검증: TestStageImpl.OnDestroyCalled 확인
    /// </remarks>
    [Fact(DisplayName = "CloseStage - OnDestroy 콜백 호출 (Stage 닫힘)")]
    public async Task CloseStage_CallsOnDestroy_WhenStageClosed()
    {
        // Given - 서버에 연결 및 인증
        var stageId = await ConnectToServerAsync();
        var stage = TestStageImpl.GetByStageId(stageId);
        stage.Should().NotBeNull("Stage가 생성되어야 함");

        // When - CloseStageRequest 전송
        var closeRequest = new CloseStageRequest { Reason = "Test" };
        using var packet = new Packet(closeRequest);
        var response = await _connector.RequestAsync(packet);

        // 콜백 처리 대기
        await Task.Delay(200);

        // Then - E2E 검증: 응답 검증
        response.MsgId.Should().EndWith("CloseStageReply", "응답 메시지를 받아야 함");
        var reply = CloseStageReply.Parser.ParseFrom(response.Payload.DataSpan);
        reply.Success.Should().BeTrue("Stage 닫기가 성공해야 함");

        // Then - E2E 검증: 콜백 호출 검증
        stage!.OnDestroyCalled.Should().BeTrue("OnDestroy 콜백이 호출되어야 함");
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
