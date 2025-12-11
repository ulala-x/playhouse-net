#nullable enable

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

        return Task.FromResult(true);
    }

    public Task OnPostAuthenticate()
    {
        OnPostAuthenticateCalled = true;
        return Task.CompletedTask;
    }
}
