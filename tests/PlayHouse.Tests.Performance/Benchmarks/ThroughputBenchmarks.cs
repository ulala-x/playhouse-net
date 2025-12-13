using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Diagnosers;
using PlayHouse.Bootstrap;
using PlayHouse.Connector;
using PlayHouse.Connector.Protocol;
using PlayHouse.Tests.E2E.Infrastructure;
using PlayHouse.Tests.E2E.Proto;
using ClientConnector = PlayHouse.Connector.Connector;
using ClientPacket = PlayHouse.Connector.Protocol.Packet;

namespace PlayHouse.Tests.Performance.Benchmarks;

/// <summary>
/// 메시지 처리량(Throughput) 측정.
/// 초당 처리 가능한 메시지 수 측정.
/// </summary>
[MemoryDiagnoser]
public class ThroughputBenchmarks
{
    private PlayServer _playServer = null!;
    private ClientConnector _connector = null!;
    private Timer? _callbackTimer;

    [Params(100, 1000)]
    public int MessageCount { get; set; }

    [GlobalSetup]
    public async Task Setup()
    {
        TestActorImpl.ResetAll();
        TestStageImpl.ResetAll();
        TestSystemController.Reset();

        _playServer = new PlayServerBootstrap()
            .Configure(options =>
            {
                options.ServerId = "throughput-1";
                options.BindEndpoint = "tcp://127.0.0.1:15300";
                options.TcpPort = 15310;
                options.RequestTimeoutMs = 30000;
                options.AuthenticateMessageId = "AuthenticateRequest";
                options.DefaultStageType = "TestStage";
            })
            .UseStage<TestStageImpl>("TestStage")
            .UseActor<TestActorImpl>()
            .UseSystemController<TestSystemController>()
            .Build();

        await _playServer.StartAsync();
        await Task.Delay(1000);

        _connector = new ClientConnector();
        _connector.Init(new ConnectorConfig());
        _connector.Connect("127.0.0.1", 15310, 1000);

        // 콜백 타이머 시작
        _callbackTimer = new Timer(_ => _connector.MainThreadAction(), null, 0, 20);

        await Task.Delay(100);

        var authRequest = new AuthenticateRequest { UserId = "throughput-user", Token = "token" };
        await _connector.RequestAsync(new ClientPacket(authRequest));
    }

    [IterationSetup]
    public void IterationSetup()
    {
        // 각 반복마다 초기화하여 벤치마크 격리 보장
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
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
    /// 순차 메시지 전송 처리량
    /// </summary>
    [Benchmark(Baseline = true, Description = "Sequential send")]
    public async Task SendMessages_Sequential()
    {
        for (int i = 0; i < MessageCount; i++)
        {
            var request = new EchoRequest { Content = $"msg-{i}", Sequence = i };
            await _connector.RequestAsync(new ClientPacket(request));
        }
    }

    /// <summary>
    /// 병렬 메시지 전송 처리량
    /// </summary>
    [Benchmark(Description = "Parallel send")]
    public async Task SendMessages_Parallel()
    {
        var tasks = new Task[MessageCount];
        for (int i = 0; i < MessageCount; i++)
        {
            var seq = i;
            tasks[i] = Task.Run(async () =>
            {
                var request = new EchoRequest { Content = $"msg-{seq}", Sequence = seq };
                await _connector.RequestAsync(new ClientPacket(request));
            });
        }
        await Task.WhenAll(tasks);
    }

    /// <summary>
    /// Fire-and-forget 메시지 처리량 (응답 대기 없음)
    /// </summary>
    [Benchmark(Description = "Fire-and-forget send")]
    public void SendMessages_FireAndForget()
    {
        for (int i = 0; i < MessageCount; i++)
        {
            var request = new EchoRequest { Content = $"msg-{i}", Sequence = i };
            _connector.Send(new ClientPacket(request));
        }
    }
}
