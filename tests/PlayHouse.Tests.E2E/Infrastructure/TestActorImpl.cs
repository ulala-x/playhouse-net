#nullable enable

using System.Collections.Concurrent;
using PlayHouse.Abstractions;
using PlayHouse.Abstractions.Play;

namespace PlayHouse.Tests.E2E.Infrastructure;

/// <summary>
/// E2E 테스트용 Actor 구현.
/// 인증 처리 및 테스트용 데이터 기록을 수행합니다.
/// </summary>
public class TestActorImpl : IActor
{
    private static long _accountIdCounter;

    // Static 필드 - E2E 테스트 검증용
    public static ConcurrentBag<TestActorImpl> Instances { get; } = new();
    public static ConcurrentBag<string> AuthenticatedAccountIds { get; } = new();
    public static int OnAuthenticateCallCount => _onAuthenticateCallCount;
    private static int _onAuthenticateCallCount;

    public IActorSender ActorSender { get; }

    // 테스트 검증용 데이터
    public bool OnCreateCalled { get; private set; }
    public bool OnDestroyCalled { get; private set; }
    public bool OnAuthenticateCalled { get; private set; }
    public bool OnPostAuthenticateCalled { get; private set; }
    public IPacket? LastAuthPacket { get; private set; }

    public TestActorImpl(IActorSender actorSender)
    {
        ActorSender = actorSender;
        Instances.Add(this);
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
        LastAuthPacket = authPacket;

        // 인증 시 AccountId 설정 (필수)
        var accountId = Interlocked.Increment(ref _accountIdCounter);
        ActorSender.AccountId = accountId.ToString();

        // Static 필드 업데이트
        Interlocked.Increment(ref _onAuthenticateCallCount);
        AuthenticatedAccountIds.Add(ActorSender.AccountId);

        return Task.FromResult(true);
    }

    public Task OnPostAuthenticate()
    {
        OnPostAuthenticateCalled = true;
        return Task.CompletedTask;
    }

    /// <summary>
    /// 모든 Static 필드를 초기화합니다.
    /// 테스트 간 격리를 위해 각 테스트 시작 시 호출해야 합니다.
    /// </summary>
    public static void ResetAll()
    {
        while (Instances.TryTake(out _)) { }
        while (AuthenticatedAccountIds.TryTake(out _)) { }
        Interlocked.Exchange(ref _onAuthenticateCallCount, 0);
    }
}
