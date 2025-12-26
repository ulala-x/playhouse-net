using System.Collections.Concurrent;
using System.Diagnostics;
using Google.Protobuf;
using PlayHouse.Abstractions;
using PlayHouse.Abstractions.Play;
using PlayHouse.Benchmark.Shared.Proto;
using PlayHouse.Core.Shared;

namespace PlayHouse.Benchmark.Server;

/// <summary>
/// 벤치마크용 Stage 구현
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

    public Task OnDispatch(IActor actor, IPacket packet)
    {
        var sw = Stopwatch.StartNew();

        switch (packet.MsgId)
        {
            case "BenchmarkRequest":
                HandleBenchmarkRequest(actor, packet, sw);
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

    private void HandleBenchmarkRequest(IActor actor, IPacket packet, Stopwatch sw)
    {
        var request = BenchmarkRequest.Parser.ParseFrom(packet.Payload.DataSpan);

        // 캐시된 페이로드 사용 (없으면 생성)
        var payload = GetOrCreatePayload(request.ResponseSize);

        var reply = new BenchmarkReply
        {
            Sequence = request.Sequence,
            ServerTimestamp = Stopwatch.GetTimestamp(),
            Payload = payload
        };

        actor.ActorSender.Reply(CPacket.Of(reply));

        // 메트릭 기록
        sw.Stop();
        var messageSize = packet.Payload.DataSpan.Length + reply.CalculateSize();
        ServerMetricsCollector.Instance.RecordMessage(sw.ElapsedTicks, messageSize);
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
