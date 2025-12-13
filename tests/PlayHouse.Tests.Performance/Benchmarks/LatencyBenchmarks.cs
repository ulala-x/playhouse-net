using System.Diagnostics;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Diagnosers;
using BenchmarkDotNet.Order;
using PlayHouse.Bootstrap;
using PlayHouse.Connector;
using PlayHouse.Connector.Protocol;
using PlayHouse.Tests.Integration.Infrastructure;
using PlayHouse.Tests.Integration.Proto;
using ClientConnector = PlayHouse.Connector.Connector;
using ClientPacket = PlayHouse.Connector.Protocol.Packet;

namespace PlayHouse.Tests.Performance.Benchmarks;

/// <summary>
/// 서버 간 Request-Reply 지연시간(RTT) 측정.
/// 목표: < 10ms
/// </summary>
[MemoryDiagnoser]
[ThreadingDiagnoser]
[Orderer(SummaryOrderPolicy.FastestToSlowest)]
[RankColumn]
public class LatencyBenchmarks
{
    private PlayServer _playServerA = null!;
    private PlayServer _playServerB = null!;
    private ClientConnector _connectorA = null!;
    private ClientConnector _connectorB = null!;
    private Timer? _callbackTimer;
    private readonly object _callbackLock = new();

    private const long StageIdA = 11111L;
    private const long StageIdB = 22222L;

    [GlobalSetup]
    public async Task Setup()
    {
        // Static 필드 리셋
        TestActorImpl.ResetAll();
        TestStageImpl.ResetAll();
        TestSystemController.Reset();

        // PlayServer A 시작 (port 15200)
        _playServerA = new PlayServerBootstrap()
            .Configure(options =>
            {
                options.ServerId = "bench-1";
                options.BindEndpoint = "tcp://127.0.0.1:15200";
                options.TcpPort = 15210;  // 클라이언트 A 연결용
                options.RequestTimeoutMs = 30000;
                options.AuthenticateMessageId = "AuthenticateRequest";
                options.DefaultStageType = "TestStage";
            })
            .UseStage<TestStageImpl>("TestStage")
            .UseActor<TestActorImpl>()
            .UseSystemController<TestSystemController>()
            .Build();

        // PlayServer B 시작 (port 15201)
        _playServerB = new PlayServerBootstrap()
            .Configure(options =>
            {
                options.ServerId = "bench-2";
                options.BindEndpoint = "tcp://127.0.0.1:15201";
                options.TcpPort = 15220;  // 클라이언트 B 연결용 (Stage B 생성 필요)
                options.RequestTimeoutMs = 30000;
                options.AuthenticateMessageId = "AuthenticateRequest";
                options.DefaultStageType = "TestStage";
            })
            .UseStage<TestStageImpl>("TestStage")
            .UseActor<TestActorImpl>()
            .UseSystemController<TestSystemController>()
            .Build();

        await _playServerA.StartAsync();
        await _playServerB.StartAsync();

        // 서버 메시 연결 대기
        await Task.Delay(5000);

        // 클라이언트 A 연결 (PlayServer A, Stage A 생성)
        _connectorA = new ClientConnector();
        _connectorA.Init(new ConnectorConfig { RequestTimeoutMs = 30000 });
        var connectedA = await _connectorA.ConnectAsync("127.0.0.1", 15210, StageIdA);
        if (!connectedA) throw new Exception("Failed to connect to PlayServer A");

        // 클라이언트 B 연결 (PlayServer B, Stage B 생성)
        _connectorB = new ClientConnector();
        _connectorB.Init(new ConnectorConfig { RequestTimeoutMs = 30000 });
        var connectedB = await _connectorB.ConnectAsync("127.0.0.1", 15220, StageIdB);
        if (!connectedB) throw new Exception("Failed to connect to PlayServer B");

        // 콜백 타이머 시작
        _callbackTimer = new Timer(_ =>
        {
            lock (_callbackLock)
            {
                _connectorA.MainThreadAction();
                _connectorB.MainThreadAction();
            }
        }, null, 0, 20);

        await Task.Delay(100);

        // 클라이언트 A 인증 (Stage A 생성)
        var authRequestA = new AuthenticateRequest { UserId = "bench-user-A", Token = "token" };
        await _connectorA.AuthenticateAsync(new ClientPacket(authRequestA));

        // 클라이언트 B 인증 (Stage B 생성)
        var authRequestB = new AuthenticateRequest { UserId = "bench-user-B", Token = "token" };
        await _connectorB.AuthenticateAsync(new ClientPacket(authRequestB));

        await Task.Delay(100);
    }

    [GlobalCleanup]
    public async Task Cleanup()
    {
        _callbackTimer?.Dispose();
        _callbackTimer = null;

        _connectorA?.Disconnect();
        _connectorB?.Disconnect();

        if (_playServerB != null)
            await _playServerB.DisposeAsync();

        if (_playServerA != null)
            await _playServerA.DisposeAsync();
    }

    /// <summary>
    /// 클라이언트 → 서버 Request-Reply RTT 측정
    /// </summary>
    [Benchmark(Baseline = true, Description = "Client→Server RTT")]
    public async Task<IPacket> ClientToServer_RequestReply()
    {
        var request = new EchoRequest { Content = "ping", Sequence = 1 };
        return await _connectorA.RequestAsync(new ClientPacket(request));
    }

    /// <summary>
    /// 서버 간 Request-Reply RTT 측정 (Stage A → Stage B)
    /// 이 벤치마크가 핵심 측정 대상 (현재 ~500ms, 목표 <10ms)
    /// </summary>
    [Benchmark(Description = "Server→Server RTT (via client trigger)")]
    public async Task<IPacket> ServerToServer_RequestReply()
    {
        // 클라이언트가 Stage A에 요청 → Stage A가 Stage B에 요청 → 응답 반환
        var request = new TriggerRequestToStageRequest
        {
            TargetNid = "bench-2",
            TargetStageId = StageIdB,
            Query = "ping"
        };
        return await _connectorA.RequestAsync(new ClientPacket(request));
    }

    /// <summary>
    /// 연속 메시지 전송 시 지연시간 측정
    /// </summary>
    [Benchmark(Description = "Sequential 10 messages")]
    public async Task Sequential_10_Messages()
    {
        for (int i = 0; i < 10; i++)
        {
            var request = new EchoRequest { Content = "ping", Sequence = i };
            await _connectorA.RequestAsync(new ClientPacket(request));
        }
    }

    /// <summary>
    /// 게임 서버 Tick 통합 검증 시나리오.
    /// 60 FPS 게임 서버에서 16.67ms 내에 처리 가능한지 검증.
    /// </summary>
    [Benchmark(Description = "Game Tick Simulation (60 FPS)")]
    public async Task GameTick_Simulation()
    {
        // 60 FPS = 16.67ms per frame
        var tickStart = Stopwatch.GetTimestamp();

        // Stage A → Stage B 통신 (예: AI 서버에 쿼리)
        var request = new TriggerRequestToStageRequest
        {
            TargetNid = "bench-2",
            TargetStageId = StageIdB,
            Query = "game-state"
        };
        await _connectorA.RequestAsync(new ClientPacket(request));

        var tickEnd = Stopwatch.GetTimestamp();
        var elapsedMs = (tickEnd - tickStart) * 1000.0 / Stopwatch.Frequency;

        // 16.67ms 내에 완료되어야 함
        if (elapsedMs > 16.67)
        {
            Console.WriteLine($"[Warning] Tick exceeded 60 FPS budget: {elapsedMs:F2}ms");
        }
    }
}
