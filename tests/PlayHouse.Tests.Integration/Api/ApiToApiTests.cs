#nullable enable

using FluentAssertions;
using PlayHouse.Bootstrap;
using PlayHouse.Core.Shared;
using PlayHouse.Tests.Integration.Infrastructure;
using PlayHouse.Tests.Integration.Proto;
using Xunit;

namespace PlayHouse.Tests.Integration.Api;

/// <summary>
/// ApiServer 간 통신 E2E 테스트
///
/// 이 테스트는 두 개의 ApiServer 간 양방향 통신을 검증합니다.
/// - SendToApi: 비동기 메시지 전송 (응답 없음)
/// - RequestToApi: 동기 요청-응답 패턴
///
/// 테스트 아키텍처:
/// - ApiServer A (ServerId="1", Port=15101)
/// - ApiServer B (ServerId="2", Port=15102)
/// - 양방향 ZMQ Router-Router 연결
/// </summary>
[Collection("E2E ApiToApi Tests")]
public class ApiToApiTests : IAsyncLifetime
{
    private ApiServer? _apiServerA;
    private ApiServer? _apiServerB;

    public async Task InitializeAsync()
    {
        TestApiController.ResetAll();
        TestSystemController.Reset();

        // ApiServer A (ServiceType.Api=2, ServerId=1)
        _apiServerA = new ApiServerBootstrap()
            .Configure(options =>
            {
                options.ServerId = "1";
                options.BindEndpoint = "tcp://127.0.0.1:15101";
                options.RequestTimeoutMs = 30000;
            })
            .UseController<TestApiController>()
            .UseSystemController<TestSystemController>()
            .Build();

        // ApiServer B (ServiceType.Api=2, ServerId=2)
        _apiServerB = new ApiServerBootstrap()
            .Configure(options =>
            {
                options.ServerId = "2";
                options.BindEndpoint = "tcp://127.0.0.1:15102";
                options.RequestTimeoutMs = 30000;
            })
            .UseController<TestApiController>()
            .UseSystemController<TestSystemController>()
            .Build();

        await _apiServerA.StartAsync();
        await _apiServerB.StartAsync();

        // ServerAddressResolver가 서버를 자동으로 연결할 시간을 줌
        await Task.Delay(5000);
    }

    public async Task DisposeAsync()
    {
        if (_apiServerA != null)
        {
            await _apiServerA.DisposeAsync();
        }
        if (_apiServerB != null)
        {
            await _apiServerB.DisposeAsync();
        }
    }

    #region SendToApi 테스트

    /// <summary>
    /// ApiServer A → ApiServer B SendToApi 테스트
    ///
    /// 테스트 플로우:
    /// 1. ApiServer A의 ApiSender.SendToApi("2", message) 호출
    /// 2. ApiServer A → ApiServer B로 ZMQ 메시지 전송
    /// 3. ApiServer B의 핸들러 콜백 호출
    /// 4. 콜백 호출 검증 (응답은 기대하지 않음)
    ///
    /// Note: SendToApi는 비동기 전송이므로 응답을 기대하지 않습니다.
    /// InterApiMessage 핸들러가 등록되어 있지만, 이 테스트에서는 호출 검증만 합니다.
    /// </summary>
    [Fact(DisplayName = "SendToApi - ApiServer A에서 ApiServer B로 메시지 전송 성공")]
    public async Task SendToApi_FromApiAToApiB_MessageReceived()
    {
        // Given
        var initialCallCount = TestApiController.OnDispatchCallCount;

        // When - ApiServer A에서 ApiServer B로 SendToApi (InterApiMessage 사용)
        var message = new InterApiMessage
        {
            FromApiNid = "1",
            Content = "Hello from API A"
        };
        _apiServerA!.ApiSender!.SendToApi("2", CPacket.Of(message));

        // 메시지 전달 대기
        await Task.Delay(1000);

        // Then
        // 1. ApiServer B의 HandleInterApiMessage 콜백 호출됨
        TestApiController.ReceivedMsgIds.Should().Contain(typeof(InterApiMessage).Name,
            "HandleInterApiMessage should be called on ApiServer B");

        // 2. 핸들러 호출 횟수 증가
        TestApiController.OnDispatchCallCount.Should().BeGreaterThan(initialCallCount,
            "handler should be called");
    }

    #endregion

    #region RequestToApi 테스트

