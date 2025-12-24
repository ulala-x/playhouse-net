using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Diagnosers;
using PlayHouse.Bootstrap;
using PlayHouse.Connector;
using PlayHouse.Connector.Protocol;
using PlayHouse.Tests.Integration.Infrastructure;
using PlayHouse.Tests.Integration.Proto;
using PlayHouse.Tests.Performance.Infrastructure;
using ClientConnector = PlayHouse.Connector.Connector;
using ClientPacket = PlayHouse.Connector.Protocol.Packet;

namespace PlayHouse.Tests.Performance.Benchmarks;

/// <summary>
/// 시나리오 A: Client Connector ↔ PlayServer(Stage) Request/Reply 성능 측정.
///
/// 측정 항목:
/// - Throughput (msg/sec, bytes/sec)
/// - Latency (Mean, P95, P99)
/// - Memory (Allocated)
/// - GC (Gen0, Gen1, Gen2)
///
/// 메시지 사이즈: 1KB, 64KB, 128KB, 256KB (Response만 가변)
/// </summary>
[MemoryDiagnoser]
[ThreadingDiagnoser]
public class ClientToPlayServerBenchmarks
{
    private PlayServer _playServer = null!;
    private ClientConnector _connector = null!;
    private Timer? _callbackTimer;

    [Params(1024, 65536, 131072, 262144)]
    public int ResponseSize { get; set; }

    [GlobalSetup]
    public async Task Setup()
    {
        _playServer = BenchmarkServerFixture.CreateClientToPlayServerFixture();
        await _playServer.StartAsync();
        await Task.Delay(1000); // 서버 시작 대기

        _connector = new ClientConnector();
        _connector.Init(new ConnectorConfig { RequestTimeoutMs = 30000 });
        _connector.Connect("127.0.0.1", 16110, 1000);

        // 콜백 타이머 시작 (20ms 간격)
        _callbackTimer = new Timer(_ => _connector.MainThreadAction(), null, 0, 20);

        await Task.Delay(100);

        // 인증 (Stage 생성)
        var authRequest = new AuthenticateRequest { UserId = "bench-user", Token = "token" };
        await _connector.RequestAsync(new ClientPacket(authRequest));
    }

    [GlobalCleanup]
    public async Task Cleanup()
    {
        _callbackTimer?.Dispose();
        _callbackTimer = null;

        _connector?.Disconnect();
        if (_playServer != null)
            await _playServer.DisposeAsync();
    }

    /// <summary>
    /// 단일 Request-Reply 성능 측정 (Baseline)
    /// </summary>
    [Benchmark(Baseline = true, Description = "Single Request-Reply")]
    public async Task<IPacket> SingleRequestReply()
    {
        var request = new BenchmarkRequest
        {
            Sequence = 1,
            ResponseSize = ResponseSize
        };
        return await _connector.RequestAsync(new ClientPacket(request));
    }

    /// <summary>
    /// 순차적 100개 메시지 처리 성능
    /// </summary>
    [Benchmark(Description = "Sequential 100 Requests")]
    public async Task Sequential_100_Requests()
    {
        for (int i = 0; i < 100; i++)
        {
            var request = new BenchmarkRequest
            {
                Sequence = i,
                ResponseSize = ResponseSize
            };
            await _connector.RequestAsync(new ClientPacket(request));
        }
    }

    /// <summary>
    /// 병렬 10개 메시지 처리 성능
    /// </summary>
    [Benchmark(Description = "Parallel 10 Requests")]
    public async Task Parallel_10_Requests()
    {
        var tasks = new Task<IPacket>[10];
        for (int i = 0; i < 10; i++)
        {
            var seq = i;
            tasks[i] = Task.Run(async () =>
            {
                var request = new BenchmarkRequest
                {
                    Sequence = seq,
                    ResponseSize = ResponseSize
                };
                return await _connector.RequestAsync(new ClientPacket(request));
            });
        }
        await Task.WhenAll(tasks);
    }
}
