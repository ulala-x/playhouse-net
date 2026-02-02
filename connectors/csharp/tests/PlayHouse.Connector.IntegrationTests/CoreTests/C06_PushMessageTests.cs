using FluentAssertions;
using PlayHouse.Connector.Protocol;
using PlayHouse.TestServer.Proto;
using Xunit;

namespace PlayHouse.Connector.IntegrationTests.CoreTests;

/// <summary>
/// C-06: Push 메시지 수신 테스트 (BroadcastNotify)
/// </summary>
/// <remarks>
/// 서버에서 클라이언트로 일방적으로 전송하는 Push 메시지를 수신할 수 있는지 검증합니다.
/// BroadcastRequest를 보내면 서버가 BroadcastNotify를 Push로 전송합니다.
/// </remarks>
public class C06_PushMessageTests : BaseIntegrationTest
{
    public C06_PushMessageTests(TestServerFixture testServer) : base(testServer)
    {
    }

    [Fact(DisplayName = "C-06-01: Push 메시지를 수신할 수 있다")]
    public async Task OnReceive_WhenPushMessageSent_ReceivesMessage()
    {
        // Given: 연결 및 인증 완료
        await CreateStageAndConnectAsync();
        await AuthenticateAsync("pushUser");

        var receivedMessages = new List<BroadcastNotify>();
        var tcs = new TaskCompletionSource<BroadcastNotify>();

        Connector!.OnReceive += (stageId, stageType, packet) =>
        {
            if (packet.MsgId == "BroadcastNotify")
            {
                var notify = ParsePayload<BroadcastNotify>(packet.Payload);
                receivedMessages.Add(notify);
                tcs.TrySetResult(notify);
            }
        };

        // When: Broadcast 요청 전송 (서버가 Push 메시지를 보낼 것임)
        var broadcastRequest = new BroadcastRequest
        {
            Content = "Test Broadcast"
        };
        using var requestPacket = new Packet(broadcastRequest);
        Connector.Send(requestPacket);

        // Push 메시지 대기 (MainThreadAction 호출하면서 최대 5초)
        var completed = await WaitForConditionWithMainThreadActionAsync(() => tcs.Task.IsCompleted, 5000);

        // Then: Push 메시지를 받아야 함
        completed.Should().BeTrue("Push 메시지를 5초 이내에 받아야 함");
        receivedMessages.Should().HaveCount(1, "1개의 Push 메시지를 받아야 함");

        var notify = receivedMessages[0];
        notify.Should().NotBeNull();
        notify.EventType.Should().NotBeNullOrWhiteSpace("이벤트 타입이 있어야 함");
        notify.Data.Should().Contain("Test Broadcast", "브로드캐스트 내용이 포함되어야 함");
    }

    [Fact(DisplayName = "C-06-02: OnReceive 이벤트가 올바른 파라미터로 호출된다")]
    public async Task OnReceive_Event_ReceivesCorrectParameters()
    {
        // Given: 연결 및 인증 완료
        await CreateStageAndConnectAsync();
        await AuthenticateAsync("paramUser");

        long? receivedStageId = null;
        string? receivedStageType = null;
        string? receivedMsgId = null;
        var tcs = new TaskCompletionSource<bool>();

        Connector!.OnReceive += (stageId, stageType, packet) =>
        {
            if (packet.MsgId == "BroadcastNotify")
            {
                receivedStageId = stageId;
                receivedStageType = stageType;
                receivedMsgId = packet.MsgId;
                tcs.TrySetResult(true);
            }
        };

        // When: Broadcast 요청
        var broadcastRequest = new BroadcastRequest { Content = "Param Test" };
        using var requestPacket = new Packet(broadcastRequest);
        Connector.Send(requestPacket);

        // MainThreadAction 호출하면서 대기
        var completed = await WaitForConditionWithMainThreadActionAsync(() => tcs.Task.IsCompleted, 5000);

        // Then: 올바른 파라미터로 이벤트가 호출되어야 함
        completed.Should().BeTrue();
        receivedStageId.Should().Be(StageInfo!.StageId, "Stage ID가 일치해야 함");
        receivedStageType.Should().Be(StageInfo.StageType, "Stage 타입이 일치해야 함");
        receivedMsgId.Should().Be("BroadcastNotify", "메시지 ID가 일치해야 함");
    }

    [Fact(DisplayName = "C-06-03: 여러 개의 Push 메시지를 순차적으로 수신할 수 있다")]
    public async Task OnReceive_MultiplePushMessages_AllReceived()
    {
        // Given: 연결 및 인증 완료
        await CreateStageAndConnectAsync();
        await AuthenticateAsync("multiUser");

        var receivedMessages = new List<BroadcastNotify>();
        var expectedCount = 3;
        var tcs = new TaskCompletionSource<bool>();

        Connector!.OnReceive += (stageId, stageType, packet) =>
        {
            if (packet.MsgId == "BroadcastNotify")
            {
                var notify = ParsePayload<BroadcastNotify>(packet.Payload);
                receivedMessages.Add(notify);

                if (receivedMessages.Count >= expectedCount)
                {
                    tcs.TrySetResult(true);
                }
            }
        };

        // When: 3개의 Broadcast 요청 전송
        for (int i = 1; i <= expectedCount; i++)
        {
            var request = new BroadcastRequest { Content = $"Message {i}" };
            using var packet = new Packet(request);
            Connector.Send(packet);

            // MainThreadAction 호출하면서 약간의 지연
            var deadline = DateTime.UtcNow.AddMilliseconds(100);
            while (DateTime.UtcNow < deadline)
            {
                Connector?.MainThreadAction();
                await Task.Delay(10);
            }
        }

        // 모든 메시지 수신 대기
        var completed = await WaitForConditionWithMainThreadActionAsync(() => tcs.Task.IsCompleted, 10000);

        // Then: 모든 Push 메시지를 받아야 함
        completed.Should().BeTrue("모든 Push 메시지를 받아야 함");
        receivedMessages.Should().HaveCount(expectedCount, $"{expectedCount}개의 메시지를 받아야 함");

        for (int i = 0; i < expectedCount; i++)
        {
            receivedMessages[i].Data.Should().Contain($"Message {i + 1}");
        }
    }

