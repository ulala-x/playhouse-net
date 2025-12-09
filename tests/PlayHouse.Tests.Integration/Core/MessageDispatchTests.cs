#nullable enable

using PlayHouse.Tests.Integration.TestHelpers;
using FluentAssertions;
using PlayHouse.Infrastructure.Serialization;
using Google.Protobuf.WellKnownTypes;
using Xunit;

namespace PlayHouse.Tests.Integration.Core;

/// <summary>
/// 통합 테스트: 메시지 디스패치 및 송수신 검증
/// Request-Reply, Fire-and-Forget, Broadcast 등 메시지 패턴을 검증합니다.
/// </summary>
public class MessageDispatchTests
{
    #region 1. 기본 동작 (Basic Operations)

    [Fact(DisplayName = "OnDispatch 호출 시 메시지가 Stage에 전달됨")]
    public async Task OnDispatch_DeliverMessageToStage()
    {
        // Given (전제조건)
        var fakeStage = new FakeStage();
        var fakeActor = new FakeActor
        {
            ActorSender = new FakeActorSender { AccountId = 1001, SessionId = 2001 }
        };
        var message = new SimplePacket(new Empty()) { MsgSeq = 1 };

        // When (행동)
        await fakeStage.OnDispatch(fakeActor, message);

        // Then (결과)
        fakeStage.ReceivedMessages.Should().ContainSingle("메시지가 전달되어야 함");
        fakeStage.ReceivedMessages[0].Actor.Should().BeSameAs(fakeActor);
        fakeStage.ReceivedMessages[0].Packet.Should().BeSameAs(message);
    }

    [Fact(DisplayName = "여러 메시지를 순차적으로 디스패치 가능")]
    public async Task OnDispatch_MultipleMessages_Sequential()
    {
        // Given (전제조건)
        var fakeStage = new FakeStage();
        var fakeActor = new FakeActor
        {
            ActorSender = new FakeActorSender { AccountId = 1001, SessionId = 2001 }
        };

        // When (행동)
        var message1 = new SimplePacket(new Empty()) { MsgSeq = 1 };
        var message2 = new SimplePacket(new Empty()) { MsgSeq = 2 };
        var message3 = new SimplePacket(new Empty()) { MsgSeq = 3 };

        await fakeStage.OnDispatch(fakeActor, message1);
        await fakeStage.OnDispatch(fakeActor, message2);
        await fakeStage.OnDispatch(fakeActor, message3);

        // Then (결과)
        fakeStage.ReceivedMessages.Should().HaveCount(3, "3개 메시지가 전달되어야 함");
        fakeStage.ReceivedMessages[0].Packet.MsgSeq.Should().Be(1);
        fakeStage.ReceivedMessages[1].Packet.MsgSeq.Should().Be(2);
        fakeStage.ReceivedMessages[2].Packet.MsgSeq.Should().Be(3);
    }

    [Fact(DisplayName = "ActorSender를 통해 메시지 전송")]
    public async Task ActorSender_SendMessage_RecordsPacket()
    {
        // Given (전제조건)
        var actorSender = new FakeActorSender { AccountId = 1001, SessionId = 2001 };
        var message = new SimplePacket(new Empty());

        // When (행동)
        await actorSender.SendAsync(message);

        // Then (결과)
        actorSender.SentPackets.Should().ContainSingle();
        actorSender.SentPackets[0].Should().BeSameAs(message);
    }

    [Fact(DisplayName = "ActorSender를 통해 Reply 전송")]
    public void ActorSender_Reply_RecordsResponse()
    {
        // Given (전제조건)
        var actorSender = new FakeActorSender { AccountId = 1001, SessionId = 2001 };
        var replyMessage = new SimplePacket(new Empty());

        // When (행동)
        actorSender.Reply(replyMessage);

        // Then (결과)
        actorSender.Replies.Should().ContainSingle();
        actorSender.Replies[0].ErrorCode.Should().Be(0);
        actorSender.Replies[0].Reply.Should().BeSameAs(replyMessage);
    }

