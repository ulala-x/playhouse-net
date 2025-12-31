#nullable enable

using System.Collections.Concurrent;
using PlayHouse.Abstractions;
using PlayHouse.Abstractions.Play;

namespace PlayHouse.Tests.Integration.Infrastructure;

/// <summary>
/// DI 통합 테스트용 Actor 구현.
/// IActorSender와 ITestService를 모두 DI로 주입받아 DI 통합을 검증합니다.
/// </summary>
public class DITestActor : IActor
{
    public IActorSender ActorSender { get; }
    private readonly ITestService _testService;

    private static long _accountIdCounter;

    // Static 필드 - DI 검증용
    public static ConcurrentBag<DITestActor> Instances { get; } = new();
    public static ConcurrentBag<ITestService> InjectedServices { get; } = new();
    public static int OnAuthenticateCallCount => _onAuthenticateCallCount;
    private static int _onAuthenticateCallCount;

    // 테스트 검증용 데이터
    public bool OnCreateCalled { get; private set; }
    public bool OnDestroyCalled { get; private set; }
    public bool OnAuthenticateCalled { get; private set; }
    public string? InjectedValue { get; private set; }

    public static void ResetAll()
    {
        while (Instances.TryTake(out _)) { }
        while (InjectedServices.TryTake(out _)) { }
        Interlocked.Exchange(ref _onAuthenticateCallCount, 0);
        Interlocked.Exchange(ref _accountIdCounter, 0);
    }

    /// <summary>
    /// DI 컨테이너가 IActorSender와 ITestService를 모두 주입합니다.
    /// </summary>
    public DITestActor(IActorSender actorSender, ITestService testService)
    {
        ActorSender = actorSender;
        _testService = testService;

        // DI 주입 성공 기록
        InjectedValue = testService.GetValue();
        Instances.Add(this);
        InjectedServices.Add(testService);
    }

    public Task OnCreate()
    {
        OnCreateCalled = true;
        return Task.CompletedTask;
    }

    public Task OnDestroy()
    {
        OnDestroyCalled = true;
        return Task.CompletedTask;
    }

    public Task<bool> OnAuthenticate(IPacket authPacket)
    {
        OnAuthenticateCalled = true;
        Interlocked.Increment(ref _onAuthenticateCallCount);

        // 인증 시 AccountId 설정 (필수)
        var accountId = Interlocked.Increment(ref _accountIdCounter);
        ActorSender.AccountId = accountId.ToString();

        return Task.FromResult(true);
    }

    public Task OnPostAuthenticate()
    {
        return Task.CompletedTask;
    }
}
