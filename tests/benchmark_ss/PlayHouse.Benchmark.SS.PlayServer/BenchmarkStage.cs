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

            case "StartSSBenchmarkRequest":
                await HandleStartSSBenchmarkRequest(actor, packet);
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
            case "SSBenchmarkRequest":
                HandleSSBenchmarkRequest(packet);
                break;

            case "SSEchoRequest":
                HandleSSEchoRequest(packet);
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
        var request = TriggerApiRequest.Parser.ParseFrom(packet.Payload.DataSpan);

        // API 서버에 요청 전송 및 SS 구간 측정
        var ssRequest = new SSBenchmarkRequest
        {
            Sequence = request.Sequence,
            ResponseSize = request.ResponseSize
        };

        // ★ Stage → Api 구간만 측정
        var ssSw = Stopwatch.StartNew();

        // CommMode에 따라 RequestAsync 또는 RequestCallback 사용
        if (request.CommMode == SSCommMode.RequestCallback)
        {
            // RequestCallback 모드: 콜백 기반 비동기 호출
            var tcs = new TaskCompletionSource<IPacket>();
            StageSender.RequestToApi("api-1", CPacket.Of(ssRequest), (errorCode, reply) =>
            {
                if (errorCode == 0 && reply != null)
                    tcs.TrySetResult(reply);
                else
                    tcs.TrySetException(new Exception($"API call failed with error code: {errorCode}"));
            });
            await tcs.Task;
        }
        else
        {
            // RequestAsync 모드: await 기반 비동기 호출 (기본값)
            await StageSender.RequestToApi("api-1", CPacket.Of(ssRequest));
        }

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

        // 메트릭 기록 (전체 구간 - 클라이언트와 동일한 범위)
        var messageSize = packet.Payload.DataSpan.Length + reply.CalculateSize();
        ServerMetricsCollector.Instance.RecordMessage(totalSw.ElapsedTicks, messageSize);
    }

    /// <summary>
    /// 클라이언트 → Stage A → Stage B 벤치마크 트리거 처리
    /// </summary>
    private async Task HandleTriggerStageRequest(IActor actor, IPacket packet, Stopwatch totalSw)
    {
        var request = TriggerStageRequest.Parser.ParseFrom(packet.Payload.DataSpan);

        Serilog.Log.Information("[BenchmarkStage] HandleTriggerStageRequest - TargetNid: {TargetNid}, TargetStageId: {TargetStageId}",
            request.TargetNid, request.TargetStageId);

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

        // 메트릭 기록 (전체 구간 - 클라이언트와 동일한 범위)
        var messageSize = packet.Payload.DataSpan.Length + reply.CalculateSize();
        ServerMetricsCollector.Instance.RecordMessage(totalSw.ElapsedTicks, messageSize);
    }

    /// <summary>
    /// Stage 간 통신 시 수신 처리 (다른 Stage에서 온 요청)
    /// </summary>
    private void HandleSSBenchmarkRequest(IPacket packet)
    {
        var request = SSBenchmarkRequest.Parser.ParseFrom(packet.Payload.DataSpan);

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
    /// 서버 내부 반복 벤치마크 처리 (StageToApi, StageToStage, ApiToApi)
    /// </summary>
    private async Task HandleStartSSBenchmarkRequest(IActor actor, IPacket packet)
    {
        var request = StartSSBenchmarkRequest.Parser.ParseFrom(packet.Payload.DataSpan);

        // API → API 테스트는 API 서버로 위임
        if (request.CallType == SSCallType.ApiToApi)
        {
            // API 서버에 벤치마크 시작 요청 전달 (API 서버가 내부 반복 처리)
            var apiReply = await StageSender.RequestToApi("api-1", CPacket.Of(request));

            // API 서버의 응답을 클라이언트에게 그대로 전달
            if (apiReply.MsgId == "StartSSBenchmarkReply")
            {
                actor.ActorSender.Reply(apiReply);
            }
            else
            {
                // 오류 응답
                var errorReply = new StartSSBenchmarkReply
                {
                    CallType = request.CallType,
                    CommMode = request.CommMode,
                    TotalIterations = request.Iterations,
                    SuccessCount = 0,
                    FailedCount = request.Iterations
                };
                actor.ActorSender.Reply(CPacket.Of(errorReply));
            }
            return;
        }

        // RequestCallback 모드는 별도 핸들러 사용
        if (request.CommMode == SSCommMode.RequestCallback)
        {
            await HandleRequestCallbackMode(actor, request);
            return;
        }

        // RequestAsync 모드: 순차 await 방식
        var latencies = new List<double>(request.Iterations);
        var successCount = 0;
        var failedCount = 0;

        var totalSw = Stopwatch.StartNew();

        for (int i = 0; i < request.Iterations; i++)
        {
            var ssRequest = new SSBenchmarkRequest
            {
                Sequence = i,
                ResponseSize = request.ResponseSize
            };

            var iterSw = Stopwatch.StartNew();

            try
            {
                if (request.CallType == SSCallType.StageToApi)
                {
                    await StageSender.RequestToApi("api-1", CPacket.Of(ssRequest));
                }
                else if (request.CallType == SSCallType.StageToStage)
                {
                    await StageSender.RequestToStage(
                        request.TargetNid,
                        request.TargetStageId,
                        CPacket.Of(ssRequest));
                }

                iterSw.Stop();
                latencies.Add(iterSw.Elapsed.TotalMilliseconds);
                successCount++;
            }
            catch (Exception)
            {
                failedCount++;
            }
        }

        totalSw.Stop();

        SendBenchmarkReply(actor, request, latencies, successCount, failedCount, totalSw.Elapsed.TotalSeconds);
    }

    /// <summary>
    /// RequestCallback 모드: 모든 요청을 비동기로 발사하고 콜백에서 결과 수집
    /// </summary>
    private async Task HandleRequestCallbackMode(IActor actor, StartSSBenchmarkRequest request)
    {
        var latencies = new ConcurrentBag<double>();
        var successCount = 0;
        var failedCount = 0;
        var completedCount = 0;
        var tcs = new TaskCompletionSource();

        // 각 요청별 시작 시간 기록
        var startTimes = new ConcurrentDictionary<int, long>();

        var totalSw = Stopwatch.StartNew();

        for (int i = 0; i < request.Iterations; i++)
        {
            var seq = i;
            var ssRequest = new SSBenchmarkRequest
            {
                Sequence = seq,
                ResponseSize = request.ResponseSize
            };

            startTimes[seq] = Stopwatch.GetTimestamp();

            if (request.CallType == SSCallType.StageToApi)
            {
                StageSender.RequestToApi("api-1", CPacket.Of(ssRequest), (errorCode, reply) =>
                {
                    if (startTimes.TryRemove(seq, out var startTicks))
                    {
                        var elapsedTicks = Stopwatch.GetTimestamp() - startTicks;
                        var elapsedMs = elapsedTicks * 1000.0 / Stopwatch.Frequency;
                        latencies.Add(elapsedMs);
                    }

                    if (errorCode == 0)
                        Interlocked.Increment(ref successCount);
                    else
                        Interlocked.Increment(ref failedCount);

                    if (Interlocked.Increment(ref completedCount) >= request.Iterations)
                        tcs.TrySetResult();
                });
            }
            else if (request.CallType == SSCallType.StageToStage)
            {
                StageSender.RequestToStage(request.TargetNid, request.TargetStageId, CPacket.Of(ssRequest), (errorCode, reply) =>
                {
                    if (startTimes.TryRemove(seq, out var startTicks))
                    {
                        var elapsedTicks = Stopwatch.GetTimestamp() - startTicks;
                        var elapsedMs = elapsedTicks * 1000.0 / Stopwatch.Frequency;
                        latencies.Add(elapsedMs);
                    }

                    if (errorCode == 0)
                        Interlocked.Increment(ref successCount);
                    else
                        Interlocked.Increment(ref failedCount);

                    if (Interlocked.Increment(ref completedCount) >= request.Iterations)
                        tcs.TrySetResult();
                });
            }
        }

        // 모든 콜백 완료 대기 (최대 60초)
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        cts.Token.Register(() => tcs.TrySetCanceled());

        try
        {
            await tcs.Task;
        }
        catch (TaskCanceledException)
        {
            // Timeout - 남은 요청은 실패로 처리
            failedCount += request.Iterations - completedCount;
        }

        totalSw.Stop();

        var latencyList = latencies.ToList();
        SendBenchmarkReply(actor, request, latencyList, successCount, failedCount, totalSw.Elapsed.TotalSeconds);
    }

    /// <summary>
    /// 벤치마크 결과 응답 전송
    /// </summary>
    private void SendBenchmarkReply(
        IActor actor,
        StartSSBenchmarkRequest request,
        List<double> latencies,
        int successCount,
        int failedCount,
        double elapsedSeconds)
    {
        latencies.Sort();
        var meanLatency = latencies.Count > 0 ? latencies.Average() : 0;
        var p50Latency = latencies.Count > 0 ? latencies[(int)(latencies.Count * 0.50)] : 0;
        var p95Latency = latencies.Count > 0 ? latencies[(int)(latencies.Count * 0.95)] : 0;
        var p99Latency = latencies.Count > 0 ? latencies[(int)(latencies.Count * 0.99)] : 0;
        var throughput = successCount / elapsedSeconds;

        var reply = new StartSSBenchmarkReply
        {
            CallType = request.CallType,
            CommMode = request.CommMode,
            TotalIterations = request.Iterations,
            SuccessCount = successCount,
            FailedCount = failedCount,
            ElapsedSeconds = elapsedSeconds,
            ThroughputPerSec = throughput,
            LatencyMeanMs = meanLatency,
            LatencyP50Ms = p50Latency,
            LatencyP95Ms = p95Latency,
            LatencyP99Ms = p99Latency
        };

        actor.ActorSender.Reply(CPacket.Of(reply));
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

                // 즉시 응답 (Echo는 페이로드 그대로 반환)
                actor.ActorSender.Reply(CPacket.Of(new TriggerSSEchoReply
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

                // 즉시 응답 (Echo는 페이로드 그대로 반환)
                actor.ActorSender.Reply(CPacket.Of(new TriggerSSEchoReply
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
    /// Stage 간 통신 시 SSEchoRequest 수신 처리 (다른 Stage 또는 API에서 온 요청)
    /// </summary>
    private void HandleSSEchoRequest(IPacket packet)
    {
        var request = SSEchoRequest.Parser.ParseFrom(packet.Payload.DataSpan);

        // Echo 응답 (페이로드를 그대로 반환)
        var reply = new SSEchoReply { Payload = request.Payload };
        StageSender.Reply(CPacket.Of(reply));
    }
}
