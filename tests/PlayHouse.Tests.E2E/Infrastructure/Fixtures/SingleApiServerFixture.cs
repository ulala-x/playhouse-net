#nullable enable

using PlayHouse.Bootstrap;
using Xunit;

namespace PlayHouse.Tests.E2E.Infrastructure.Fixtures;

/// <summary>
/// ApiServer 1대를 공유하는 Collection Fixture.
/// API 단독 테스트를 위해 ApiServer를 제공합니다.
/// </summary>
public class SingleApiServerFixture : IAsyncLifetime
{
    public ApiServer? ApiServer { get; private set; }

    public async Task InitializeAsync()
    {
        // 테스트 간 격리를 위해 Static 필드 리셋
        TestActorImpl.ResetAll();
        TestStageImpl.ResetAll();
        TestApiController.ResetAll();
        TestSystemController.Reset();

        // ApiServer (ServerId, ServiceType=Api, ServerId=1)
        ApiServer = new ApiServerBootstrap()
            .Configure(options =>
            {
                // ServiceType.Api (=2) is default, no need to set
                options.ServerId = "1";
                options.BindEndpoint = "tcp://127.0.0.1:0";
                options.RequestTimeoutMs = 30000;
            })
            .UseController<TestApiController>()
            .UseSystemController<TestSystemController>()
            .Build();

        await ApiServer.StartAsync();
    }

    public async Task DisposeAsync()
    {
        if (ApiServer != null)
        {
            await ApiServer.DisposeAsync();
        }
    }
}