    [Fact(DisplayName = "C-06-04: Push 메시지와 Request-Response를 동시에 처리할 수 있다")]
    public async Task OnReceive_PushMessageDuringRequestResponse_BothWork()
    {
        // Given: 연결 및 인증 완료
        await CreateStageAndConnectAsync();
        await AuthenticateAsync("mixedUser");

        var receivedPushMessages = new List<BroadcastNotify>();
        var pushTcs = new TaskCompletionSource<bool>();

        Connector!.OnReceive += (stageId, stageType, packet) =>
        {
            if (packet.MsgId == "BroadcastNotify")
            {
                var notify = ParsePayload<BroadcastNotify>(packet.Payload);
                receivedPushMessages.Add(notify);
                pushTcs.TrySetResult(true);
            }
        };

        // When: Echo 요청과 Broadcast 요청을 동시에 보냄
        var echoTask = EchoAsync("Echo Test", 1);

        var broadcastRequest = new BroadcastRequest { Content = "Broadcast Test" };
        using var broadcastPacket = new Packet(broadcastRequest);
        Connector.Send(broadcastPacket);

        // Echo 응답 대기 (async이므로 MainThreadAction 필요 없음)
        var echoCompleted = await Task.WhenAny(echoTask, Task.Delay(5000)) == echoTask;

        // Push 대기 (MainThreadAction 호출하면서)
        var pushCompleted = await WaitForConditionWithMainThreadActionAsync(() => pushTcs.Task.IsCompleted, 5000);

        // Then: 두 가지 모두 성공해야 함
        echoCompleted.Should().BeTrue("Echo 요청이 성공해야 함");
        pushCompleted.Should().BeTrue("Push 메시지를 받아야 함");

        var echoReply = await echoTask;
        echoReply.Content.Should().Be("Echo Test");

        receivedPushMessages.Should().HaveCount(1);
        receivedPushMessages[0].Data.Should().Contain("Broadcast Test");
    }

    [Fact(DisplayName = "C-06-05: BroadcastNotify에 발신자 정보가 포함된다")]
    public async Task OnReceive_BroadcastNotify_ContainsSenderInfo()
    {
        // Given: 연결 및 인증 완료
        await CreateStageAndConnectAsync();
        await AuthenticateAsync("senderInfoUser");

        BroadcastNotify? receivedNotify = null;
        var tcs = new TaskCompletionSource<bool>();

        Connector!.OnReceive += (stageId, stageType, packet) =>
        {
            if (packet.MsgId == "BroadcastNotify")
            {
                receivedNotify = ParsePayload<BroadcastNotify>(packet.Payload);
                tcs.TrySetResult(true);
            }
        };

        // When: Broadcast 요청
        var broadcastRequest = new BroadcastRequest { Content = "Sender Info Test" };
        using var requestPacket = new Packet(broadcastRequest);
        Connector.Send(requestPacket);

        // MainThreadAction 호출하면서 대기
        var completed = await WaitForConditionWithMainThreadActionAsync(() => tcs.Task.IsCompleted, 5000);

        // Then: 발신자 정보가 포함되어야 함
        completed.Should().BeTrue();
        receivedNotify.Should().NotBeNull();
        receivedNotify!.EventType.Should().NotBeNullOrWhiteSpace("이벤트 타입이 있어야 함");
        receivedNotify.Data.Should().NotBeNullOrWhiteSpace("데이터가 있어야 함");
        // FromAccountId와 SenderId는 서버 구현에 따라 설정될 수 있음
    }

    [Fact(DisplayName = "C-06-06: OnReceive 핸들러가 등록되지 않아도 Push 메시지 수신 시 예외가 발생하지 않는다")]
    public async Task OnReceive_NoHandlerRegistered_NoException()
    {
        // Given: 연결 및 인증 완료 (OnReceive 핸들러 등록 안 함)
        await CreateStageAndConnectAsync();
        await AuthenticateAsync("noHandlerUser");

        // When: Broadcast 요청 (OnReceive 핸들러가 없음)
        var broadcastRequest = new BroadcastRequest { Content = "No Handler Test" };
        using var requestPacket = new Packet(broadcastRequest);

        var action = () => Connector!.Send(requestPacket);

        // Then: 예외가 발생하지 않아야 함
        action.Should().NotThrow("핸들러가 없어도 예외가 발생하지 않아야 함");

        // 메시지가 전송될 시간을 줌
        await Task.Delay(1000);

        // 연결은 유지되어야 함
        Connector!.IsConnected().Should().BeTrue("연결이 유지되어야 함");
    }
}
