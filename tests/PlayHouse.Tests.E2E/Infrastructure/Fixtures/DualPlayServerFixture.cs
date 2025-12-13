#nullable enable

using PlayHouse.Bootstrap;
using Xunit;

namespace PlayHouse.Tests.E2E.Infrastructure.Fixtures;

/// <summary>
/// PlayServer 2대를 공유하는 Collection Fixture.
/// Stage간 통신 테스트를 위해 두 개의 PlayServer를 제공합니다.
/// </summary>
public class DualPlayServerFixture : IAsyncLifetime
{
    public PlayServer? PlayServerA { get; private set; }
    public PlayServer? PlayServerB { get; private set; }

    public async Task InitializeAsync()
    {
        // 테스트 간 격리를 위해 Static 필드 리셋
        TestActorImpl.ResetAll();
        TestStageImpl.ResetAll();
        TestApiController.ResetAll();
        TestSystemController.Reset();

        // PlayServer A (NID="1:1", ServerId=1)
        PlayServerA = new PlayServerBootstrap()
            .Configure(options =>
            {
                options.ServerId = 1;
                options.BindEndpoint = "tcp://127.0.0.1:15200";
                options.TcpPort = 0;
                options.RequestTimeoutMs = 30000;
                options.AuthenticateMessageId = "AuthenticateRequest";
                options.DefaultStageType = "TestStage";
            })
            .UseStage<TestStageImpl>("TestStage")
            .UseActor<TestActorImpl>()
            .UseSystemController<TestSystemController>()
            .Build();

        // PlayServer B (NID="1:2", ServerId=2)
        PlayServerB = new PlayServerBootstrap()
            .Configure(options =>
            {
                options.ServerId = 2;
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

        await PlayServerA.StartAsync();
        await PlayServerB.StartAsync();

        // ServerAddressResolver가 서버를 자동으로 연결할 시간을 줌
        await Task.Delay(1000);
    }

    public async Task DisposeAsync()
    {
        if (PlayServerB != null)
        {
            await PlayServerB.DisposeAsync();
        }
        if (PlayServerA != null)
        {
            await PlayServerA.DisposeAsync();
        }
    }
}
