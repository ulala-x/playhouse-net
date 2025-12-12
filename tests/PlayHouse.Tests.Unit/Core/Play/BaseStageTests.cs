#nullable enable

using FluentAssertions;
using PlayHouse.Abstractions;
using PlayHouse.Abstractions.Play;
using PlayHouse.Core.Shared;
using PlayHouse.Core.Messaging;
using PlayHouse.Core.Play;
using PlayHouse.Core.Play.Base;
using PlayHouse.Runtime.ServerMesh.Communicator;
using PlayHouse.Runtime.ServerMesh.Message;
using PlayHouse.Runtime.Proto;
using Xunit;
using NSubstitute;

namespace PlayHouse.Tests.Unit.Core.Play;

/// <summary>
/// 단위 테스트: BaseStage의 이벤트 루프 및 Actor 관리 기능 검증
/// </summary>
public class BaseStageTests
{
    #region Fake Implementations

    private class FakeStage : IStage
    {
        public IStageSender StageSender { get; }
        public int OnCreateCallCount { get; private set; }
        public int OnDestroyCallCount { get; private set; }
        public int OnDispatchActorCallCount { get; private set; }
        public int OnDispatchCallCount { get; private set; }
        public List<IPacket> ReceivedPackets { get; } = new();

        public FakeStage(IStageSender stageSender)
        {
            StageSender = stageSender;
        }

        public Task<(bool result, IPacket reply)> OnCreate(IPacket packet)
        {
            OnCreateCallCount++;
            return Task.FromResult<(bool result, IPacket reply)>((true, CPacket.Empty("CreateReply")));
        }

        public Task OnPostCreate() => Task.CompletedTask;

        public Task OnDestroy()
        {
            OnDestroyCallCount++;
            return Task.CompletedTask;
        }

        public Task<bool> OnJoinStage(IActor actor) => Task.FromResult(true);
        public Task OnPostJoinStage(IActor actor) => Task.CompletedTask;
        public ValueTask OnConnectionChanged(IActor actor, bool isConnected) => ValueTask.CompletedTask;

        public Task OnDispatch(IActor actor, IPacket packet)
        {
            OnDispatchActorCallCount++;
            ReceivedPackets.Add(packet);
            return Task.CompletedTask;
        }

        public Task OnDispatch(IPacket packet)
        {
            OnDispatchCallCount++;
            ReceivedPackets.Add(packet);
            return Task.CompletedTask;
        }
    }

    private class FakeActor : IActor
    {
        public IActorSender ActorSender { get; }
        public int OnDestroyCallCount { get; private set; }

        public FakeActor(IActorSender actorSender)
        {
            ActorSender = actorSender;
        }

        public Task OnCreate() => Task.CompletedTask;

        public Task OnDestroy()
        {
            OnDestroyCallCount++;
            return Task.CompletedTask;
        }

        public Task<bool> OnAuthenticate(IPacket authPacket) => Task.FromResult(true);
        public Task OnPostAuthenticate() => Task.CompletedTask;
    }

    #endregion

    private (BaseStage baseStage, FakeStage fakeStage, XStageSender stageSender) CreateTestStage()
    {
        var communicator = Substitute.For<IClientCommunicator>();
        var requestCache = new RequestCache();
        var dispatcher = Substitute.For<IPlayDispatcher>();

        var stageSender = new XStageSender(
            communicator,
            requestCache,
            serviceId: 1,
            nid: "1:1",
            stageId: 100,
            dispatcher);

        stageSender.SetStageType("test_stage");

        var fakeStage = new FakeStage(stageSender);
        var baseStage = new BaseStage(fakeStage, stageSender);

        return (baseStage, fakeStage, stageSender);
    }

    [Fact(DisplayName = "StageId는 StageSender에서 가져온다")]
    public void StageId_ReturnsFromStageSender()
    {
        // Given (전제조건)
        var (baseStage, _, stageSender) = CreateTestStage();

        // When (행동)
        var stageId = baseStage.StageId;

        // Then (결과)
        stageId.Should().Be(stageSender.StageId, "StageId는 StageSender에서 가져와야 함");
    }

    [Fact(DisplayName = "StageType은 StageSender에서 가져온다")]
    public void StageType_ReturnsFromStageSender()
    {
        // Given (전제조건)
        var (baseStage, _, _) = CreateTestStage();

        // When (행동)
        var stageType = baseStage.StageType;

        // Then (결과)
        stageType.Should().Be("test_stage");
    }

