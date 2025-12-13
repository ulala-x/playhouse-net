#nullable enable

using FluentAssertions;
using PlayHouse.Abstractions;
using PlayHouse.Abstractions.Api;
using PlayHouse.Core.Api.Reflection;
using Xunit;

namespace PlayHouse.Tests.Unit.Core.Api.Reflection;

/// <summary>
/// 단위 테스트: HandlerRegister의 핸들러 등록 기능 검증
/// </summary>
public class HandlerRegisterTests
{
    private readonly Dictionary<string, ApiHandler> _handlers = new();
    private readonly HandlerRegister _register;

    public HandlerRegisterTests()
    {
        _register = new HandlerRegister(_handlers);
    }

    [Fact(DisplayName = "Add - 핸들러를 등록할 수 있다")]
    public void Add_Handler_SuccessfullyRegistered()
    {
        // Given (전제조건)
        const string msgId = "TestMessage";
        Task handler(IPacket packet, IApiSender sender) => Task.CompletedTask;

        // When (행동)
        _register.Add(msgId, handler);

        // Then (결과)
        _handlers.Should().ContainKey(msgId, "등록된 핸들러가 딕셔너리에 존재해야 함");
        _handlers[msgId].Should().NotBeNull("핸들러가 null이 아니어야 함");
    }

    [Fact(DisplayName = "Add<T> - 제네릭 타입으로 핸들러를 등록할 수 있다")]
    public void AddGeneric_Handler_UsesTypeName()
    {
        // Given (전제조건)
        Task handler(IPacket packet, IApiSender sender) => Task.CompletedTask;

        // When (행동)
        _register.Add<TestMessageType>(handler);

        // Then (결과)
        var expectedKey = typeof(TestMessageType).Name;
        _handlers.Should().ContainKey(expectedKey!, "타입 Name이 키로 사용되어야 함");
    }

    [Fact(DisplayName = "Add - 중복 등록 시 예외를 발생한다")]
    public void Add_DuplicateHandler_ThrowsException()
    {
        // Given (전제조건)
        const string msgId = "TestMessage";
        Task handler1(IPacket packet, IApiSender sender) => Task.CompletedTask;
        Task handler2(IPacket packet, IApiSender sender) => Task.CompletedTask;

        _register.Add(msgId, handler1);

        // When (행동)
        var action = () => _register.Add(msgId, handler2);

        // Then (결과)
        action.Should().Throw<InvalidOperationException>()
            .WithMessage($"*{msgId}*", "중복 등록 시 예외 메시지에 msgId가 포함되어야 함");
    }

    [Fact(DisplayName = "Add - 빈 msgId는 예외를 발생한다")]
    public void Add_EmptyMsgId_ThrowsException()
    {
        // Given (전제조건)
        Task handler(IPacket packet, IApiSender sender) => Task.CompletedTask;

        // When (행동)
        var action = () => _register.Add("", handler);

        // Then (결과)
        action.Should().Throw<ArgumentException>();
    }

    [Fact(DisplayName = "Add - null 핸들러는 예외를 발생한다")]
    public void Add_NullHandler_ThrowsException()
    {
        // Given (전제조건)
        const string msgId = "TestMessage";

        // When (행동)
        var action = () => _register.Add(msgId, null!);

        // Then (결과)
        action.Should().Throw<ArgumentNullException>();
    }

    [Fact(DisplayName = "여러 핸들러 등록 - 각각 독립적으로 등록된다")]
    public void Add_MultipleHandlers_AllRegistered()
    {
        // Given (전제조건)
        Task handler1(IPacket packet, IApiSender sender) => Task.CompletedTask;
        Task handler2(IPacket packet, IApiSender sender) => Task.CompletedTask;
        Task handler3(IPacket packet, IApiSender sender) => Task.CompletedTask;

        // When (행동)
        _register.Add("Msg1", handler1);
        _register.Add("Msg2", handler2);
        _register.Add("Msg3", handler3);

        // Then (결과)
        _handlers.Should().HaveCount(3, "3개의 핸들러가 등록되어야 함");
        _handlers.Should().ContainKey("Msg1");
        _handlers.Should().ContainKey("Msg2");
        _handlers.Should().ContainKey("Msg3");
    }

    // Helper class for generic type testing
    private class TestMessageType { }
}