    #endregion

    #region 2. 응답 데이터 검증 (Response Validation)

    [Fact(DisplayName = "Request 메시지는 MsgSeq가 0보다 큼")]
    public void RequestMessage_HasNonZeroMsgSeq()
    {
        // Given & When (전제조건 및 행동)
        var requestMessage = new SimplePacket(new Empty()) { MsgSeq = 1 };

        // Then (결과)
        requestMessage.IsRequest.Should().BeTrue("MsgSeq > 0이면 Request임");
        requestMessage.MsgSeq.Should().BeGreaterThan((ushort)0);
    }

    [Fact(DisplayName = "Fire-and-Forget 메시지는 MsgSeq가 0")]
    public void FireAndForgetMessage_HasZeroMsgSeq()
    {
        // Given & When (전제조건 및 행동)
        var fireAndForgetMessage = new SimplePacket(new Empty()) { MsgSeq = 0 };

        // Then (결과)
        fireAndForgetMessage.IsRequest.Should().BeFalse("MsgSeq = 0이면 Fire-and-Forget임");
        fireAndForgetMessage.MsgSeq.Should().Be((ushort)0);
    }

    [Fact(DisplayName = "Reply 시 에러 코드 설정 가능")]
    public void Reply_CanSetErrorCode()
    {
        // Given (전제조건)
        var actorSender = new FakeActorSender { AccountId = 1001, SessionId = 2001 };
        ushort errorCode = 404; // Not Found

        // When (행동)
        actorSender.Reply(errorCode);

        // Then (결과)
        actorSender.Replies.Should().ContainSingle();
        actorSender.Replies[0].ErrorCode.Should().Be(404);
        actorSender.Replies[0].Reply.Should().BeNull();
    }

    #endregion

    #region 3. 입력 파라미터 검증 (Input Validation)

    [Fact(DisplayName = "여러 Actor로부터 메시지 수신 가능")]
    public async Task OnDispatch_FromMultipleActors_AllReceived()
    {
        // Given (전제조건)
        var fakeStage = new FakeStage();
        var actor1 = new FakeActor { ActorSender = new FakeActorSender { AccountId = 1001, SessionId = 2001 } };
        var actor2 = new FakeActor { ActorSender = new FakeActorSender { AccountId = 1002, SessionId = 2002 } };
        var actor3 = new FakeActor { ActorSender = new FakeActorSender { AccountId = 1003, SessionId = 2003 } };

        // When (행동)
        await fakeStage.OnDispatch(actor1, new SimplePacket(new Empty()));
        await fakeStage.OnDispatch(actor2, new SimplePacket(new Empty()));
        await fakeStage.OnDispatch(actor3, new SimplePacket(new Empty()));

        // Then (결과)
        fakeStage.ReceivedMessages.Should().HaveCount(3);
        fakeStage.ReceivedMessages[0].Actor.ActorSender.AccountId.Should().Be(1001);
        fakeStage.ReceivedMessages[1].Actor.ActorSender.AccountId.Should().Be(1002);
        fakeStage.ReceivedMessages[2].Actor.ActorSender.AccountId.Should().Be(1003);
    }

    [Fact(DisplayName = "동일한 Actor가 여러 메시지 전송 가능")]
    public async Task OnDispatch_SameActorMultipleMessages_AllReceived()
    {
        // Given (전제조건)
        var fakeStage = new FakeStage();
        var fakeActor = new FakeActor
        {
            ActorSender = new FakeActorSender { AccountId = 1001, SessionId = 2001 }
        };

        // When (행동)
        for (int i = 1; i <= 10; i++)
        {
            var message = new SimplePacket(new Empty()) { MsgSeq = (ushort)i };
            await fakeStage.OnDispatch(fakeActor, message);
        }

        // Then (결과)
        fakeStage.ReceivedMessages.Should().HaveCount(10);
        fakeStage.ReceivedMessages.Should().AllSatisfy(m =>
        {
            m.Actor.Should().BeSameAs(fakeActor);
        });
    }