    [Fact(DisplayName = "IsCreated - 초기값은 false이다")]
    public void IsCreated_Initially_IsFalse()
    {
        // Given (전제조건)
        var (baseStage, _, _) = CreateTestStage();

        // When (행동)
        var isCreated = baseStage.IsCreated;

        // Then (결과)
        isCreated.Should().BeFalse("초기에는 생성되지 않은 상태여야 함");
    }

    [Fact(DisplayName = "MarkAsCreated - IsCreated를 true로 설정한다")]
    public void MarkAsCreated_SetsIsCreatedToTrue()
    {
        // Given (전제조건)
        var (baseStage, _, _) = CreateTestStage();

        // When (행동)
        baseStage.MarkAsCreated();

        // Then (결과)
        baseStage.IsCreated.Should().BeTrue("MarkAsCreated 호출 후 IsCreated는 true여야 함");
    }

    [Fact(DisplayName = "ActorCount - 초기값은 0이다")]
    public void ActorCount_Initially_IsZero()
    {
        // Given (전제조건)
        var (baseStage, _, _) = CreateTestStage();

        // When (행동)
        var count = baseStage.ActorCount;

        // Then (결과)
        count.Should().Be(0, "초기 Actor 수는 0이어야 함");
    }

    [Fact(DisplayName = "AddActor - Actor를 추가한다")]
    public void AddActor_AddsActorToStage()
    {
        // Given (전제조건)
        var (baseStage, _, _) = CreateTestStage();
        var actorSender = Substitute.For<IActorSender>();
        var fakeActor = new FakeActor(actorSender);
        var baseActor = CreateBaseActor("1001", fakeActor, baseStage);

        // When (행동)
        baseStage.AddActor(baseActor);

        // Then (결과)
        baseStage.ActorCount.Should().Be(1, "Actor가 추가되어야 함");
    }

    [Fact(DisplayName = "GetActor - 존재하는 Actor를 반환한다")]
    public void GetActor_ExistingActor_ReturnsActor()
    {
        // Given (전제조건)
        var (baseStage, _, _) = CreateTestStage();
        var actorSender = Substitute.For<IActorSender>();
        var fakeActor = new FakeActor(actorSender);
        const string accountId = "1001";
        var baseActor = CreateBaseActor(accountId, fakeActor, baseStage);
        baseStage.AddActor(baseActor);

        // When (행동)
        var retrievedActor = baseStage.GetActor(accountId);

        // Then (결과)
        retrievedActor.Should().NotBeNull();
        retrievedActor.Should().BeSameAs(baseActor);
    }

    [Fact(DisplayName = "GetActor - 존재하지 않는 Actor는 null을 반환한다")]
    public void GetActor_NonExistentActor_ReturnsNull()
    {
        // Given (전제조건)
        var (baseStage, _, _) = CreateTestStage();

        // When (행동)
        var actor = baseStage.GetActor("9999");

        // Then (결과)
        actor.Should().BeNull("존재하지 않는 Actor는 null을 반환해야 함");
    }

    [Fact(DisplayName = "RemoveActor - 존재하는 Actor를 제거한다")]
    public void RemoveActor_ExistingActor_ReturnsTrue()
    {
        // Given (전제조건)
        var (baseStage, _, _) = CreateTestStage();
        var actorSender = Substitute.For<IActorSender>();
        var fakeActor = new FakeActor(actorSender);
        const string accountId = "1001";
        var baseActor = CreateBaseActor(accountId, fakeActor, baseStage);
        baseStage.AddActor(baseActor);

        // When (행동)
        var result = baseStage.RemoveActor(accountId);

        // Then (결과)
        result.Should().BeTrue("존재하는 Actor 제거는 성공해야 함");
        baseStage.ActorCount.Should().Be(0, "제거 후 Actor 수는 0이어야 함");
    }

    [Fact(DisplayName = "RemoveActor - 존재하지 않는 Actor는 false를 반환한다")]
    public void RemoveActor_NonExistentActor_ReturnsFalse()
    {
        // Given (전제조건)
        var (baseStage, _, _) = CreateTestStage();

        // When (행동)
        var result = baseStage.RemoveActor("9999");

        // Then (결과)
        result.Should().BeFalse("존재하지 않는 Actor 제거는 실패해야 함");
    }

