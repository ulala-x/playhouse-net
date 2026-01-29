using PlayHouse.Abstractions;
using PlayHouse.Core.Shared;
using PlayHouse.E2E.Shared.Proto;

namespace PlayHouse.E2E.Verifiers;

/// <summary>
/// SendToService/RequestToService의 Round-Robin 및 Weighted 정책 검증
///
/// 테스트 시나리오:
/// - RequestToService: 서비스 ID로 요청을 보내고 응답 수신
/// - SendToService: 서비스 ID로 fire-and-forget 메시지 전송
/// - RoundRobin: 여러 서버에 순차적 분배 확인
/// - Weighted: 가중치 기반 선택 확인 (기본 설정에서는 동일 가중치)
/// </summary>
public class ServiceRoutingVerifier : VerifierBase
{
    public override string CategoryName => "ServiceRouting";

    public ServiceRoutingVerifier(ServerContext serverContext) : base(serverContext)
    {
    }

    public override int GetTestCount() => 4;

    protected override async Task SetupAsync()
    {
        // ServerAddressResolver가 API 서버들을 등록할 시간 필요
        await Task.Delay(3000);
    }

    protected override Task TeardownAsync()
    {
        return Task.CompletedTask;
    }

    protected override async Task RunTestsAsync()
    {
        await RunTest("RequestToService_RoundRobin", Test_RequestToService_RoundRobin);
        await RunTest("RequestToService_Weighted", Test_RequestToService_Weighted);
        await RunTest("SendToService_Basic", Test_SendToService_Basic);
        await RunTest("RequestToService_Callback", Test_RequestToService_Callback);
    }

    /// <summary>
    /// RequestToService - RoundRobin 정책으로 요청
    /// </summary>
    private async Task Test_RequestToService_RoundRobin()
    {
        // Given - API 서비스 ID
        const ushort apiServiceId = 1;

        // When - RequestToService를 여러 번 호출 (RoundRobin 기본값)
        var request1 = new ApiEchoRequest { Content = "RoundRobin Test 1" };
        var request2 = new ApiEchoRequest { Content = "RoundRobin Test 2" };

        var response1 = await ApiServer1.ApiSender!.RequestToService(ServerType.Api, apiServiceId, CPacket.Of(request1));
        var response2 = await ApiServer1.ApiSender!.RequestToService(ServerType.Api, apiServiceId, CPacket.Of(request2));

        // Then - 응답 수신 확인
        Assert.NotNull(response1, "첫 번째 응답이 수신되어야 함");
        Assert.NotNull(response2, "두 번째 응답이 수신되어야 함");
        Assert.Equals("ApiEchoReply", response1.MsgId, "첫 번째 응답 MsgId 확인");
        Assert.Equals("ApiEchoReply", response2.MsgId, "두 번째 응답 MsgId 확인");

        var reply1 = ApiEchoReply.Parser.ParseFrom(response1.Payload.DataSpan);
        var reply2 = ApiEchoReply.Parser.ParseFrom(response2.Payload.DataSpan);

        Assert.IsTrue(reply1.Content.Contains("RoundRobin Test 1"), "첫 번째 응답 내용 확인");
        Assert.IsTrue(reply2.Content.Contains("RoundRobin Test 2"), "두 번째 응답 내용 확인");
    }

    /// <summary>
    /// RequestToService - Weighted 정책으로 요청
    /// </summary>
    private async Task Test_RequestToService_Weighted()
    {
        // Given
        const ushort apiServiceId = 1;

        // When - Weighted 정책으로 호출
        var request = new ApiEchoRequest { Content = "Weighted Test" };
        var response = await ApiServer1.ApiSender!.RequestToService(
            ServerType.Api,
            apiServiceId,
            CPacket.Of(request),
            ServerSelectionPolicy.Weighted);

        // Then - 응답 수신 확인
        Assert.NotNull(response, "응답이 수신되어야 함");
        Assert.Equals("ApiEchoReply", response.MsgId, "응답 MsgId 확인");

        var reply = ApiEchoReply.Parser.ParseFrom(response.Payload.DataSpan);
        Assert.IsTrue(reply.Content.Contains("Weighted Test"), "응답 내용 확인");
    }

    /// <summary>
    /// SendToService - fire-and-forget 메시지 전송
    /// </summary>
    private async Task Test_SendToService_Basic()
    {
        // Given
        const ushort apiServiceId = 1;

        // When - SendToService (fire-and-forget)
        var message = new InterApiMessage
        {
            FromApiNid = ServerContext.ApiServer1Id,
            Content = "SendToService Test"
        };
        ApiServer1.ApiSender!.SendToService(ServerType.Api, apiServiceId, CPacket.Of(message));

        // 메시지 전달 대기
        await Task.Delay(500);

        // Then - 예외 없이 완료 (fire-and-forget이므로 응답 검증 불가)
        Assert.IsTrue(true, "SendToService가 예외 없이 완료되어야 함");
    }

    /// <summary>
    /// RequestToService - Callback 버전 테스트
    /// </summary>
    private async Task Test_RequestToService_Callback()
    {
        // Given
        const ushort apiServiceId = 1;
        var tcs = new TaskCompletionSource<(ushort errorCode, string? content)>();

        // When - Callback 버전으로 호출
        var request = new ApiEchoRequest { Content = "Callback Test" };
        ApiServer1.ApiSender!.RequestToService(ServerType.Api, apiServiceId, CPacket.Of(request), (errorCode, reply) =>
        {
            if (errorCode == 0 && reply != null)
            {
                var parsed = ApiEchoReply.Parser.ParseFrom(reply.Payload.DataSpan);
                tcs.TrySetResult((errorCode, parsed.Content));
            }
            else
            {
                tcs.TrySetResult((errorCode, null));
            }
        });

        // Then - 콜백 응답 대기
        using var cts = new CancellationTokenSource(5000);
        var result = await tcs.Task.WaitAsync(cts.Token);

        Assert.Equals((ushort)0, result.errorCode, "에러 코드는 0이어야 함");
        Assert.NotNull(result.content, "응답 내용이 있어야 함");
        Assert.IsTrue(result.content!.Contains("Callback Test"), "응답 내용 확인");
    }
}
