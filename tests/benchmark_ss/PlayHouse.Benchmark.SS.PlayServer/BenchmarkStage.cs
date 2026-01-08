using System.Collections.Concurrent;
using System.Diagnostics;
using Google.Protobuf;
using PlayHouse.Abstractions;
using PlayHouse.Abstractions.Play;
using PlayHouse.Benchmark.SS.Shared.Proto;
using PlayHouse.Core.Shared;

namespace PlayHouse.Benchmark.SS.PlayServer;

/// <summary>
/// Event-driven S2S Benchmark Stage.
/// Restored to the version that achieved 25M TPS by using non-blocking self-pipelining.
/// </summary>
public class BenchmarkStage(IStageSender stageSender) : IStage
{
    public IStageSender StageSender { get; } = stageSender;
    
    private bool _isBenchmarking;
    private StartBenchmarkRequest? _req;
    private byte[]? _payload;
    private readonly ConcurrentQueue<long> _latencyQueue = new();
    
    private long _sentCount;
    private long _recvCount;
    private readonly List<double> _latencies = new(1000000);
    private Stopwatch? _swTotal;
    private IActor? _triggerActor;

    public Task<(bool result, IPacket reply)> OnCreate(IPacket packet) => 
        Task.FromResult<(bool, IPacket)>((true, CPacket.Empty("CreateStageReply")));

    public Task OnPostCreate() => Task.CompletedTask;
    public Task OnDestroy() => Task.CompletedTask;
    public Task<bool> OnJoinStage(IActor actor) => Task.FromResult(true);
    public Task OnPostJoinStage(IActor actor) => Task.CompletedTask;
    public ValueTask OnConnectionChanged(IActor actor, bool isConnected) => ValueTask.CompletedTask;

    public async Task OnDispatch(IActor actor, IPacket packet)
    {
        switch (packet.MsgId)
        {
            case "StartBenchmarkRequest":
                _triggerActor = actor;
                // Immediate acceptance
                actor.ActorSender.Reply(CPacket.Empty("StartBenchmarkAccepted"));
                await HandleStartBenchmark(packet);
                break;
            case "SSEchoReply":
                HandleSSEchoReply(packet);
                break;
        }
    }

    public Task OnDispatch(IPacket packet)
    {
        if (packet.MsgId == "SSEchoReply")
        {
            HandleSSEchoReply(packet);
        }
        return Task.CompletedTask;
    }

    private void HandleSSEchoReply(IPacket packet)
    {
        if (!_isBenchmarking || _req == null) return;

        // 1. Latency & Count
        if (_latencyQueue.TryDequeue(out var startTicks))
        {
            var elapsed = Stopwatch.GetTimestamp() - startTicks;
            Interlocked.Increment(ref _recvCount);
            
            // Sampling (1/10) to reduce contention
            if (_recvCount % 10 == 0)
            {
                var ms = (double)elapsed * 1000 / Stopwatch.Frequency;
                lock (_latencies) { if (_latencies.Count < 1000000) _latencies.Add(ms); }
            }
        }

        // 2. Check Finish Condition
        if (_swTotal!.Elapsed.TotalSeconds >= _req.DurationSeconds)
        {
            // Wait for all in-flight to return (or close enough)
            if (Interlocked.Read(ref _recvCount) >= Interlocked.Read(ref _sentCount) && _isBenchmarking)
            {
                FinishBenchmark();
            }
            return;
        }

        // 3. Self-Pipelining: Send next request immediately (keep in-flight)
        TriggerNextRequest();
    }

    private async Task HandleStartBenchmark(IPacket packet)
    {
        if (_isBenchmarking) return;
        _isBenchmarking = true;

        _req = StartBenchmarkRequest.Parser.ParseFrom(packet.Payload.DataSpan);
        _payload = new byte[_req.MessageSize];
        new Random().NextBytes(_payload);
        
        ResetMetrics();
        _swTotal = Stopwatch.StartNew();

        Console.WriteLine($"[Benchmark] Starting Stage {StageSender.StageId}: Mode={_req.CommMode}, InFlight={_req.MaxInflight}");

        // Initial Burst
        for (int i = 0; i < _req.MaxInflight; i++)
        {
            TriggerNextRequest();
        }
        await Task.CompletedTask;
    }

    private void TriggerNextRequest()
    {
        // Use AsyncBlock for offloading send logic, but NO LOOP inside.
        // Just one send, then exit. This keeps worker threads free.
        StageSender.AsyncBlock(
            async () =>
            {
                Interlocked.Increment(ref _sentCount);
                _latencyQueue.Enqueue(Stopwatch.GetTimestamp());

                if (_req!.CommMode == SSCommMode.Send)
                {
                    var echoSend = new SSEchoSend { Payload = ByteString.CopyFrom(_payload) };
                    StageSender.SendToApi("api-1", CPacket.Of(echoSend));
                }
                else
                {
                    var echoReq = new SSEchoRequest { Payload = ByteString.CopyFrom(_payload) };
                    // Fire-and-forget request. Response handled via OnDispatch.
                    _ = StageSender.RequestToApi("api-1", CPacket.Of(echoReq));
                }
                
                await Task.CompletedTask;
                return true;
            });
    }

    private void FinishBenchmark()
    {
        if (!_isBenchmarking || _swTotal == null || _triggerActor == null) return;
        _isBenchmarking = false;
        _swTotal.Stop();

        var result = AggregateResult(_swTotal.Elapsed.TotalSeconds);
        _triggerActor.ActorSender.SendToClient(CPacket.Of(result));
        
        Console.WriteLine($"[Benchmark] Stage {StageSender.StageId} Finished. TPS: {result.Tps:F0}");
    }

    private void ResetMetrics()
    {
        _sentCount = 0; _recvCount = 0;
        _latencyQueue.Clear();
        lock (_latencies) _latencies.Clear();
    }

    private BenchmarkResult AggregateResult(double elapsedSeconds)
    {
        List<double> sorted;
        lock (_latencies) sorted = _latencies.OrderBy(x => x).ToList();
        var p99 = sorted.Count > 0 ? sorted[(int)(sorted.Count * 0.99)] : 0;
        var mean = sorted.Count > 0 ? sorted.Average() : 0;
        var tps = _recvCount / elapsedSeconds;

        return new BenchmarkResult {
            TotalSent = _sentCount, TotalReceived = _recvCount,
            Tps = tps, P99LatencyMs = p99, MeanLatencyMs = mean,
            DurationActual = (int)elapsedSeconds
        };
    }
}