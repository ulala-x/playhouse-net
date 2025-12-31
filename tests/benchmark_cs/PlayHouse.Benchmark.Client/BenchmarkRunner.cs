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
    int stageIdOffset = 0)
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
        Log.Information("  Messages per connection: {Messages:N0}", messagesPerConnection);
        Log.Information("  Total messages: {Total:N0}", connections * messagesPerConnection);
        Log.Information("  Request size: {RequestSize:N0} bytes", requestSize);
        Log.Information("  Response size: {ResponseSize:N0} bytes", responseSize);

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

        var stageId = 1000 + stageIdOffset + connectionId; // 각 연결마다 고유 StageId

        // 연결
        var connected = await connector.ConnectAsync(serverHost, serverPort, stageId, "BenchStage");
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
        else
        {
            await RunRequestCallbackMode(connector, connectionId);
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
                var response = await connector.RequestAsync(packet);
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

        // 동시 요청 수 제한 (backpressure)
        const int maxConcurrentRequests = 100;
        var semaphore = new SemaphoreSlim(maxConcurrentRequests);

        // 콜백 처리 타이머 시작
        using var callbackTimer = new Timer(_ => connector.MainThreadAction(), null, 0, 1);

        // 요청 객체 재사용
        var request = new BenchmarkRequest
        {
            ResponseSize = responseSize,
            Payload = _requestPayload
        };

        // 메시지 전송 (Request with callback)
        for (int i = 0; i < messagesPerConnection; i++)
        {
            // 타임아웃 포함 대기 (에러 발생 시 semaphore가 릴리즈되지 않을 수 있음)
            if (!await semaphore.WaitAsync(TimeSpan.FromSeconds(5)))
            {
                Log.Warning("[Connection {ConnectionId}] Semaphore wait timeout at message {Seq}", connectionId, i);
                continue;
            }

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
        }

        // 모든 응답 수신 대기 (최대 30초)
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        cts.Token.Register(() => tcs.TrySetCanceled());

        try
        {
            await tcs.Task;
        }
        catch (TaskCanceledException)
        {
            Log.Warning("[Connection {ConnectionId}] Timeout waiting for responses (received: {Received}/{Total})",
                connectionId, receivedCount, messagesPerConnection);
        }
    }
}

public enum BenchmarkMode
{
    RequestAsync,
    RequestCallback
}
