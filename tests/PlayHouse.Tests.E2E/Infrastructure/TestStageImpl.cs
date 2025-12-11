#nullable enable

using Google.Protobuf;
using PlayHouse.Abstractions;
using PlayHouse.Abstractions.Play;
using PlayHouse.Core.Shared;
using PlayHouse.Tests.E2E.Proto;

namespace PlayHouse.Tests.E2E.Infrastructure;

/// <summary>
/// E2E 테스트용 Stage 구현.
/// Connector에서 전송한 메시지를 처리하고 적절한 응답을 반환합니다.
/// </summary>
/// <remarks>
/// 지원하는 메시지:
/// - EchoRequest → EchoReply (동일 내용 반환)
/// - FailRequest → 에러코드 500 반환
/// - BroadcastTrigger → SendToClient로 Push 전송
/// - NoResponseRequest → 응답 없음 (타임아웃 테스트용)
/// </remarks>
public class TestStageImpl : IStage
{
    public IStageSender StageSender { get; }

    // 테스트 검증용 데이터
    public List<string> ReceivedMsgIds { get; } = new();
    public List<IActor> JoinedActors { get; } = new();
    public List<(IActor actor, bool isConnected)> ConnectionChanges { get; } = new();
    public bool OnCreateCalled { get; private set; }
    public bool OnDestroyCalled { get; private set; }
    public IPacket? LastCreatePacket { get; private set; }

    public TestStageImpl(IStageSender stageSender)
    {
        StageSender = stageSender;
    }

    public Task<(bool result, IPacket reply)> OnCreate(IPacket packet)
    {
        OnCreateCalled = true;
        LastCreatePacket = packet;
        return Task.FromResult<(bool, IPacket)>((true, CPacket.Empty("CreateStageReply")));
    }

    public Task OnPostCreate()
    {
        return Task.CompletedTask;
    }

    public Task OnDestroy()
    {
        OnDestroyCalled = true;
        return Task.CompletedTask;
    }

    public Task<bool> OnJoinStage(IActor actor)
    {
        JoinedActors.Add(actor);
        return Task.FromResult(true);
    }

    public Task OnPostJoinStage(IActor actor)
    {
        return Task.CompletedTask;
    }

    public ValueTask OnConnectionChanged(IActor actor, bool isConnected)
    {
        ConnectionChanges.Add((actor, isConnected));
        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// 클라이언트(Actor)로부터 메시지 수신 시 처리.
    /// </summary>
    public async Task OnDispatch(IActor actor, IPacket packet)
    {
        ReceivedMsgIds.Add(packet.MsgId);

        switch (packet.MsgId)
        {
            case "EchoRequest":
                await HandleEchoRequest(actor, packet);
                break;

            case "FailRequest":
                // 에러 응답
                actor.ActorSender.Reply(500);
                break;

            case "BroadcastTrigger":
                // Push 메시지 전송 트리거
                await HandleBroadcastTrigger(actor, packet);
                break;

            case "NoResponseRequest":
                // 의도적으로 응답하지 않음 (타임아웃 테스트용)
                break;

            case "StatusRequest":
                await HandleStatusRequest(actor);
                break;

            case "TriggerSendToClient":
                // IStageSender.SendToClient 트리거
                await HandleSendToClientTrigger(actor, packet);
                break;

            default:
                // 기본 성공 응답
                actor.ActorSender.Reply(CPacket.Empty(packet.MsgId + "Reply"));
                break;
        }
    }

    /// <summary>
    /// 서버 간 메시지 수신 시 처리.
    /// </summary>
    public Task OnDispatch(IPacket packet)
    {
        ReceivedMsgIds.Add(packet.MsgId);
        return Task.CompletedTask;
    }

    private Task HandleEchoRequest(IActor actor, IPacket packet)
    {
        var echoRequest = EchoRequest.Parser.ParseFrom(packet.Payload.Data.Span);
        var echoReply = new EchoReply
        {
            Content = echoRequest.Content,
            Sequence = echoRequest.Sequence,
            ProcessedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };

        actor.ActorSender.Reply(CPacket.Of(echoReply));
        return Task.CompletedTask;
    }

    private Task HandleBroadcastTrigger(IActor actor, IPacket packet)
    {
        var trigger = BroadcastNotify.Parser.ParseFrom(packet.Payload.Data.Span);
        var pushMessage = new BroadcastNotify
        {
            EventType = trigger.EventType,
            Data = trigger.Data,
            FromAccountId = long.Parse(actor.ActorSender.AccountId)
        };

        // SendToClient로 Push 전송
        actor.ActorSender.SendToClient(CPacket.Of(pushMessage));

        // 성공 응답
        actor.ActorSender.Reply(CPacket.Empty("BroadcastTriggerReply"));
        return Task.CompletedTask;
    }

    private Task HandleStatusRequest(IActor actor)
    {
        var statusReply = new StatusReply
        {
            ActorCount = JoinedActors.Count,
            UptimeSeconds = 100,
            StageType = StageSender.StageType
        };

        actor.ActorSender.Reply(CPacket.Of(statusReply));
        return Task.CompletedTask;
    }

    private Task HandleSendToClientTrigger(IActor actor, IPacket packet)
    {
        // IStageSender.SendToClient 테스트용
        // 요청을 받으면 Push 메시지를 보내고 성공 응답
        var pushNotify = new BroadcastNotify
        {
            EventType = "push_test",
            Data = "triggered_by_sendtoclient",
            FromAccountId = 0
        };

        actor.ActorSender.SendToClient(CPacket.Of(pushNotify));
        actor.ActorSender.Reply(CPacket.Empty("TriggerSendToClientReply"));
        return Task.CompletedTask;
    }
}
