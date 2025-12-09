#nullable enable

using System.Collections.Concurrent;
using PlayHouse.Abstractions;

namespace PlayHouse.Tests.E2E.TestFixtures;

/// <summary>
/// E2E 테스트용 Chat Actor 구현.
/// 콜백 호출을 추적하여 테스트에서 검증할 수 있습니다.
/// </summary>
public class ChatActor : IActor
{
    // ============================================
    // Static tracking for all instances
    // ============================================
    private static readonly ConcurrentBag<ActorLifecycleEvent> _lifecycleEvents = new();
    private static readonly ConcurrentDictionary<long, ChatActor> _instances = new();

    public IActorSender ActorSender { get; set; } = null!;
    public bool IsConnected { get; private set; }

    // ============================================
    // Instance-specific tracking
    // ============================================
    public int OnCreateCallCount { get; private set; }
    public int OnAuthenticateCallCount { get; private set; }
    public int OnDestroyCallCount { get; private set; }
    public IPacket? LastAuthData { get; private set; }
    public DateTimeOffset? CreatedAt { get; private set; }
    public DateTimeOffset? AuthenticatedAt { get; private set; }
    public DateTimeOffset? DestroyedAt { get; private set; }

    // ============================================
    // IActor Implementation
    // ============================================
    public Task OnCreate()
    {
        OnCreateCallCount++;
        CreatedAt = DateTimeOffset.UtcNow;

        _lifecycleEvents.Add(new ActorLifecycleEvent
        {
            AccountId = ActorSender?.AccountId ?? 0,
            EventType = "OnCreate",
            Timestamp = CreatedAt.Value
        });

        // Register this instance
        if (ActorSender != null)
        {
            _instances[ActorSender.AccountId] = this;
        }

        return Task.CompletedTask;
    }

    public Task OnDestroy()
    {
        OnDestroyCallCount++;
        DestroyedAt = DateTimeOffset.UtcNow;

        _lifecycleEvents.Add(new ActorLifecycleEvent
        {
            AccountId = ActorSender?.AccountId ?? 0,
            EventType = "OnDestroy",
            Timestamp = DestroyedAt.Value
        });

        // Unregister this instance
        if (ActorSender != null)
        {
            _instances.TryRemove(ActorSender.AccountId, out _);
        }

        return Task.CompletedTask;
    }

    public Task OnAuthenticate(IPacket? authData)
    {
        OnAuthenticateCallCount++;
        AuthenticatedAt = DateTimeOffset.UtcNow;
        LastAuthData = authData;
        IsConnected = true;

        _lifecycleEvents.Add(new ActorLifecycleEvent
        {
            AccountId = ActorSender?.AccountId ?? 0,
            EventType = "OnAuthenticate",
            Timestamp = AuthenticatedAt.Value,
            Data = authData
        });

        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        return ValueTask.CompletedTask;
    }

    // ============================================
    // Static Query Methods for Testing
    // ============================================
    public static void Reset()
    {
        _lifecycleEvents.Clear();
        _instances.Clear();
    }

    public static IReadOnlyList<ActorLifecycleEvent> GetLifecycleEvents()
    {
        return _lifecycleEvents.ToList();
    }

    public static ChatActor? GetInstance(long accountId)
    {
        return _instances.TryGetValue(accountId, out var instance) ? instance : null;
    }

    public static int GetTotalOnCreateCalls()
    {
        return _lifecycleEvents.Count(e => e.EventType == "OnCreate");
    }

    public static int GetTotalOnAuthenticateCalls()
    {
        return _lifecycleEvents.Count(e => e.EventType == "OnAuthenticate");
    }

    public static int GetTotalOnDestroyCalls()
    {
        return _lifecycleEvents.Count(e => e.EventType == "OnDestroy");
    }

    public static async Task<bool> WaitForEventAsync(
        string eventType,
        long? accountId = null,
        TimeSpan? timeout = null)
    {
        timeout ??= TimeSpan.FromSeconds(3);
        var deadline = DateTimeOffset.UtcNow + timeout.Value;

        while (DateTimeOffset.UtcNow < deadline)
        {
            var exists = _lifecycleEvents.Any(e =>
                e.EventType == eventType &&
                (!accountId.HasValue || e.AccountId == accountId.Value));

            if (exists)
            {
                return true;
            }

            await Task.Delay(50);
        }

        return false;
    }
}

/// <summary>
/// Actor 생명주기 이벤트를 추적하는 데이터 클래스.
/// </summary>
public record ActorLifecycleEvent
{
    public long AccountId { get; init; }
    public string EventType { get; init; } = string.Empty;
    public DateTimeOffset Timestamp { get; init; }
    public IPacket? Data { get; init; }
}
