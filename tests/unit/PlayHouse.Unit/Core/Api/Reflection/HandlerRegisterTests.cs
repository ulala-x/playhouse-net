#nullable enable

using FluentAssertions;
using PlayHouse.Abstractions;
using PlayHouse.Abstractions.Api;
using PlayHouse.Core.Api.Reflection;
using Xunit;

namespace PlayHouse.Unit.Core.Api.Reflection;

/// <summary>
/// 단위 테스트: HandlerRegister의 핸들러 등록 기능 검증
/// </summary>
public class HandlerRegisterTests
{
    private readonly Dictionary<string, HandlerDescriptor> _descriptors = new();
    private readonly HandlerRegister _register;

    public HandlerRegisterTests()
    {
        _register = new HandlerRegister(_descriptors, typeof(TestController));
    }

    [Fact(DisplayName = "Add - 핸들러를 등록할 수 있다")]
    public void Add_Handler_SuccessfullyRegistered()
    {
        // Given (전제조건)
        const string msgId = "TestMessage";

        // When (행동)
        _register.Add(msgId, nameof(TestController.HandleTest));

        // Then (결과)
        _descriptors.Should().ContainKey(msgId, "등록된 핸들러가 딕셔너리에 존재해야 함");
        _descriptors[msgId].Should().NotBeNull("핸들러 디스크립터가 null이 아니어야 함");
        _descriptors[msgId].ControllerType.Should().Be(typeof(TestController));
    }

    [Fact(DisplayName = "Add<T> - 제네릭 타입으로 핸들러를 등록할 수 있다")]
    public void AddGeneric_Handler_UsesTypeName()
    {
        // Given (전제조건) & When (행동)
        _register.Add<TestMessageType>(nameof(TestController.HandleTest));

        // Then (결과)
        var expectedKey = typeof(TestMessageType).Name;
        _descriptors.Should().ContainKey(expectedKey!, "타입 Name이 키로 사용되어야 함");
    }

    [Fact(DisplayName = "Add - 중복 등록 시 예외를 발생한다")]
    public void Add_DuplicateHandler_ThrowsException()
    {
        // Given (전제조건)
        const string msgId = "TestMessage";
        _register.Add(msgId, nameof(TestController.HandleTest));

        // When (행동)
        var action = () => _register.Add(msgId, nameof(TestController.HandleTest2));

        // Then (결과)
        action.Should().Throw<InvalidOperationException>()
            .WithMessage($"*{msgId}*", "중복 등록 시 예외 메시지에 msgId가 포함되어야 함");
    }

    [Fact(DisplayName = "Add - 빈 msgId는 예외를 발생한다")]
    public void Add_EmptyMsgId_ThrowsException()
    {
        // When (행동)
        var action = () => _register.Add("", nameof(TestController.HandleTest));

        // Then (결과)
        action.Should().Throw<ArgumentException>();
    }

    [Fact(DisplayName = "Add - null 핸들러(delegate)는 예외를 발생한다")]
    public void Add_NullHandler_ThrowsException()
    {
        // Given (전제조건)
        const string msgId = "TestMessage";
        ApiHandler? nullHandler = null;

        // When (행동)
        var action = () => _register.Add(msgId, nullHandler!);

        // Then (결과)
        action.Should().Throw<ArgumentNullException>();
    }

    [Fact(DisplayName = "Add - 빈 메서드 이름은 예외를 발생한다")]
    public void Add_EmptyMethodName_ThrowsException()
    {
        // When (행동)
        var action = () => _register.Add("TestMessage", "");

        // Then (결과)
        action.Should().Throw<ArgumentException>();
    }

    [Fact(DisplayName = "Add - 존재하지 않는 메서드 이름은 예외를 발생한다")]
    public void Add_NonExistentMethodName_ThrowsException()
    {
        // When (행동)
        var action = () => _register.Add("TestMessage", "NonExistentMethod");

        // Then (결과)
        action.Should().Throw<InvalidOperationException>()
            .WithMessage("*NonExistentMethod*");
    }

    [Fact(DisplayName = "여러 핸들러 등록 - 각각 독립적으로 등록된다")]
    public void Add_MultipleHandlers_AllRegistered()
    {
        // When (행동)
        _register.Add("Msg1", nameof(TestController.HandleTest));
        _register.Add("Msg2", nameof(TestController.HandleTest2));
        _register.Add("Msg3", nameof(TestController.HandleTest3));

        // Then (결과)
        _descriptors.Should().HaveCount(3, "3개의 핸들러가 등록되어야 함");
        _descriptors.Should().ContainKey("Msg1");
        _descriptors.Should().ContainKey("Msg2");
        _descriptors.Should().ContainKey("Msg3");
    }

    [Fact(DisplayName = "Add(delegate) - 기존 delegate 방식도 동작한다")]
    public void Add_DelegateStyle_BackwardCompatible()
    {
        // Given (전제조건)
        var controller = new TestController();
        const string msgId = "TestMessage";

        // When (행동)
        _register.Add(msgId, controller.HandleTest);

        // Then (결과)
        _descriptors.Should().ContainKey(msgId);
        _descriptors[msgId].ControllerType.Should().Be(typeof(TestController));
        _descriptors[msgId].Method.Name.Should().Be(nameof(TestController.HandleTest));
    }

    // Test controller for handler registration
    private class TestController : IApiController
    {
        public void Handles(IHandlerRegister register)
        {
            register.Add("Test", nameof(HandleTest));
        }

        public Task HandleTest(IPacket packet, IApiSender sender) => Task.CompletedTask;
        public Task HandleTest2(IPacket packet, IApiSender sender) => Task.CompletedTask;
        public Task HandleTest3(IPacket packet, IApiSender sender) => Task.CompletedTask;
    }

    // Helper class for generic type testing
    private class TestMessageType { }
}
