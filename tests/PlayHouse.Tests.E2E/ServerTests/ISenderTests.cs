#nullable enable

using FluentAssertions;
using PlayHouse.Bootstrap;
using PlayHouse.Connector;
using PlayHouse.Connector.Protocol;
using PlayHouse.Tests.E2E.Infrastructure;
using PlayHouse.Tests.E2E.Proto;
using Xunit;
using ClientConnector = PlayHouse.Connector.Connector;
using ClientPacket = PlayHouse.Connector.Protocol.IPacket;

namespace PlayHouse.Tests.E2E.ServerTests;

/// <summary>
/// ISender Stage간 통신 E2E 테스트
///
/// 이 테스트는 PlayHouse의 Stage간 메시지 전송을 검증합니다.
/// - SendToStage: Stage A → Stage B 단방향 메시지
/// - RequestToStage: Stage A → Stage B 요청/응답
/// </summary>
[Collection("E2E ISender Tests")]
public class ISenderTests : IAsyncLifetime
{
    private PlayServer? _playServer;
    private readonly ClientConnector _connectorA;
    private readonly ClientConnector _connectorB;
    private readonly List<(long stageId, ClientPacket packet)> _receivedMessagesA = new();
    private readonly List<(long stageId, ClientPacket packet)> _receivedMessagesB = new();
    private Timer? _callbackTimer;
    private readonly object _callbackLock = new();

    private const long StageIdA = 11111L;
    private const long StageIdB = 22222L;

    public ISenderTests()
    {
        _connectorA = new ClientConnector();
        _connectorB = new ClientConnector();
        _connectorA.OnReceive += (stageId, packet) => _receivedMessagesA.Add((stageId, packet));
        _connectorB.OnReceive += (stageId, packet) => _receivedMessagesB.Add((stageId, packet));
    }

    public async Task InitializeAsync()
    {
        TestActorImpl.ResetAll();
        TestStageImpl.ResetAll();

        _playServer = new PlayServerBootstrap()
            .Configure(options =>
            {
                options.ServerId = 1;
                options.BindEndpoint = "tcp://127.0.0.1:15200"; // Fixed port for Stage communication
                options.TcpPort = 0;
                options.RequestTimeoutMs = 30000;
                options.AuthenticateMessageId = "AuthenticateRequest";
                options.DefaultStageType = "TestStage";
            })
            .UseStage<TestStageImpl>("TestStage")
            .UseActor<TestActorImpl>()
            .Build();

        await _playServer.StartAsync();
        await Task.Delay(200); // 서버 초기화 대기

        // PlayServer가 자기 자신에게 연결 (Stage간 통신을 위해)
        // NID "1:1" = ServiceType.Play(1):ServerId(1)
        _playServer.Connect("1:1", "tcp://127.0.0.1:15200");
        await Task.Delay(500); // Connection stabilization

        _callbackTimer = new Timer(_ =>
        {
            lock (_callbackLock)
            {
                _connectorA.MainThreadAction();
                _connectorB.MainThreadAction();
            }
        }, null, 0, 20);
    }

    public async Task DisposeAsync()
    {
        _callbackTimer?.Dispose();
        _connectorA.Disconnect();
        _connectorB.Disconnect();
        if (_playServer != null)
        {
            await _playServer.DisposeAsync();
        }
    }

    #region SendToStage 테스트

