using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PlayHouse.Abstractions;
using PlayHouse.Abstractions.Play;

namespace PlayHouse.Benchmark.SS.PlayServer;

/// <summary>
/// 벤치마크용 Actor 구현 (Server-to-Server 벤치마크)
/// </summary>
public class BenchmarkActor : IActor
{
    private static long _accountIdCounter;
    private readonly ILogger<BenchmarkActor> _logger;

    public BenchmarkActor(IActorSender actorSender, ILogger<BenchmarkActor>? logger = null)
    {
        ActorSender = actorSender;
        _logger = logger ?? NullLogger<BenchmarkActor>.Instance;
    }

    public IActorSender ActorSender { get; }

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
        // 간단한 인증 처리: 순차적으로 AccountId 할당
        var accountId = Interlocked.Increment(ref _accountIdCounter);
        ActorSender.AccountId = accountId.ToString();

        // NOTE: PlayHouse 프레임워크가 자동으로 인증 응답을 처리하므로
        // 여기서 Reply를 호출하지 않습니다.

        return Task.FromResult(true);
    }

    public Task OnPostAuthenticate()
    {
        return Task.CompletedTask;
    }
}
