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
    int requestSize,
    int responseSize,
    BenchmarkMode mode,
    ClientMetricsCollector metricsCollector,
    int stageIdOffset = 0,
    string stageName = "BenchStage",
    int durationSeconds = 10,  // Test duration in seconds
    int delayMs = 0)           // Delay between messages in milliseconds
{
    // 재사용 버퍼
    private readonly ByteString _requestPayload = CreatePayload(requestSize);

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
        Log.Information("  Message size: {RequestSize:N0} bytes", requestSize);

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
        var i = 0;

        while (DateTime.UtcNow < endTime)
        {
            using var packet = new ClientPacket("EchoRequest", payloadBytes);

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
                Log.Error(ex, "[Connection {ConnectionId}] Request failed at message {Sequence}", connectionId, i);
            }

            // 메시지 간 딜레이
            if (delayMs > 0)
            {
                await Task.Delay(delayMs);
            }

            i++;
        }
    }

    private async Task RunRequestCallbackMode(ClientConnector connector, int connectionId)
    {
        var receivedCount = 0;
        var sentCount = 0;
        var timestamps = new ConcurrentDictionary<int, long>();

        var endTime = DateTime.UtcNow.AddSeconds(durationSeconds);

        // Echo 요청: raw payload 사용 (서버에서 zero-copy Move로 반환)
        var payloadBytes = _requestPayload.ToByteArray();

        // 메시지 전송 (Request with callback)
        var i = 0;
        while (DateTime.UtcNow < endTime)
        {
            var packet = new ClientPacket("EchoRequest", payloadBytes);
            var seq = i;
            timestamps[seq] = Stopwatch.GetTimestamp();
            metricsCollector.RecordSent();
            Interlocked.Increment(ref sentCount);

            connector.Request(packet, response =>
            {
                // 콜백에서 응답 처리
                if (timestamps.TryRemove(seq, out var startTicks))
                {
                    var elapsed = Stopwatch.GetTimestamp() - startTicks;
                    metricsCollector.RecordReceived(elapsed);
                }

                Interlocked.Increment(ref receivedCount);
                packet.Dispose();
            });

            i++;
        }

        // 모든 응답 수신 대기
        var overallTimeoutSec = responseSize > 32768 ? 60 : 30;
        var deadline = DateTime.UtcNow.AddSeconds(overallTimeoutSec);

        while (receivedCount < sentCount && DateTime.UtcNow < deadline)
        {
            await Task.Delay(1);
        }

        if (receivedCount < sentCount)
        {
            Log.Warning("[Connection {ConnectionId}] Timeout waiting for responses (received: {Received}/{Total})",
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
        var timestamps = new ConcurrentDictionary<int, long>();
        var sequence = 0;
        var sentCount = 0;
        var receivedCount = 0;

        // OnReceive 이벤트 핸들러 등록 (서버의 SendToClient 응답 수신)
        void OnReceiveHandler(long stageId, string stageType, IPacket packet)
        {
            if (packet.MsgId == "SendReply")
            {
                // 시퀀스 번호로 타임스탬프 매칭 (간단히 최근 것 사용)
                var elapsed = Stopwatch.GetTimestamp() - timestamps.Values.FirstOrDefault();
                metricsCollector.RecordReceived(elapsed > 0 ? elapsed : 0);
                Interlocked.Increment(ref receivedCount);
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
                Interlocked.Increment(ref sentCount);

                try
                {
                    connector.Send(packet);
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "[Connection {ConnectionId}] Send request failed", connectionId);
                }
            }

            // 모든 응답 수신 대기
            var overallTimeoutSec = responseSize > 32768 ? 60 : 30;
            var deadline = DateTime.UtcNow.AddSeconds(overallTimeoutSec);

            while (receivedCount < sentCount && DateTime.UtcNow < deadline)
            {
                await Task.Delay(1);
            }

            if (receivedCount < sentCount)
            {
                Log.Warning("[Connection {ConnectionId}] Timeout waiting for responses (received: {Received}/{Total})",
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
