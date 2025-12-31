#nullable enable

using System.Collections.Concurrent;
using PlayHouse.Abstractions;
using PlayHouse.Abstractions.Play;
using PlayHouse.Core.Shared;
using PlayHouse.Tests.Integration.Proto;

namespace PlayHouse.Tests.Integration.Infrastructure;

/// <summary>
/// DI 통합 테스트용 Stage 구현.
/// IStageSender와 ITestService를 모두 DI로 주입받아 DI 통합을 검증합니다.
/// </summary>
public class DITestStage : IStage
{
    public IStageSender StageSender { get; }
    private readonly ITestService _testService;

    // Static 필드 - DI 검증용
    public static ConcurrentBag<DITestStage> Instances { get; } = new();
    public static ConcurrentBag<ITestService> InjectedServices { get; } = new();
    public static int OnCreateCallCount => _onCreateCallCount;
    private static int _onCreateCallCount;

    // 테스트 검증용 데이터
    public List<string> ReceivedMsgIds { get; } = new();
    public bool OnCreateCalled { get; private set; }
    public bool OnDestroCalled { get; private set; }
    public string? InjectedValue { get; private set; }

    public static void ResetAll()
    {
        while (Instances.TryTake(out _)) { }
        while (InjectedServices.TryTake(out _)) { }
        Interlocked.Exchange(ref _onCreateCallCount, 0);
    }

    /// <summary>
    /// DI 컨테이너가 IStageSender와 ITestService를 모두 주입합니다.
    /// </summary>
    public DITestStage(IStageSender stageSender, ITestService testService)
    {
        StageSender = stageSender;
        _testService = testService;

        // DI 주입 성공 기록
        InjectedValue = testService.GetValue();
        Instances.Add(this);
        InjectedServices.Add(testService);
    }

    public Task<(bool result, IPacket reply)> OnCreate(IPacket packet)
    {
        OnCreateCalled = true;
        Interlocked.Increment(ref _onCreateCallCount);
        return Task.FromResult<(bool, IPacket)>((true, CPacket.Empty("CreateStageReply")));
    }

    public Task OnPostCreate()
    {
        return Task.CompletedTask;
    }

    public Task OnDestroy()
    {
        OnDestroCalled = true;
        return Task.CompletedTask;
    }

    public Task<bool> OnJoinStage(IActor actor)
    {
        return Task.FromResult(true);
    }

    public Task OnPostJoinStage(IActor actor)
    {
        return Task.CompletedTask;
    }

    public ValueTask OnConnectionChanged(IActor actor, bool isConnected)
    {
        return ValueTask.CompletedTask;
    }

    public Task OnDispatch(IActor actor, IPacket packet)
    {
        ReceivedMsgIds.Add(packet.MsgId);

        switch (packet.MsgId)
        {
            case "EchoRequest":
                HandleEchoRequest(actor, packet);
                break;

            case "GetDIValueRequest":
                HandleGetDIValue(actor);
                break;

            default:
                actor.ActorSender.Reply(CPacket.Empty(packet.MsgId + "Reply"));
                break;
        }

        return Task.CompletedTask;
    }

    public Task OnDispatch(IPacket packet)
    {
        ReceivedMsgIds.Add(packet.MsgId);
        return Task.CompletedTask;
    }

    private void HandleEchoRequest(IActor actor, IPacket packet)
    {
        var echoRequest = EchoRequest.Parser.ParseFrom(packet.Payload.DataSpan);
        var echoReply = new EchoReply
        {
            Content = echoRequest.Content,
            Sequence = echoRequest.Sequence,
            ProcessedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };

        actor.ActorSender.Reply(CPacket.Of(echoReply));
    }

    private void HandleGetDIValue(IActor actor)
    {
        // DI로 주입받은 서비스의 값을 반환
        var reply = new EchoReply
        {
            Content = _testService.GetValue(),
            Sequence = 0,
            ProcessedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };

        actor.ActorSender.Reply(CPacket.Of(reply));
    }
}