    #endregion

    #region 4. 엣지 케이스 (Edge Cases)

    [Fact(DisplayName = "OnDispatch 콜백을 통해 메시지 처리 로직 실행 가능")]
    public async Task OnDispatch_WithCallback_ExecutesCustomLogic()
    {
        // Given (전제조건)
        var messageProcessed = false;
        var fakeStage = new FakeStage
        {
            OnDispatchCallback = (actor, packet) =>
            {
                messageProcessed = true;
                return Task.CompletedTask;
            }
        };
        var fakeActor = new FakeActor
        {
            ActorSender = new FakeActorSender { AccountId = 1001, SessionId = 2001 }
        };
        var message = new SimplePacket(new Empty());

        // When (행동)
        await fakeStage.OnDispatch(fakeActor, message);

        // Then (결과)
        messageProcessed.Should().BeTrue("OnDispatch 콜백이 실행되어야 함");
    }

    [Fact(DisplayName = "StageSender Broadcast 시 모든 메시지 기록")]
    public async Task StageSender_Broadcast_RecordsAllMessages()
    {
        // Given (전제조건)
        var stageSender = new FakeStageSender { StageId = 1 };

        // When (행동)
        var message1 = new SimplePacket(new Empty());
        var message2 = new SimplePacket(new Empty());
        await stageSender.BroadcastAsync(message1);
        await stageSender.BroadcastAsync(message2);

        // Then (결과)
        stageSender.BroadcastedPackets.Should().HaveCount(2);
        stageSender.BroadcastedPackets[0].Should().BeSameAs(message1);
        stageSender.BroadcastedPackets[1].Should().BeSameAs(message2);
    }

    [Fact(DisplayName = "StageSender Broadcast with Filter 기록")]
    public async Task StageSender_BroadcastWithFilter_RecordsFilteredMessages()
    {
        // Given (전제조건)
        var stageSender = new FakeStageSender { StageId = 1 };
        var message = new SimplePacket(new Empty());
        var filter = (PlayHouse.Abstractions.IActor actor) => actor.ActorSender.AccountId > 1000;

        // When (행동)
        await stageSender.BroadcastAsync(message, filter);

        // Then (결과)
        stageSender.FilteredBroadcasts.Should().ContainSingle();
        stageSender.FilteredBroadcasts[0].Packet.Should().BeSameAs(message);
        stageSender.FilteredBroadcasts[0].Filter.Should().BeSameAs(filter);
    }

    [Fact(DisplayName = "StageSender SendToStage 기록")]
    public async Task StageSender_SendToStage_RecordsStageMessage()
    {
        // Given (전제조건)
        var stageSender = new FakeStageSender { StageId = 1 };
        var targetStageId = 2;
        var message = new SimplePacket(new Empty());

        // When (행동)
        await stageSender.SendToStageAsync(targetStageId, message);

        // Then (결과)
        stageSender.StageMessages.Should().ContainSingle();
        stageSender.StageMessages[0].TargetStageId.Should().Be(targetStageId);
        stageSender.StageMessages[0].Packet.Should().BeSameAs(message);
    }

    #endregion

    #region 5. 실무 활용 예제 (Usage Examples)

    [Fact(DisplayName = "실무 예제: Request-Reply 패턴 시뮬레이션")]
    public async Task UsageExample_RequestReplyPattern()
    {
        // Given (전제조건)
        var fakeStage = new FakeStage();
        var fakeActor = new FakeActor
        {
            ActorSender = new FakeActorSender { AccountId = 1001, SessionId = 2001 }
        };

        // When (행동 1) - Client가 Request 전송
        var requestMessage = new SimplePacket(new Empty()) { MsgSeq = 1 };
        await fakeStage.OnDispatch(fakeActor, requestMessage);

        // When (행동 2) - Server가 Reply 전송
        var replyMessage = new SimplePacket(new Empty());
        fakeActor.ActorSender.Reply(replyMessage);

        // Then (결과)
        fakeStage.ReceivedMessages.Should().ContainSingle("Request가 Stage에 전달됨");
        var actorSender = (FakeActorSender)fakeActor.ActorSender;
        actorSender.Replies.Should().ContainSingle("Reply가 전송됨");
        actorSender.Replies[0].ErrorCode.Should().Be(0);
    }

