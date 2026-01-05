using System.Diagnostics;
using PlayHouse.Abstractions;
using PlayHouse.Abstractions.Play;
using PlayHouse.Benchmark.SS.Shared.Proto;
using PlayHouse.Core.Shared;

namespace PlayHouse.Benchmark.SS.PlayServer;

/// <summary>
/// 벤치마크용 Stage 구현 (Server-to-Server Echo 벤치마크)
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

    public async Task OnDispatch(IActor actor, IPacket packet)
    {
        switch (packet.MsgId)
        {
            case "EchoRequest":
                HandleEchoRequest(actor, packet);
                break;

            case "TriggerSSEchoRequest":
                await HandleTriggerSSEchoRequest(actor, packet);
                break;

            default:
                // 기본 응답
                actor.ActorSender.Reply(CPacket.Empty(packet.MsgId + "Reply"));
                break;
        }
    }

    public Task OnDispatch(IPacket packet)
    {
        // Stage 간 통신 수신 처리
        switch (packet.MsgId)
        {
            case "SSEchoRequest":
                HandleSSEchoRequestZeroCopy(packet);
                break;

            default:
                // 기본 응답
                StageSender.Reply(CPacket.Empty(packet.MsgId + "Reply"));
                break;
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// 클라이언트 → Stage → Api/Stage Echo 트리거 처리
    /// </summary>
    private async Task HandleTriggerSSEchoRequest(IActor actor, IPacket packet)
    {
        var request = TriggerSSEchoRequest.Parser.ParseFrom(packet.Payload.DataSpan);

        // SSCallType에 따라 분기
        if (request.CallType == SSCallType.StageToApi)
        {
            await HandleSSEchoToApi(actor, request);
        }
        else if (request.CallType == SSCallType.StageToStage)
        {
            await HandleSSEchoToStage(actor, request);
        }
    }

    /// <summary>
    /// Stage → API Echo 처리 (3가지 모드: Send, RequestAsync, RequestCallback)
    /// </summary>
    private async Task HandleSSEchoToApi(IActor actor, TriggerSSEchoRequest request)
    {
        var echoRequest = new SSEchoRequest { Payload = request.Payload };

        switch (request.CommMode)
        {
            case SSCommMode.Send:
                // Send 모드: 응답 대기 없이 즉시 전송
                StageSender.SendToApi("api-1", CPacket.Of(echoRequest));

                // SendToClient로 응답 (Reply가 아님 - benchmark_cs와 동일하게)
                actor.ActorSender.SendToClient(CPacket.Of(new TriggerSSEchoReply
                {
                    Sequence = request.Sequence,
                    Payload = request.Payload
                }));
                break;

            case SSCommMode.RequestAsync:
                // RequestAsync 모드: await 기반 비동기 호출
                using (var reply = await StageSender.RequestToApi("api-1", CPacket.Of(echoRequest)))
                {
                    var echoReply = SSEchoReply.Parser.ParseFrom(reply.Payload.DataSpan);
                    actor.ActorSender.Reply(CPacket.Of(new TriggerSSEchoReply
                    {
                        Sequence = request.Sequence,
                        Payload = echoReply.Payload
                    }));
                }
                break;

            case SSCommMode.RequestCallback:
                // RequestCallback 모드: 콜백 기반 비동기 호출
                var tcs = new TaskCompletionSource<IPacket>();
                StageSender.RequestToApi("api-1", CPacket.Of(echoRequest), (errorCode, reply) =>
                {
                    if (errorCode == 0 && reply != null)
                        tcs.TrySetResult(reply);
                    else
                        tcs.TrySetException(new Exception($"Error: {errorCode}"));
                });

                using (var reply = await tcs.Task)
                {
                    var echoReply = SSEchoReply.Parser.ParseFrom(reply.Payload.DataSpan);
                    actor.ActorSender.Reply(CPacket.Of(new TriggerSSEchoReply
                    {
                        Sequence = request.Sequence,
                        Payload = echoReply.Payload
                    }));
                }
                break;
        }
    }

    /// <summary>
    /// Stage → Stage Echo 처리 (3가지 모드: Send, RequestAsync, RequestCallback)
    /// </summary>
    private async Task HandleSSEchoToStage(IActor actor, TriggerSSEchoRequest request)
    {
        var echoRequest = new SSEchoRequest { Payload = request.Payload };

        switch (request.CommMode)
        {
            case SSCommMode.Send:
                // Send 모드: 응답 대기 없이 즉시 전송
                StageSender.SendToStage(request.TargetNid, request.TargetStageId, CPacket.Of(echoRequest));

                // SendToClient로 응답 (Reply가 아님 - benchmark_cs와 동일하게)
                actor.ActorSender.SendToClient(CPacket.Of(new TriggerSSEchoReply
                {
                    Sequence = request.Sequence,
                    Payload = request.Payload
                }));
                break;

            case SSCommMode.RequestAsync:
                // RequestAsync 모드: await 기반 비동기 호출
                using (var reply = await StageSender.RequestToStage(request.TargetNid, request.TargetStageId, CPacket.Of(echoRequest)))
                {
                    var echoReply = SSEchoReply.Parser.ParseFrom(reply.Payload.DataSpan);
                    actor.ActorSender.Reply(CPacket.Of(new TriggerSSEchoReply
                    {
                        Sequence = request.Sequence,
                        Payload = echoReply.Payload
                    }));
                }
                break;

            case SSCommMode.RequestCallback:
                // RequestCallback 모드: 콜백 기반 비동기 호출
                var tcs = new TaskCompletionSource<IPacket>();
                StageSender.RequestToStage(request.TargetNid, request.TargetStageId, CPacket.Of(echoRequest), (errorCode, reply) =>
                {
                    if (errorCode == 0 && reply != null)
                        tcs.TrySetResult(reply);
                    else
                        tcs.TrySetException(new Exception($"Error: {errorCode}"));
                });

                using (var reply = await tcs.Task)
                {
                    var echoReply = SSEchoReply.Parser.ParseFrom(reply.Payload.DataSpan);
                    actor.ActorSender.Reply(CPacket.Of(new TriggerSSEchoReply
                    {
                        Sequence = request.Sequence,
                        Payload = echoReply.Payload
                    }));
                }
                break;
        }
    }

    /// <summary>
    /// Zero-copy Echo 핸들러 (클라이언트 → Stage)
    /// </summary>
    private void HandleEchoRequest(IActor actor, IPacket packet)
    {
        // Zero-copy: 소유권 이전
        var echoPayload = packet.Payload.Move();

        actor.ActorSender.Reply(CPacket.Of("EchoReply", echoPayload));

        // 메트릭 기록
        ServerMetricsCollector.Instance.RecordMessage(0, packet.Payload.Length * 2);
    }

    /// <summary>
    /// Zero-copy Stage 간 Echo 핸들러
    /// </summary>
    private void HandleSSEchoRequestZeroCopy(IPacket packet)
    {
        // Zero-copy: 소유권 이전
        var echoPayload = packet.Payload.Move();
        StageSender.Reply(CPacket.Of("SSEchoReply", echoPayload));
    }
}
