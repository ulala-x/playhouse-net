#nullable enable

using FluentAssertions;
using Microsoft.Extensions.Logging;
using PlayHouse.Bootstrap;
using PlayHouse.Connector;
using PlayHouse.Connector.Protocol;
using PlayHouse.Core.Api.Bootstrap;
using PlayHouse.Tests.Integration.Infrastructure;
using PlayHouse.Tests.Integration.Infrastructure.Fixtures;
using PlayHouse.Tests.Integration.Proto;
using Xunit;
using ClientConnector = PlayHouse.Connector.Connector;
using ClientPacket = PlayHouse.Connector.Protocol.IPacket;

namespace PlayHouse.Tests.Integration.Play;

/// <summary>
/// Stage → API 통신 E2E 테스트
///
/// 이 테스트는 PlayHouse의 Stage에서 API 서버로의 메시지 전송을 검증합니다.
/// - SendToApi: Stage → API 단방향 메시지
/// - RequestToApi (async): Stage → API 요청/응답 (async/await)
/// - RequestToApi (callback): Stage → API 요청/응답 (callback)
///
/// 테스트 환경:
/// - PlayServer (ServerId="play-1"): Stage가 속한 서버
/// - ApiServer (ServerId="api-1"): API 핸들러가 있는 서버
/// - Client (Connector): Stage에 연결된 클라이언트
///
/// Note: PlayServer와 ApiServer는 다른 ServerId를 사용해야 ZMQ Router가 올바르게 라우팅할 수 있습니다.
/// </summary>
[Collection("E2E ApiPlayServer")]
public class StageToApiTests : IAsyncLifetime
{
    private readonly ApiPlayServerFixture _fixture;
    private PlayServer? PlayServer => _fixture.PlayServer;
    private ApiServer? ApiServer => _fixture.ApiServer;
    private readonly ClientConnector _connector;
    private readonly List<(long stageId, string stageType, ClientPacket packet)> _receivedMessages = new();
    private Timer? _callbackTimer;
    private readonly object _callbackLock = new();

    private const long StageId = 33333L;

    public StageToApiTests(ApiPlayServerFixture fixture)
    {
        _fixture = fixture;
        _connector = new ClientConnector();
        _connector.OnReceive += (stageId, stageType, packet) => _receivedMessages.Add((stageId, stageType, packet));
    }

    public async Task InitializeAsync()
    {
        TestActorImpl.ResetAll();
        TestStageImpl.ResetAll();
        TestApiController.ResetAll();

        _callbackTimer = new Timer(_ =>
        {
            lock (_callbackLock)
            {
                _connector.MainThreadAction();
            }
        }, null, 0, 20);

        // Fixture가 서버를 관리하므로 추가 초기화 불필요
        await Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        _callbackTimer?.Dispose();
        _connector.Disconnect();
        await Task.CompletedTask;
    }

    #region SendToApi 테스트

    /// <summary>
    /// SendToApi E2E 테스트
    /// Stage에서 API 서버로 단방향 메시지를 전송합니다.
    ///
    /// 테스트 플로우:
    /// 1. 클라이언트를 PlayServer (play-1)에 연결하여 Stage 생성
    /// 2. 클라이언트가 Stage에 TriggerSendToApiRequest 전송
    /// 3. Stage에서 IStageSender.SendToApi("api-1", message)로 API 서버에 메시지 전송
    /// 4. API 서버의 TestApiController에서 HandleApiEcho 콜백 호출 검증
    /// </summary>
    [Fact(DisplayName = "SendToApi - Stage에서 API로 단방향 메시지 전송 성공")]
    public async Task SendToApi_Success_MessageDelivered()
    {
        // Given - PlayServer에 Stage 연결
        await ConnectAndAuthenticateAsync(_connector, StageId);

        var initialApiCallCount = TestApiController.OnDispatchCallCount;

        // When - Stage에서 API로 SendToApi 트리거
        var request = new TriggerSendToApiRequest
        {
            Message = "Hello from Stage"
        };
        using var packet = new Packet(request);
        var response = await _connector.RequestAsync(packet);

        await Task.Delay(500); // 비동기 처리 대기

        // Then - E2E 검증 1: 응답 검증
        response.MsgId.Should().EndWith("TriggerSendToApiReply");
        var reply = TriggerSendToApiReply.Parser.ParseFrom(response.Payload.DataSpan);
        reply.Success.Should().BeTrue("SendToApi가 성공해야 함");

        // Then - E2E 검증 2: API Controller에서 메시지 수신 확인
        TestApiController.OnDispatchCallCount.Should().BeGreaterThan(initialApiCallCount,
            "API Controller에서 메시지를 수신해야 함");
        TestApiController.ReceivedMsgIds.Should().Contain(msgId => msgId.Contains("ApiEchoRequest"),
            "ApiEchoRequest가 기록되어야 함");
    }

    #endregion

    #region RequestToApi (async) 테스트

