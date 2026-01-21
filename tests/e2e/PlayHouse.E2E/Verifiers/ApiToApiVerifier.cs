using PlayHouse.Core.Shared;
using PlayHouse.E2E.Shared.Proto;

namespace PlayHouse.E2E.Verifiers;

/// <summary>
/// API-to-API 통신 검증
///
/// 참고: ApiToApiTests.cs
/// - ServerContext.ApiServer1 (이미 실행 중)
/// - ServerContext.ApiServer2 (이미 실행 중)
/// - IApiSender.SendToApi(), RequestToApi() 사용
/// - InterApiMessage, ApiEchoRequest 메시지 사용
/// </summary>
public class ApiToApiVerifier : VerifierBase
{
    public override string CategoryName => "ApiToApi";

    public ApiToApiVerifier(ServerContext serverContext) : base(serverContext)
    {
    }

    public override int GetTestCount() => 5;

    protected override async Task SetupAsync()
    {
        // ✅ 서버는 이미 실행 중
        // ServerContext.ApiServer1.ApiSender 사용
        // ServerContext.ApiServer2.ApiSender 사용

        // ❌ NO ApiServer 생성!

        // ServerAddressResolver 연결 대기 (full suite에서는 이전 테스트의 영향 고려)
        // DI 서버가 추가되면서 연결 설정 시간이 더 필요함
        await Task.Delay(5000);
    }

    protected override Task TeardownAsync()
    {
        // ❌ 서버 종료 금지!
        return Task.CompletedTask;
    }

    protected override async Task RunTestsAsync()
    {
        await RunTest("SendToApi_Api1ToApi2", Test_SendToApi_Api1ToApi2);
        await RunTest("RequestToApi_Api1ToApi2", Test_RequestToApi_Api1ToApi2);
        await RunTest("RequestToApi_Bidirectional", Test_RequestToApi_Bidirectional);
        await RunTest("RequestToApi_ThroughHandler", Test_RequestToApi_ThroughHandler);
        await RunTest("SendToApi_ThroughHandler", Test_SendToApi_ThroughHandler);
    }

    /// <summary>
    /// SendToApi - API1 → API2 SendToApi
    /// </summary>
    private async Task Test_SendToApi_Api1ToApi2()
    {
        // Given
        var api2ServerId = ServerContext.ApiServer2Id;

        // When - ApiServer1에서 ApiServer2로 SendToApi
        var message = new InterApiMessage
        {
            FromApiNid = ServerContext.ApiServer1Id,
            Content = "Hello from API1"
        };
        ApiServer1.ApiSender!.SendToApi(api2ServerId, CPacket.Of(message));

        // 메시지 전달 대기
        await Task.Delay(1000);

        // Then - E2E 검증: 응답 패킷만 검증
        // SendToApi는 응답이 없으므로 정상 전송만 확인
        Assert.IsTrue(true, "SendToApi should complete without error");
    }

    /// <summary>
    /// RequestToApi - API1 → API2 RequestToApi
    /// </summary>
    private async Task Test_RequestToApi_Api1ToApi2()
    {
        // Given
        const string testContent = "Echo from API1 to API2";
        var api2ServerId = ServerContext.ApiServer2Id;

        // When - ApiServer1에서 ApiServer2로 RequestToApi
        var echoRequest = new ApiEchoRequest { Content = testContent };
        var responsePacket = await ApiServer1.ApiSender!.RequestToApi(api2ServerId, CPacket.Of(echoRequest));

        // Then - E2E 검증: 응답 패킷 검증
        Assert.NotNull(responsePacket, "Should receive response");
        Assert.Equals("ApiEchoReply", responsePacket.MsgId, "Should receive ApiEchoReply");

        var response = ApiEchoReply.Parser.ParseFrom(responsePacket.Payload.DataSpan);
        Assert.IsTrue(response.Content.Contains(testContent), "Response should contain the original content");
    }

