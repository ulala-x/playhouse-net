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
    long initialStageId,
    long targetStageId,
    string targetNid,
    ClientMetricsCollector metricsCollector)
{
    // 재사용 버퍼
    private readonly ByteString _requestPayload = CreatePayload(requestSize);

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
        Log.Information("  Messages per connection: {Messages:N0}", messagesPerConnection);
        Log.Information("  Total messages: {Total:N0}", connections * messagesPerConnection);
        Log.Information("  Request size: {RequestSize:N0} bytes", requestSize);
        Log.Information("  Response size: {ResponseSize:N0} bytes", responseSize);

        if (mode == BenchmarkMode.PlayToStage)
        {
            Log.Information("  Target Stage ID: {TargetStageId}", targetStageId);
            Log.Information("  Target NID: {TargetNid}", targetNid);
        }

        metricsCollector.Reset();

        var tasks = new List<Task>();

        for (int i = 0; i < connections; i++)
        {
            var connectionId = i;
            tasks.Add(Task.Run(async () => await RunConnectionAsync(connectionId)));
        }

        await Task.WhenAll(tasks);

        Log.Information("[{Time:HH:mm:ss}] Benchmark completed.", DateTime.Now);
    }

    private async Task RunConnectionAsync(int connectionId)
    {
        var connector = new ClientConnector();
        connector.Init(new ConnectorConfig());

        var stageId = initialStageId + connectionId; // 각 연결마다 고유 StageId

        // 연결
        var connected = await connector.ConnectAsync(serverHost, serverPort, stageId);
        if (!connected)
        {
            Log.Warning("[Connection {ConnectionId}] Failed to connect", connectionId);
            return;
        }

        // 인증
        try
        {
            using (var authPacket = ClientPacket.Empty("Authenticate"))
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

        // 메시지 수가 0이면 연결만 유지 (Stage 생성 목적)
        if (messagesPerConnection == 0)
        {
            Log.Information("[Connection {ConnectionId}] Stage {StageId} created, maintaining connection...", connectionId, stageId);
            // 무한 대기 (외부에서 종료될 때까지)
            while (true)
            {
                await Task.Delay(1000);
                connector.MainThreadAction(); // 콜백 처리
            }
        }

        // 모드에 따라 메시지 전송
        switch (mode)
        {
            case BenchmarkMode.PlayToApi:
                await RunPlayToApiMode(connector, connectionId);
                break;
            case BenchmarkMode.PlayToStage:
                await RunPlayToStageMode(connector, connectionId);
                break;
            case BenchmarkMode.StageToApi:
            case BenchmarkMode.StageToStage:
            case BenchmarkMode.ApiToApi:
                // 신규 모드는 내부 반복 방식이므로 여기서는 처리하지 않음
                // Program.cs에서 직접 호출됨
                break;
            default:
                Log.Warning("[Connection {ConnectionId}] Unknown mode: {Mode}", connectionId, mode);
                break;
        }

        connector.Disconnect();
    }

    /// <summary>
    /// 내부 반복 벤치마크 실행 (서버 측에서 반복)
    /// </summary>
    public static async Task<StartSSBenchmarkReply?> RunInternalBenchmarkAsync(
        string serverHost,
        int serverPort,
        int iterations,
        int requestSize,
        int responseSize,
        SSCallType callType,
        SSCommMode commMode,
        long initialStageId = 1000,
        long targetStageId = 2000,
        string targetNid = "play-2",
        string targetApiNid = "api-2")
    {
        var connector = new ClientConnector();
        connector.Init(new ConnectorConfig());

        // 연결
        var connected = await connector.ConnectAsync(serverHost, serverPort, initialStageId);
        if (!connected)
        {
            Log.Warning("Failed to connect to server");
            return null;
        }

        // 인증
        try
        {
            using (var authPacket = ClientPacket.Empty("Authenticate"))
            {
                var authReply = await connector.AuthenticateAsync(authPacket);
                Log.Information("Authentication succeeded. Reply: {MsgId}", authReply.MsgId);
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Authentication failed");
            connector.Disconnect();
            return null;
        }

        // 벤치마크 요청 생성
        var request = new StartSSBenchmarkRequest
        {
            Iterations = iterations,
            RequestSize = requestSize,
            ResponseSize = responseSize,
            CallType = callType,
            CommMode = commMode,
            TargetNid = targetNid,
            TargetStageId = targetStageId,
            TargetApiNid = targetApiNid
        };

        using var packet = new ClientPacket(request);

        try
        {
            // API 서버로 직접 요청하거나 Stage를 통해 요청
            IPacket response;
            if (callType == SSCallType.ApiToApi)
            {
                // API → API 테스트는 API 서버에 직접 요청
                // Note: Connector는 Stage에만 연결되므로, Stage를 통해 API 호출
                response = await connector.RequestAsync(packet);
            }
            else
            {
                // Stage → API, Stage → Stage는 Stage에 요청
                response = await connector.RequestAsync(packet);
            }

            if (response.MsgId != "StartSSBenchmarkReply")
            {
                Log.Warning("Unexpected response: {MsgId}", response.MsgId);
                connector.Disconnect();
                return null;
            }

            var reply = StartSSBenchmarkReply.Parser.ParseFrom(response.Payload.DataSpan);
            connector.Disconnect();
            return reply;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Benchmark request failed");
            connector.Disconnect();
            return null;
        }
    }

    private async Task RunPlayToApiMode(ClientConnector connector, int connectionId)
    {
        // 요청 객체 재사용
        var request = new TriggerApiRequest
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

            var e2eStopwatch = Stopwatch.StartNew();
            try
            {
                var response = await connector.RequestAsync(packet);
                e2eStopwatch.Stop();

                if (response.MsgId != "TriggerApiReply")
                {
                    Log.Warning("[Connection {ConnectionId}] Unexpected response: {MsgId}", connectionId, response.MsgId);
                }

                // 응답 파싱하여 SS Latency 추출
                var reply = TriggerApiReply.Parser.ParseFrom(response.Payload.DataSpan);
                var ssElapsedTicks = reply.SsElapsedTicks;

                metricsCollector.RecordReceived(e2eStopwatch.ElapsedTicks, ssElapsedTicks);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[Connection {ConnectionId}] Request failed at message {Sequence}", connectionId, i);
            }

            // 콜백 처리
            connector.MainThreadAction();
        }
    }

    private async Task RunPlayToStageMode(ClientConnector connector, int connectionId)
    {
        // 요청 객체 재사용
        var request = new TriggerStageRequest
        {
            ResponseSize = responseSize,
            TargetStageId = targetStageId,
            TargetNid = targetNid,
            Payload = _requestPayload
        };

        for (int i = 0; i < messagesPerConnection; i++)
        {
            // 변경되는 필드만 업데이트
            request.Sequence = i;
            request.ClientTimestamp = Stopwatch.GetTimestamp();

            using var packet = new ClientPacket(request);

            metricsCollector.RecordSent();

            var e2eStopwatch = Stopwatch.StartNew();
            try
            {
                var response = await connector.RequestAsync(packet);
                e2eStopwatch.Stop();

                if (response.MsgId != "TriggerStageReply")
                {
                    Log.Warning("[Connection {ConnectionId}] Unexpected response: {MsgId}", connectionId, response.MsgId);
                }

                // 응답 파싱하여 SS Latency 추출
                var reply = TriggerStageReply.Parser.ParseFrom(response.Payload.DataSpan);
                var ssElapsedTicks = reply.SsElapsedTicks;

                metricsCollector.RecordReceived(e2eStopwatch.ElapsedTicks, ssElapsedTicks);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[Connection {ConnectionId}] Request failed at message {Sequence}", connectionId, i);
            }

            // 콜백 처리
            connector.MainThreadAction();
        }
    }
}

public enum BenchmarkMode
{
    /// <summary>
    /// Stage → API 통신 측정 (구 방식: 클라이언트가 매번 트리거)
    /// </summary>
    PlayToApi,

    /// <summary>
    /// Stage → Stage 통신 측정 (구 방식: 클라이언트가 매번 트리거)
    /// </summary>
    PlayToStage,

    /// <summary>
    /// Stage → API 내부 반복 (신규: 서버 측에서 내부 반복)
    /// </summary>
    StageToApi,

    /// <summary>
    /// Stage → Stage 내부 반복 (신규: 서버 측에서 내부 반복)
    /// </summary>
    StageToStage,

    /// <summary>
    /// API → API 내부 반복 (신규: 서버 측에서 내부 반복)
    /// </summary>
    ApiToApi,

    /// <summary>
    /// 모든 테스트 실행 (StageToApi, StageToStage, ApiToApi - 각각 두 모드)
    /// </summary>
    All
}
