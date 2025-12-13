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

namespace PlayHouse.Tests.E2E.Play;

/// <summary>
/// ISender Stage간 통신 E2E 테스트
///
/// 이 테스트는 PlayHouse의 Stage간 메시지 전송을 검증합니다.
/// - SendToStage: Stage A → Stage B 단방향 메시지
/// - RequestToStage: Stage A → Stage B 요청/응답
///
/// Note: Stage간 통신을 테스트하기 위해 두 개의 PlayServer를 구동합니다.
/// - PlayServer A (NID="1:1"): Stage A가 속한 서버
/// - PlayServer B (NID="1:2"): Stage B가 속한 서버
/// </summary>
[Collection("E2E ISender Tests")]
public class ISenderTests : IAsyncLifetime
{
    private PlayServer? _playServerA;
    private PlayServer? _playServerB;
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
        TestSystemController.Reset();

        // PlayServer A (NID="1:1", ServerId=1)
        _playServerA = new PlayServerBootstrap()
            .Configure(options =>
            {
                options.ServerId = 1;
                options.BindEndpoint = "tcp://127.0.0.1:15200";
                options.TcpPort = 0;
                options.RequestTimeoutMs = 30000;
                options.AuthenticateMessageId = "AuthenticateRequest";
                options.DefaultStageType = "TestStage";
            })
            .UseStage<TestStageImpl>("TestStage")
            .UseActor<TestActorImpl>()
            .UseSystemController<TestSystemController>()
            .Build();

        // PlayServer B (NID="1:2", ServerId=2)
        _playServerB = new PlayServerBootstrap()
            .Configure(options =>
            {
                options.ServerId = 2;
                options.BindEndpoint = "tcp://127.0.0.1:15201";
                options.TcpPort = 0;
                options.RequestTimeoutMs = 30000;
                options.AuthenticateMessageId = "AuthenticateRequest";
                options.DefaultStageType = "TestStage";
            })
            .UseStage<TestStageImpl>("TestStage")
            .UseActor<TestActorImpl>()
            .UseSystemController<TestSystemController>()
            .Build();

        await _playServerA.StartAsync();
        await _playServerB.StartAsync();

