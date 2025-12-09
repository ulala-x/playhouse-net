#nullable enable

using PlayHouse.Abstractions;

namespace PlayHouse.Tests.E2E.TestFixtures;

/// <summary>
/// E2E 테스트용 Chat Actor 구현.
/// </summary>
public class ChatActor : IActor
{
    public IActorSender ActorSender { get; set; } = null!;
    public bool IsConnected { get; private set; }

    public Task OnCreate()
    {
        return Task.CompletedTask;
    }

    public Task OnDestroy()
    {
        return Task.CompletedTask;
    }

    public Task OnAuthenticate(IPacket? authData)
    {
        IsConnected = true;
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        return ValueTask.CompletedTask;
    }

    public static void Reset()
    {
        // 테스트 초기화용
    }
}
