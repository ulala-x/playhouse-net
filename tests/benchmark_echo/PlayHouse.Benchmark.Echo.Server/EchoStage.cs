using System.Diagnostics;
using PlayHouse.Abstractions;
using PlayHouse.Abstractions.Play;
using PlayHouse.Benchmark.Echo.Shared.Proto;
using PlayHouse.Core.Shared;

namespace PlayHouse.Benchmark.Echo.Server;

/// <summary>
/// Echo 벤치마크용 Stage 구현
/// </summary>
public class EchoStage(IStageSender stageSender) : IStage
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

            case "EchoSendRequest":
                HandleEchoSendRequest(actor, packet, sw);
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
        switch (packet.MsgId)
        {
            case "CloseStageCommand":
                // API에서 전송한 Stage 종료 명령
                StageSender.CloseStage();
                break;
        }

        return Task.CompletedTask;
    }

    private void HandleEchoRequest(IActor actor, IPacket packet, Stopwatch sw)
    {
        var request = EchoRequest.Parser.ParseFrom(packet.Payload.DataSpan);

        var reply = new EchoReply
        {
            Content = request.Content,
            ServerTimestamp = Stopwatch.GetTimestamp()
        };

        actor.ActorSender.Reply(CPacket.Of(reply));

        // 메트릭 기록
        sw.Stop();
        var messageSize = packet.Payload.DataSpan.Length + reply.CalculateSize();
        ServerMetricsCollector.Instance.RecordMessage(sw.ElapsedTicks, messageSize);
    }

    private void HandleEchoSendRequest(IActor actor, IPacket packet, Stopwatch sw)
    {
        var request = EchoRequest.Parser.ParseFrom(packet.Payload.DataSpan);

        var reply = new EchoReply
        {
            Content = request.Content,
            ServerTimestamp = Stopwatch.GetTimestamp()
        };

        // Send 모드: Reply 대신 SendToClient 사용
        actor.ActorSender.SendToClient(CPacket.Of(reply));

        // 메트릭 기록
        sw.Stop();
        var messageSize = packet.Payload.DataSpan.Length + reply.CalculateSize();
        ServerMetricsCollector.Instance.RecordMessage(sw.ElapsedTicks, messageSize);
    }
}
