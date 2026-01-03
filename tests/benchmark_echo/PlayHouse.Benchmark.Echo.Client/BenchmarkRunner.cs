using System.Collections.Concurrent;
using System.Diagnostics;
using PlayHouse.Benchmark.Echo.Shared.Proto;
using PlayHouse.Connector;
using PlayHouse.Connector.Protocol;
using Serilog;
using ClientConnector = PlayHouse.Connector.Connector;
using ClientPacket = PlayHouse.Connector.Protocol.Packet;

namespace PlayHouse.Benchmark.Echo.Client;

/// <summary>
/// Echo 벤치마크 시나리오를 실행합니다.
/// </summary>
public class BenchmarkRunner(
    string serverHost,
    int serverPort,
    int connections,
    int durationSeconds,
    int payloadSize,
    BenchmarkMode mode,
    ClientMetricsCollector metricsCollector,
    string stageName = "EchoStage")
{
    /// <summary>
    /// 지정된 크기의 페이로드를 생성합니다. (압축 방지를 위해 패턴으로 채움)
    /// </summary>
    private static string CreatePayload(int size)
    {
        if (size <= 0) return string.Empty;

        // 반복 패턴으로 채워서 압축 방지
        var pattern = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
        var result = new char[size];
        for (int i = 0; i < size; i++)
        {
            result[i] = pattern[i % pattern.Length];
        }
        return new string(result);
    }

    public async Task RunAsync()
    {
        Log.Information("[{Time:HH:mm:ss}] Starting Echo benchmark...", DateTime.Now);
        Log.Information("  Mode: {Mode}", mode);
        Log.Information("  Connections: {Connections:N0}", connections);
        Log.Information("  Duration: {Duration:N0} seconds", durationSeconds);
        Log.Information("  Payload size: {PayloadSize:N0} bytes", payloadSize);

        metricsCollector.Reset();

        // 모든 연결을 병렬로 시작 (단일 Stage 공유로 Stage 생성 부하 없음)
        var tasks = new List<Task>();
        for (int i = 0; i < connections; i++)
        {
            var connectionId = i;
            tasks.Add(Task.Run(async () => await RunConnectionAsync(connectionId)));
        }

        await Task.WhenAll(tasks);

        Log.Information("[{Time:HH:mm:ss}] Echo benchmark completed.", DateTime.Now);
    }

    private async Task RunConnectionAsync(int connectionId)
    {
        var connector = new ClientConnector();
        connector.Init(new ConnectorConfig
        {
            RequestTimeoutMs = 60000 // 60초 타임아웃 (대량 연결 시 Stage 생성 시간 고려)
        });

        var stageId = 1000; // 모든 연결이 동일한 Stage 공유

        // 연결
        var connected = await connector.ConnectAsync(serverHost, serverPort, stageId, stageName);
        if (!connected)
        {
            Log.Warning("[Connection {ConnectionId}] Failed to connect", connectionId);
            return;
        }

        // 대량 연결 시 Stage 초기화 안정화를 위한 짧은 대기
        if (connections > 100)
        {
            await Task.Delay(10);
        }

        // 인증 (benchmark_cs와 동일한 방식)
        try
        {
            using (var authPacket = ClientPacket.Empty("AuthenticateRequest"))
            {
                var authReply = await connector.AuthenticateAsync(authPacket);
                Log.Debug("[Connection {ConnectionId}] Authentication succeeded. Reply: {MsgId}", connectionId, authReply.MsgId);
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[Connection {ConnectionId}] Authentication failed", connectionId);
            connector.Disconnect();
            return;
        }

        // 모드에 따라 메시지 전송
        if (mode == BenchmarkMode.RequestAsync)
        {
            await RunRequestAsyncMode(connector, connectionId);
        }
        else
        {
            await RunRequestCallbackMode(connector, connectionId);
        }

        connector.Disconnect();
    }

    private async Task RunRequestAsyncMode(ClientConnector connector, int connectionId)
    {
        // 페이로드 생성 (재사용)
        var payload = CreatePayload(payloadSize);

        // 요청 객체 재사용
        var request = new EchoRequest
        {
            Content = payload
        };

        var deadline = DateTime.UtcNow.AddSeconds(durationSeconds);
        var messageIndex = 0;

        while (DateTime.UtcNow < deadline)
        {
            // 변경되는 필드만 업데이트
            request.ClientTimestamp = Stopwatch.GetTimestamp();

            using var packet = new ClientPacket(request);

            metricsCollector.RecordSent();

            var sw = Stopwatch.StartNew();
            try
            {
                using var response = await connector.RequestAsync(packet);
                sw.Stop();

                if (response.MsgId != "EchoReply")
                {
                    Log.Warning("[Connection {ConnectionId}] Unexpected response: {MsgId}", connectionId, response.MsgId);
                }

                metricsCollector.RecordReceived(sw.ElapsedTicks);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[Connection {ConnectionId}] Request failed at message {Index}", connectionId, messageIndex);
            }

            // 콜백 처리
            connector.MainThreadAction();
            messageIndex++;
        }
    }

    private async Task RunRequestCallbackMode(ClientConnector connector, int connectionId)
    {
        var timestamps = new ConcurrentDictionary<int, long>();

        // 동시 요청 수 제한 (backpressure)
        var maxConcurrentRequests = 100;
        var semaphore = new SemaphoreSlim(maxConcurrentRequests);

        // 페이로드 생성 (재사용)
        var payload = CreatePayload(payloadSize);

        // 요청 객체 재사용
        var request = new EchoRequest
        {
            Content = payload
        };

        var deadline = DateTime.UtcNow.AddSeconds(durationSeconds);
        var messageIndex = 0;

        // 메시지 전송 (Request with callback) - 시간 기반
        while (DateTime.UtcNow < deadline)
        {
            // 동시 요청 수 제한을 위해 semaphore 대기
            while (semaphore.CurrentCount == 0)
            {
                connector.MainThreadAction();
                await Task.Delay(1);
            }

            await semaphore.WaitAsync();

            request.ClientTimestamp = Stopwatch.GetTimestamp();

            var packet = new ClientPacket(request);
            var seq = messageIndex;
            timestamps[seq] = Stopwatch.GetTimestamp();
            metricsCollector.RecordSent();

            connector.Request(packet, response =>
            {
                // 콜백에서 응답 처리
                if (timestamps.TryRemove(seq, out var startTicks))
                {
                    var elapsed = Stopwatch.GetTimestamp() - startTicks;
                    metricsCollector.RecordReceived(elapsed);
                }

                semaphore.Release();
                packet.Dispose();
            });

            // 콜백 처리를 위해 주기적으로 호출
            connector.MainThreadAction();
            messageIndex++;
        }

        // 남은 응답 수신 대기
        var responseTimeoutSec = 10;
        var responseDeadline = DateTime.UtcNow.AddSeconds(responseTimeoutSec);

        while (timestamps.Count > 0 && DateTime.UtcNow < responseDeadline)
        {
            connector.MainThreadAction();
            await Task.Delay(1);
        }

        if (timestamps.Count > 0)
        {
            Log.Warning("[Connection {ConnectionId}] Timeout waiting for responses (pending: {Pending})",
                connectionId, timestamps.Count);
        }
    }
}

public enum BenchmarkMode
{
    RequestAsync,
    RequestCallback
}
