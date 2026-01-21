#nullable enable

using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using PlayHouse.Abstractions;
using PlayHouse.Abstractions.Api;
using PlayHouse.Core.Api;
using PlayHouse.Core.Api.Reflection;
using PlayHouse.Core.Shared;
using Xunit;

namespace PlayHouse.Unit.Core.Api.Reflection;

/// <summary>
/// 단위 테스트: ApiReflection의 핸들러 호출 기능 검증
/// </summary>
public class ApiReflectionTests
{
    #region Test Controllers

    private class TestApiController : IApiController
    {
        // Static to track across multiple instances (per-request instantiation)
        public static bool HandlerCalled { get; set; }
        public static IPacket? LastPacket { get; set; }
        public static IApiSender? LastSender { get; set; }
        public static int InstanceCount { get; set; }

        public TestApiController()
        {
            InstanceCount++;
        }

        public static void Reset()
        {
            HandlerCalled = false;
            LastPacket = null;
            LastSender = null;
            InstanceCount = 0;
        }

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

    private class ScopedDependency : IDisposable
    {
        public bool IsDisposed { get; private set; }
        public string Id { get; } = Guid.NewGuid().ToString();

        public void Dispose()
        {
            IsDisposed = true;
        }
    }

    private class ControllerWithScopedDependency : IApiController
    {
        public static ScopedDependency? LastDependency { get; set; }

        private readonly ScopedDependency _dependency;

        public ControllerWithScopedDependency(ScopedDependency dependency)
        {
            _dependency = dependency;
        }

        public void Handles(IHandlerRegister register)
        {
            register.Add("TestScoped", HandleTestScoped);
        }

        private Task HandleTestScoped(IPacket packet, IApiSender sender)
        {
            LastDependency = _dependency;
            return Task.CompletedTask;
        }
    }

    #endregion

    private IServiceProvider CreateServiceProvider()
    {
        var services = new ServiceCollection();
        services.AddTransient<IApiController, TestApiController>();
        return services.BuildServiceProvider();
    }

    [Fact(DisplayName = "생성자 - 컨트롤러에서 핸들러를 등록한다")]
    public void Constructor_RegistersHandlersFromControllers()
    {
        // Given (전제조건)
        TestApiController.Reset();
        var serviceProvider = CreateServiceProvider();

        // When (행동)
        var reflection = new ApiReflection(serviceProvider, NullLogger<ApiReflection>.Instance);

        // Then (결과)
        reflection.HandlerCount.Should().Be(2, "TestApiController에서 2개의 핸들러가 등록되어야 함");
    }

    [Fact(DisplayName = "CallMethodAsync - 등록된 핸들러를 호출한다")]
    public async Task CallMethodAsync_RegisteredHandler_CallsHandler()
    {
        // Given (전제조건)
        TestApiController.Reset();
        var serviceProvider = CreateServiceProvider();
        var reflection = new ApiReflection(serviceProvider, NullLogger<ApiReflection>.Instance);

        var packet = CPacket.Empty("TestMessage");
        var sender = Substitute.For<IApiSender>();

        // When (행동)
        await reflection.CallMethodAsync(packet, sender);

        // Then (결과)
        TestApiController.HandlerCalled.Should().BeTrue("핸들러가 호출되어야 함");
        TestApiController.LastPacket.Should().BeSameAs(packet, "같은 패킷이 전달되어야 함");
        TestApiController.LastSender.Should().BeSameAs(sender, "같은 sender가 전달되어야 함");
    }

    [Fact(DisplayName = "CallMethodAsync - 매 요청마다 새 컨트롤러 인스턴스가 생성된다")]
    public async Task CallMethodAsync_CreatesNewInstancePerRequest()
    {
        // Given (전제조건)
        TestApiController.Reset();
        var serviceProvider = CreateServiceProvider();
        var reflection = new ApiReflection(serviceProvider, NullLogger<ApiReflection>.Instance);

        var initialCount = TestApiController.InstanceCount;  // 1 (registration)
        var packet = CPacket.Empty("TestMessage");
        var sender = Substitute.For<IApiSender>();

        // When (행동)
        await reflection.CallMethodAsync(packet, sender);
        await reflection.CallMethodAsync(packet, sender);
        await reflection.CallMethodAsync(packet, sender);

        // Then (결과)
        // 1 instance for registration + 3 instances for 3 requests = 4 total
        TestApiController.InstanceCount.Should().Be(initialCount + 3,
            "매 요청마다 새 컨트롤러 인스턴스가 생성되어야 함");
    }

