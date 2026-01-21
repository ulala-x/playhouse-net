#nullable enable

using Microsoft.Extensions.Logging;
using PlayHouse.Abstractions;
using PlayHouse.Abstractions.Play;
using PlayHouse.Core.Shared;
using PlayHouse.E2E.Shared.Proto;

namespace PlayHouse.E2E.Shared.Infrastructure;

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
    private readonly ILogger<TestActorImpl> _logger;
    private static long _accountIdCounter;

    public IActorSender ActorSender { get; }

    public TestActorImpl(IActorSender actorSender, ILogger<TestActorImpl>? logger = null)
    {
        ActorSender = actorSender;
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<TestActorImpl>.Instance;
        _logger.LogDebug("TestActorImpl created");
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

        _logger.LogInformation("OnAuthenticate called for AccountId={AccountId}", ActorSender.AccountId);

        // PlayServer가 자동으로 AuthenticateReply를 전송하므로
        // 여기서는 AccountId만 설정하고 true를 반환하면 됨
        return Task.FromResult(true);
    }

    public Task OnPostAuthenticate()
    {
        return Task.CompletedTask;
    }
}
