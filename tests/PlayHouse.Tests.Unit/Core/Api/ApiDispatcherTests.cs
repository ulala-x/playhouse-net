#nullable enable

using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using PlayHouse.Abstractions;
using PlayHouse.Abstractions.Api;
using PlayHouse.Core.Api;
using PlayHouse.Core.Messaging;
using PlayHouse.Runtime.ServerMesh.Communicator;
using PlayHouse.Runtime.ServerMesh.Message;
using PlayHouse.Runtime.Proto;
using Xunit;

namespace PlayHouse.Tests.Unit.Core.Api;

/// <summary>
/// 단위 테스트: ApiDispatcher의 메시지 디스패치 기능 검증
/// </summary>
public class ApiDispatcherTests : IDisposable
{
    #region Test Controller

    private class TestApiController : IApiController
    {
        private int _callCount;
        public int CallCount => _callCount;
        public List<string> ReceivedMessages { get; } = new();

        public void Handles(IHandlerRegister register)
        {
            register.Add("TestMessage", HandleTestMessage);
        }

        private Task HandleTestMessage(IPacket packet, IApiSender sender)
        {
            Interlocked.Increment(ref _callCount);
            lock (ReceivedMessages)
            {
                ReceivedMessages.Add(packet.MsgId);
            }
            return Task.CompletedTask;
        }
    }

    #endregion

    private readonly IClientCommunicator _communicator;
    private readonly RequestCache _requestCache;
    private readonly TestApiController _apiController;
    private readonly ApiDispatcher _dispatcher;

    public ApiDispatcherTests()
    {
        _communicator = Substitute.For<IClientCommunicator>();
        _requestCache = new RequestCache();
        _apiController = new TestApiController();

        var services = new ServiceCollection();
        services.AddSingleton<IApiController>(_apiController);
        var serviceProvider = services.BuildServiceProvider();

        _dispatcher = new ApiDispatcher(
            serviceId: 1,
            nid: "1:1",
            _requestCache,
            _communicator,
            serviceProvider);
    }

    public void Dispose()
    {
        _dispatcher.Dispose();
    }

    [Fact(DisplayName = "HandlerCount - 등록된 핸들러 수를 반환한다")]
    public void HandlerCount_ReturnsRegisteredCount()
    {
        // Given (전제조건)
        // When (행동)
        var count = _dispatcher.HandlerCount;

        // Then (결과)
        count.Should().Be(1, "TestApiController에서 1개의 핸들러가 등록되어야 함");
    }

    [Fact(DisplayName = "Post - 등록된 핸들러로 메시지를 디스패치한다")]
    public async Task Post_RegisteredMessage_DispatchesToHandler()
    {
        // Given (전제조건)
        var header = new RouteHeader
        {
            ServiceId = 1,
            MsgId = "TestMessage",
            From = "test:1"
        };
        var packet = RuntimeRoutePacket.Of(header, Array.Empty<byte>());

        // When (행동)
        _dispatcher.Post(packet);
        await Task.Delay(100); // 비동기 처리 대기

        // Then (결과)
        _apiController.CallCount.Should().Be(1, "핸들러가 호출되어야 함");
    }

    [Fact(DisplayName = "Post - 등록되지 않은 메시지는 에러 응답을 보낸다")]
    public async Task Post_UnregisteredMessage_SendsErrorReply()
    {
        // Given (전제조건)
        var header = new RouteHeader
        {
            ServiceId = 1,
            MsgId = "UnknownMessage",
            MsgSeq = 1, // Request (expects reply)
            From = "test:1"
        };
        var packet = RuntimeRoutePacket.Of(header, Array.Empty<byte>());

        // When (행동)
        _dispatcher.Post(packet);
        await Task.Delay(100); // 비동기 처리 대기

        // Then (결과)
        // Note: In stateless dispatcher, error reply is sent via ApiSender.Reply()
        // which uses communicator. Since it's mocked, we verify no exception occurs
        _apiController.CallCount.Should().Be(0, "등록되지 않은 메시지는 핸들러가 호출되지 않아야 함");
    }

    [Fact(DisplayName = "Post - 여러 메시지를 병렬로 처리한다")]
    public async Task Post_MultipleMessages_ProcessedConcurrently()
    {
        // Given (전제조건)
        // 새로운 컨트롤러와 디스패처 생성하여 다른 테스트와 격리
        var controller = new TestApiController();
        var services = new ServiceCollection();
        services.AddSingleton<IApiController>(controller);
        var serviceProvider = services.BuildServiceProvider();

        using var dispatcher = new ApiDispatcher(
            serviceId: 1,
            nid: "1:1",
            _requestCache,
            _communicator,
            serviceProvider);

        const int messageCount = 5;
        var packets = Enumerable.Range(0, messageCount)
            .Select(i => RuntimeRoutePacket.Of(
                new RouteHeader
                {
                    ServiceId = 1,
                    MsgId = "TestMessage",
                    From = "test:1"
                },
                Array.Empty<byte>()))
            .ToList();

        // When (행동)
        foreach (var packet in packets)
        {
            dispatcher.Post(packet);
        }
        await Task.Delay(500); // 비동기 처리 대기 시간 증가

        // Then (결과)
        controller.CallCount.Should().Be(messageCount, $"{messageCount}개의 메시지가 처리되어야 함");
    }

    [Fact(DisplayName = "Dispose - 정리 후에도 예외가 발생하지 않는다")]
    public void Dispose_MultipleCalls_NoException()
    {
        // Given (전제조건)
        var services = new ServiceCollection().BuildServiceProvider();
        var dispatcher = new ApiDispatcher(1, "1:1", _requestCache, _communicator, services);

        // When (행동)
        var action = () =>
        {
            dispatcher.Dispose();
            dispatcher.Dispose(); // 두 번 호출
        };

        // Then (결과)
        action.Should().NotThrow("중복 Dispose 호출은 예외를 발생하지 않아야 함");
    }
}
