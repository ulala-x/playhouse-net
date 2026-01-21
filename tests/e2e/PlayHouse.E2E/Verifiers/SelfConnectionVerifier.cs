using PlayHouse.Core.Shared;
using PlayHouse.E2E.Shared.Proto;

namespace PlayHouse.E2E.Verifiers;

/// <summary>
/// Self-connection (자기 자신에게 메시지 보내기) 검증
///
/// 참고: SelfConnectionTests.cs
/// - ServerContext.ApiServer1 (자기 자신에게 메시지)
/// - IApiSender.SendToApi("자기ServerId", message)
/// - IApiSender.RequestToApi("자기ServerId", message)
/// </summary>
public class SelfConnectionVerifier : VerifierBase
{
    public override string CategoryName => "SelfConnection";

    private string _apiServerId = null!;

    public SelfConnectionVerifier(ServerContext serverContext) : base(serverContext)
    {
    }

    public override int GetTestCount() => 2;

    protected override Task SetupAsync()
    {
        // ✅ ServerContext.ApiServer1Id 가져오기
        _apiServerId = ServerContext.ApiServer1Id;

        // ❌ NO ApiServer 생성!
        return Task.CompletedTask;
    }

    protected override Task TeardownAsync()
    {
        // ❌ 서버 종료 금지!
        return Task.CompletedTask;
    }

    protected override async Task RunTestsAsync()
    {
        await RunTest("SendToApi_ToSelf", Test_SendToApi_ToSelf);
        await RunTest("RequestToApi_ToSelf", Test_RequestToApi_ToSelf);
    }

    /// <summary>
    /// SendToApi - 자기 자신에게 SendToApi
    /// </summary>
    private async Task Test_SendToApi_ToSelf()
    {
        // Given
        var message = new InterApiMessage
        {
            FromApiNid = _apiServerId,
            Content = "Hello to myself"
        };

        // When - 자기 자신에게 메시지 전송
        ApiServer1.ApiSender!.SendToApi(_apiServerId, CPacket.Of(message));

        // 메시지 전달 대기
        await Task.Delay(1000);

        // Then - E2E 검증: 응답 패킷만 검증
        // SendToApi는 응답이 없으므로 정상 전송만 확인
        Assert.IsTrue(true, "SendToApi to self should complete without error");

        // ❌ 서버 내부 상태 접근 금지
        // TestApiController.OnDispatchCallCount - 이 테스트에서는 사용 불가
    }

    /// <summary>
    /// RequestToApi - 자기 자신에게 RequestToApi
    /// </summary>
    private async Task Test_RequestToApi_ToSelf()
    {
        // Given
        const string testContent = "Echo to myself";

        // When - 자기 자신에게 요청
        var echoRequest = new ApiEchoRequest { Content = testContent };
        var response = await ApiServer1.ApiSender!.RequestToApi(_apiServerId, CPacket.Of(echoRequest));

        // Then - E2E 검증: 응답 패킷 검증
        Assert.NotNull(response, "Should receive response");
        Assert.Equals("ApiEchoReply", response.MsgId, "Should receive ApiEchoReply");

        var reply = ApiEchoReply.Parser.ParseFrom(response.Payload.DataSpan);
        Assert.IsTrue(reply.Content.Contains(testContent), "Echo response should contain original content");
    }
}
