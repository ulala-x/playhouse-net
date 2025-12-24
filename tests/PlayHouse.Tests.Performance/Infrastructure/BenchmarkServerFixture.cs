using PlayHouse.Bootstrap;
using PlayHouse.Tests.Integration.Infrastructure;

namespace PlayHouse.Tests.Performance.Infrastructure;

/// <summary>
/// 벤치마크용 서버 설정.
/// E2E 테스트의 Fixture 패턴을 재사용.
/// </summary>
public static class BenchmarkServerFixture
{
    /// <summary>
    /// 시나리오 A: Client Connector ↔ PlayServer 벤치마크용 서버
    /// </summary>
    public static PlayServer CreateClientToPlayServerFixture()
    {
        TestActorImpl.ResetAll();
        TestStageImpl.ResetAll();
        TestSystemController.Reset();

        return new PlayServerBootstrap()
            .Configure(options =>
            {
                options.ServerId = "bench-play-1";
                options.BindEndpoint = "tcp://127.0.0.1:16100";
                options.TcpPort = 16110;
                options.RequestTimeoutMs = 30000;
                options.AuthenticateMessageId = "AuthenticateRequest";
                options.DefaultStageType = "TestStage";
            })
            .UseStage<TestStageImpl>("TestStage")
            .UseActor<TestActorImpl>()
            .UseSystemController<TestSystemController>()
            .Build();
    }

    /// <summary>
    /// 시나리오 B: PlayServer ↔ ApiServer 벤치마크용 서버
    /// </summary>
    public static (PlayServer playServer, ApiServer apiServer) CreatePlayToApiFixture()
    {
        TestActorImpl.ResetAll();
        TestStageImpl.ResetAll();
        TestSystemController.Reset();
        TestApiController.ResetAll();

        var playServer = new PlayServerBootstrap()
            .Configure(options =>
            {
                options.ServerId = "bench-play-1";
                options.BindEndpoint = "tcp://127.0.0.1:16200";
                options.TcpPort = 16210;
                options.RequestTimeoutMs = 30000;
                options.AuthenticateMessageId = "AuthenticateRequest";
                options.DefaultStageType = "TestStage";
            })
            .UseStage<TestStageImpl>("TestStage")
            .UseActor<TestActorImpl>()
            .UseSystemController<TestSystemController>()
            .Build();

        var apiServer = new ApiServerBootstrap()
            .Configure(options =>
            {
                options.ServerId = "bench-api-1";
                options.BindEndpoint = "tcp://127.0.0.1:16201";
                options.RequestTimeoutMs = 30000;
            })
            .UseController<TestApiController>()
            .UseSystemController<TestSystemController>()
            .Build();

        return (playServer, apiServer);
    }
}
