#nullable enable

using System.Collections.Concurrent;
using PlayHouse.Abstractions;
using PlayHouse.Infrastructure.Serialization;
using PlayHouse.Tests.E2E.Proto;

namespace PlayHouse.Tests.E2E.TestFixtures;

/// <summary>
/// E2E 테스트용 Chat Stage 구현.
/// 여러 Actor를 관리하고 메시지를 처리합니다.
/// </summary>
public class ChatStage : IStage
{
    private readonly ConcurrentDictionary<long, IActor> _actors = new();

    public IStageSender StageSender { get; init; } = null!;

    public Task<(ushort errorCode, IPacket? reply)> OnCreate(IPacket packet)
    {
        return Task.FromResult<(ushort, IPacket?)>((ErrorCode.Success, null));
    }

    public Task OnPostCreate()
    {
        return Task.CompletedTask;
    }

    public Task<(ushort errorCode, IPacket? reply)> OnJoinRoom(IActor actor, IPacket userInfo)
    {
        _actors[actor.ActorSender.AccountId] = actor;
        return Task.FromResult<(ushort, IPacket?)>((ErrorCode.Success, null));
    }

    public Task OnPostJoinRoom(IActor actor)
    {
        return Task.CompletedTask;
    }

    public ValueTask OnLeaveRoom(IActor actor, LeaveReason reason)
    {
        _actors.TryRemove(actor.ActorSender.AccountId, out _);
        return ValueTask.CompletedTask;
    }

    public ValueTask OnActorConnectionChanged(IActor actor, bool isConnected, DisconnectReason? reason)
    {
        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// Actor로부터 받은 메시지를 처리합니다.
    /// - AuthenticateRequest: 인증 요청 처리 및 AuthenticateReply 응답
    /// - EchoRequest: Actor에게 EchoReply 응답
    /// - ChatMessage: 모든 Actor에게 브로드캐스트 (발신자 제외)
    /// </summary>
    public async ValueTask OnDispatch(IActor sender, IPacket packet)
    {
        // AuthenticateRequest 처리
        if (packet.MsgId == AuthenticateRequest.Descriptor.Name)
        {
            var request = packet.Parse<AuthenticateRequest>();

            // TODO: roomToken 검증 로직 (현재는 단순히 성공 응답)
            var reply = new AuthenticateReply
            {
                AccountId = sender.ActorSender.AccountId,
                StageId = StageSender.StageId,
                Authenticated = true,
                ErrorMessage = string.Empty
            };

            await sender.ActorSender.SendAsync(new SimplePacket(reply));
        }
        // EchoRequest 처리
        else if (packet.MsgId == EchoRequest.Descriptor.Name)
        {
            var request = packet.Parse<EchoRequest>();
            var reply = new EchoReply
            {
                Content = request.Content,
                Sequence = request.Sequence,
                ProcessedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };
            await sender.ActorSender.SendAsync(new SimplePacket(reply));
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

    public static void Reset()
    {
        // 테스트 초기화용
    }
}