    /// <summary>
    /// RequestToApi (async) E2E 테스트
    /// Stage에서 API 서버로 요청을 보내고 async/await로 응답을 받습니다.
    ///
    /// 테스트 플로우:
    /// 1. 클라이언트를 PlayServer (play-1)에 연결하여 Stage 생성
    /// 2. 클라이언트가 Stage에 TriggerRequestToApiRequest 전송
    /// 3. Stage에서 IStageSender.RequestToApi("api-1", message)로 API 서버에 요청 전송 (await)
    /// 4. API 서버의 TestApiController에서 HandleApiEcho 콜백 호출되고 Reply 반환
    /// 5. Stage가 API의 응답을 받아서 클라이언트에 전달
    /// </summary>
    [Fact(DisplayName = "RequestToApi - Stage에서 API로 요청/응답 성공 (async)")]
    public async Task RequestToApi_Async_Success_ResponseReceived()
    {
        // Given - PlayServer에 Stage 연결
        await ConnectAndAuthenticateAsync(_connector, StageId);

        var initialApiCallCount = TestApiController.OnDispatchCallCount;

        // When - Stage에서 API로 RequestToApi 트리거 (async)
        var request = new TriggerRequestToApiRequest
        {
            Query = "Query from Stage"
        };
        using var packet = new Packet(request);
        var response = await _connector.RequestAsync(packet);

        // Then - E2E 검증 1: 응답 검증
        response.MsgId.Should().EndWith("TriggerRequestToApiReply");
        var reply = TriggerRequestToApiReply.Parser.ParseFrom(response.Payload.DataSpan);
        reply.ApiResponse.Should().Contain("Query from Stage",
            "API의 에코 응답이 포함되어야 함");

        // Then - E2E 검증 2: API Controller 호출 확인
        TestApiController.OnDispatchCallCount.Should().BeGreaterThan(initialApiCallCount,
            "API Controller에서 요청을 처리해야 함");
    }

    #endregion

    #region RequestToApi (callback) 테스트

    /// <summary>
    /// RequestToApi Callback 버전 E2E 테스트
    /// Stage에서 API 서버로 요청을 보내고 callback으로 응답을 받습니다.
    ///
    /// 테스트 플로우:
    /// 1. 클라이언트를 PlayServer (play-1)에 연결하여 Stage 생성
    /// 2. 클라이언트가 Stage에 TriggerRequestToApiCallbackRequest 전송
    /// 3. Stage에서 IStageSender.RequestToApi("api-1", message, callback)로 API 서버에 요청 전송
    /// 4. API 서버의 TestApiController에서 HandleApiEcho 콜백 호출되고 Reply 반환
    /// 5. Stage의 callback이 호출되고 응답을 클라이언트에 Push 메시지로 전달
    /// 6. E2E 검증: 즉시 수락 응답 + callback 호출 횟수 + Push 메시지 내용 검증
    /// </summary>
    [Fact(DisplayName = "RequestToApi - Stage에서 API로 요청/응답 성공 (callback)")]
    public async Task RequestToApi_Callback_Success_CallbackInvoked()
    {
        // Given - PlayServer에 Stage 연결
        await ConnectAndAuthenticateAsync(_connector, StageId);

        var initialCallbackCount = TestStageImpl.RequestToApiCallbackCount;
        var initialApiCallCount = TestApiController.OnDispatchCallCount;
        _receivedMessages.Clear();

        // When - Stage에서 API로 RequestToApi Callback 버전 트리거
        var request = new TriggerRequestToApiCallbackRequest
        {
            Query = "Callback Query from Stage"
        };
        using var packet = new Packet(request);
        var response = await _connector.RequestAsync(packet);

        // Then - E2E 검증 1: 즉시 수락 응답
        response.MsgId.Should().EndWith("TriggerRequestToApiCallbackAccepted",
            "즉시 수락 응답을 받아야 함");

        // Callback이 실행되고 Push 메시지가 도착할 시간 대기
        await Task.Delay(1000);

        // Then - E2E 검증 2: Callback 호출 횟수 증가
        TestStageImpl.RequestToApiCallbackCount.Should().BeGreaterThan(initialCallbackCount,
            "RequestToApi callback이 호출되어야 함");

        // Then - E2E 검증 3: API Controller 호출 확인
        TestApiController.OnDispatchCallCount.Should().BeGreaterThan(initialApiCallCount,
            "API Controller에서 요청을 처리해야 함");

        // Then - E2E 검증 4: Push 메시지 수신 (callback에서 SendToClient로 전송)
        _receivedMessages.Should().NotBeEmpty("Push 메시지를 수신해야 함");
        var pushMessage = _receivedMessages.FirstOrDefault(m => m.packet.MsgId.Contains("TriggerRequestToApiCallbackReply"));
        pushMessage.Should().NotBe(default, "TriggerRequestToApiCallbackReply Push 메시지가 있어야 함");

        var pushReply = TriggerRequestToApiCallbackReply.Parser.ParseFrom(pushMessage.packet.Payload.DataSpan);
        pushReply.ApiResponse.Should().Contain("Callback Query from Stage",
            "API의 에코 응답이 callback을 통해 전달되어야 함");
    }

    #endregion

    #region Helper Methods

    private async Task ConnectAndAuthenticateAsync(ClientConnector connector, long stageId)
    {
        connector.Init(new ConnectorConfig { RequestTimeoutMs = 30000 });
        var connected = await connector.ConnectAsync("127.0.0.1", PlayServer!.ActualTcpPort, stageId, "TestStage");
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