    /// <summary>
    /// ApiServer A → ApiServer B RequestToApi 테스트
    ///
    /// 테스트 플로우:
    /// 1. ApiServer A의 ApiSender.RequestToApi("2", message) 호출
    /// 2. ApiServer A → ApiServer B로 ZMQ 메시지 전송
    /// 3. ApiServer B의 HandleInterApiMessage 콜백 호출 및 응답 생성
    /// 4. ApiServer B → ApiServer A로 응답 전송
    /// 5. 응답 내용 검증
    ///
    /// Note: 이 테스트는 ApiServer에 등록된 핸들러를 사용합니다.
    /// ApiEchoRequest 핸들러가 등록되어 있으므로 이를 사용하여 통신을 테스트합니다.
    /// </summary>
    [Fact(DisplayName = "RequestToApi - ApiServer A에서 ApiServer B로 요청-응답 성공")]
    public async Task RequestToApi_FromApiAToApiB_ResponseReceived()
    {
        // Given
        const string testContent = "Echo from API A to API B";
        var initialCallCount = TestApiController.OnDispatchCallCount;

        // When - ApiServer A에서 ApiServer B로 RequestToApi (ApiEchoRequest 사용)
        var echoRequest = new ApiEchoRequest
        {
            Content = testContent
        };
        var responsePacket = await _apiServerA!.ApiSender!.RequestToApi("2", CPacket.Of(echoRequest));

        // Then
        // 1. 응답 패킷 수신됨
        responsePacket.Should().NotBeNull("response should be received");
        responsePacket.MsgId.Should().Be(typeof(ApiEchoReply).Name,
            "response should be ApiEchoReply");

        // 2. 응답 내용 검증
        var response = ApiEchoReply.Parser.ParseFrom(responsePacket.Payload.Data.Span);
        response.Content.Should().Contain(testContent,
            "response should contain the original content");

        // 3. ApiServer B의 핸들러 호출됨
        TestApiController.ReceivedMsgIds.Should().Contain(typeof(ApiEchoRequest).Name,
            "HandleApiEcho should be called on ApiServer B");

        TestApiController.OnDispatchCallCount.Should().BeGreaterThan(initialCallCount,
            "handler should be called");
    }

    #endregion

    #region 양방향 통신 테스트

    /// <summary>
    /// ApiServer A ↔ ApiServer B 양방향 통신 테스트
    ///
    /// 테스트 플로우:
    /// 1. ApiServer A → ApiServer B 요청
    /// 2. ApiServer B → ApiServer A 응답
    /// 3. ApiServer B → ApiServer A 요청
    /// 4. ApiServer A → ApiServer B 응답
    /// 5. 양방향 통신 검증
    /// </summary>
    [Fact(DisplayName = "RequestToApi - 양방향 통신 성공")]
    public async Task RequestToApi_Bidirectional_BothDirectionsWork()
    {
        // Given
        const string contentAtoB = "A to B";
        const string contentBtoA = "B to A";

        // When & Then - A → B
        var messageAtoB = new InterApiMessage
        {
            FromApiNid = "1",
            Content = contentAtoB
        };
        var responseFromB = await _apiServerA!.ApiSender!.RequestToApi("2", CPacket.Of(messageAtoB));
        var replyFromB = InterApiReply.Parser.ParseFrom(responseFromB.Payload.Data.Span);

        replyFromB.Response.Should().Contain(contentAtoB, "response from B should contain A's content");

        // When & Then - B → A
        var messageBtoA = new InterApiMessage
        {
            FromApiNid = "2",
            Content = contentBtoA
        };
        var responseFromA = await _apiServerB!.ApiSender!.RequestToApi("1", CPacket.Of(messageBtoA));
        var replyFromA = InterApiReply.Parser.ParseFrom(responseFromA.Payload.Data.Span);

        replyFromA.Response.Should().Contain(contentBtoA, "response from A should contain B's content");

        // 두 메시지 모두 처리됨
        TestApiController.ReceivedMsgIds.Count(x => x == typeof(InterApiMessage).Name)
            .Should().BeGreaterOrEqualTo(2, "both messages should be received");
    }

    #endregion

    #region 다단계 요청 테스트

