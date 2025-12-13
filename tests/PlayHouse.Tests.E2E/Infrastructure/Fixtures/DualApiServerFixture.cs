#nullable enable

using PlayHouse.Bootstrap;
using Xunit;

namespace PlayHouse.Tests.E2E.Infrastructure.Fixtures;

/// <summary>
/// ApiServer 2대를 공유하는 Collection Fixture.
/// API ↔ API 통신 테스트를 위해 두 개의 ApiServer를 제공합니다.
/// </summary>
public class DualApiServerFixture : IAsyncLifetime
{
    public ApiServer? ApiServerA { get; private set; }
    public ApiServer? ApiServerB { get; private set; }

    public async Task InitializeAsync()
    {
        // 테스트 간 격리를 위해 Static 필드 리셋
        TestActorImpl.ResetAll();
        TestStageImpl.ResetAll();
        TestApiController.ResetAll();
        TestSystemController.Reset();

        // ApiServer A (NID="2:1", ServiceType=Api, ServerId=1)
        ApiServerA = new ApiServerBootstrap()
            .Configure(options =>
            {
                // ServiceType.Api (=2) is default, no need to set
                options.ServerId = 1;
                options.BindEndpoint = "tcp://127.0.0.1:15300";
                options.RequestTimeoutMs = 30000;
            })
            .UseController<TestApiController>()
            .UseSystemController<TestSystemController>()
            .Build();

        // ApiServer B (NID="2:2", ServiceType=Api, ServerId=2)
        ApiServerB = new ApiServerBootstrap()
            .Configure(options =>
            {
                // ServiceType.Api (=2) is default, no need to set
                options.ServerId = 2;
                options.BindEndpoint = "tcp://127.0.0.1:15301";
                options.RequestTimeoutMs = 30000;
            })
            .UseController<TestApiController>()
            .UseSystemController<TestSystemController>()
            .Build();

        await ApiServerA.StartAsync();
        await ApiServerB.StartAsync();

        // ServerAddressResolver가 서버를 자동으로 연결할 시간을 줌
        await Task.Delay(1000);
    }

    public async Task DisposeAsync()
    {
        if (ApiServerB != null)
        {
            await ApiServerB.DisposeAsync();
        }
        if (ApiServerA != null)
        {
            await ApiServerA.DisposeAsync();
        }
    }
}
