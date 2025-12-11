#nullable enable

using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using PlayHouse.Abstractions;
using PlayHouse.Abstractions.Api;
using PlayHouse.Core.Api;
using PlayHouse.Core.Api.Reflection;
using PlayHouse.Core.Shared;
using Xunit;

namespace PlayHouse.Tests.Unit.Core.Api.Reflection;

/// <summary>
/// 단위 테스트: ApiReflection의 핸들러 호출 기능 검증
/// </summary>
public class ApiReflectionTests
{
    #region Test Controllers

    private class TestApiController : IApiController
    {
        public bool HandlerCalled { get; private set; }
        public IPacket? LastPacket { get; private set; }
        public IApiSender? LastSender { get; private set; }

        public void Handles(IHandlerRegister register)
        {
            register.Add("TestMessage", HandleTestMessage);
            register.Add("AnotherMessage", HandleAnotherMessage);
        }

        private Task HandleTestMessage(IPacket packet, IApiSender sender)
        {
            HandlerCalled = true;
            LastPacket = packet;
            LastSender = sender;
            return Task.CompletedTask;
        }

        private Task HandleAnotherMessage(IPacket packet, IApiSender sender)
        {
            return Task.CompletedTask;
        }
    }

    #endregion

    private IServiceProvider CreateServiceProvider(IApiController? controller = null)
    {
        var services = new ServiceCollection();

        if (controller != null)
        {
            services.AddSingleton(controller);
        }

        return services.BuildServiceProvider();
    }

    [Fact(DisplayName = "생성자 - 컨트롤러에서 핸들러를 등록한다")]
    public void Constructor_RegistersHandlersFromControllers()
    {
        // Given (전제조건)
        var controller = new TestApiController();
        var serviceProvider = CreateServiceProvider(controller: controller);

        // When (행동)
        var reflection = new ApiReflection(serviceProvider);

        // Then (결과)
        reflection.HandlerCount.Should().Be(2, "TestApiController에서 2개의 핸들러가 등록되어야 함");
    }

    [Fact(DisplayName = "CallMethodAsync - 등록된 핸들러를 호출한다")]
    public async Task CallMethodAsync_RegisteredHandler_CallsHandler()
    {
        // Given (전제조건)
        var controller = new TestApiController();
        var serviceProvider = CreateServiceProvider(controller: controller);
        var reflection = new ApiReflection(serviceProvider);

        var packet = CPacket.Empty("TestMessage");
        var sender = Substitute.For<IApiSender>();

        // When (행동)
        await reflection.CallMethodAsync(packet, sender);

        // Then (결과)
        controller.HandlerCalled.Should().BeTrue("핸들러가 호출되어야 함");
        controller.LastPacket.Should().BeSameAs(packet, "같은 패킷이 전달되어야 함");
        controller.LastSender.Should().BeSameAs(sender, "같은 sender가 전달되어야 함");
    }

    [Fact(DisplayName = "CallMethodAsync - 등록되지 않은 핸들러는 예외를 발생한다")]
    public async Task CallMethodAsync_UnregisteredHandler_ThrowsException()
    {
        // Given (전제조건)
        var controller = new TestApiController();
        var serviceProvider = CreateServiceProvider(controller: controller);
        var reflection = new ApiReflection(serviceProvider);

        var packet = CPacket.Empty("UnknownMessage");
        var sender = Substitute.For<IApiSender>();

        // When (행동)
        var action = () => reflection.CallMethodAsync(packet, sender);

        // Then (결과)
        await action.Should().ThrowAsync<ServiceException.NotRegisterMethod>()
            .WithMessage("*UnknownMessage*");
    }

    [Fact(DisplayName = "HasHandler - 등록된 핸들러가 있으면 true를 반환한다")]
    public void HasHandler_RegisteredMessage_ReturnsTrue()
    {
        // Given (전제조건)
        var controller = new TestApiController();
        var serviceProvider = CreateServiceProvider(controller: controller);
        var reflection = new ApiReflection(serviceProvider);

        // When (행동)
        var result = reflection.HasHandler("TestMessage");

        // Then (결과)
        result.Should().BeTrue("등록된 핸들러가 있으면 true를 반환해야 함");
    }

    [Fact(DisplayName = "HasHandler - 등록되지 않은 핸들러는 false를 반환한다")]
    public void HasHandler_UnregisteredMessage_ReturnsFalse()
    {
        // Given (전제조건)
        var controller = new TestApiController();
        var serviceProvider = CreateServiceProvider(controller: controller);
        var reflection = new ApiReflection(serviceProvider);

        // When (행동)
        var result = reflection.HasHandler("NonExistentMessage");

        // Then (결과)
        result.Should().BeFalse("등록되지 않은 핸들러는 false를 반환해야 함");
    }

    [Fact(DisplayName = "GetRegisteredMessageIds - 모든 등록된 메시지 ID를 반환한다")]
    public void GetRegisteredMessageIds_ReturnsAllIds()
    {
        // Given (전제조건)
        var controller = new TestApiController();
        var serviceProvider = CreateServiceProvider(controller: controller);
        var reflection = new ApiReflection(serviceProvider);

        // When (행동)
        var ids = reflection.GetRegisteredMessageIds();

        // Then (결과)
        ids.Should().Contain("TestMessage");
        ids.Should().Contain("AnotherMessage");
        ids.Should().HaveCount(2);
    }
}
