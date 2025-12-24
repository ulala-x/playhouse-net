using PlayHouse.Abstractions;
using PlayHouse.Abstractions.Play;

namespace PlayHouse.Benchmark.Server;

/// <summary>
/// 벤치마크용 Actor 구현
/// </summary>
public class BenchmarkActor(IActorSender actorSender) : IActor
{
    private static long _accountIdCounter;

    public IActorSender ActorSender { get; } = actorSender;

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
        return Task.FromResult(true);
    }

    public Task OnPostAuthenticate()
    {
        return Task.CompletedTask;
    }
}