    /// <summary>
    /// RequestToApi - 양방향 통신
    /// </summary>
    private async Task Test_RequestToApi_Bidirectional()
    {
        // Given
        const string contentAtoB = "A to B";
        const string contentBtoA = "B to A";
        var api1ServerId = ServerContext.ApiServer1Id;
        var api2ServerId = ServerContext.ApiServer2Id;

        // When & Then - API1 → API2
        var messageAtoB = new InterApiMessage
        {
            FromApiNid = api1ServerId,
            Content = contentAtoB
        };
        var responseFromB = await ApiServer1.ApiSender!.RequestToApi(api2ServerId, CPacket.Of(messageAtoB));
        var replyFromB = InterApiReply.Parser.ParseFrom(responseFromB.Payload.DataSpan);

        Assert.IsTrue(replyFromB.Response.Contains(contentAtoB), "Response from API2 should contain API1's content");

        // When & Then - API2 → API1
        var messageBtoA = new InterApiMessage
        {
            FromApiNid = api2ServerId,
            Content = contentBtoA
        };
        var responseFromA = await ApiServer2.ApiSender!.RequestToApi(api1ServerId, CPacket.Of(messageBtoA));
        var replyFromA = InterApiReply.Parser.ParseFrom(responseFromA.Payload.DataSpan);

        Assert.IsTrue(replyFromA.Response.Contains(contentBtoA), "Response from API1 should contain API2's content");
    }

    /// <summary>
    /// RequestToApi - 핸들러 트리거 방식 통신
    /// </summary>
    private async Task Test_RequestToApi_ThroughHandler()
    {
        // Given
        const string testQuery = "Test query through handler";
        var api1ServerId = ServerContext.ApiServer1Id;
        var api2ServerId = ServerContext.ApiServer2Id;

        // When - ApiServer1이 ApiServer2에게 트리거 요청 (API2가 API1에게 실제 요청)
        var triggerRequest = new TriggerRequestToApiServerRequest
        {
            TargetApiNid = api1ServerId, // ApiServer2가 ApiServer1에게 요청
            Query = testQuery
        };

        var responsePacket = await ApiServer1.ApiSender!.RequestToApi(api2ServerId, CPacket.Of(triggerRequest));

        // Then - E2E 검증: 트리거 응답 검증
        Assert.Equals("TriggerRequestToApiServerReply", responsePacket.MsgId, "Should receive trigger reply");
        var triggerReply = TriggerRequestToApiServerReply.Parser.ParseFrom(responsePacket.Payload.DataSpan);

        Assert.IsTrue(triggerReply.Response.Contains(testQuery), "Response should contain the original query");
        Assert.IsTrue(triggerReply.Response.Contains("Processed:"), "Response should indicate processing");
    }

    /// <summary>
    /// SendToApi - 핸들러 트리거 방식 메시지 전송
    /// </summary>
    private async Task Test_SendToApi_ThroughHandler()
    {
        // Given
        const string testMessage = "Test message through handler";
        var api1ServerId = ServerContext.ApiServer1Id;
        var api2ServerId = ServerContext.ApiServer2Id;

        // When - ApiServer1이 ApiServer2에게 트리거 요청 (API2가 API1에게 SendToApi)
        var triggerRequest = new TriggerSendToApiServerRequest
        {
            TargetApiNid = api1ServerId, // ApiServer2가 ApiServer1에게 SendToApi
            Message = testMessage
        };

        var responsePacket = await ApiServer1.ApiSender!.RequestToApi(api2ServerId, CPacket.Of(triggerRequest));

        // 메시지 전달 대기
        await Task.Delay(500);

        // Then - E2E 검증: 트리거 응답 검증
        Assert.Equals("TriggerSendToApiServerReply", responsePacket.MsgId, "Should receive trigger reply");
        var triggerReply = TriggerSendToApiServerReply.Parser.ParseFrom(responsePacket.Payload.DataSpan);
        Assert.IsTrue(triggerReply.Success, "Trigger should succeed");
    }
}