    [Fact(DisplayName = "여러 Actor 추가 - 모두 관리된다")]
    public void AddMultipleActors_AllManaged()
    {
        // Given (전제조건)
        var (baseStage, _, _) = CreateTestStage();
        const int actorCount = 5;

        // When (행동)
        for (int i = 0; i < actorCount; i++)
        {
            var actorSender = Substitute.For<IActorSender>();
            var fakeActor = new FakeActor(actorSender);
            var baseActor = CreateBaseActor((1000 + i).ToString(), fakeActor, baseStage);
            baseStage.AddActor(baseActor);
        }

        // Then (결과)
        baseStage.ActorCount.Should().Be(actorCount, $"{actorCount}개의 Actor가 있어야 함");
    }

    [Fact(DisplayName = "PostDestroy - Stage의 OnDestroy가 호출된다")]
    public async Task PostDestroy_CallsStageOnDestroy()
    {
        // Given (전제조건)
        var (baseStage, fakeStage, _) = CreateTestStage();

        // When (행동)
        baseStage.PostDestroy();
        await Task.Delay(100); // 이벤트 루프 처리 대기

        // Then (결과)
        fakeStage.OnDestroyCallCount.Should().Be(1, "OnDestroy가 호출되어야 함");
    }

    [Fact(DisplayName = "PostDestroy - 모든 Actor의 OnDestroy가 호출된다")]
    public async Task PostDestroy_CallsAllActorOnDestroy()
    {
        // Given (전제조건)
        var (baseStage, _, _) = CreateTestStage();
        var actors = new List<FakeActor>();

        for (int i = 0; i < 3; i++)
        {
            var actorSender = Substitute.For<IActorSender>();
            var fakeActor = new FakeActor(actorSender);
            actors.Add(fakeActor);
            var baseActor = CreateBaseActor((1000 + i).ToString(), fakeActor, baseStage);
            baseStage.AddActor(baseActor);
        }

        // When (행동)
        baseStage.PostDestroy();
        await Task.Delay(100); // 이벤트 루프 처리 대기

        // Then (결과)
        foreach (var actor in actors)
        {
            actor.OnDestroyCallCount.Should().Be(1, "모든 Actor의 OnDestroy가 호출되어야 함");
        }
        baseStage.ActorCount.Should().Be(0, "Destroy 후 Actor가 없어야 함");
    }

    [Fact(DisplayName = "PostTimerCallback - 콜백이 이벤트 루프에서 실행된다")]
    public async Task PostTimerCallback_ExecutesInEventLoop()
    {
        // Given (전제조건)
        var (baseStage, _, _) = CreateTestStage();
        var callbackExecuted = false;

        // When (행동)
        baseStage.PostTimerCallback(1, async () =>
        {
            callbackExecuted = true;
            await Task.CompletedTask;
        });
        await Task.Delay(100); // 이벤트 루프 처리 대기

        // Then (결과)
        callbackExecuted.Should().BeTrue("타이머 콜백이 실행되어야 함");
    }

    [Fact(DisplayName = "PostAsyncBlock - 결과가 이벤트 루프에서 처리된다")]
    public async Task PostAsyncBlock_ProcessesInEventLoop()
    {
        // Given (전제조건)
        var (baseStage, _, _) = CreateTestStage();
        var postCallbackExecuted = false;
        object? receivedResult = null;
        const string expectedResult = "test_result";

        var asyncPacket = new AsyncBlockPacket(
            baseStage.StageId,
            async (result) =>
            {
                postCallbackExecuted = true;
                receivedResult = result;
                await Task.CompletedTask;
            },
            expectedResult);

        // When (행동)
        baseStage.PostAsyncBlock(asyncPacket);
        await Task.Delay(100); // 이벤트 루프 처리 대기

        // Then (결과)
        postCallbackExecuted.Should().BeTrue("PostCallback이 실행되어야 함");
        receivedResult.Should().Be(expectedResult, "결과가 전달되어야 함");
    }

    #region Helper Methods

    private static BaseActor CreateBaseActor(string accountId, FakeActor fakeActor, BaseStage baseStage)
    {
        var xActorSender = new XActorSender(
            sessionNid: "session:1",
            sid: 1,
            apiNid: "api:1",
            baseStage);

        // AccountId는 인증 시 설정되므로 여기서는 직접 설정
        xActorSender.AccountId = accountId;

        return new BaseActor(fakeActor, xActorSender);
    }

    #endregion
}
