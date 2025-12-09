#nullable enable

using System.Collections.Concurrent;
using PlayHouse.Abstractions;
using PlayHouse.Infrastructure.Serialization;
using PlayHouse.Tests.E2E.Proto;

namespace PlayHouse.Tests.E2E.TestFixtures;

/// <summary>
/// E2E 테스트용 Chat Stage 구현.
/// 여러 Actor를 관리하고 메시지를 처리합니다.
/// 메시지 라우팅과 Stage 생명주기를 추적할 수 있습니다.
/// </summary>
public class ChatStage : IStage
{
    private readonly ConcurrentDictionary<long, IActor> _actors = new();

    // ============================================
    // Static tracking for all stage instances
    // ============================================
    private static readonly ConcurrentBag<StageLifecycleEvent> _lifecycleEvents = new();
    private static readonly ConcurrentBag<MessageRoutingEvent> _messageRoutingEvents = new();

    public IStageSender StageSender { get; init; } = null!;

    // ============================================
    // Instance-specific tracking
    // ============================================
    public int OnCreateCallCount { get; private set; }
    public int OnPostCreateCallCount { get; private set; }
    public int OnJoinRoomCallCount { get; private set; }
    public int OnDispatchCallCount { get; private set; }
    public DateTimeOffset? CreatedAt { get; private set; }

    public Task<(ushort errorCode, IPacket? reply)> OnCreate(IPacket packet)
    {
        OnCreateCallCount++;
        CreatedAt = DateTimeOffset.UtcNow;

        _lifecycleEvents.Add(new StageLifecycleEvent
        {
            StageId = StageSender?.StageId ?? 0,
            EventType = "OnCreate",
            Timestamp = CreatedAt.Value
        });

        return Task.FromResult<(ushort, IPacket?)>((ErrorCode.Success, null));
    }

    public Task OnPostCreate()
    {
        OnPostCreateCallCount++;

        _lifecycleEvents.Add(new StageLifecycleEvent
        {
            StageId = StageSender?.StageId ?? 0,
            EventType = "OnPostCreate",
            Timestamp = DateTimeOffset.UtcNow
        });

        return Task.CompletedTask;
    }

    public Task<(ushort errorCode, IPacket? reply)> OnJoinRoom(IActor actor, IPacket userInfo)
    {
        OnJoinRoomCallCount++;
        _actors[actor.ActorSender.AccountId] = actor;

        _lifecycleEvents.Add(new StageLifecycleEvent
        {
            StageId = StageSender?.StageId ?? 0,
            EventType = "OnJoinRoom",
            Timestamp = DateTimeOffset.UtcNow,
            ActorId = actor.ActorSender.AccountId
        });

        return Task.FromResult<(ushort, IPacket?)>((ErrorCode.Success, null));
    }

    public Task OnPostJoinRoom(IActor actor)
    {
        _lifecycleEvents.Add(new StageLifecycleEvent
        {
            StageId = StageSender?.StageId ?? 0,
            EventType = "OnPostJoinRoom",
            Timestamp = DateTimeOffset.UtcNow,
            ActorId = actor.ActorSender.AccountId
        });

        return Task.CompletedTask;
    }

    public ValueTask OnLeaveRoom(IActor actor, LeaveReason reason)
    {
        _actors.TryRemove(actor.ActorSender.AccountId, out _);

        _lifecycleEvents.Add(new StageLifecycleEvent
        {
            StageId = StageSender?.StageId ?? 0,
            EventType = "OnLeaveRoom",
            Timestamp = DateTimeOffset.UtcNow,
            ActorId = actor.ActorSender.AccountId
        });

        return ValueTask.CompletedTask;
    }

