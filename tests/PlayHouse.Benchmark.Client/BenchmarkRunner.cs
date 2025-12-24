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
public class BenchmarkRunner
{
    private readonly string _serverHost;
    private readonly int _serverPort;
    private readonly int _connections;
    private readonly int _messagesPerConnection;
    private readonly int _requestSize;
    private readonly int _responseSize;
    private readonly BenchmarkMode _mode;
    private readonly ClientMetricsCollector _metricsCollector;

    // 재사용 버퍼
    private readonly ByteString _requestPayload;

    public BenchmarkRunner(
        string serverHost,
        int serverPort,
        int connections,
        int messagesPerConnection,
        int requestSize,
        int responseSize,
        BenchmarkMode mode,
        ClientMetricsCollector metricsCollector)
    {
        _serverHost = serverHost;
        _serverPort = serverPort;
        _connections = connections;
        _messagesPerConnection = messagesPerConnection;
        _requestSize = requestSize;
        _responseSize = responseSize;
        _mode = mode;
        _metricsCollector = metricsCollector;

        // 요청 페이로드 미리 생성 (재사용)
        _requestPayload = CreatePayload(requestSize);
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
        Log.Information("  Mode: {Mode}", _mode);
        Log.Information("  Connections: {Connections:N0}", _connections);
        Log.Information("  Messages per connection: {Messages:N0}", _messagesPerConnection);
        Log.Information("  Total messages: {Total:N0}", _connections * _messagesPerConnection);
        Log.Information("  Request size: {RequestSize:N0} bytes", _requestSize);
        Log.Information("  Response size: {ResponseSize:N0} bytes", _responseSize);

        _metricsCollector.Reset();

        var tasks = new List<Task>();

        for (int i = 0; i < _connections; i++)
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

        var stageId = 1000 + connectionId; // 각 연결마다 고유 StageId

        // 연결
        var connected = await connector.ConnectAsync(_serverHost, _serverPort, stageId);
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
                await connector.AuthenticateAsync(authPacket);
            }
        }
        catch (Exception ex)
        {
            Log.Warning("[Connection {ConnectionId}] Authentication failed: {Message}", connectionId, ex.Message);
            connector.Disconnect();
            return;
        }

        // 모드에 따라 메시지 전송
        if (_mode == BenchmarkMode.RequestAsync)
        {
            await RunRequestAsyncMode(connector, connectionId);
        }
        else
        {
            await RunSendOnReceiveMode(connector, connectionId);
        }

        connector.Disconnect();
    }

    private async Task RunRequestAsyncMode(ClientConnector connector, int connectionId)
    {
        // 요청 객체 재사용
        var request = new BenchmarkRequest
        {
            ResponseSize = _responseSize,
            Payload = _requestPayload
        };

        for (int i = 0; i < _messagesPerConnection; i++)
        {
            // 변경되는 필드만 업데이트
            request.Sequence = i;
            request.ClientTimestamp = Stopwatch.GetTimestamp();

            using var packet = new ClientPacket(request);

            _metricsCollector.RecordSent();

            var sw = Stopwatch.StartNew();
            try
            {
                var response = await connector.RequestAsync(packet);
                sw.Stop();
                _metricsCollector.RecordReceived(sw.ElapsedTicks);
            }
            catch
            {
                // 에러 발생 시 무시하고 계속
            }

            // 콜백 처리
            connector.MainThreadAction();
        }
    }

    private async Task RunSendOnReceiveMode(ClientConnector connector, int connectionId)
    {
        var receivedCount = 0;
        var timestamps = new Dictionary<int, long>();
        var tcs = new TaskCompletionSource();

        // OnReceive 콜백 설정
        connector.OnReceive += (long stageId, IPacket packet) =>
        {
            if (packet.MsgId == "BenchmarkReply")
            {
                var reply = BenchmarkReply.Parser.ParseFrom(packet.Payload.Data.Span);

                if (timestamps.TryGetValue(reply.Sequence, out var startTicks))
                {
                    var elapsed = Stopwatch.GetTimestamp() - startTicks;
                    _metricsCollector.RecordReceived(elapsed);
                }

                receivedCount++;

                if (receivedCount >= _messagesPerConnection)
                {
                    tcs.TrySetResult();
                }
            }
        };

        // 콜백 처리 타이머 시작
        using var callbackTimer = new Timer(_ => connector.MainThreadAction(), null, 0, 1);

        // 요청 객체 재사용
        var request = new BenchmarkRequest
        {
            ResponseSize = _responseSize,
            Payload = _requestPayload
        };

        // 메시지 전송
        for (int i = 0; i < _messagesPerConnection; i++)
        {
            request.Sequence = i;
            request.ClientTimestamp = Stopwatch.GetTimestamp();

            using var packet = new ClientPacket(request);

            timestamps[i] = Stopwatch.GetTimestamp();
            connector.Send(packet);
            _metricsCollector.RecordSent();
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
                connectionId, receivedCount, _messagesPerConnection);
        }
    }
}

public enum BenchmarkMode
{
    RequestAsync,
    SendOnReceive
}
