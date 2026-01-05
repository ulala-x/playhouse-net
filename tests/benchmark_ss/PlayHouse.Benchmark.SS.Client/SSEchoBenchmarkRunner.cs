using System.Diagnostics;
using Google.Protobuf;
using PlayHouse.Benchmark.SS.Shared.Proto;
using PlayHouse.Connector;
using PlayHouse.Connector.Protocol;
using Serilog;
using ClientConnector = PlayHouse.Connector.Connector;
using ClientPacket = PlayHouse.Connector.Protocol.Packet;

namespace PlayHouse.Benchmark.SS.Client;

/// <summary>
/// SS Echo 벤치마크 시나리오를 실행합니다 (duration 기반, TriggerSSEchoRequest 사용).
/// </summary>
public class SSEchoBenchmarkRunner(
    string serverHost,
    int serverPort,
    int connections,
    int requestSize,
    int responseSize,
    SSCommMode commMode,
    SSCallType callType,
    long targetStageId,
    string targetNid,
    ClientMetricsCollector metricsCollector,
    int durationSeconds = 10)
{
    // 재사용 버퍼
    private readonly ByteString _requestPayload = CreatePayload(requestSize);

    // 연결 동기화용 카운트다운 (Stage→Stage에서 모든 연결이 완료된 후 메시지 전송 시작)
    private readonly CountdownEvent _connectionCountdown = new(connections);
    private readonly ManualResetEventSlim _allConnectionsReady = new(false);

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
        Log.Information("[{Time:HH:mm:ss}] Starting SS Echo benchmark...", DateTime.Now);
        Log.Information("  Connections: {Connections:N0}", connections);
        Log.Information("  Duration: {Duration:N0} seconds", durationSeconds);
        Log.Information("  Request size: {RequestSize:N0} bytes", requestSize);
        Log.Information("  Response size: {ResponseSize:N0} bytes", responseSize);
        Log.Information("  CommMode: {CommMode}", commMode);
        Log.Information("  CallType: {CallType}", callType);

        if (callType == SSCallType.StageToStage)
        {
            // Stage→Stage: 이미 생성된 다른 Stage를 타겟으로 사용
            // 각 connectionId의 Stage는 다음 connectionId의 Stage를 타겟으로 함 (순환)
            Log.Information("  Target Strategy: Connection N targets Stage of Connection (N+1) mod {Connections}", connections);
            Log.Information("  Target NID: {TargetNid}", targetNid);
        }

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
        Log.Information("[{Time:HH:mm:ss}] SS Echo benchmark completed.", DateTime.Now);
    }

    private async Task RunConnectionAsync(int connectionId)
    {
        // 각 Task마다 ImmediateSynchronizationContext 설정하여 폴링 지연 제거
        SynchronizationContext.SetSynchronizationContext(
            new ImmediateSynchronizationContext());

        var connector = new ClientConnector();
        connector.Init(new ConnectorConfig());

        var stageId = 1000 + connectionId; // 각 연결마다 고유 StageId

        // PlayServer에 연결
        var connected = await connector.ConnectAsync(serverHost, serverPort, stageId, "BenchmarkStage");
        if (!connected)
        {
            Interlocked.Increment(ref _failedCount);
            UpdateConnectionProgress();
            // 카운트다운에서 신호 (다른 연결이 무한 대기하지 않도록)
            if (callType == SSCallType.StageToStage)
            {
                try { _connectionCountdown.Signal(); } catch { }
            }
            return;
        }

        // 인증
        try
        {
            using (var authPacket = ClientPacket.Empty("Authenticate"))
            {
                await connector.AuthenticateAsync(authPacket);
                Interlocked.Increment(ref _connectedCount);
                UpdateConnectionProgress();
            }
        }
        catch
        {
            Interlocked.Increment(ref _failedCount);
            UpdateConnectionProgress();
            connector.Disconnect();
            if (callType == SSCallType.StageToStage)
            {
                try { _connectionCountdown.Signal(); } catch { }
            }
            return;
        }

        // Stage→Stage: 모든 연결이 완료될 때까지 대기 (타겟 Stage가 생성되어 있어야 함)
        if (callType == SSCallType.StageToStage)
        {
            Log.Debug("[Connection {ConnectionId}] Signaling connection ready...", connectionId);
            // Stage→Stage에서는 connectionCountdown 신호를 별도로 보냄
            _connectionCountdown.Signal();

            // 비동기 대기
            while (!_allConnectionsReady.IsSet)
            {
                await Task.Delay(10);

                if (_connectionCountdown.IsSet && !_allConnectionsReady.IsSet)
                {
                    _allConnectionsReady.Set();
                }
            }
            Log.Debug("[Connection {ConnectionId}] All connections ready, starting echo messages", connectionId);

            // Stage가 서버에서 완전히 등록될 시간을 줌
            await Task.Delay(500);
        }

        // Echo 메시지 전송
        await RunEchoMessagesAsync(connector, connectionId);

        connector.Disconnect();
    }

    private async Task RunEchoMessagesAsync(ClientConnector connector, int connectionId)
    {
        // Stage→Stage: 동적으로 타겟 Stage 계산 (다음 connection의 Stage를 타겟으로 함)
        // Connection N → Stage (1000 + (N+1) % connections)
        // 예: connections=3인 경우
        //   - Connection 0 (Stage 1000) → Stage 1001
        //   - Connection 1 (Stage 1001) → Stage 1002
        //   - Connection 2 (Stage 1002) → Stage 1000 (순환)
        var actualTargetStageId = callType == SSCallType.StageToStage
            ? 1000 + ((connectionId + 1) % connections)
            : targetStageId;

        // Send 모드는 별도 처리 (OnReceive 콜백 사용)
        if (commMode == SSCommMode.Send)
        {
            await RunSendModeAsync(connector, connectionId, actualTargetStageId);
            return;
        }

        // Duration-based
        var endTime = DateTime.UtcNow.AddSeconds(durationSeconds);

        for (int i = 0; DateTime.UtcNow < endTime; i++)
        {
            // 요청 객체 생성 (매번 새로 생성)
            var request = new TriggerSSEchoRequest
            {
                Sequence = i,
                Payload = _requestPayload,
                CommMode = commMode,
                CallType = callType,
                TargetNid = targetNid,
                TargetStageId = actualTargetStageId
            };

            using var packet = new ClientPacket(request);

            metricsCollector.RecordSent();

            var e2eStopwatch = Stopwatch.StartNew();
            try
            {
                using var response = await connector.RequestAsync(packet);
                e2eStopwatch.Stop();

                if (response.MsgId != "TriggerSSEchoReply")
                {
                    Log.Warning("[Connection {ConnectionId}] Unexpected response: {MsgId}", connectionId, response.MsgId);
                    continue;
                }

                // 응답 파싱
                var reply = TriggerSSEchoReply.Parser.ParseFrom(response.Payload.DataSpan);

                // E2E 레이턴시만 기록 (서버 측 SS 레이턴시는 별도로 측정되지 않음)
                // SS 레이턴시는 0으로 설정 (Echo 벤치마크에서는 측정하지 않음)
                metricsCollector.RecordReceived(e2eStopwatch.ElapsedTicks, ssElapsedTicks: 0);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[Connection {ConnectionId}] Request failed at message {Sequence}", connectionId, i);
            }
        }
    }

    /// <summary>
    /// Send 모드: connector.Send() → 서버 SendToClient() → OnReceive 콜백
    /// benchmark_cs와 동일한 방식
    /// </summary>
    private async Task RunSendModeAsync(ClientConnector connector, int connectionId, long actualTargetStageId)
    {
        var endTime = DateTime.UtcNow.AddSeconds(durationSeconds);
        var timestamps = new System.Collections.Concurrent.ConcurrentDictionary<int, long>();
        var sequence = 0;
        var sentCount = 0;
        var receivedCount = 0;

        // OnReceive 이벤트 핸들러 등록 (서버의 SendToClient 응답 수신)
        void OnReceiveHandler(long stageId, string stageType, IPacket packet)
        {
            if (packet.MsgId == "TriggerSSEchoReply")
            {
                // 시퀀스 번호로 타임스탬프 매칭 (간단히 최근 것 사용)
                var elapsed = Stopwatch.GetTimestamp() - timestamps.Values.FirstOrDefault();
                metricsCollector.RecordReceived(elapsed > 0 ? elapsed : 0, ssElapsedTicks: 0);
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

                var request = new TriggerSSEchoRequest
                {
                    Sequence = seq,
                    Payload = _requestPayload,
                    CommMode = SSCommMode.Send,
                    CallType = callType,
                    TargetNid = targetNid,
                    TargetStageId = actualTargetStageId
                };

                using var packet = new ClientPacket(request);

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
            var overallTimeoutSec = 60;
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
