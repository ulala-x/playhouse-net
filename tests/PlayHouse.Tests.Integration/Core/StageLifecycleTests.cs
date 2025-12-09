#nullable enable

using PlayHouse.Abstractions;
using PlayHouse.Tests.Integration.TestHelpers;
using FluentAssertions;
using PlayHouse.Infrastructure.Serialization;
using Google.Protobuf.WellKnownTypes;
using Xunit;

namespace PlayHouse.Tests.Integration.Core;

/// <summary>
/// 통합 테스트: Stage 라이프사이클 검증
/// Stage 생성, Actor 입장, 퇴장, 종료 등 전체 흐름을 검증합니다.
/// </summary>
public class StageLifecycleTests
{
    #region 1. 기본 동작 (Basic Operations)

    [Fact(DisplayName = "Stage 생성 시 OnCreate와 OnPostCreate가 순차적으로 호출됨")]
    public async Task CreateStage_CallsOnCreateAndOnPostCreate()
    {
        // Given (전제조건)
        var fakeStage = new FakeStage();
        var fakeSender = new FakeStageSender { StageId = 1, StageType = "TestStage" };
        fakeStage.StageSender = fakeSender;
        var initPacket = new SimplePacket(new Empty());

        // When (행동)
        var (errorCode, reply) = await fakeStage.OnCreate(initPacket);
        if (errorCode == 0)
        {
            await fakeStage.OnPostCreate();
        }

        // Then (결과)
        fakeStage.OnCreateCalled.Should().BeTrue("OnCreate가 호출되어야 함");
        fakeStage.OnPostCreateCalled.Should().BeTrue("OnCreate 성공 후 OnPostCreate가 호출되어야 함");
        errorCode.Should().Be(0, "성공 코드 0을 반환해야 함");
    }

    [Fact(DisplayName = "Stage OnCreate 실패 시 OnPostCreate가 호출되지 않음")]
    public async Task CreateStage_WhenOnCreateFails_OnPostCreateNotCalled()
    {
        // Given (전제조건)
        var fakeStage = new FakeStage
        {
            CreateErrorCode = 400 // 실패 코드 설정
        };
        var fakeSender = new FakeStageSender { StageId = 1 };
        fakeStage.StageSender = fakeSender;
        var initPacket = new SimplePacket(new Empty());

        // When (행동)
        var (errorCode, reply) = await fakeStage.OnCreate(initPacket);

        // Then (결과)
        errorCode.Should().Be(400, "OnCreate에서 설정한 에러 코드를 반환해야 함");
        fakeStage.OnPostCreateCalled.Should().BeFalse("OnCreate 실패 시 OnPostCreate가 호출되지 않아야 함");
    }

    [Fact(DisplayName = "Actor 입장 시 OnJoinRoom과 OnPostJoinRoom이 순차적으로 호출됨")]
    public async Task JoinRoom_CallsOnJoinRoomAndOnPostJoinRoom()
    {
        // Given (전제조건)
        var fakeStage = new FakeStage();
        var fakeActor = new FakeActor
        {
            ActorSender = new FakeActorSender { AccountId = 1001, SessionId = 2001 }
        };
        var userInfo = new SimplePacket(new Empty());

        // When (행동)
        var (errorCode, reply) = await fakeStage.OnJoinRoom(fakeActor, userInfo);
        if (errorCode == 0)
        {
            await fakeStage.OnPostJoinRoom(fakeActor);
        }

        // Then (결과)
        fakeStage.JoinedActors.Should().ContainSingle("OnJoinRoom이 호출되어야 함");
        fakeStage.JoinedActors[0].Actor.Should().BeSameAs(fakeActor);
        fakeStage.JoinedActors[0].Packet.Should().BeSameAs(userInfo);
        errorCode.Should().Be(0, "성공 코드 0을 반환해야 함");
    }

    [Fact(DisplayName = "Actor 퇴장 시 OnLeaveRoom이 호출됨")]
    public async Task LeaveRoom_CallsOnLeaveRoom()
    {
        // Given (전제조건)
        var fakeStage = new FakeStage();
        var fakeActor = new FakeActor
        {
            ActorSender = new FakeActorSender { AccountId = 1001, SessionId = 2001 }
        };
        var leaveReason = LeaveReason.UserRequest;

        // When (행동)
        await fakeStage.OnLeaveRoom(fakeActor, leaveReason);

        // Then (결과)
        fakeStage.LeftActors.Should().ContainSingle("OnLeaveRoom이 호출되어야 함");
        fakeStage.LeftActors[0].Actor.Should().BeSameAs(fakeActor);
        fakeStage.LeftActors[0].Reason.Should().Be(leaveReason);
    }

    #endregion

    #region 2. 응답 데이터 검증 (Response Validation)

