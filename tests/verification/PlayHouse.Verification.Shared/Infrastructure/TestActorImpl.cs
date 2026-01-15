#nullable enable

using PlayHouse.Abstractions;
using PlayHouse.Abstractions.Play;
using PlayHouse.Core.Shared;
using PlayHouse.Verification.Shared.Proto;

namespace PlayHouse.Verification.Shared.Infrastructure;

/// <summary>
/// E2E 검증용 Actor 구현 (Client Response Only).
/// 상태 기록 없이 순수하게 인증 및 응답만 처리.
/// </summary>
/// <remarks>
/// Client Response Only 원칙:
/// - ❌ Static collections, instance tracking 금지
/// - ❌ OnCreateCalled, OnAuthenticateCalled 같은 상태 기록 금지
/// - ✅ OnAuthenticate에서 AccountId 생성 및 AuthenticateReply 반환만 수행
/// </remarks>
public class TestActorImpl : IActor
{
    private static long _accountIdCounter;

    public IActorSender ActorSender { get; }

    public TestActorImpl(IActorSender actorSender)
    {
        ActorSender = actorSender;
    }

    public Task OnCreate()
    {
        return Task.CompletedTask;
    }

    public Task OnDestroy()
    {
        return Task.CompletedTask;
    }

    public Task<bool> OnAuthenticate(IPacket authPacket)
    {
        // 인증 시 AccountId 설정 (필수)
        var accountId = Interlocked.Increment(ref _accountIdCounter);
        ActorSender.AccountId = accountId.ToString();

        // PlayServer가 자동으로 AuthenticateReply를 전송하므로
        // 여기서는 AccountId만 설정하고 true를 반환하면 됨
        return Task.FromResult(true);
    }

    public Task OnPostAuthenticate()
    {
        return Task.CompletedTask;
    }
}
