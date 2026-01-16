#nullable enable

using Microsoft.Extensions.Logging;
using PlayHouse.Abstractions;
using PlayHouse.Abstractions.Play;
using PlayHouse.Core.Shared;
using PlayHouse.Verification.Shared.Proto;

namespace PlayHouse.Verification.Shared.Infrastructure;

/// <summary>
/// DI 통합 검증용 Actor 구현 (Client Response Only).
/// IActorSender와 ITestService를 모두 DI로 주입받아 DI 통합을 검증합니다.
/// </summary>
/// <remarks>
/// Client Response Only 원칙:
/// - ❌ Static collections 금지
/// - ✅ DI로 주입받은 서비스를 사용하여 인증 처리
/// </remarks>
public class DITestActor : IActor
{
    private readonly ILogger<DITestActor> _logger;
    public IActorSender ActorSender { get; }
    private readonly ITestService _testService;

    private static long _accountIdCounter;

    /// <summary>
    /// DI 컨테이너가 IActorSender, ITestService, ILogger를 모두 주입합니다.
    /// </summary>
    public DITestActor(IActorSender actorSender, ITestService testService, ILogger<DITestActor>? logger = null)
    {
        ActorSender = actorSender;
        _testService = testService;
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<DITestActor>.Instance;
        _logger.LogDebug("DITestActor created");
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
