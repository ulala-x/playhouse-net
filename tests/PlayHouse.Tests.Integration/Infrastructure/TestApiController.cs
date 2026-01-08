#nullable enable

using System.Collections.Concurrent;
using PlayHouse.Abstractions;
using PlayHouse.Abstractions.Api;
using PlayHouse.Core.Shared;
using PlayHouse.Tests.Integration.Proto;

namespace PlayHouse.Tests.Integration.Infrastructure;

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
        register.Add(typeof(ApiEchoRequest).Name!, HandleApiEcho);
        register.Add(typeof(ApiDirectEchoRequest).Name!, HandleApiDirectEcho);
        register.Add(typeof(TriggerCreateStageRequest).Name!, HandleCreateStage);
        register.Add(typeof(TriggerGetOrCreateStageRequest).Name!, HandleGetOrCreateStage);
        register.Add(typeof(TriggerSendToApiServerRequest).Name!, HandleSendToApiServer);
        register.Add(typeof(TriggerRequestToApiServerRequest).Name!, HandleRequestToApiServer);
        register.Add(typeof(InterApiMessage).Name!, HandleInterApiMessage);
        register.Add(typeof(BenchmarkApiRequest).Name!, HandleBenchmarkApi);
        register.Add(typeof(TimerApiRequest).Name!, HandleTimerApi);
    }

    private Task HandleApiEcho(IPacket packet, IApiSender sender)
    {
        ReceivedMsgIds.Add(packet.MsgId);
        Interlocked.Increment(ref _onDispatchCallCount);

        var request = ApiEchoRequest.Parser.ParseFrom(packet.Payload.DataSpan);
        var reply = new ApiEchoReply { Content = $"Echo: {request.Content}" };
        sender.Reply(CPacket.Of(reply));
        return Task.CompletedTask;
    }

    private Task HandleApiDirectEcho(IPacket packet, IApiSender sender)
    {
        ReceivedMsgIds.Add(packet.MsgId);
        Interlocked.Increment(ref _onDispatchCallCount);

        var request = ApiDirectEchoRequest.Parser.ParseFrom(packet.Payload.DataSpan);
        var reply = new ApiDirectEchoReply { Message = $"Direct: {request.Message}" };

        // [핵심] StageId 정보가 제대로 왔는지 확인하고 SendToStage 호출
        if (sender.StageId != 0)
        {
            sender.SendToStage(sender.SessionNid, sender.StageId, CPacket.Of(reply));
        }

        return Task.CompletedTask;
    }

    private async Task HandleCreateStage(IPacket packet, IApiSender sender)
    {
        ReceivedMsgIds.Add(packet.MsgId);
        Interlocked.Increment(ref _onDispatchCallCount);

        var request = TriggerCreateStageRequest.Parser.ParseFrom(packet.Payload.DataSpan);

        // PlayServer ServerId는 "play-1"
        const string playNid = "play-1";
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

        var request = TriggerGetOrCreateStageRequest.Parser.ParseFrom(packet.Payload.DataSpan);

        // PlayServer ServerId는 "play-1"
        const string playNid = "play-1";
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

    private Task HandleSendToApiServer(IPacket packet, IApiSender sender)
    {
        ReceivedMsgIds.Add(packet.MsgId);
        Interlocked.Increment(ref _onDispatchCallCount);

        var request = TriggerSendToApiServerRequest.Parser.ParseFrom(packet.Payload.DataSpan);

        // SendToApi (비동기, 응답 없음)
        var message = new InterApiMessage
        {
            FromApiNid = "sender-api",
            Content = request.Message
        };
        sender.SendToApi(request.TargetApiNid, CPacket.Of(message));

        var reply = new TriggerSendToApiServerReply { Success = true };
        sender.Reply(CPacket.Of(reply));
        return Task.CompletedTask;
    }

    private async Task HandleRequestToApiServer(IPacket packet, IApiSender sender)
    {
        ReceivedMsgIds.Add(packet.MsgId);
        Interlocked.Increment(ref _onDispatchCallCount);

        var request = TriggerRequestToApiServerRequest.Parser.ParseFrom(packet.Payload.DataSpan);

        // RequestToApi (동기, 응답 있음)
        var message = new InterApiMessage
        {
            FromApiNid = "sender-api",
            Content = request.Query
        };
        var responsePacket = await sender.RequestToApi(request.TargetApiNid, CPacket.Of(message));
        var response = InterApiReply.Parser.ParseFrom(responsePacket.Payload.DataSpan);

        var reply = new TriggerRequestToApiServerReply
        {
            Response = response.Response
        };
        sender.Reply(CPacket.Of(reply));
    }

    private Task HandleInterApiMessage(IPacket packet, IApiSender sender)
    {
        ReceivedMsgIds.Add(packet.MsgId);
        Interlocked.Increment(ref _onDispatchCallCount);

        var request = InterApiMessage.Parser.ParseFrom(packet.Payload.DataSpan);

        // 다른 API 서버에서 온 메시지 처리
        var reply = new InterApiReply
        {
            Response = $"Processed: {request.Content} from {request.FromApiNid}"
        };
        sender.Reply(CPacket.Of(reply));
        return Task.CompletedTask;
    }

    /// <summary>
    /// 벤치마크용 페이로드 사전 할당 (메모리 할당 오염 방지)
    /// </summary>
    private static readonly Dictionary<int, Google.Protobuf.ByteString> PreallocatedPayloads = new()
    {
        { 1024, CreatePayload(1024) },
        { 65536, CreatePayload(65536) },
        { 131072, CreatePayload(131072) },
        { 262144, CreatePayload(262144) }
    };

    private static Google.Protobuf.ByteString CreatePayload(int size)
    {
        var payload = new byte[size];
        for (int i = 0; i < payload.Length; i++)
        {
            payload[i] = (byte)(i % 256);
        }
        return Google.Protobuf.ByteString.CopyFrom(payload);
    }

    /// <summary>
    /// 벤치마크 API 요청 처리 - 지정된 크기의 응답 반환
    /// </summary>
    private Task HandleBenchmarkApi(IPacket packet, IApiSender sender)
    {
        ReceivedMsgIds.Add(packet.MsgId);
        Interlocked.Increment(ref _onDispatchCallCount);

        var request = BenchmarkApiRequest.Parser.ParseFrom(packet.Payload.DataSpan);

        // 사전 할당된 페이로드 사용 (메모리 할당 오염 방지)
        var payload = PreallocatedPayloads.TryGetValue(request.ResponseSize, out var p)
            ? p
            : CreatePayload(request.ResponseSize);

        var reply = new BenchmarkApiReply
        {
            Sequence = request.Sequence,
            Payload = payload
        };

        sender.Reply(CPacket.Of(reply));
        return Task.CompletedTask;
    }

    /// <summary>
    /// Timer에서 호출되는 API 요청 처리
    /// </summary>
    private Task HandleTimerApi(IPacket packet, IApiSender sender)
    {
        ReceivedMsgIds.Add(packet.MsgId);
        Interlocked.Increment(ref _onDispatchCallCount);

        var request = TimerApiRequest.Parser.ParseFrom(packet.Payload.DataSpan);

        var reply = new TimerApiReply
        {
            Content = $"Timer API Response: {request.Content}"
        };
        sender.Reply(CPacket.Of(reply));
        return Task.CompletedTask;
    }
}