    [Fact(DisplayName = "CallMethodAsync - Scoped 의존성이 각 요청마다 다른 인스턴스를 받는다")]
    public async Task CallMethodAsync_ScopedDependency_DifferentPerRequest()
    {
        // Given (전제조건)
        var services = new ServiceCollection();
        services.AddTransient<IApiController, ControllerWithScopedDependency>();
        services.AddScoped<ScopedDependency>();
        var serviceProvider = services.BuildServiceProvider();

        var reflection = new ApiReflection(serviceProvider, NullLogger<ApiReflection>.Instance);
        var packet = CPacket.Empty("TestScoped");
        var sender = Substitute.For<IApiSender>();

        // When (행동)
        await reflection.CallMethodAsync(packet, sender);
        var firstDependency = ControllerWithScopedDependency.LastDependency;

        await reflection.CallMethodAsync(packet, sender);
        var secondDependency = ControllerWithScopedDependency.LastDependency;

        // Then (결과)
        firstDependency.Should().NotBeNull();
        secondDependency.Should().NotBeNull();
        firstDependency!.Id.Should().NotBe(secondDependency!.Id,
            "각 요청마다 다른 Scoped 인스턴스가 주입되어야 함");
    }

    [Fact(DisplayName = "CallMethodAsync - 등록되지 않은 핸들러는 예외를 발생한다")]
    public async Task CallMethodAsync_UnregisteredHandler_ThrowsException()
    {
        // Given (전제조건)
        TestApiController.Reset();
        var serviceProvider = CreateServiceProvider();
        var reflection = new ApiReflection(serviceProvider, NullLogger<ApiReflection>.Instance);

        var packet = CPacket.Empty("UnknownMessage");
        var sender = Substitute.For<IApiSender>();

        // When (행동)
        var action = () => reflection.CallMethodAsync(packet, sender);

        // Then (결과)
        await action.Should().ThrowAsync<PlayException>()
            .Where(e => e.ErrorCode == ErrorCode.HandlerNotFound);
    }

    [Fact(DisplayName = "HasHandler - 등록된 핸들러가 있으면 true를 반환한다")]
    public void HasHandler_RegisteredMessage_ReturnsTrue()
    {
        // Given (전제조건)
        TestApiController.Reset();
        var serviceProvider = CreateServiceProvider();
        var reflection = new ApiReflection(serviceProvider, NullLogger<ApiReflection>.Instance);

        // When (행동)
        var result = reflection.HasHandler("TestMessage");

        // Then (결과)
        result.Should().BeTrue("등록된 핸들러가 있으면 true를 반환해야 함");
    }

    [Fact(DisplayName = "HasHandler - 등록되지 않은 핸들러는 false를 반환한다")]
    public void HasHandler_UnregisteredMessage_ReturnsFalse()
    {
        // Given (전제조건)
        TestApiController.Reset();
        var serviceProvider = CreateServiceProvider();
        var reflection = new ApiReflection(serviceProvider, NullLogger<ApiReflection>.Instance);

        // When (행동)
        var result = reflection.HasHandler("NonExistentMessage");

        // Then (결과)
        result.Should().BeFalse("등록되지 않은 핸들러는 false를 반환해야 함");
    }

    [Fact(DisplayName = "GetRegisteredMessageIds - 모든 등록된 메시지 ID를 반환한다")]
    public void GetRegisteredMessageIds_ReturnsAllIds()
    {
        // Given (전제조건)
        TestApiController.Reset();
        var serviceProvider = CreateServiceProvider();
        var reflection = new ApiReflection(serviceProvider, NullLogger<ApiReflection>.Instance);

        // When (행동)
        var ids = reflection.GetRegisteredMessageIds();

        // Then (결과)
        ids.Should().Contain("TestMessage");
        ids.Should().Contain("AnotherMessage");
        ids.Should().HaveCount(2);
    }
}
