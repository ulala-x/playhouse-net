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

[Collection("E2E ApiPlayServer")]
public class StageToApiTests : IAsyncLifetime
{
    private readonly ApiPlayServerFixture _fixture;
    private PlayServer? PlayServer => _fixture.PlayServer;
    private ApiServer? ApiServer => _fixture.ApiServer;
    private readonly ClientConnector _connector;
    private readonly List<(long stageId, string stageType, string msgId, byte[] payloadData)> _receivedMessages = new();
    private readonly object _callbackLock = new();

    private const long StageId = 33333L;

    public StageToApiTests(ApiPlayServerFixture fixture)
    {
        _fixture = fixture;
        _connector = new ClientConnector();
        _connector.OnReceive += (stageId, stageType, packet) =>
        {
            Console.WriteLine($"[Client] Received Push Message: {packet.MsgId}");
            var msgId = packet.MsgId;
            var payloadData = packet.Payload.DataSpan.ToArray();
            lock (_receivedMessages) _receivedMessages.Add((stageId, stageType, msgId, payloadData));
        };
    }

    public async Task InitializeAsync()
    {
        TestActorImpl.ResetAll();
        TestStageImpl.ResetAll();
        TestApiController.ResetAll();
        await Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        _connector.Disconnect();
        await Task.CompletedTask;
    }

    #region SendToApi 테스트

    [Fact(DisplayName = "SendToApi - Stage에서 API로 단방향 메시지 전송 성공")]
    public async Task SendToApi_Success_MessageDelivered()
    {
        await ConnectAndAuthenticateAsync(_connector, StageId);
        var initialApiCallCount = TestApiController.OnDispatchCallCount;

        var request = new TriggerSendToApiRequest { Message = "Hello from Stage" };
        using var packet = new Packet(request);
        
        var responseTask = _connector.RequestAsync(packet);
        var response = await WaitForResponse(responseTask);

        await Task.Delay(500); 

        response.MsgId.Should().EndWith("TriggerSendToApiReply");
        TestApiController.OnDispatchCallCount.Should().BeGreaterThan(initialApiCallCount);
    }

    #endregion

    #region AsyncBlock & S2S Routing 테스트

    [Fact(DisplayName = "AsyncBlock - PreBlock 내에서 SendToApi 호출 시 API 서버로 메시지 전달 성공")]
    public async Task AsyncBlock_SendToApi_Success()
    {
        await ConnectAndAuthenticateAsync(_connector, StageId);
        var initialApiCallCount = TestApiController.OnDispatchCallCount;

        var request = new TriggerAsyncBlockSendToApiRequest { Message = "Hello from AsyncBlock" };
        using var packet = new Packet(request);
        var response = await WaitForResponse(_connector.RequestAsync(packet));

        response.MsgId.Should().EndWith("TriggerAsyncBlockSendToApiAccepted");

        await Task.Delay(500);
        for (int i = 0; i < 10; i++) { lock (_callbackLock) _connector.MainThreadAction(); await Task.Delay(50); }

        TestApiController.OnDispatchCallCount.Should().BeGreaterThan(initialApiCallCount);
    }

    [Fact(DisplayName = "S2S 라우팅 - StageId 정보가 API로 전달되고 SendToStage 응답 수신 성공")]
    public async Task S2S_DirectRouting_Success()
    {
        await ConnectAndAuthenticateAsync(_connector, StageId);
        
        using var packet = Packet.Empty("TriggerApiDirectEcho");
        var response = await WaitForResponse(_connector.RequestAsync(packet));
        response.MsgId.Should().EndWith("Accepted");

        bool received = false;
        var timeout = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < timeout)
        {
            lock (_callbackLock) _connector.MainThreadAction();
            if (TestStageImpl.AllReceivedMsgIds.Contains("ApiDirectEchoReply_Success")) { received = true; break; }
            await Task.Delay(100);
        }

        received.Should().BeTrue("API 서버가 보낸 SendToStage 메시지가 스테이지에 도착해야 함");
    }

    [Fact(DisplayName = "AsyncBlock - PreBlock 내에서 RequestToApi 호출 및 PostBlock 실행 성공")]
    public async Task AsyncBlock_RequestToApi_Success()
    {
        await ConnectAndAuthenticateAsync(_connector, StageId);
        
        var request = new TriggerAsyncBlockRequestToApiRequest { Query = "Async Request Query" };
        using var packet = new Packet(request);
        await WaitForResponse(_connector.RequestAsync(packet));

        // 최종 Push 응답 대기 (전역 _receivedMessages 로그 확인)
        TriggerAsyncBlockRequestToApiReply? finalReply = null;
        var timeout = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < timeout)
        {
            lock (_callbackLock) { _connector.MainThreadAction(); }
            
            lock (_receivedMessages)
            {
                var msg = _receivedMessages.FirstOrDefault(m => m.msgId.Contains("TriggerAsyncBlockRequestToApiReply"));
                if (!msg.Equals(default))
                {
                    finalReply = TriggerAsyncBlockRequestToApiReply.Parser.ParseFrom(msg.payloadData);
                    break;
                }
            }
            await Task.Delay(100);
        }

        finalReply.Should().NotBeNull("PostBlock에서 보낸 Push 메시지를 수신해야 함");
        finalReply!.ApiResponse.Should().Contain("Async Request Query");
        finalReply.PostBlockCalled.Should().BeTrue();
    }

    #endregion

    #region Basic S2S 테스트

    [Fact(DisplayName = "S2S 기본 - OnDispatch 내에서 RequestToApi 호출 및 응답 성공")]
    public async Task S2S_BasicRequestReply_Success()
    {
        await ConnectAndAuthenticateAsync(_connector, StageId);

        var request = new TriggerRequestToApiRequest { Query = "Basic S2S Test" };
        using var packet = new Packet(request);
        var response = await WaitForResponse(_connector.RequestAsync(packet));

        var reply = TriggerRequestToApiReply.Parser.ParseFrom(response.Payload.DataSpan);
        reply.ApiResponse.Should().Contain("Basic S2S Test");
    }

    #endregion

    #region Helper Methods

    private async Task<PlayHouse.Connector.Protocol.IPacket> WaitForResponse(Task<PlayHouse.Connector.Protocol.IPacket> task)
    {
        var timeout = DateTime.UtcNow.AddSeconds(10);
        while (!task.IsCompleted && DateTime.UtcNow < timeout)
        {
            lock (_callbackLock) _connector.MainThreadAction();
            await Task.Delay(20);
        }
        return await task;
    }

    private async Task ConnectAndAuthenticateAsync(ClientConnector connector, long stageId)
    {
        connector.Init(new ConnectorConfig { RequestTimeoutMs = 30000 });
        var connected = await connector.ConnectAsync("127.0.0.1", PlayServer!.ActualTcpPort, stageId, "TestStage");
        connected.Should().BeTrue();

        var authRequest = new AuthenticateRequest { UserId = $"user-{stageId}", Token = "token" };
        using var authPacket = new Packet(authRequest);
        await WaitForResponse(connector.AuthenticateAsync(authPacket));
    }

    #endregion
}
