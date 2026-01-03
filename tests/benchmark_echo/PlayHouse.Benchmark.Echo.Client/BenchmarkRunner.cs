using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.Http.Json;
using Google.Protobuf;
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
    int httpPort,
    int connections,
    int durationSeconds,
    int payloadSize,
    BenchmarkMode mode,
    ClientMetricsCollector metricsCollector,
    long baseStageId = 10000,
    string stageName = "EchoStage",
    int times = 200)
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
        Log.Information("  In-flight messages per connection: {Times:N0}", times);

        // Stage 사전 생성
        await CreateStagesAsync();

        metricsCollector.Reset();

        // 모든 연결을 병렬로 시작 (각 클라이언트가 독립 Stage에 1:1 연결)
        var tasks = new List<Task>();
        for (int i = 0; i < connections; i++)
        {
            var connectionId = i;
            tasks.Add(Task.Run(async () => await RunConnectionAsync(connectionId)));
        }

        await Task.WhenAll(tasks);

        Log.Information("[{Time:HH:mm:ss}] Echo benchmark completed.", DateTime.Now);
    }

    private async Task CreateStagesAsync()
    {
        Log.Information("[{Time:HH:mm:ss}] Creating {Count} stages (baseStageId: {BaseStageId})...",
            DateTime.Now, connections, baseStageId);

        using var httpClient = new HttpClient();
        var request = new { count = connections, baseStageId };

        try
        {
            var response = await httpClient.PostAsJsonAsync(
                $"http://{serverHost}:{httpPort}/benchmark/stages",
                request);
            response.EnsureSuccessStatusCode();

            var result = await response.Content.ReadAsStringAsync();
            Log.Information("[{Time:HH:mm:ss}] Stages created: {Result}", DateTime.Now, result);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to create stages via API");
            throw;
        }
    }

    private async Task RunConnectionAsync(int connectionId)
    {
        // RequestCallback/Send 모드: 네트워크 스레드에서 즉시 콜백 실행 (폴링 불필요)
        // RequestAsync 모드: SyncContext 설정하지 않음 (병렬 Task await 최적화)
        if (mode != BenchmarkMode.RequestAsync)
        {
            SynchronizationContext.SetSynchronizationContext(new ImmediateSynchronizationContext());
        }

        var connector = new ClientConnector();
        connector.Init(new ConnectorConfig
        {
            RequestTimeoutMs = 60000 // 60초 타임아웃 (대량 연결 시 Stage 생성 시간 고려)
        });

        var stageId = baseStageId + connectionId; // 각 클라이언트 독립 Stage

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
        switch (mode)
        {
            case BenchmarkMode.RequestAsync:
                await RunRequestAsyncMode(connector, connectionId);
                break;
            case BenchmarkMode.RequestCallback:
                await RunRequestCallbackMode(connector, connectionId);
                break;
            case BenchmarkMode.Send:
                await RunSendMode(connector, connectionId);
                break;
        }

        connector.Disconnect();
    }

    private async Task RunRequestAsyncMode(ClientConnector connector, int connectionId)
    {
        // 페이로드 생성 (재사용)
        var payload = CreatePayload(payloadSize);

        var deadline = DateTime.UtcNow.AddSeconds(durationSeconds);

        // times개의 병렬 Task로 파이프라인 시뮬레이션
        var tasks = new Task[times];
        for (int i = 0; i < times; i++)
        {
            var taskId = i;
            tasks[i] = Task.Run(async () =>
            {
                // Task별 요청 객체
                var request = new EchoRequest
                {
                    Content = payload
                };

                var messageIndex = 0;

                while (DateTime.UtcNow < deadline)
                {
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
                            Log.Warning("[Connection {ConnectionId}][Task {TaskId}] Unexpected response: {MsgId}",
                                connectionId, taskId, response.MsgId);
                        }

                        metricsCollector.RecordReceived(sw.ElapsedTicks);
                    }
                    catch (Exception ex)
                    {
                        Log.Error(ex, "[Connection {ConnectionId}][Task {TaskId}] Request failed at message {Index}",
                            connectionId, taskId, messageIndex);
                    }

                    messageIndex++;
                }
            });
        }

        await Task.WhenAll(tasks);
    }

    private async Task RunRequestCallbackMode(ClientConnector connector, int connectionId)
    {
        var timestamps = new ConcurrentDictionary<int, long>();

        // SemaphoreSlim(times)로 슬라이딩 윈도우 구현
        var semaphore = new SemaphoreSlim(times);

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
            // 슬라이딩 윈도우: times개까지 동시 in-flight 유지
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

            messageIndex++;
        }

        // 남은 응답 수신 대기
        var responseTimeoutSec = 10;
        var responseDeadline = DateTime.UtcNow.AddSeconds(responseTimeoutSec);

        while (timestamps.Count > 0 && DateTime.UtcNow < responseDeadline)
        {
            await Task.Delay(10);
        }

        if (timestamps.Count > 0)
        {
            Log.Warning("[Connection {ConnectionId}] Timeout waiting for responses (pending: {Pending})",
                connectionId, timestamps.Count);
        }
    }

    private async Task RunSendMode(ClientConnector connector, int connectionId)
    {
        var inFlightCount = 0;
        var deadline = DateTime.UtcNow.AddSeconds(durationSeconds);

        // 페이로드 생성 (재사용)
        var payload = CreatePayload(payloadSize);

        // OnReceive 콜백 설정: 응답 수신 시 다음 전송 (슬라이딩 윈도우 유지)
        void OnReceiveHandler(long stageId, string stageType, IPacket packet)
        {
            if (packet.MsgId == "EchoReply")
            {
                // 응답 처리 (RTT 측정은 timestamp 기반으로 할 수 없으므로 수신 시점 기록)
                metricsCollector.RecordReceived(0);

                Interlocked.Decrement(ref inFlightCount);

                // 시간이 남았으면 다음 메시지 전송
                if (DateTime.UtcNow < deadline)
                {
                    var request = new EchoRequest
                    {
                        Content = payload,
                        ClientTimestamp = Stopwatch.GetTimestamp()
                    };

                    using var newPacket = new ClientPacket(request);
                    metricsCollector.RecordSent();
                    Interlocked.Increment(ref inFlightCount);
                    connector.Send(newPacket);
                }
            }
        }

        connector.OnReceive += OnReceiveHandler;

        try
        {
            // 초기 times개 전송 (슬라이딩 윈도우 초기화)
            for (int i = 0; i < times; i++)
            {
                var request = new EchoRequest
                {
                    Content = payload,
                    ClientTimestamp = Stopwatch.GetTimestamp()
                };

                using var packet = new ClientPacket(request);
                metricsCollector.RecordSent();
                Interlocked.Increment(ref inFlightCount);
                connector.Send(packet);
            }

            // 지정된 시간만큼 대기 (콜백에서 계속 메시지 전송)
            while (DateTime.UtcNow < deadline)
            {
                await Task.Delay(10);
            }

            // 남은 응답 수신 대기
            var responseTimeoutSec = 10;
            var responseDeadline = DateTime.UtcNow.AddSeconds(responseTimeoutSec);

            while (inFlightCount > 0 && DateTime.UtcNow < responseDeadline)
            {
                await Task.Delay(10);
            }

            if (inFlightCount > 0)
            {
                Log.Warning("[Connection {ConnectionId}] Timeout waiting for responses (pending: {Pending})",
                    connectionId, inFlightCount);
            }
        }
        finally
        {
            // OnReceive 콜백 해제
            connector.OnReceive -= OnReceiveHandler;
        }
    }
}

public enum BenchmarkMode
{
    RequestAsync,
    RequestCallback,
    Send
}