    [Fact(DisplayName = "OnCreate에서 reply 패킷을 반환할 수 있음")]
    public async Task OnCreate_CanReturnReplyPacket()
    {
        // Given (전제조건)
        var fakeStage = new FakeStage();
        var replyMessage = new Empty();
        fakeStage.CreateReply = new SimplePacket(replyMessage);
        var initPacket = new SimplePacket(new Empty());

        // When (행동)
        var (errorCode, reply) = await fakeStage.OnCreate(initPacket);

        // Then (결과)
        errorCode.Should().Be(0);
        reply.Should().NotBeNull("reply 패킷이 반환되어야 함");
        reply.Should().BeSameAs(fakeStage.CreateReply);
    }

    [Fact(DisplayName = "OnJoinRoom에서 reply 패킷을 반환할 수 있음")]
    public async Task OnJoinRoom_CanReturnReplyPacket()
    {
        // Given (전제조건)
        var fakeStage = new FakeStage();
        var replyMessage = new Empty();
        fakeStage.JoinReply = new SimplePacket(replyMessage);
        var fakeActor = new FakeActor
        {
            ActorSender = new FakeActorSender { AccountId = 1001, SessionId = 2001 }
        };
        var userInfo = new SimplePacket(new Empty());

        // When (행동)
        var (errorCode, reply) = await fakeStage.OnJoinRoom(fakeActor, userInfo);

        // Then (결과)
        errorCode.Should().Be(0);
        reply.Should().NotBeNull("reply 패킷이 반환되어야 함");
        reply.Should().BeSameAs(fakeStage.JoinReply);
    }

    #endregion

    #region 3. 입력 파라미터 검증 (Input Validation)

    [Fact(DisplayName = "OnJoinRoom 실패 시 Actor가 추가되지만 에러 코드를 반환")]
    public async Task OnJoinRoom_WhenFails_ReturnsErrorCode()
    {
        // Given (전제조건)
        var fakeStage = new FakeStage
        {
            JoinErrorCode = 403 // 입장 거부
        };
        var fakeActor = new FakeActor
        {
            ActorSender = new FakeActorSender { AccountId = 1001, SessionId = 2001 }
        };
        var userInfo = new SimplePacket(new Empty());

        // When (행동)
        var (errorCode, reply) = await fakeStage.OnJoinRoom(fakeActor, userInfo);

        // Then (결과)
        errorCode.Should().Be(403, "설정한 에러 코드를 반환해야 함");
        fakeStage.JoinedActors.Should().ContainSingle("OnJoinRoom은 호출되지만 실패 처리됨");
    }

    [Fact(DisplayName = "여러 Actor가 순차적으로 입장 가능")]
    public async Task MultipleActors_CanJoinSequentially()
    {
        // Given (전제조건)
        var fakeStage = new FakeStage();
        var actor1 = new FakeActor { ActorSender = new FakeActorSender { AccountId = 1001, SessionId = 2001 } };
        var actor2 = new FakeActor { ActorSender = new FakeActorSender { AccountId = 1002, SessionId = 2002 } };
        var actor3 = new FakeActor { ActorSender = new FakeActorSender { AccountId = 1003, SessionId = 2003 } };
        var userInfo = new SimplePacket(new Empty());

        // When (행동)
        await fakeStage.OnJoinRoom(actor1, userInfo);
        await fakeStage.OnJoinRoom(actor2, userInfo);
        await fakeStage.OnJoinRoom(actor3, userInfo);

        // Then (결과)
        fakeStage.JoinedActors.Should().HaveCount(3, "3명의 Actor가 입장해야 함");
        fakeStage.JoinedActors[0].Actor.Should().BeSameAs(actor1);
        fakeStage.JoinedActors[1].Actor.Should().BeSameAs(actor2);
        fakeStage.JoinedActors[2].Actor.Should().BeSameAs(actor3);
    }

    #endregion

    #region 4. 엣지 케이스 (Edge Cases)

    [Fact(DisplayName = "연결 상태 변경 시 OnActorConnectionChanged가 호출됨")]
    public async Task ActorConnectionChanged_CallsOnActorConnectionChanged()
    {
        // Given (전제조건)
        var fakeStage = new FakeStage();
        var fakeActor = new FakeActor
        {
            ActorSender = new FakeActorSender { AccountId = 1001, SessionId = 2001 },
            IsConnected = true
        };

        // When (행동) - 연결 끊김
        await fakeStage.OnActorConnectionChanged(fakeActor, false, DisconnectReason.NetworkError);

        // Then (결과)
        fakeStage.ConnectionChanges.Should().ContainSingle();
        fakeStage.ConnectionChanges[0].Actor.Should().BeSameAs(fakeActor);
        fakeStage.ConnectionChanges[0].IsConnected.Should().BeFalse();
        fakeStage.ConnectionChanges[0].Reason.Should().Be(DisconnectReason.NetworkError);
    }

