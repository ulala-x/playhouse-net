using System.Collections.Concurrent;
using System.Diagnostics;
using Google.Protobuf;
using PlayHouse.Benchmark.Shared.Proto;
using PlayHouse.Connector;
using PlayHouse.Connector.Protocol;
using Serilog;
using ClientConnector = PlayHouse.Connector.Connector;
using ClientPacket = PlayHouse.Connector.Protocol.Packet;

namespace PlayHouse.Benchmark.Client;

/// <summary>
/// 벤치마크 시나리오를 실행합니다.
/// </summary>
public class BenchmarkRunner(
    string serverHost,
    int serverPort,
    int connections,
    int messagesPerConnection,
    int requestSize,
    int responseSize,
    BenchmarkMode mode,
    ClientMetricsCollector metricsCollector,
    int stageIdOffset = 0,
    string stageName = "BenchStage",
    int durationSeconds = 10,  // Time-based test duration for Echo mode
    int batchSize = 100)       // Number of connections to create per batch
{
    // 재사용 버퍼
    private readonly ByteString _requestPayload = CreatePayload(requestSize);

    // 요청 페이로드 미리 생성 (재사용)

    /// <summary>
    /// 지정된 크기의 페이로드를 생성합니다. (압축 방지를 위해 패턴으로 채움)
    /// </summary>
    private static ByteString CreatePayload(int size)
    {
        if (size <= 0) return ByteString.Empty;

        var payload = new byte[size];
        for (int i = 0; i < payload.Length; i++)
        {
            payload[i] = (byte)(i % 256);
        }
        return ByteString.CopyFrom(payload);
    }

    public async Task RunAsync()
    {
        Log.Information("[{Time:HH:mm:ss}] Starting benchmark...", DateTime.Now);
        Log.Information("  Mode: {Mode}", mode);
        Log.Information("  Connections: {Connections:N0}", connections);
        Log.Information("  Batch size: {BatchSize:N0}", batchSize);

        if (mode == BenchmarkMode.Send)
        {
            Log.Information("  Duration: {Duration:N0} seconds", durationSeconds);
            Log.Information("  Request size: {RequestSize:N0} bytes", requestSize);
        }
        else
        {
            Log.Information("  Messages per connection: {Messages:N0}", messagesPerConnection);
            Log.Information("  Total messages: {Total:N0}", connections * messagesPerConnection);
            Log.Information("  Request size: {RequestSize:N0} bytes", requestSize);
            Log.Information("  Response size: {ResponseSize:N0} bytes", responseSize);
        }

        metricsCollector.Reset();

        var tasks = new List<Task>();
        var batches = (connections + batchSize - 1) / batchSize;

        // 연결을 배치로 나누어 점진적으로 생성
        for (int batch = 0; batch < batches; batch++)
        {
            var start = batch * batchSize;
            var end = Math.Min(start + batchSize, connections);

            Log.Information("Connecting batch {Batch}/{Total} (connections {Start}-{End})",
                batch + 1, batches, start + 1, end);

            // 현재 배치의 연결 시작
            for (int i = start; i < end; i++)
            {
                var connectionId = i;
                tasks.Add(Task.Run(async () => await RunConnectionAsync(connectionId)));
            }

            // 배치 간 대기 (마지막 배치 제외)
            if (batch < batches - 1)
            {
                await Task.Delay(500);
            }
        }

        await Task.WhenAll(tasks);

        Log.Information("[{Time:HH:mm:ss}] Benchmark completed.", DateTime.Now);
    }

    private async Task RunConnectionAsync(int connectionId)
    {
        // 각 Task마다 ImmediateSynchronizationContext 설정하여 폴링 지연 제거
        SynchronizationContext.SetSynchronizationContext(
            new ImmediateSynchronizationContext());

        var connector = new ClientConnector();
        connector.Init(new ConnectorConfig());

        var stageId = 1000 + stageIdOffset + connectionId; // 각 연결마다 고유 StageId

        // 연결
        var connected = await connector.ConnectAsync(serverHost, serverPort, stageId, stageName);
        if (!connected)
        {
            Log.Warning("[Connection {ConnectionId}] Failed to connect", connectionId);
            return;
        }

        // 인증
        try
        {
            using (var authPacket = ClientPacket.Empty("AuthenticateRequest"))
            {
                var authReply = await connector.AuthenticateAsync(authPacket);
                Log.Information("[Connection {ConnectionId}] Authentication succeeded. Reply: {MsgId}", connectionId, authReply.MsgId);
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
        else if (mode == BenchmarkMode.RequestCallback)
        {
            await RunRequestCallbackMode(connector, connectionId);
        }
        else if (mode == BenchmarkMode.Send)
        {
            await RunSendMode(connector, connectionId);
        }

        connector.Disconnect();
    }

    private async Task RunRequestAsyncMode(ClientConnector connector, int connectionId)
    {
        // 요청 객체 재사용
        var request = new BenchmarkRequest
        {
            ResponseSize = responseSize,
            Payload = _requestPayload
        };

        for (int i = 0; i < messagesPerConnection; i++)
        {
            // 변경되는 필드만 업데이트
            request.Sequence = i;
            request.ClientTimestamp = Stopwatch.GetTimestamp();

            using var packet = new ClientPacket(request);

            metricsCollector.RecordSent();

            var sw = Stopwatch.StartNew();
            try
            {
                using var response = await connector.RequestAsync(packet);
                sw.Stop();

                if (response.MsgId != "BenchmarkReply")
                {
                    Log.Warning("[Connection {ConnectionId}] Unexpected response: {MsgId}", connectionId, response.MsgId);
                }

                metricsCollector.RecordReceived(sw.ElapsedTicks);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[Connection {ConnectionId}] Request failed at message {Sequence}", connectionId, i);
            }

            // 콜백 처리
            connector.MainThreadAction();
        }
    }

    private async Task RunRequestCallbackMode(ClientConnector connector, int connectionId)
    {
        var receivedCount = 0;
        var timestamps = new ConcurrentDictionary<int, long>();
        var tcs = new TaskCompletionSource();

        // 동시 요청 수 제한 (backpressure) - 응답 크기에 따라 조절
        var maxConcurrentRequests = responseSize switch
        {
            > 32768 => 10,   // 32KB 이상: 10개
            > 8192 => 30,    // 8KB 이상: 30개
            > 1024 => 50,    // 1KB 이상: 50개
            _ => 100         // 기본: 100개
        };
        var semaphore = new SemaphoreSlim(maxConcurrentRequests);

        // 요청 객체 재사용
        var request = new BenchmarkRequest
        {
            ResponseSize = responseSize,
            Payload = _requestPayload
        };

        // 메시지 전송 (Request with callback)
        for (int i = 0; i < messagesPerConnection; i++)
        {
            // 동시 요청 수 제한을 위해 semaphore 대기
            while (semaphore.CurrentCount == 0)
            {
                connector.MainThreadAction();
                await Task.Delay(1);
            }

            await semaphore.WaitAsync();

            request.Sequence = i;
            request.ClientTimestamp = Stopwatch.GetTimestamp();

            var packet = new ClientPacket(request);
            var seq = i;
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

                var count = Interlocked.Increment(ref receivedCount);
                if (count >= messagesPerConnection)
                {
                    tcs.TrySetResult();
                }

                packet.Dispose();
            });

            // 콜백 처리를 위해 주기적으로 호출
            connector.MainThreadAction();
        }

        // 모든 응답 수신 대기 (응답 크기에 따라 타임아웃 조절)
        var overallTimeoutSec = responseSize > 32768 ? 60 : 30;
        var deadline = DateTime.UtcNow.AddSeconds(overallTimeoutSec);

        while (receivedCount < messagesPerConnection && DateTime.UtcNow < deadline)
        {
            connector.MainThreadAction();
            await Task.Delay(1);
        }

        if (receivedCount < messagesPerConnection)
        {
            Log.Warning("[Connection {ConnectionId}] Timeout waiting for responses (received: {Received}/{Total})",
                connectionId, receivedCount, messagesPerConnection);
        }
    }

    /// <summary>
    /// Time-based Send mode: Send 요청 → SendToClient 응답 (OnReceive 콜백)
    /// </summary>
    private async Task RunSendMode(ClientConnector connector, int connectionId)
    {
        var endTime = DateTime.UtcNow.AddSeconds(durationSeconds);
        var payload = CreatePayloadBytes(requestSize);
        var timestamps = new ConcurrentDictionary<int, long>();
        var sequence = 0;

        // OnReceive 이벤트 핸들러 등록 (서버의 SendToClient 응답 수신)
        void OnReceiveHandler(long stageId, string stageType, IPacket packet)
        {
            if (packet.MsgId == "SendReply")
            {
                // 시퀀스 번호로 타임스탬프 매칭 (간단히 최근 것 사용)
                var elapsed = Stopwatch.GetTimestamp() - timestamps.Values.FirstOrDefault();
                metricsCollector.RecordReceived(elapsed > 0 ? elapsed : 0);
            }
            packet.Dispose();
        }

        connector.OnReceive += OnReceiveHandler;

        try
        {
            while (DateTime.UtcNow < endTime)
            {
                var seq = Interlocked.Increment(ref sequence);
                timestamps[seq] = Stopwatch.GetTimestamp();

                using var packet = new ClientPacket("SendRequest", payload);

                metricsCollector.RecordSent();

                try
                {
                    connector.Send(packet);
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "[Connection {ConnectionId}] Send request failed", connectionId);
                }

                // 콜백 처리 (OnReceive 호출)
                connector.MainThreadAction();

                // 서버 과부하 방지
                await Task.Yield();
            }

            // 남은 응답 처리를 위해 잠시 대기
            var drainEnd = DateTime.UtcNow.AddMilliseconds(500);
            while (DateTime.UtcNow < drainEnd)
            {
                connector.MainThreadAction();
                await Task.Delay(10);
            }
        }
        finally
        {
            connector.OnReceive -= OnReceiveHandler;
        }
    }

    /// <summary>
    /// Creates raw byte payload for Echo mode (zero-copy)
    /// </summary>
    private static byte[] CreatePayloadBytes(int size)
    {
        if (size <= 0) return Array.Empty<byte>();

        var payload = new byte[size];
        for (int i = 0; i < payload.Length; i++)
        {
            payload[i] = (byte)(i % 256);
        }
        return payload;
    }
}

public enum BenchmarkMode
{
    RequestAsync,     // await 기반 요청/응답 (Echo 테스트)
    RequestCallback,  // 콜백 기반 요청/응답 (Echo 테스트)
    Send              // Fire-and-forget (응답 없음, 시간 기반)
}