    public ValueTask OnActorConnectionChanged(IActor actor, bool isConnected, DisconnectReason? reason)
    {
        _lifecycleEvents.Add(new StageLifecycleEvent
        {
            StageId = StageSender?.StageId ?? 0,
            EventType = "OnActorConnectionChanged",
            Timestamp = DateTimeOffset.UtcNow,
            ActorId = actor.ActorSender.AccountId
        });

        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// Actor로부터 받은 메시지를 처리합니다.
    /// - AuthenticateRequest: 인증 요청 처리 및 AuthenticateReply 응답
    /// - EchoRequest: Actor에게 EchoReply 응답 (Request-Response인 경우 Reply 사용)
    /// - ChatMessage: 모든 Actor에게 브로드캐스트 (발신자 제외)
    /// </summary>
    public async ValueTask OnDispatch(IActor sender, IPacket packet)
    {
        OnDispatchCallCount++;

        _messageRoutingEvents.Add(new MessageRoutingEvent
        {
            StageId = StageSender?.StageId ?? 0,
            ActorId = sender.ActorSender.AccountId,
            MessageId = packet.MsgId,
            Timestamp = DateTimeOffset.UtcNow
        });

        // EchoRequest 처리
        if (packet.MsgId == EchoRequest.Descriptor.Name)
        {
            var request = packet.Parse<EchoRequest>();
            var reply = new EchoReply
            {
                Content = request.Content,
                Sequence = request.Sequence,
                ProcessedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };

            // MsgSeq > 0이면 Request-Response 패턴이므로 Reply 사용
            if (packet.MsgSeq > 0)
            {
                sender.ActorSender.Reply(new SimplePacket(reply));
            }
            else
            {
                // Push 메시지 패턴
                await sender.ActorSender.SendAsync(new SimplePacket(reply));
            }
        }
        // ChatMessage 브로드캐스트
        else if (packet.MsgId == ChatMessage.Descriptor.Name)
        {
            var chatMsg = packet.Parse<ChatMessage>();

            // 모든 연결된 Actor에게 브로드캐스트 (자신 제외)
            foreach (var actor in _actors.Values)
            {
                if (actor.ActorSender.AccountId != sender.ActorSender.AccountId)
                {
                    await actor.ActorSender.SendAsync(new SimplePacket(chatMsg));
                }
            }
        }
    }

    public ValueTask DisposeAsync()
    {
        _actors.Clear();
        return ValueTask.CompletedTask;
    }

    // ============================================
    // Static Query Methods for Testing
    // ============================================
    public static void Reset()
    {
        _lifecycleEvents.Clear();
        _messageRoutingEvents.Clear();
    }

    public static IReadOnlyList<StageLifecycleEvent> GetLifecycleEvents()
    {
        return _lifecycleEvents.ToList();
    }

    public static IReadOnlyList<MessageRoutingEvent> GetMessageRoutingEvents()
    {
        return _messageRoutingEvents.ToList();
    }

    public static int GetTotalOnCreateCalls()
    {
        return _lifecycleEvents.Count(e => e.EventType == "OnCreate");
    }

    public static int GetTotalOnJoinRoomCalls()
    {
        return _lifecycleEvents.Count(e => e.EventType == "OnJoinRoom");
    }

    public static int GetTotalMessageRoutingCount(string? messageId = null)
    {
        if (messageId == null)
        {
            return _messageRoutingEvents.Count;
        }

        return _messageRoutingEvents.Count(e => e.MessageId == messageId);
    }

    public static async Task<bool> WaitForMessageRoutingAsync(
        string messageId,
        int expectedCount = 1,
        TimeSpan? timeout = null)
    {
        timeout ??= TimeSpan.FromSeconds(3);
        var deadline = DateTimeOffset.UtcNow + timeout.Value;

        while (DateTimeOffset.UtcNow < deadline)
        {
            var count = _messageRoutingEvents.Count(e => e.MessageId == messageId);
            if (count >= expectedCount)
            {
                return true;
            }

            await Task.Delay(50);
        }

        return false;
    }
}

/// <summary>
/// Stage 생명주기 이벤트를 추적하는 데이터 클래스.
/// </summary>
public record StageLifecycleEvent
{
    public long StageId { get; init; }
    public string EventType { get; init; } = string.Empty;
    public DateTimeOffset Timestamp { get; init; }
    public long? ActorId { get; init; }
}

/// <summary>
/// 메시지 라우팅 이벤트를 추적하는 데이터 클래스.
/// </summary>
public record MessageRoutingEvent
{
    public long StageId { get; init; }
    public long ActorId { get; init; }
    public string MessageId { get; init; } = string.Empty;
    public DateTimeOffset Timestamp { get; init; }
}