    [Fact(DisplayName = "재연결 시 OnActorConnectionChanged가 true로 호출됨")]
    public async Task Reconnect_CallsOnActorConnectionChangedWithTrue()
    {
        // Given (전제조건)
        var fakeStage = new FakeStage();
        var fakeActor = new FakeActor
        {
            ActorSender = new FakeActorSender { AccountId = 1001, SessionId = 2001 },
            IsConnected = false
        };

        // When (행동) - 재연결
        await fakeStage.OnActorConnectionChanged(fakeActor, true, null);

        // Then (결과)
        fakeStage.ConnectionChanges.Should().ContainSingle();
        fakeStage.ConnectionChanges[0].Actor.Should().BeSameAs(fakeActor);
        fakeStage.ConnectionChanges[0].IsConnected.Should().BeTrue();
        fakeStage.ConnectionChanges[0].Reason.Should().BeNull("재연결 시에는 disconnect reason이 없음");
    }

    [Fact(DisplayName = "동일한 Actor가 여러 번 연결 상태 변경 가능")]
    public async Task ActorConnectionChanged_MultipleTimesForSameActor()
    {
        // Given (전제조건)
        var fakeStage = new FakeStage();
        var fakeActor = new FakeActor
        {
            ActorSender = new FakeActorSender { AccountId = 1001, SessionId = 2001 }
        };

        // When (행동)
        await fakeStage.OnActorConnectionChanged(fakeActor, false, DisconnectReason.NetworkError);
        await fakeStage.OnActorConnectionChanged(fakeActor, true, null);
        await fakeStage.OnActorConnectionChanged(fakeActor, false, DisconnectReason.Timeout);

        // Then (결과)
        fakeStage.ConnectionChanges.Should().HaveCount(3, "3번의 연결 상태 변경이 기록되어야 함");
        fakeStage.ConnectionChanges[0].IsConnected.Should().BeFalse();
        fakeStage.ConnectionChanges[1].IsConnected.Should().BeTrue();
        fakeStage.ConnectionChanges[2].IsConnected.Should().BeFalse();
    }

    #endregion

    #region 5. 실무 활용 예제 (Usage Examples)

    [Fact(DisplayName = "실무 예제: Stage 생성 → Actor 입장 → 메시지 수신 → Actor 퇴장")]
    public async Task UsageExample_CompleteStageLifecycle()
    {
        // Given (전제조건)
        var fakeStage = new FakeStage();
        var fakeSender = new FakeStageSender { StageId = 1, StageType = "ChatRoom" };
        fakeStage.StageSender = fakeSender;
        var initPacket = new SimplePacket(new Empty());

        // When (행동 1) - Stage 생성
        var (createError, createReply) = await fakeStage.OnCreate(initPacket);
        await fakeStage.OnPostCreate();

        // When (행동 2) - Actor 입장
        var player1 = new FakeActor { ActorSender = new FakeActorSender { AccountId = 1001, SessionId = 2001 } };
        var userInfo = new SimplePacket(new Empty());
        var (joinError, joinReply) = await fakeStage.OnJoinRoom(player1, userInfo);
        await fakeStage.OnPostJoinRoom(player1);

        // When (행동 3) - 메시지 수신
        var chatMessage = new SimplePacket(new Empty { });
        await fakeStage.OnDispatch(player1, chatMessage);

        // When (행동 4) - Actor 퇴장
        await fakeStage.OnLeaveRoom(player1, LeaveReason.UserRequest);

        // Then (결과)
        fakeStage.OnCreateCalled.Should().BeTrue();
        fakeStage.OnPostCreateCalled.Should().BeTrue();
        fakeStage.JoinedActors.Should().ContainSingle();
        fakeStage.ReceivedMessages.Should().ContainSingle();
        fakeStage.LeftActors.Should().ContainSingle();
    }

    [Fact(DisplayName = "실무 예제: 2명의 플레이어가 순차 입장 후 한 명씩 퇴장")]
    public async Task UsageExample_TwoPlayersJoinAndLeave()
    {
        // Given (전제조건)
        var fakeStage = new FakeStage();
        var player1 = new FakeActor { ActorSender = new FakeActorSender { AccountId = 1001, SessionId = 2001 } };
        var player2 = new FakeActor { ActorSender = new FakeActorSender { AccountId = 1002, SessionId = 2002 } };
        var userInfo = new SimplePacket(new Empty());

        // When (행동) - 입장
        await fakeStage.OnJoinRoom(player1, userInfo);
        await fakeStage.OnPostJoinRoom(player1);
        await fakeStage.OnJoinRoom(player2, userInfo);
        await fakeStage.OnPostJoinRoom(player2);

        // When (행동) - 퇴장
        await fakeStage.OnLeaveRoom(player1, LeaveReason.UserRequest);
        await fakeStage.OnLeaveRoom(player2, LeaveReason.UserRequest);

        // Then (결과)
        fakeStage.JoinedActors.Should().HaveCount(2);
        fakeStage.LeftActors.Should().HaveCount(2);
        fakeStage.LeftActors[0].Actor.ActorSender.AccountId.Should().Be(1001);
        fakeStage.LeftActors[1].Actor.ActorSender.AccountId.Should().Be(1002);
    }

    #endregion
}
