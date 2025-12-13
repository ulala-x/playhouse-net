#nullable enable

using PlayHouse.Bootstrap;
using Xunit;

namespace PlayHouse.Tests.Integration.Infrastructure.Fixtures;

/// <summary>
/// PlayServer 1대 + ApiServer 1대를 공유하는 Collection Fixture.
/// Stage ↔ API 통신 테스트를 위해 PlayServer와 ApiServer를 제공합니다.
/// </summary>
public class ApiPlayServerFixture : IAsyncLifetime
{
    public PlayServer? PlayServer { get; private set; }
    public ApiServer? ApiServer { get; private set; }

    public async Task InitializeAsync()
    {
        // 테스트 간 격리를 위해 Static 필드 리셋
        TestActorImpl.ResetAll();
        TestStageImpl.ResetAll();
        TestApiController.ResetAll();
        TestSystemController.Reset();

        // PlayServer (ServerId, ServiceId=1, ServerId=1)
        PlayServer = new PlayServerBootstrap()
            .Configure(options =>
            {
                options.ServerId = "1";
                options.BindEndpoint = "tcp://127.0.0.1:15100";
                options.TcpPort = 0;
                options.RequestTimeoutMs = 30000;
                options.AuthenticateMessageId = "AuthenticateRequest";
                options.DefaultStageType = "TestStage";
            })
            .UseStage<TestStageImpl>("TestStage")
            .UseActor<TestActorImpl>()
            .UseSystemController<TestSystemController>()
            .Build();

        // ApiServer (ServerId, ServiceType=Api, ServerId=1)
        ApiServer = new ApiServerBootstrap()
            .Configure(options =>
            {
                // ServiceType.Api (=2) is default, no need to set
                options.ServerId = "1";
                options.BindEndpoint = "tcp://127.0.0.1:15101";
                options.RequestTimeoutMs = 30000;
            })
            .UseController<TestApiController>()
            .UseSystemController<TestSystemController>()
            .Build();

        await PlayServer.StartAsync();
        await ApiServer.StartAsync();

        // ServerAddressResolver가 서버를 자동으로 연결할 시간을 줌
        await Task.Delay(1000);
    }

    public async Task DisposeAsync()
    {
        if (ApiServer != null)
        {
            await ApiServer.DisposeAsync();
        }
        if (PlayServer != null)
        {
            await PlayServer.DisposeAsync();
        }
    }
}
