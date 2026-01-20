#nullable enable

using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using PlayHouse.Abstractions;
using PlayHouse.Abstractions.Api;
using PlayHouse.Bootstrap;
using PlayHouse.Core.Api.Bootstrap;
using PlayHouse.Extensions;
using Xunit;

namespace PlayHouse.Tests.Unit.Extensions;

/// <summary>
/// ApiServerBuilder의 단위 테스트
/// </summary>
public class ApiServerBuilderTests
{
    private class TestController : IApiController
    {
        public void Handles(IHandlerRegister register)
        {
            // Test controller - no handlers needed for builder tests
        }
    }

    [Fact(DisplayName = "AddApiServer - ApiServer가 싱글톤으로 등록된다")]
    public void AddApiServer_RegistersApiServerAsSingleton()
    {
        // Given
        var services = new ServiceCollection();

        // When
        services.AddApiServer(options =>
        {
            options.ServiceType = ServiceType.Api;
        })
        .UseSystemController<TestSystemController>()
        .UseController<TestController>();

        var serviceProvider = services.BuildServiceProvider();

        // Then
        var apiServer1 = serviceProvider.GetRequiredService<ApiServer>();
        var apiServer2 = serviceProvider.GetRequiredService<ApiServer>();

        apiServer1.Should().NotBeNull("ApiServer가 등록되어야 함");
        apiServer1.Should().BeSameAs(apiServer2, "싱글톤으로 등록되어 같은 인스턴스여야 함");
    }

    [Fact(DisplayName = "AddApiServer - IApiServerControl 인터페이스로 해결 가능하다")]
    public void AddApiServer_RegistersIApiServerControl()
    {
        // Given
        var services = new ServiceCollection();

        // When
        services.AddApiServer(options =>
        {
            options.ServiceType = ServiceType.Api;
        })
        .UseSystemController<TestSystemController>()
        .UseController<TestController>();

        var serviceProvider = services.BuildServiceProvider();

        // Then
        var apiServerControl = serviceProvider.GetRequiredService<IApiServerControl>();
        var apiServer = serviceProvider.GetRequiredService<ApiServer>();

        apiServerControl.Should().NotBeNull("IApiServerControl이 등록되어야 함");
        apiServerControl.Should().BeSameAs(apiServer, "IApiServerControl이 ApiServer와 같은 인스턴스여야 함");
    }

    [Fact(DisplayName = "UseController - Controller가 Transient로 등록된다")]
    public void UseController_RegistersControllerAsTransient()
    {
        // Given
        var services = new ServiceCollection();

        // When
        services.AddApiServer(options =>
        {
            options.ServiceType = ServiceType.Api;
        })
        .UseController<TestController>()
        .UseSystemController<TestSystemController>();

        var serviceProvider = services.BuildServiceProvider();

        // Then
        var controller1 = serviceProvider.GetService<IApiController>();
        var controller2 = serviceProvider.GetService<IApiController>();

        controller1.Should().NotBeNull("Controller가 등록되어야 함");
        controller2.Should().NotBeNull("Controller가 등록되어야 함");
        controller1.Should().NotBeSameAs(controller2, "Transient로 등록되어 다른 인스턴스여야 함");

        controller1.Should().BeOfType<TestController>("등록한 타입이어야 함");
        controller2.Should().BeOfType<TestController>("등록한 타입이어야 함");
    }

    [Fact(DisplayName = "빌더 체인 - UseController를 여러 번 호출할 수 있다")]
    public void BuilderChain_CanCallUseControllerMultipleTimes()
    {
        // Given
        var services = new ServiceCollection();

        // When
        var builder = services.AddApiServer(options =>
        {
            options.ServiceType = ServiceType.Api;
        })
        .UseController<TestController>()
        .UseController<TestController>() // 같은 타입을 여러 번 등록 가능
        .UseSystemController<TestSystemController>();

        var serviceProvider = services.BuildServiceProvider();
        var apiServer = serviceProvider.GetRequiredService<ApiServer>();

        // Then
        builder.Should().NotBeNull("빌더가 반환되어야 함");
        apiServer.Should().NotBeNull("ApiServer가 등록되어야 함");
    }

    [Fact(DisplayName = "Services 프로퍼티 - IServiceCollection에 접근 가능하다")]
    public void Services_Property_ProvidesAccessToServiceCollection()
    {
        // Given
        var services = new ServiceCollection();

        // When
        var builder = services.AddApiServer(options =>
        {
            options.ServiceType = ServiceType.Api;
        })
        .UseSystemController<TestSystemController>();

        // Then
        builder.Services.Should().BeSameAs(services, "Services 프로퍼티는 원본 IServiceCollection을 반환해야 함");
    }
}
