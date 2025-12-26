using System.Collections.Concurrent;
using System.Diagnostics;
using Google.Protobuf;
using PlayHouse.Abstractions;
using PlayHouse.Abstractions.Play;
using PlayHouse.Benchmark.SS.Shared.Proto;
using PlayHouse.Core.Shared;

namespace PlayHouse.Benchmark.SS.PlayServer;

/// <summary>
/// 벤치마크용 Stage 구현 (Server-to-Server 벤치마크)
/// </summary>
public class BenchmarkStage(IStageSender stageSender) : IStage
{
    public IStageSender StageSender { get; } = stageSender;

    // 응답 페이로드 캐시 (크기별로 재사용)
    private static readonly ConcurrentDictionary<int, ByteString> PayloadCache = new();

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
        var totalSw = Stopwatch.StartNew();

        switch (packet.MsgId)
        {
            case "TriggerApiRequest":
                await HandleTriggerApiRequest(actor, packet, totalSw);
                break;

            case "TriggerStageRequest":
                await HandleTriggerStageRequest(actor, packet, totalSw);
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
            case "SSBenchmarkRequest":
                HandleSSBenchmarkRequest(packet);
                break;

            default:
                // 기본 응답
                StageSender.Reply(CPacket.Empty(packet.MsgId + "Reply"));
                break;
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// 클라이언트 → Stage → Api 벤치마크 트리거 처리
    /// </summary>
    private async Task HandleTriggerApiRequest(IActor actor, IPacket packet, Stopwatch totalSw)
    {
        var request = TriggerApiRequest.Parser.ParseFrom(packet.Payload.Data.Span);

        // API 서버에 요청 전송 및 SS 구간 측정
        var ssRequest = new SSBenchmarkRequest
        {
            Sequence = request.Sequence,
            ResponseSize = request.ResponseSize
        };

        // ★ Stage → Api 구간만 측정
        var ssSw = Stopwatch.StartNew();
        var apiResponse = await StageSender.RequestToApi("api-1", CPacket.Of(ssRequest));
        ssSw.Stop();

        totalSw.Stop();

        // 응답 페이로드 생성 (요청된 크기만큼)
        var payload = GetOrCreatePayload(request.ResponseSize);

        var reply = new TriggerApiReply
        {
            Sequence = request.Sequence,
            SsElapsedTicks = ssSw.ElapsedTicks,
            TotalElapsedTicks = totalSw.ElapsedTicks,
            Payload = payload
        };

        actor.ActorSender.Reply(CPacket.Of(reply));

        // 메트릭 기록 (SS 구간만)
        var messageSize = packet.Payload.Data.Length + reply.CalculateSize();
        ServerMetricsCollector.Instance.RecordMessage(ssSw.ElapsedTicks, messageSize);
    }

    /// <summary>
    /// 클라이언트 → Stage A → Stage B 벤치마크 트리거 처리
    /// </summary>
    private async Task HandleTriggerStageRequest(IActor actor, IPacket packet, Stopwatch totalSw)
    {
        var request = TriggerStageRequest.Parser.ParseFrom(packet.Payload.Data.Span);

        // 다른 Stage에 요청 전송 및 SS 구간 측정
        var ssRequest = new SSBenchmarkRequest
        {
            Sequence = request.Sequence,
            ResponseSize = request.ResponseSize
        };

        // ★ Stage → Stage 구간만 측정
        var ssSw = Stopwatch.StartNew();
        var stageResponse = await StageSender.RequestToStage(
            request.TargetNid,
            request.TargetStageId,
            CPacket.Of(ssRequest));
        ssSw.Stop();

        totalSw.Stop();

        // 응답 페이로드 생성 (요청된 크기만큼)
        var payload = GetOrCreatePayload(request.ResponseSize);

        var reply = new TriggerStageReply
        {
            Sequence = request.Sequence,
            SsElapsedTicks = ssSw.ElapsedTicks,
            TotalElapsedTicks = totalSw.ElapsedTicks,
            Payload = payload
        };

        actor.ActorSender.Reply(CPacket.Of(reply));

        // 메트릭 기록 (SS 구간만)
        var messageSize = packet.Payload.Data.Length + reply.CalculateSize();
        ServerMetricsCollector.Instance.RecordMessage(ssSw.ElapsedTicks, messageSize);
    }

    /// <summary>
    /// Stage 간 통신 시 수신 처리 (다른 Stage에서 온 요청)
    /// </summary>
    private void HandleSSBenchmarkRequest(IPacket packet)
    {
        var request = SSBenchmarkRequest.Parser.ParseFrom(packet.Payload.Data.Span);

        // 캐시된 페이로드 사용
        var payload = GetOrCreatePayload(request.ResponseSize);

        var reply = new SSBenchmarkReply
        {
            Sequence = request.Sequence,
            Payload = payload
        };

        StageSender.Reply(CPacket.Of(reply));
    }

    /// <summary>
    /// 지정된 크기의 페이로드를 캐시에서 가져오거나 생성합니다.
    /// </summary>
    private static ByteString GetOrCreatePayload(int size)
    {
        if (size <= 0) return ByteString.Empty;

        return PayloadCache.GetOrAdd(size, s =>
        {
            // 압축 방지를 위해 패턴으로 채움
            var payload = new byte[s];
            for (int i = 0; i < payload.Length; i++)
            {
                payload[i] = (byte)(i % 256);
            }
            return ByteString.CopyFrom(payload);
        });
    }
}
