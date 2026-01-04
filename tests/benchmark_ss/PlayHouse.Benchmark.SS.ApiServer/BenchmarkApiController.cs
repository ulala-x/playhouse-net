#nullable enable

using System.Collections.Concurrent;
using PlayHouse.Abstractions;
using PlayHouse.Abstractions.Api;
using PlayHouse.Benchmark.SS.Shared.Proto;
using PlayHouse.Core.Shared;

namespace PlayHouse.Benchmark.SS.ApiServer;

/// <summary>
/// Server-to-Server 벤치마크용 API Controller.
/// SSBenchmarkRequest를 받아서 지정된 크기의 SSBenchmarkReply를 반환합니다.
/// </summary>
public class BenchmarkApiController : IApiController
{
    /// <summary>
    /// 페이로드 캐시 - 크기별로 한 번만 생성하여 재사용
    /// </summary>
    private static readonly ConcurrentDictionary<int, Google.Protobuf.ByteString> PayloadCache = new();

    private static Google.Protobuf.ByteString GetOrCreatePayload(int size)
    {
        if (size <= 0) return Google.Protobuf.ByteString.Empty;

        return PayloadCache.GetOrAdd(size, s =>
        {
            var payload = new byte[s];
            for (int i = 0; i < payload.Length; i++)
            {
                payload[i] = (byte)(i % 256);
            }
            return Google.Protobuf.ByteString.CopyFrom(payload);
        });
    }

    private IApiSender? _apiSender;

    public void Handles(IHandlerRegister register)
    {
        register.Add(nameof(SSBenchmarkRequest), HandleSSBenchmark);
        register.Add(nameof(StartSSBenchmarkRequest), HandleStartSSBenchmark);
        register.Add(nameof(CreateStageRequest), HandleCreateStage);
        register.Add(nameof(SSEchoRequest), HandleSSEchoRequest);
    }

    /// <summary>
    /// Stage 생성 요청 처리.
    /// HTTP 클라이언트에서 온 CreateStageRequest를 받아서 PlayServer에 Stage를 생성합니다.
    /// </summary>
    private async Task HandleCreateStage(IPacket packet, IApiSender sender)
    {
        _apiSender = sender;

        var request = CreateStageRequest.Parser.ParseFrom(packet.Payload.DataSpan);

        try
        {
            var createPacket = CPacket.Empty("CreateStage");
            var result = await sender.CreateStage(
                request.PlayNid,
                request.StageType,
                request.StageId,
                createPacket);

            var reply = new CreateStageReply
            {
                Success = result.Result,
                ErrorCode = result.Result ? 0 : -1,
                StageId = request.StageId,
                PlayNid = request.PlayNid
            };

            sender.Reply(CPacket.Of(reply));
        }
        catch (Exception ex)
        {
            var reply = new CreateStageReply
            {
                Success = false,
                ErrorCode = -1,
                StageId = request.StageId,
                PlayNid = request.PlayNid,
                ErrorMessage = ex.Message
            };

            sender.Reply(CPacket.Of(reply));
        }
    }

    /// <summary>
    /// Echo 요청 처리.
    /// SSEchoRequest를 받아서 동일한 Payload를 담은 SSEchoReply를 반환합니다.
    /// </summary>
    private Task HandleSSEchoRequest(IPacket packet, IApiSender sender)
    {
        _apiSender = sender;

        var request = SSEchoRequest.Parser.ParseFrom(packet.Payload.DataSpan);

        // 그대로 Echo 응답
        var reply = new SSEchoReply
        {
            Payload = request.Payload
        };

        sender.Reply(CPacket.Of(reply));
        return Task.CompletedTask;
    }

    /// <summary>
    /// Server-to-Server 벤치마크 요청 처리.
    /// Stage나 다른 API Server에서 온 SSBenchmarkRequest를 처리하여
    /// 지정된 크기의 페이로드를 담은 SSBenchmarkReply를 반환합니다.
    /// </summary>
    private Task HandleSSBenchmark(IPacket packet, IApiSender sender)
    {
        _apiSender = sender;

        var request = SSBenchmarkRequest.Parser.ParseFrom(packet.Payload.DataSpan);

        // 캐시된 페이로드 사용 (첫 번째 요청 시 생성, 이후 재사용)
        var payload = GetOrCreatePayload(request.ResponseSize);

        var reply = new SSBenchmarkReply
        {
            Sequence = request.Sequence,
            Payload = payload
        };

        sender.Reply(CPacket.Of(reply));
        return Task.CompletedTask;
    }

    /// <summary>
    /// API → API 내부 반복 벤치마크 처리
    /// </summary>
    private async Task HandleStartSSBenchmark(IPacket packet, IApiSender sender)
    {
        _apiSender = sender;

        var request = StartSSBenchmarkRequest.Parser.ParseFrom(packet.Payload.DataSpan);

        // RequestCallback 모드는 별도 핸들러 사용
        if (request.CommMode == SSCommMode.RequestCallback)
        {
            await HandleRequestCallbackMode(request, sender);
            return;
        }

        // RequestAsync 모드: 순차 await 방식
        var latencies = new List<double>(request.Iterations);
        var successCount = 0;
        var failedCount = 0;

        var totalSw = System.Diagnostics.Stopwatch.StartNew();

        for (int i = 0; i < request.Iterations; i++)
        {
            var ssRequest = new SSBenchmarkRequest
            {
                Sequence = i,
                ResponseSize = request.ResponseSize
            };

            var iterSw = System.Diagnostics.Stopwatch.StartNew();

            try
            {
                if (request.CallType == SSCallType.ApiToApi)
                {
                    await sender.RequestToApi(request.TargetApiNid, CPacket.Of(ssRequest));
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

        SendBenchmarkReply(sender, request, latencies, successCount, failedCount, totalSw.Elapsed.TotalSeconds);
    }

    /// <summary>
    /// RequestCallback 모드: 모든 요청을 비동기로 발사하고 콜백에서 결과 수집
    /// </summary>
    private async Task HandleRequestCallbackMode(StartSSBenchmarkRequest request, IApiSender sender)
    {
        var latencies = new ConcurrentBag<double>();
        var successCount = 0;
        var failedCount = 0;
        var completedCount = 0;
        var tcs = new TaskCompletionSource();

        // 각 요청별 시작 시간 기록
        var startTimes = new ConcurrentDictionary<int, long>();

        var totalSw = System.Diagnostics.Stopwatch.StartNew();

        for (int i = 0; i < request.Iterations; i++)
        {
            var seq = i;
            var ssRequest = new SSBenchmarkRequest
            {
                Sequence = seq,
                ResponseSize = request.ResponseSize
            };

            startTimes[seq] = System.Diagnostics.Stopwatch.GetTimestamp();

            if (request.CallType == SSCallType.ApiToApi)
            {
                sender.RequestToApi(request.TargetApiNid, CPacket.Of(ssRequest), (errorCode, reply) =>
                {
                    if (startTimes.TryRemove(seq, out var startTicks))
                    {
                        var elapsedTicks = System.Diagnostics.Stopwatch.GetTimestamp() - startTicks;
                        var elapsedMs = elapsedTicks * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
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
        SendBenchmarkReply(sender, request, latencyList, successCount, failedCount, totalSw.Elapsed.TotalSeconds);
    }

    /// <summary>
    /// 벤치마크 결과 응답 전송
    /// </summary>
    private void SendBenchmarkReply(
        IApiSender sender,
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

        sender.Reply(CPacket.Of(reply));
    }
}