        // ServerAddressResolver가 서버를 자동으로 연결할 시간을 줌
        await Task.Delay(5000);

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
        if (_playServerB != null)
        {
            await _playServerB.DisposeAsync();
        }
        if (_playServerA != null)
        {
            await _playServerA.DisposeAsync();
        }
    }

    #region SendToStage 테스트

    /// <summary>
    /// SendToStage E2E 테스트
    /// Stage A에서 Stage B로 단방향 메시지를 전송합니다.
    ///
    /// 테스트 플로우:
    /// 1. 클라이언트 A를 PlayServer A에 연결하여 Stage A 생성
    /// 2. 클라이언트 B를 PlayServer B에 연결하여 Stage B 생성
    /// 3. 클라이언트 A가 Stage A에 TriggerSendToStageRequest 전송
    /// 4. Stage A에서 IStageSender.SendToStage("1:2", StageIdB, message)로 PlayServer B의 Stage B에 메시지 전송
    /// 5. PlayServer B의 Stage B에서 OnDispatch(IPacket) 콜백 호출 검증
    /// </summary>
    [Fact(DisplayName = "SendToStage - Stage간 단방향 메시지 전송 성공")]
    public async Task SendToStage_Success_MessageDelivered()
    {
        // Given - 두 개의 서버에 각각 Stage 연결
        await ConnectAndAuthenticateAsync(_connectorA, StageIdA, _playServerA!);
        await ConnectAndAuthenticateAsync(_connectorB, StageIdB, _playServerB!);

        var initialCount = TestStageImpl.InterStageMessageCount;
        var instanceCount = TestStageImpl.Instances.Count;

        // When - Stage A에서 Stage B로 SendToStage 트리거
        // PlayServer B의 NID는 "1:2" (ServiceType.Play=1, ServerId=2)
        var request = new TriggerSendToStageRequest
        {
            TargetNid = "1:2",  // PlayServer B
            TargetStageId = StageIdB,
            Message = "Hello from Stage A"
        };
        using var packet = new Packet(request);
        var response = await _connectorA.RequestAsync(packet);

        await Task.Delay(500); // 비동기 처리 대기

        // Then - E2E 검증: 응답 검증
        response.MsgId.Should().EndWith("TriggerSendToStageReply");
        var reply = TriggerSendToStageReply.Parser.ParseFrom(response.Payload.Data.Span);
        reply.Success.Should().BeTrue("SendToStage가 성공해야 함");

        // Then - E2E 검증: Stage B에서 메시지 수신 확인
        TestStageImpl.InterStageMessageCount.Should().BeGreaterThan(initialCount,
            "Stage B에서 InterStageMessage를 수신해야 함");
        TestStageImpl.InterStageReceivedMsgIds.Should().Contain(msgId => msgId.Contains("InterStageMessage"),
            "InterStageMessage가 기록되어야 함");
    }

    #endregion

    #region RequestToStage 테스트

    /// <summary>
    /// RequestToStage (async) E2E 테스트
    /// Stage A에서 Stage B로 요청을 보내고 응답을 받습니다.
    ///
    /// 테스트 플로우:
    /// 1. 클라이언트 A를 PlayServer A에 연결하여 Stage A 생성
    /// 2. 클라이언트 B를 PlayServer B에 연결하여 Stage B 생성
    /// 3. 클라이언트 A가 Stage A에 TriggerRequestToStageRequest 전송
    /// 4. Stage A에서 IStageSender.RequestToStage("1:2", StageIdB, message)로 PlayServer B의 Stage B에 요청 전송
    /// 5. PlayServer B의 Stage B에서 OnDispatch(IPacket) 콜백 호출되고 Reply 반환
    /// 6. Stage A가 Stage B의 응답을 받아서 클라이언트에 전달
    /// </summary>
    [Fact(DisplayName = "RequestToStage - Stage간 요청/응답 성공")]
    public async Task RequestToStage_Async_Success_ResponseReceived()
    {
        // Given - 두 개의 서버에 각각 Stage 연결
        await ConnectAndAuthenticateAsync(_connectorA, StageIdA, _playServerA!);
        await ConnectAndAuthenticateAsync(_connectorB, StageIdB, _playServerB!);

        // When - Stage A에서 Stage B로 RequestToStage 트리거
        // PlayServer B의 NID는 "1:2" (ServiceType.Play=1, ServerId=2)
        var request = new TriggerRequestToStageRequest
        {
            TargetNid = "1:2",  // PlayServer B
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

    #region 같은 서버 내 Stage간 통신 테스트

    /// <summary>
    /// 같은 서버 내 Stage간 SendToStage 테스트
    /// Stage A에서 같은 서버의 Stage B로 단방향 메시지를 전송합니다.
    ///
    /// 테스트 플로우:
    /// 1. 클라이언트 A를 PlayServer A에 연결하여 Stage A 생성
    /// 2. 클라이언트 B를 같은 PlayServer A에 연결하여 Stage B 생성
    /// 3. 클라이언트 A가 Stage A에 TriggerSendToStageRequest 전송 (targetNid="1:1", 같은 서버)
    /// 4. Stage A에서 IStageSender.SendToStage("1:1", StageIdB, message)로 같은 서버의 Stage B에 메시지 전송
    /// 5. Stage B에서 OnDispatch(IPacket) 콜백 호출 검증
    /// </summary>
    [Fact(DisplayName = "SendToStage - 같은 서버 내 Stage간 단방향 메시지 전송 성공")]
    public async Task SendToStage_SameServer_Success_MessageDelivered()
    {
        // Given - 같은 서버에 두 개의 Stage 연결
        await ConnectAndAuthenticateAsync(_connectorA, StageIdA, _playServerA!);
        await ConnectAndAuthenticateAsync(_connectorB, StageIdB, _playServerA!); // 같은 서버!

        var initialCount = TestStageImpl.InterStageMessageCount;

        // When - Stage A에서 같은 서버의 Stage B로 SendToStage 트리거
        // 같은 서버이므로 NID는 "1:1" (PlayServer A)
        var request = new TriggerSendToStageRequest
        {
            TargetNid = "1:1",  // 같은 서버 (PlayServer A)
            TargetStageId = StageIdB,
            Message = "Hello from Stage A (same server)"
        };
        using var packet = new Packet(request);
        var response = await _connectorA.RequestAsync(packet);

        await Task.Delay(500); // 비동기 처리 대기

        // Then - E2E 검증: 응답 검증
        response.MsgId.Should().EndWith("TriggerSendToStageReply");
        var reply = TriggerSendToStageReply.Parser.ParseFrom(response.Payload.Data.Span);
        reply.Success.Should().BeTrue("SendToStage가 성공해야 함");

        // Then - E2E 검증: Stage B에서 메시지 수신 확인
        TestStageImpl.InterStageMessageCount.Should().BeGreaterThan(initialCount,
            "같은 서버의 Stage B에서 InterStageMessage를 수신해야 함");
        TestStageImpl.InterStageReceivedMsgIds.Should().Contain(msgId => msgId.Contains("InterStageMessage"),
            "InterStageMessage가 기록되어야 함");
    }

    /// <summary>
    /// 같은 서버 내 Stage간 RequestToStage 테스트
    /// Stage A에서 같은 서버의 Stage B로 요청을 보내고 응답을 받습니다.
    ///
    /// 테스트 플로우:
    /// 1. 클라이언트 A를 PlayServer A에 연결하여 Stage A 생성
    /// 2. 클라이언트 B를 같은 PlayServer A에 연결하여 Stage B 생성
    /// 3. 클라이언트 A가 Stage A에 TriggerRequestToStageRequest 전송 (targetNid="1:1", 같은 서버)
    /// 4. Stage A에서 IStageSender.RequestToStage("1:1", StageIdB, message)로 같은 서버의 Stage B에 요청 전송
    /// 5. Stage B에서 OnDispatch(IPacket) 콜백 호출되고 Reply 반환
    /// 6. Stage A가 Stage B의 응답을 받아서 클라이언트에 전달
    /// </summary>
    [Fact(DisplayName = "RequestToStage - 같은 서버 내 Stage간 요청/응답 성공")]
    public async Task RequestToStage_SameServer_Success_ResponseReceived()
    {
        // Given - 같은 서버에 두 개의 Stage 연결
        await ConnectAndAuthenticateAsync(_connectorA, StageIdA, _playServerA!);
        await ConnectAndAuthenticateAsync(_connectorB, StageIdB, _playServerA!); // 같은 서버!

        // When - Stage A에서 같은 서버의 Stage B로 RequestToStage 트리거
        // 같은 서버이므로 NID는 "1:1" (PlayServer A)
        var request = new TriggerRequestToStageRequest
        {
            TargetNid = "1:1",  // 같은 서버 (PlayServer A)
            TargetStageId = StageIdB,
            Query = "Query from Stage A (same server)"
        };
        using var packet = new Packet(request);
        var response = await _connectorA.RequestAsync(packet);

        // Then - E2E 검증: 응답 검증
        response.MsgId.Should().EndWith("TriggerRequestToStageReply");
        var reply = TriggerRequestToStageReply.Parser.ParseFrom(response.Payload.Data.Span);
        reply.Response.Should().Contain("Query from Stage A (same server)",
            "같은 서버의 Stage B의 에코 응답이 포함되어야 함");
    }

    #endregion

    #region Helper Methods

    private async Task ConnectAndAuthenticateAsync(ClientConnector connector, long stageId, PlayServer server)
    {
        connector.Init(new ConnectorConfig { RequestTimeoutMs = 30000 });
        var connected = await connector.ConnectAsync("127.0.0.1", server.ActualTcpPort, stageId);
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