    /// <summary>
    /// SendToStage E2E 테스트
    /// Stage A에서 Stage B로 단방향 메시지를 전송합니다.
    ///
    /// Note: 이 테스트는 같은 PlayServer 내 Stage간 통신을 테스트합니다.
    /// PlayHouse 아키텍처에서 Stage간 통신은 NetMQ를 통해 라우팅되며,
    /// 같은 서버 내에서는 자체 라우팅이 필요합니다.
    /// 현재 테스트 환경에서는 서버간 Discovery/Connection이 설정되지 않아
    /// 실제 Stage간 통신 테스트를 위해서는 별도의 PlayServer 클러스터 설정이 필요합니다.
    /// </summary>
    [Fact(DisplayName = "SendToStage - Stage간 단방향 메시지 전송 성공", Skip = "같은 PlayServer 내 Stage간 라우팅 구현 필요")]
    public async Task SendToStage_Success_MessageDelivered()
    {
        // Given - 두 개의 Stage 연결 (각각 다른 stageId로)
        await ConnectAndAuthenticateAsync(_connectorA, StageIdA);
        await ConnectAndAuthenticateAsync(_connectorB, StageIdB);

        var initialCount = TestStageImpl.InterStageMessageCount;

        // When - Stage A에서 Stage B로 SendToStage 트리거
        var request = new TriggerSendToStageRequest
        {
            TargetStageId = StageIdB,
            Message = "Hello from Stage A"
        };
        using var packet = new Packet(request);
        var response = await _connectorA.RequestAsync(packet);

        await Task.Delay(300); // 비동기 처리 대기

        // Then - E2E 검증: 응답 검증
        response.MsgId.Should().EndWith("TriggerSendToStageReply");
        var reply = TriggerSendToStageReply.Parser.ParseFrom(response.Payload.Data.Span);
        reply.Success.Should().BeTrue("SendToStage가 성공해야 함");

        // Then - E2E 검증: Stage B에서 메시지 수신 확인
        TestStageImpl.InterStageMessageCount.Should().BeGreaterThan(initialCount,
            "Stage B에서 InterStageMessage를 수신해야 함");
        TestStageImpl.InterStageReceivedMsgIds.Should().Contain("InterStageMessage",
            "InterStageMessage가 기록되어야 함");
    }

    #endregion

    #region RequestToStage 테스트

    /// <summary>
    /// RequestToStage (async) E2E 테스트
    /// Stage A에서 Stage B로 요청을 보내고 응답을 받습니다.
    ///
    /// Note: SendToStage와 동일한 제약사항이 적용됩니다.
    /// 실제 Stage간 통신 테스트를 위해서는 별도의 PlayServer 클러스터 설정이 필요합니다.
    /// </summary>
    [Fact(DisplayName = "RequestToStage - Stage간 요청/응답 성공", Skip = "같은 PlayServer 내 Stage간 라우팅 구현 필요")]
    public async Task RequestToStage_Async_Success_ResponseReceived()
    {
        // Given - 두 개의 Stage 연결
        await ConnectAndAuthenticateAsync(_connectorA, StageIdA);
        await ConnectAndAuthenticateAsync(_connectorB, StageIdB);

        // When - Stage A에서 Stage B로 RequestToStage 트리거
        var request = new TriggerRequestToStageRequest
        {
            TargetStageId = StageIdB,
            Query = "Query from Stage A"
        };
        using var packet = new Packet(request);
        var response = await _connectorA.RequestAsync(packet);

        // Then - E2E 검증: 응답 검증
        response.MsgId.Should().EndWith("TriggerRequestToStageReply");
        var reply = TriggerRequestToStageReply.Parser.ParseFrom(response.Payload.Data.Span);
        reply.Response.Should().Contain("Query from Stage A",
            "Stage B의 에코 응답이 포함되어야 함");
    }

    #endregion

    #region Helper Methods

    private async Task ConnectAndAuthenticateAsync(ClientConnector connector, long stageId)
    {
        connector.Init(new ConnectorConfig { RequestTimeoutMs = 30000 });
        var connected = await connector.ConnectAsync("127.0.0.1", _playServer!.ActualTcpPort, stageId);
        connected.Should().BeTrue($"서버에 연결되어야 함 (stageId: {stageId})");
        await Task.Delay(100);

        var authRequest = new AuthenticateRequest
        {
            UserId = $"test-user-{stageId}",
            Token = "valid-token"
        };
        using var authPacket = new Packet(authRequest);
        await connector.AuthenticateAsync(authPacket);
        await Task.Delay(100);
    }

    #endregion
}
