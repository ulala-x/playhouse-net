#nullable enable

using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using PlayHouse.Abstractions;
using PlayHouse.Abstractions.Api;
using PlayHouse.Core.Api.Bootstrap;
using PlayHouse.Extensions;
using Xunit;

namespace PlayHouse.Unit.Extensions;

/// <summary>
/// ApiServerHostedService의 단위 테스트
/// PlayServer/ApiServer는 sealed 클래스이므로 Mock 불가
/// 실제 서버 인스턴스를 사용하는 통합 테스트
/// </summary>
public class ApiServerHostedServiceTests
{
    private class TestController : IApiController
    {
        public void Handles(IHandlerRegister register)
        {
            // Test controller - no handlers needed
        }
    }

    [Fact(DisplayName = "생성자 - ApiServer를 받아 HostedService를 생성한다")]
    public void Constructor_AcceptsApiServer()
    {
        // Given
        var services = new ServiceCollection();
        services.AddApiServer(options =>
        {
            options.ServerType = ServerType.Api;
        })
        .UseSystemController<TestSystemController>()
        .UseController<TestController>();

        var serviceProvider = services.BuildServiceProvider();
        var apiServer = serviceProvider.GetRequiredService<ApiServer>();

        // When
        var hostedService = new ApiServerHostedService(apiServer);

        // Then
        hostedService.Should().NotBeNull("HostedService가 생성되어야 함");
    }

    [Fact(DisplayName = "StartAsync와 StopAsync - 정상적으로 서버 생명주기를 관리한다")]
    public async Task StartAndStop_ManagesServerLifecycle()
    {
        // Given
        var services = new ServiceCollection();
        services.AddApiServer(options =>
        {
            options.ServerType = ServerType.Api;
        })
        .UseSystemController<TestSystemController>()
        .UseController<TestController>();

        var serviceProvider = services.BuildServiceProvider();
        var apiServer = serviceProvider.GetRequiredService<ApiServer>();
        var hostedService = new ApiServerHostedService(apiServer);

        try
        {
            // When - Start
            await hostedService.StartAsync(CancellationToken.None);

            // Then - 서버가 시작되어야 함 (예외 없음)

            // When - Stop
            await hostedService.StopAsync(CancellationToken.None);

            // Then - 정상적으로 종료되어야 함 (예외 없음)
        }
        finally
        {
            // Cleanup
            await apiServer.DisposeAsync();
        }
    }
}
