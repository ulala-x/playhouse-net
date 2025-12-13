#nullable enable

using System.Collections.Concurrent;
using PlayHouse.Abstractions;
using PlayHouse.Abstractions.Api;
using PlayHouse.Core.Shared;
using PlayHouse.Tests.E2E.Proto;

namespace PlayHouse.Tests.E2E.Infrastructure;

/// <summary>
/// E2E 테스트용 API Controller.
/// </summary>
public class TestApiController : IApiController
{
    public static ConcurrentBag<string> ReceivedMsgIds { get; } = new();
    public static int OnDispatchCallCount => _onDispatchCallCount;
    private static int _onDispatchCallCount;
    public static ConcurrentBag<TestApiController> Instances { get; } = new();

    public TestApiController()
    {
        Instances.Add(this);
    }

    public static void ResetAll()
    {
        while (ReceivedMsgIds.TryTake(out _)) { }
        while (Instances.TryTake(out _)) { }
        Interlocked.Exchange(ref _onDispatchCallCount, 0);
    }

    public void Handles(IHandlerRegister register)
    {
        register.Add(typeof(ApiEchoRequest).FullName!, HandleApiEcho);
        register.Add(typeof(TriggerCreateStageRequest).FullName!, HandleCreateStage);
        register.Add(typeof(TriggerGetOrCreateStageRequest).FullName!, HandleGetOrCreateStage);
        register.Add(typeof(TriggerSendToApiServerRequest).FullName!, HandleSendToApiServer);
        register.Add(typeof(TriggerRequestToApiServerRequest).FullName!, HandleRequestToApiServer);
        register.Add(typeof(InterApiMessage).FullName!, HandleInterApiMessage);
    }

    private async Task HandleApiEcho(IPacket packet, IApiSender sender)
    {
        ReceivedMsgIds.Add(packet.MsgId);
        Interlocked.Increment(ref _onDispatchCallCount);

        var request = ApiEchoRequest.Parser.ParseFrom(packet.Payload.Data.Span);
        var reply = new ApiEchoReply { Content = $"Echo: {request.Content}" };
        sender.Reply(CPacket.Of(reply));
    }

    private async Task HandleCreateStage(IPacket packet, IApiSender sender)
    {
        ReceivedMsgIds.Add(packet.MsgId);
        Interlocked.Increment(ref _onDispatchCallCount);

        var request = TriggerCreateStageRequest.Parser.ParseFrom(packet.Payload.Data.Span);

        // PlayServer NID는 "1:1" (ServiceId=1, ServerId=1)
        const string playNid = "1:1";
        var result = await sender.CreateStage(
            playNid,
            request.StageType,
            request.StageId,
            CPacket.Empty("CreateStagePayload"));

        var reply = new TriggerCreateStageReply
        {
            Success = result.Result
        };
        sender.Reply(CPacket.Of(reply));
    }

    private async Task HandleGetOrCreateStage(IPacket packet, IApiSender sender)
    {
        ReceivedMsgIds.Add(packet.MsgId);
        Interlocked.Increment(ref _onDispatchCallCount);

        var request = TriggerGetOrCreateStageRequest.Parser.ParseFrom(packet.Payload.Data.Span);

        const string playNid = "1:1";
        var result = await sender.GetOrCreateStage(
            playNid,
            request.StageType,
            request.StageId,
            CPacket.Empty("CreatePayload"),
            CPacket.Empty("JoinPayload"));

        var reply = new TriggerGetOrCreateStageReply
        {
            Success = result.Result,
            IsCreated = result.IsCreated
        };
        sender.Reply(CPacket.Of(reply));
    }

    private async Task HandleSendToApiServer(IPacket packet, IApiSender sender)
    {
        ReceivedMsgIds.Add(packet.MsgId);
        Interlocked.Increment(ref _onDispatchCallCount);

        var request = TriggerSendToApiServerRequest.Parser.ParseFrom(packet.Payload.Data.Span);

        // SendToApi (비동기, 응답 없음)
        var message = new InterApiMessage
        {
            FromApiNid = "sender-api",
            Content = request.Message
        };
        sender.SendToApi(request.TargetApiNid, CPacket.Of(message));

        var reply = new TriggerSendToApiServerReply { Success = true };
        sender.Reply(CPacket.Of(reply));
    }

    private async Task HandleRequestToApiServer(IPacket packet, IApiSender sender)
    {
        ReceivedMsgIds.Add(packet.MsgId);
        Interlocked.Increment(ref _onDispatchCallCount);

        var request = TriggerRequestToApiServerRequest.Parser.ParseFrom(packet.Payload.Data.Span);

        // RequestToApi (동기, 응답 있음)
        var message = new InterApiMessage
        {
            FromApiNid = "sender-api",
            Content = request.Query
        };
        var responsePacket = await sender.RequestToApi(request.TargetApiNid, CPacket.Of(message));
        var response = InterApiReply.Parser.ParseFrom(responsePacket.Payload.Data.Span);

        var reply = new TriggerRequestToApiServerReply
        {
            Response = response.Response
        };
        sender.Reply(CPacket.Of(reply));
    }

    private async Task HandleInterApiMessage(IPacket packet, IApiSender sender)
    {
        ReceivedMsgIds.Add(packet.MsgId);
        Interlocked.Increment(ref _onDispatchCallCount);

        var request = InterApiMessage.Parser.ParseFrom(packet.Payload.Data.Span);

        // 다른 API 서버에서 온 메시지 처리
        var reply = new InterApiReply
        {
            Response = $"Processed: {request.Content} from {request.FromApiNid}"
        };
        sender.Reply(CPacket.Of(reply));
    }
}
