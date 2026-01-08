using System.Collections.Concurrent;
using System.Diagnostics;
using Google.Protobuf;
using PlayHouse.Connector;
using PlayHouse.Connector.Protocol;
using Serilog;
using ClientConnector = PlayHouse.Connector.Connector;
using ClientPacket = PlayHouse.Connector.Protocol.Packet;

namespace PlayHouse.Benchmark.Client;

/// <summary>
/// 벤치마크 시나리오를 실행합니다 (duration 기반).
/// </summary>
public class BenchmarkRunner(
    string serverHost,
    int serverPort,
    int connections,
    int messageSize,
    BenchmarkMode mode,
    ClientMetricsCollector metricsCollector,
    int stageIdOffset = 0,
    string stageName = "BenchStage",
    int durationSeconds = 10,  // Test duration in seconds
    int maxInFlight = 200)     // Maximum in-flight requests
{
    // 재사용 버퍼
    private readonly ByteString _requestPayload = CreatePayload(messageSize);

    // 연결 진행 카운터
    private int _connectedCount;
    private int _failedCount;
    private readonly object _progressLock = new();

    private void UpdateConnectionProgress()
    {
        lock (_progressLock)
        {
            var connected = _connectedCount;
            var failed = _failedCount;
            var failedStr = failed > 0 ? $", failed: {failed}" : "";
            Console.Write($"\r  Connecting: {connected:N0}/{connections:N0}{failedStr}    ");
        }
    }

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
        Log.Information("  Duration: {Duration:N0} seconds", durationSeconds);
        Log.Information("  Message size: {MessageSize:N0} bytes", messageSize);

        metricsCollector.Reset();

        // 모든 연결을 동시에 시작
        var tasks = new Task[connections];
        for (int i = 0; i < connections; i++)
        {
            var connectionId = i;
            tasks[i] = Task.Run(async () => await RunConnectionAsync(connectionId));
        }

        await Task.WhenAll(tasks);

        // 진행 상황 줄 마무리 후 최종 결과
        Console.WriteLine();
        Log.Information("  Connected: {Connected:N0}/{Total:N0} (failed: {Failed:N0})",
            _connectedCount, connections, _failedCount);
        Log.Information("[{Time:HH:mm:ss}] Benchmark completed.", DateTime.Now);
    }

    private async Task RunConnectionAsync(int connectionId)
    {
        var connector = new ClientConnector();
        connector.Init(new ConnectorConfig());

        var stageId = 1000 + stageIdOffset + connectionId; // 각 연결마다 고유 StageId

        // 연결
        var connected = await connector.ConnectAsync(serverHost, serverPort, stageId, stageName);
        if (!connected)
        {
            var failed = Interlocked.Increment(ref _failedCount);
            UpdateConnectionProgress();
            return;
        }

        // 인증
        try
        {
            using (var authPacket = ClientPacket.Empty("AuthenticateRequest"))
            {
                await connector.AuthenticateAsync(authPacket);
                var count = Interlocked.Increment(ref _connectedCount);
                UpdateConnectionProgress();
            }
        }
        catch
        {
            Interlocked.Increment(ref _failedCount);
            UpdateConnectionProgress();
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
        // Echo 요청: raw payload 사용 (서버에서 zero-copy Move로 반환)
        var payloadBytes = _requestPayload.ToByteArray();
        var endTime = DateTime.UtcNow.AddSeconds(durationSeconds);
        
        // SemaphoreSlim으로 인플라이트 제어
        using var semaphore = new SemaphoreSlim(maxInFlight);
        var tasks = new List<Task>();

        while (DateTime.UtcNow < endTime)
        {
            // Process incoming messages
            connector.MainThreadAction();

            await semaphore.WaitAsync();

            var task = Task.Run(async () =>
            {
                var sw = Stopwatch.StartNew();
                try
                {
                    using var packet = new ClientPacket("EchoRequest", payloadBytes);
                    metricsCollector.RecordSent();
                    using var response = await connector.RequestAsync(packet);
                    sw.Stop();
                    metricsCollector.RecordReceived(sw.ElapsedTicks);
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "[Connection {ConnectionId}] Request failed", connectionId);
                }
                finally
                {
                    semaphore.Release();
                }
            });
            
            tasks.Add(task);
            
            // 너무 많은 Task 객체가 쌓이지 않도록 주기적으로 정리
            if (tasks.Count > maxInFlight * 2)
            {
                tasks.RemoveAll(t => t.IsCompleted);
            }
        }

        await Task.WhenAll(tasks);
    }

    private async Task RunRequestCallbackMode(ClientConnector connector, int connectionId)
    {
        var receivedCount = 0;
        var sentCount = 0;
        var inFlight = 0;
        var timestamps = new ConcurrentDictionary<int, long>();

        var endTime = DateTime.UtcNow.AddSeconds(durationSeconds);

        // Echo 요청: raw payload 사용 (서버에서 zero-copy Move로 반환)
        var payloadBytes = _requestPayload.ToByteArray();

        // 메시지 전송 (Request with callback)
        var i = 0;
        while (DateTime.UtcNow < endTime)
        {
            connector.MainThreadAction();

            // In-flight 제한: 최대치에 도달하면 대기
            while (inFlight >= maxInFlight)
            {
                connector.MainThreadAction(); // 대기 중에도 메시지 처리
                await Task.Yield(); // 콜백 실행 기회 제공
            }

            var packet = new ClientPacket("EchoRequest", payloadBytes);
            var seq = i;
            timestamps[seq] = Stopwatch.GetTimestamp();
            metricsCollector.RecordSent();
            Interlocked.Increment(ref sentCount);
            Interlocked.Increment(ref inFlight);

            connector.Request(packet, response =>
            {
                // 콜백에서 응답 처리
                if (timestamps.TryRemove(seq, out var startTicks))
                {
                    var elapsed = Stopwatch.GetTimestamp() - startTicks;
                    metricsCollector.RecordReceived(elapsed);
                }

                Interlocked.Increment(ref receivedCount);
                Interlocked.Decrement(ref inFlight);
                packet.Dispose();
            });

            i++;
        }

                    // 모든 응답 수신 대기 (남은 in-flight 요청)

                    while (inFlight > 0)

                    {

                        connector.MainThreadAction();

                        await Task.Yield();

                    }

        

                    if (receivedCount < sentCount)

        
        {
            Log.Warning("[Connection {ConnectionId}] Incomplete responses (received: {Received}/{Total})",
                connectionId, receivedCount, sentCount);
        }
    }

    /// <summary>
    /// Duration-based Send mode: Send 요청 → SendToClient 응답 (OnReceive 콜백)
    /// </summary>
    private async Task RunSendMode(ClientConnector connector, int connectionId)
    {
        var endTime = DateTime.UtcNow.AddSeconds(durationSeconds);
        // Echo 요청: raw payload 사용 (서버에서 zero-copy Move로 반환)
        var payload = _requestPayload.ToByteArray();
        var timestamps = new ConcurrentQueue<long>();
        var sentCount = 0;
        var receivedCount = 0;
        var inFlight = 0;

        // OnReceive 이벤트 핸들러 등록 (서버의 SendToClient 응답 수신)
        void OnReceiveHandler(long stageId, string stageType, IPacket packet)
        {
            if (packet.MsgId == "SendReply")
            {
                // FIFO 방식으로 타임스탬프 디큐
                if (timestamps.TryDequeue(out var startTicks))
                {
                    var elapsed = Stopwatch.GetTimestamp() - startTicks;
                    metricsCollector.RecordReceived(elapsed);
                    Interlocked.Increment(ref receivedCount);
                    Interlocked.Decrement(ref inFlight);
                }
            }
            packet.Dispose();
        }

        connector.OnReceive += OnReceiveHandler;

        try
        {
            while (DateTime.UtcNow < endTime)
            {
                connector.MainThreadAction();

                // In-flight 제한: 최대치에 도달하면 대기
                while (inFlight >= maxInFlight)
                {
                    connector.MainThreadAction();
                    await Task.Yield(); // 콜백 실행 기회 제공
                }

                timestamps.Enqueue(Stopwatch.GetTimestamp());

                using var packet = new ClientPacket("SendRequest", payload);

                metricsCollector.RecordSent();
                Interlocked.Increment(ref sentCount);
                Interlocked.Increment(ref inFlight);

                try
                {
                    connector.Send(packet);
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "[Connection {ConnectionId}] Send request failed", connectionId);
                    Interlocked.Decrement(ref inFlight); // 전송 실패 시 카운트 복구
                    timestamps.TryDequeue(out _); // 전송 실패 시 Queue에서도 제거
                }
            }

            // 모든 응답 수신 대기 (남은 in-flight 요청)
            while (inFlight > 0)
            {
                connector.MainThreadAction();
                await Task.Yield();
            }

            if (receivedCount < sentCount)
            {
                Log.Warning("[Connection {ConnectionId}] Incomplete responses (received: {Received}/{Total})",
                    connectionId, receivedCount, sentCount);
            }
        }
        finally
        {
            connector.OnReceive -= OnReceiveHandler;
        }
    }
}

public enum BenchmarkMode
{
    RequestAsync,     // await 기반 요청/응답 (Echo 테스트)
    RequestCallback,  // 콜백 기반 요청/응답 (Echo 테스트)
    Send              // Fire-and-forget (응답 없음, duration 기반)
}
