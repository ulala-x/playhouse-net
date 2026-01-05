using System.Diagnostics;
using PlayHouse.Abstractions;
using PlayHouse.Abstractions.Play;
using PlayHouse.Core.Shared;

namespace PlayHouse.Benchmark.Server;

/// <summary>
/// 벤치마크용 Stage 구현
/// </summary>
public class BenchmarkStage(IStageSender stageSender) : IStage
{
    public IStageSender StageSender { get; } = stageSender;

    public Task<(bool result, IPacket reply)> OnCreate(IPacket packet)
    {
        return Task.FromResult<(bool, IPacket)>((true, CPacket.Empty("CreateStageReply")));
    }

    public Task OnPostCreate()
    {
        return Task.CompletedTask;
    }

    public Task OnDestroy()
    {
        return Task.CompletedTask;
    }

    public Task<bool> OnJoinStage(IActor actor)
    {
        return Task.FromResult(true);
    }

    public Task OnPostJoinStage(IActor actor)
    {
        return Task.CompletedTask;
    }

    public ValueTask OnConnectionChanged(IActor actor, bool isConnected)
    {
        return ValueTask.CompletedTask;
    }

    public Task OnDispatch(IActor actor, IPacket packet)
    {
        var sw = Stopwatch.StartNew();

        switch (packet.MsgId)
        {
            case "EchoRequest":
                HandleEchoRequest(actor, packet, sw);
                break;

            case "SendRequest":
                HandleSendRequest(actor, packet);
                break;

            default:
                // 기본 응답
                actor.ActorSender.Reply(CPacket.Empty(packet.MsgId + "Reply"));
                break;
        }

        return Task.CompletedTask;
    }

    public Task OnDispatch(IPacket packet)
    {
        // Stage 간 통신은 벤치마크에서 사용하지 않음
        return Task.CompletedTask;
    }

    /// <summary>
    /// Zero-copy Echo 핸들러
    /// </summary>
    private void HandleEchoRequest(IActor actor, IPacket packet, Stopwatch sw)
    {
        // Zero-copy: 소유권 이전
        var echoPayload = packet.Payload.Move();

        actor.ActorSender.Reply(CPacket.Of("EchoReply", echoPayload));

        // 메트릭 기록
        sw.Stop();
        var messageSize = packet.Payload.Length * 2;  // 요청 + 응답 (동일 크기)
        ServerMetricsCollector.Instance.RecordMessage(sw.ElapsedTicks, messageSize);
    }

    /// <summary>
    /// Send 요청 처리: SendToClient로 응답 (Zero-copy)
    /// </summary>
    private void HandleSendRequest(IActor actor, IPacket packet)
    {
        // Zero-copy: 소유권 이전
        var echoPayload = packet.Payload.Move();

        // SendToClient로 응답 (Reply가 아님)
        actor.ActorSender.SendToClient(CPacket.Of("SendReply", echoPayload));

        // 메트릭 기록
        var messageSize = packet.Payload.Length * 2;  // 요청 + 응답 (동일 크기)
        ServerMetricsCollector.Instance.RecordMessage(0, messageSize);
    }
}
