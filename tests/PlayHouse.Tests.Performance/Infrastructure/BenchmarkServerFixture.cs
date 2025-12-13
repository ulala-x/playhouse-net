using PlayHouse.Bootstrap;
using PlayHouse.Tests.E2E.Infrastructure;

namespace PlayHouse.Tests.Performance.Infrastructure;

/// <summary>
/// 벤치마크용 서버 설정.
/// E2E 테스트의 Fixture 패턴을 재사용.
/// </summary>
public static class BenchmarkServerFixture
{
    /// <summary>
    /// 벤치마크용 단일 PlayServer 생성
    /// </summary>
    public static PlayServer CreatePlayServer(
        string serverId,
        string bindEndpoint,
        int tcpPort,
        int requestTimeoutMs = 30000)
    {
        TestActorImpl.ResetAll();
        TestStageImpl.ResetAll();
        TestSystemController.Reset();

        return new PlayServerBootstrap()
            .Configure(options =>
            {
                options.ServerId = serverId;
                options.BindEndpoint = bindEndpoint;
                options.TcpPort = tcpPort;
                options.RequestTimeoutMs = requestTimeoutMs;
                options.AuthenticateMessageId = "AuthenticateRequest";
                options.DefaultStageType = "TestStage";
            })
            .UseStage<TestStageImpl>("TestStage")
            .UseActor<TestActorImpl>()
            .UseSystemController<TestSystemController>()
            .Build();
    }

    /// <summary>
    /// 벤치마크용 듀얼 PlayServer 생성 (서버간 통신 테스트용)
    /// </summary>
    public static (PlayServer serverA, PlayServer serverB) CreateDualPlayServer()
    {
        TestActorImpl.ResetAll();
        TestStageImpl.ResetAll();
        TestSystemController.Reset();

        var serverA = new PlayServerBootstrap()
            .Configure(options =>
            {
                options.ServerId = "bench-a";
                options.BindEndpoint = "tcp://127.0.0.1:15200";
                options.TcpPort = 15210;
                options.RequestTimeoutMs = 30000;
                options.AuthenticateMessageId = "AuthenticateRequest";
                options.DefaultStageType = "TestStage";
            })
            .UseStage<TestStageImpl>("TestStage")
            .UseActor<TestActorImpl>()
            .UseSystemController<TestSystemController>()
            .Build();

        var serverB = new PlayServerBootstrap()
            .Configure(options =>
            {
                options.ServerId = "bench-b";
                options.BindEndpoint = "tcp://127.0.0.1:15201";
                options.TcpPort = 0;
                options.RequestTimeoutMs = 30000;
                options.AuthenticateMessageId = "AuthenticateRequest";
                options.DefaultStageType = "TestStage";
            })
            .UseStage<TestStageImpl>("TestStage")
            .UseActor<TestActorImpl>()
            .UseSystemController<TestSystemController>()
            .Build();

        return (serverA, serverB);
    }
}