    [Fact(DisplayName = "실무 예제: Fire-and-Forget 패턴")]
    public async Task UsageExample_FireAndForgetPattern()
    {
        // Given (전제조건)
        var fakeStage = new FakeStage();
        var fakeActor = new FakeActor
        {
            ActorSender = new FakeActorSender { AccountId = 1001, SessionId = 2001 }
        };

        // When (행동) - MsgSeq=0으로 메시지 전송
        var fireAndForgetMessage = new SimplePacket(new Empty()) { MsgSeq = 0 };
        await fakeStage.OnDispatch(fakeActor, fireAndForgetMessage);

        // Then (결과)
        fakeStage.ReceivedMessages.Should().ContainSingle("Fire-and-Forget 메시지가 전달됨");
        ((SimplePacket)fakeStage.ReceivedMessages[0].Packet).IsRequest.Should().BeFalse();
        ((FakeActorSender)fakeActor.ActorSender).Replies.Should().BeEmpty("Fire-and-Forget은 응답이 없음");
    }

    [Fact(DisplayName = "실무 예제: 채팅 메시지 브로드캐스트")]
    public async Task UsageExample_ChatMessageBroadcast()
    {
        // Given (전제조건)
        var stageSender = new FakeStageSender { StageId = 1, StageType = "ChatRoom" };
        var fakeStage = new FakeStage { StageSender = stageSender };
        var sender = new FakeActor
        {
            ActorSender = new FakeActorSender { AccountId = 1001, SessionId = 2001 }
        };

        // When (행동 1) - Client가 채팅 메시지 전송
        var chatMessage = new SimplePacket(new Empty());
        await fakeStage.OnDispatch(sender, chatMessage);

        // When (행동 2) - Stage가 모든 Actor에게 브로드캐스트
        await stageSender.BroadcastAsync(chatMessage);

        // Then (결과)
        fakeStage.ReceivedMessages.Should().ContainSingle("메시지가 Stage에 전달됨");
        stageSender.BroadcastedPackets.Should().ContainSingle("모든 Actor에게 브로드캐스트됨");
    }

    [Fact(DisplayName = "실무 예제: 필터를 사용한 선택적 브로드캐스트")]
    public async Task UsageExample_FilteredBroadcast()
    {
        // Given (전제조건)
        var stageSender = new FakeStageSender { StageId = 1, StageType = "BattleRoom" };
        var message = new SimplePacket(new Empty());

        // 팀 1의 플레이어에게만 메시지 전송하는 필터
        var team1Filter = (PlayHouse.Abstractions.IActor actor) =>
        {
            // 예: AccountId가 1000번대는 팀 1
            return actor.ActorSender.AccountId >= 1000 && actor.ActorSender.AccountId < 2000;
        };

        // When (행동)
        await stageSender.BroadcastAsync(message, team1Filter);

        // Then (결과)
        stageSender.FilteredBroadcasts.Should().ContainSingle();
        stageSender.FilteredBroadcasts[0].Packet.Should().BeSameAs(message);

        // 필터 검증
        var testActor1 = new FakeActor { ActorSender = new FakeActorSender { AccountId = 1001, SessionId = 2001 } };
        var testActor2 = new FakeActor { ActorSender = new FakeActorSender { AccountId = 2001, SessionId = 2002 } };

        stageSender.FilteredBroadcasts[0].Filter(testActor1).Should().BeTrue("팀 1 플레이어는 수신");
        stageSender.FilteredBroadcasts[0].Filter(testActor2).Should().BeFalse("팀 2 플레이어는 수신 안함");
    }

    #endregion
}
