using System.Collections.Concurrent;
using System.Diagnostics;
using Google.Protobuf;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PlayHouse.Abstractions;
using PlayHouse.Abstractions.Play;
using PlayHouse.Benchmark.SS.Shared.Proto;
using PlayHouse.Core.Shared;

namespace PlayHouse.Benchmark.SS.PlayServer;

public class BenchmarkStage : IStage
{
    private readonly ILogger<BenchmarkStage> _logger;
    private readonly ConcurrentQueue<long> _latencyQueue = new();

    public BenchmarkStage(IStageSender stageSender, ILogger<BenchmarkStage>? logger = null)
    {
        StageSender = stageSender;
        _logger = logger ?? NullLogger<BenchmarkStage>.Instance;
    }

    public IStageSender StageSender { get; }

    public Task<(bool result, IPacket reply)> OnCreate(IPacket packet) => 
        Task.FromResult<(bool, IPacket)>((true, CPacket.Empty("CreateStageReply")));

    public Task OnPostCreate() => Task.CompletedTask;
    public Task OnDestroy() => Task.CompletedTask;
    public Task<bool> OnJoinStage(IActor actor) => Task.FromResult(true);
    public Task OnPostJoinStage(IActor actor) => Task.CompletedTask;
    public ValueTask OnConnectionChanged(IActor actor, bool isConnected) => ValueTask.CompletedTask;

    public async Task OnDispatch(IActor actor, IPacket packet)
    {
        if (packet.MsgId == "TriggerSSEchoRequest")
        {
            await HandleTrigger(actor, packet);
        }
    }

    public Task OnDispatch(IPacket packet)
    {
        // Send 모드 응답 처리 (SendToStage로 온 메시지)
        if (packet.MsgId == "SSEchoReply")
        {
            if (_latencyQueue.TryDequeue(out var start))
            {
                ServerMetricsCollector.Instance.RecordMessage(Stopwatch.GetTimestamp() - start, packet.Payload.Length);
            }
        }
        return Task.CompletedTask;
    }

    private async Task HandleTrigger(IActor actor, IPacket packet)
    {
        var req = TriggerSSEchoRequest.Parser.ParseFrom(packet.Payload.DataSpan);
        var sw = Stopwatch.StartNew();
        
        // 큐 초기화 (이전 테스트 잔재 제거)
        _latencyQueue.Clear();

        var echoReq = new SSEchoRequest { Payload = req.Payload };
        var serializedData = echoReq.ToByteArray();

        if (req.CommMode == SSCommMode.RequestAsync)
        {
            await RunRequestAsyncBatch(req.BatchSize, serializedData);
        }
        else if (req.CommMode == SSCommMode.RequestCallback)
        {
            await RunRequestCallbackBatch(req.BatchSize, serializedData);
        }
        else if (req.CommMode == SSCommMode.Send)
        {
            RunSendBatch(req.BatchSize, serializedData);
        }

        sw.Stop();

        actor.ActorSender.Reply(CPacket.Of(new TriggerSSEchoReply 
        { 
            Count = req.BatchSize,
            ElapsedTicks = sw.ElapsedTicks 
        }));
    }

    private async Task RunRequestAsyncBatch(int count, byte[] data)
    {
        for (int i = 0; i < count; i++)
        {
            var start = Stopwatch.GetTimestamp();
            try {
                using var reply = await StageSender.RequestToApi("api-1", CPacket.Of("SSEchoRequest", data));
                ServerMetricsCollector.Instance.RecordMessage(Stopwatch.GetTimestamp() - start, data.Length);
            } catch { }
        }
    }

    private Task RunRequestCallbackBatch(int count, byte[] data)
    {
        var tcs = new TaskCompletionSource();
        int remaining = count;
        if (count <= 0) return Task.CompletedTask;

        for (int i = 0; i < count; i++)
        {
            var start = Stopwatch.GetTimestamp();
            StageSender.RequestToApi("api-1", CPacket.Of("SSEchoRequest", data), (err, reply) => 
            {
                if (err == 0) ServerMetricsCollector.Instance.RecordMessage(Stopwatch.GetTimestamp() - start, data.Length);
                reply?.Dispose();
                if (Interlocked.Decrement(ref remaining) == 0)
                {
                    tcs.TrySetResult();
                }
            });
        }
        return tcs.Task;
    }

    private void RunSendBatch(int count, byte[] data)
    {
        for (int i = 0; i < count; i++)
        {
            // 전송 시각 기록
            _latencyQueue.Enqueue(Stopwatch.GetTimestamp());
            StageSender.SendToApi("api-1", CPacket.Of("SSEchoRequest", data));
        }
    }
}
