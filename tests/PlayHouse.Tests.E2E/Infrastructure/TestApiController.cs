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
        register.Add("ApiEchoRequest", HandleApiEcho);
        register.Add("TriggerCreateStageRequest", HandleCreateStage);
        register.Add("TriggerGetOrCreateStageRequest", HandleGetOrCreateStage);
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
}
