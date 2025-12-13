#nullable enable

using FluentAssertions;
using PlayHouse.Bootstrap;
using PlayHouse.Core.Shared;
using PlayHouse.Tests.Integration.Infrastructure;
using PlayHouse.Tests.Integration.Proto;
using Xunit;

namespace PlayHouse.Tests.Integration.Api;

/// <summary>
/// Self-connection (자기 자신에게 메시지 보내기) 테스트
/// </summary>
[Collection("E2E SelfConnection Tests")]
public class SelfConnectionTests : IAsyncLifetime
{
    private ApiServer? _apiServer;

    public async Task InitializeAsync()
    {
        TestApiController.ResetAll();
        TestSystemController.Reset();

        _apiServer = new ApiServerBootstrap()
            .Configure(options =>
            {
                options.ServerId = "1";
                options.BindEndpoint = "tcp://127.0.0.1:15300";
                options.RequestTimeoutMs = 5000;
            })
            .UseController<TestApiController>()
            .Build();

        await _apiServer.StartAsync();
        await Task.Delay(500);
    }

    public async Task DisposeAsync()
    {
        if (_apiServer != null)
        {
            await _apiServer.DisposeAsync();
        }
    }

    /// <summary>
    /// 자기 자신에게 SendToApi 테스트
    /// </summary>
    [Fact(DisplayName = "Self-connection - 자기 자신에게 SendToApi")]
    public async Task SendToApi_ToSelf_MessageReceived()
    {
        // Given
        var initialCount = TestApiController.OnDispatchCallCount;

        // When - 자기 자신("1")에게 메시지 전송
        var message = new InterApiMessage
        {
            FromApiNid = "1",
            Content = "Hello to myself"
        };
        _apiServer!.ApiSender!.SendToApi("1", CPacket.Of(message));

        // 메시지 전달 대기
        await Task.Delay(1000);

        // Then
        TestApiController.OnDispatchCallCount.Should().BeGreaterThan(initialCount,
            "자기 자신에게 보낸 메시지가 수신되어야 함");
        TestApiController.ReceivedMsgIds.Should().Contain(typeof(InterApiMessage).Name,
            "InterApiMessage가 수신되어야 함");
    }

    /// <summary>
    /// 자기 자신에게 RequestToApi 테스트
    /// </summary>
    [Fact(DisplayName = "Self-connection - 자기 자신에게 RequestToApi")]
    public async Task RequestToApi_ToSelf_ResponseReceived()
    {
        // Given
        const string testContent = "Echo to myself";

        // When - 자기 자신("1")에게 요청
        var echoRequest = new ApiEchoRequest { Content = testContent };
        var response = await _apiServer!.ApiSender!.RequestToApi("1", CPacket.Of(echoRequest));

        // Then
        response.Should().NotBeNull("응답을 받아야 함");
        response.MsgId.Should().Be(typeof(ApiEchoReply).Name, "ApiEchoReply여야 함");

        var reply = ApiEchoReply.Parser.ParseFrom(response.Payload.Data.Span);
        reply.Content.Should().Contain(testContent, "에코 응답이어야 함");
    }
}