    /// <summary>
    /// 트리거를 통한 API 간 통신 테스트
    ///
    /// 테스트 플로우:
    /// 1. ApiServer B의 HandleRequestToApiServer 핸들러 호출 (트리거 역할)
    /// 2. 핸들러 내에서 ApiServer A로 RequestToApi 호출
    /// 3. ApiServer A의 HandleInterApiMessage 콜백 호출 및 응답
    /// 4. 최종 응답 검증
    ///
    /// 이 테스트는 핸들러 내에서 다른 API 서버로 요청하는 실제 사용 패턴을 검증합니다.
    /// Note: ZMQ Router는 자기 자신에게 메시지를 보내는 것을 지원하지 않으므로,
    /// ApiServer A가 ApiServer B에게 트리거 요청을 보내고, B가 A에게 실제 요청을 합니다.
    /// </summary>
    [Fact(DisplayName = "RequestToApi - 핸들러 트리거 방식 통신 성공")]
    public async Task RequestToApi_ThroughHandler_Success()
    {
        // Given
        const string testQuery = "Test query through handler";

        // When - ApiServer A가 ApiServer B에게 트리거 요청 (B가 A에게 실제 요청)
        var triggerRequest = new TriggerRequestToApiServerRequest
        {
            TargetApiNid = "1", // ApiServer B가 ApiServer A에게 요청
            Query = testQuery
        };

        var responsePacket = await _apiServerA!.ApiSender!.RequestToApi(
            "2", // ApiServer B에게 트리거 요청
            CPacket.Of(triggerRequest));

        // Then
        // 1. 트리거 응답 검증
        responsePacket.MsgId.Should().Be(typeof(TriggerRequestToApiServerReply).Name);
        var triggerReply = TriggerRequestToApiServerReply.Parser.ParseFrom(responsePacket.Payload.Data.Span);

        // 2. ApiServer B로부터 받은 응답이 포함됨
        triggerReply.Response.Should().Contain(testQuery,
            "response should contain the original query");
        triggerReply.Response.Should().Contain("Processed:",
            "response should indicate processing");

        // 3. 두 핸들러 모두 호출됨
        TestApiController.ReceivedMsgIds.Should().Contain(typeof(TriggerRequestToApiServerRequest).Name,
            "trigger handler should be called");
        TestApiController.ReceivedMsgIds.Should().Contain(typeof(InterApiMessage).Name,
            "target API handler should be called");
    }

    /// <summary>
    /// SendToApi 트리거 테스트
    ///
    /// 테스트 플로우:
    /// 1. ApiServer B의 HandleSendToApiServer 핸들러 호출
    /// 2. 핸들러 내에서 ApiServer A로 SendToApi 호출 (비동기)
    /// 3. ApiServer A의 HandleInterApiMessage 콜백 호출
    /// 4. 콜백 호출 검증
    ///
    /// Note: ZMQ Router는 자기 자신에게 메시지를 보내는 것을 지원하지 않으므로,
    /// ApiServer A가 ApiServer B에게 트리거 요청을 보내고, B가 A에게 SendToApi 호출합니다.
    /// </summary>
    [Fact(DisplayName = "SendToApi - 핸들러 트리거 방식 메시지 전송 성공")]
    public async Task SendToApi_ThroughHandler_Success()
    {
        // Given
        const string testMessage = "Test message through handler";
        var initialCallCount = TestApiController.OnDispatchCallCount;

        // When - ApiServer A가 ApiServer B에게 트리거 요청 (B가 A에게 SendToApi)
        var triggerRequest = new TriggerSendToApiServerRequest
        {
            TargetApiNid = "1", // ApiServer B가 ApiServer A에게 SendToApi
            Message = testMessage
        };

        var responsePacket = await _apiServerA!.ApiSender!.RequestToApi(
            "2", // ApiServer B에게 트리거 요청
            CPacket.Of(triggerRequest));

        // 메시지 전달 대기
        await Task.Delay(500);

        // Then
        // 1. 트리거 응답 검증
        responsePacket.MsgId.Should().Be(typeof(TriggerSendToApiServerReply).Name);
        var triggerReply = TriggerSendToApiServerReply.Parser.ParseFrom(responsePacket.Payload.Data.Span);
        triggerReply.Success.Should().BeTrue("trigger should succeed");

        // 2. ApiServer B의 HandleInterApiMessage 콜백 호출됨
        TestApiController.ReceivedMsgIds.Should().Contain(typeof(InterApiMessage).Name,
            "target API handler should be called");

        // 3. 핸들러 호출 횟수 증가
        TestApiController.OnDispatchCallCount.Should().BeGreaterThan(initialCallCount,
            "handlers should be called");
    }

    #endregion
}
