using System.Diagnostics;
using Google.Protobuf;
using PlayHouse.Benchmark.SS.Shared.Proto;
using PlayHouse.Connector;
using PlayHouse.Connector.Protocol;
using Serilog;
using ClientConnector = PlayHouse.Connector.Connector;
using ClientPacket = PlayHouse.Connector.Protocol.Packet;
using System.Net.Http;
using System.Net.Http.Json;

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
    ClientMetricsCollector metricsCollector,
    SSCommMode commMode = SSCommMode.RequestAsync,
    int serverHttpPort = 5080)
{
    // 재사용 버퍼
    private readonly ByteString _requestPayload = CreatePayload(requestSize);
    private static readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromSeconds(5) };

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

    /// <summary>
    /// API 서버를 통해 PlayServer에 Stage를 생성합니다.
    /// </summary>
    /// <param name="apiHost">API 서버 호스트</param>
    /// <param name="apiPort">API 서버 포트</param>
    /// <param name="playNid">PlayServer NID</param>
    /// <param name="stageType">Stage 타입</param>
    /// <param name="stageId">Stage ID</param>
    /// <returns>생성 성공 여부</returns>
    public static Task<bool> CreateStageViaApiAsync(
        string apiHost,
        int apiPort,
        string playNid,
        string stageType,
        long stageId)
    {
        // API 서버는 TCP 연결을 받지 않으므로, PlayServer를 경유해야 함
        // 대신 스크립트에서 curl을 통해 HTTP 엔드포인트를 사용하도록 변경
        Log.Warning("CreateStageViaApiAsync: Direct API connection not supported. Use script-based creation instead.");
        return Task.FromResult(false);
    }

    /// <summary>
    /// PlayServer에 연결된 클라이언트를 통해 API 서버에 Stage 생성 요청을 보냅니다.
    /// </summary>
    public static async Task<bool> CreateStageViaPlayServerAsync(
        string serverHost,
        int serverPort,
        long tempStageId,
        string playNid,
        string stageType,
        long targetStageId)
    {
        var connector = new ClientConnector();
        connector.Init(new ConnectorConfig());

        try
        {
            // 임시 Stage로 연결 (이미 존재해야 함)
            var connected = await connector.ConnectAsync(serverHost, serverPort, tempStageId, stageType);
            if (!connected)
            {
                Log.Error("Failed to connect to PlayServer for stage creation");
                return false;
            }

            // 인증
            using (var authPacket = ClientPacket.Empty("Authenticate"))
            {
                await connector.AuthenticateAsync(authPacket);
            }

            // CreateStageRequest 전송
            var createRequest = new CreateStageRequest
            {
                PlayNid = playNid,
                StageType = stageType,
                StageId = targetStageId
            };

            using var packet = new ClientPacket(createRequest);
            using var response = await connector.RequestAsync(packet);

            if (response.MsgId == "CreateStageReply")
            {
                var reply = CreateStageReply.Parser.ParseFrom(response.Payload.DataSpan);
                if (reply.Success)
                {
                    Log.Information("Stage {StageId} created successfully on {PlayNid}", targetStageId, playNid);
                    connector.Disconnect();
                    return true;
                }
                else
                {
                    Log.Error("Stage creation failed: {ErrorMessage}", reply.ErrorMessage);
                    connector.Disconnect();
                    return false;
                }
            }

            connector.Disconnect();
            return false;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Exception during stage creation");
            connector.Disconnect();
            return false;
        }
    }

    /// <summary>
    /// 서버 메트릭을 Reset합니다. (측정 범위 통일)
    /// </summary>
    private async Task ResetServerMetricsAsync()
    {
        try
        {
            var url = $"http://{serverHost}:{serverHttpPort}/benchmark/reset";
            var response = await _httpClient.PostAsync(url, null);
            if (response.IsSuccessStatusCode)
            {
                Log.Information("Server metrics reset successfully via {Url}", url);
            }
            else
            {
                Log.Warning("Failed to reset server metrics: {StatusCode}", response.StatusCode);
            }
        }
        catch (Exception ex)
        {
            Log.Warning("Failed to reset server metrics: {Message}", ex.Message);
        }
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

        // 서버와 클라이언트 메트릭을 동시에 Reset하여 측정 범위 통일
        await ResetServerMetricsAsync();
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
        // 각 Task마다 ImmediateSynchronizationContext 설정하여 폴링 지연 제거
        SynchronizationContext.SetSynchronizationContext(
            new ImmediateSynchronizationContext());

        var connector = new ClientConnector();
        connector.Init(new ConnectorConfig());

        var stageId = initialStageId + connectionId; // 각 연결마다 고유 StageId

        // 1. API 서버를 통해 Stage 생성 (첫 번째 연결에서만 수행, 나머지는 이미 생성됨)
        // Note: 실제로는 스크립트에서 미리 Stage를 생성하거나, 여기서 CreateStage를 호출해야 함
        // 하지만 현재 아키텍처상 클라이언트가 직접 API 서버에 CreateStage 요청을 보내기 어려우므로
        // 이 부분은 스크립트에서 처리하도록 함

        // 2. PlayServer에 연결
        var connected = await connector.ConnectAsync(serverHost, serverPort, stageId, "BenchStage");
        if (!connected)
        {
            Log.Warning("[Connection {ConnectionId}] Failed to connect to stage {StageId}. Stage may not exist.", connectionId, stageId);
            return;
        }

        // 3. 인증
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
        var connected = await connector.ConnectAsync(serverHost, serverPort, initialStageId, "BenchStage");
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
            using var response = await connector.RequestAsync(packet);

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
            Payload = _requestPayload,
            CommMode = commMode
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
                using var response = await connector.RequestAsync(packet);
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
        Log.Information("[Connection {ConnectionId}] RunPlayToStageMode - TargetNid: {TargetNid}, TargetStageId: {TargetStageId}",
            connectionId, targetNid, targetStageId);

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
                using var response = await connector.RequestAsync(packet);
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
