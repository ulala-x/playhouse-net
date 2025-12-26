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
        if (mode == BenchmarkMode.PlayToApi)
        {
            await RunPlayToApiMode(connector, connectionId);
        }
        else
        {
            await RunPlayToStageMode(connector, connectionId);
        }

        connector.Disconnect();
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
    /// Stage → API 통신 측정
    /// </summary>
    PlayToApi,

    /// <summary>
    /// Stage → Stage 통신 측정
    /// </summary>
    PlayToStage
}
