#nullable enable

using PlayHouse.Tests.Integration.TestHelpers;
using FluentAssertions;
using PlayHouse.Infrastructure.Serialization;
using Google.Protobuf.WellKnownTypes;
using Xunit;

namespace PlayHouse.Tests.Integration.Core;

/// <summary>
/// 통합 테스트: Actor 라이프사이클 검증
/// Actor 생성, 인증, 소멸 등의 전체 흐름을 검증합니다.
/// </summary>
public class ActorLifecycleTests
{
    #region 1. 기본 동작 (Basic Operations)

    [Fact(DisplayName = "Actor 생성 시 OnCreate가 호출됨")]
    public async Task CreateActor_CallsOnCreate()
    {
        // Given (전제조건)
        var fakeActor = new FakeActor
        {
            ActorSender = new FakeActorSender { AccountId = 1001, SessionId = 2001 }
        };

        // When (행동)
        await fakeActor.OnCreate();

        // Then (결과)
        fakeActor.OnCreateCalled.Should().BeTrue("OnCreate가 호출되어야 함");
    }

    [Fact(DisplayName = "Actor 소멸 시 OnDestroy가 호출됨")]
    public async Task DestroyActor_CallsOnDestroy()
    {
        // Given (전제조건)
        var fakeActor = new FakeActor
        {
            ActorSender = new FakeActorSender { AccountId = 1001, SessionId = 2001 }
        };

        // When (행동)
        await fakeActor.OnDestroy();

        // Then (결과)
        fakeActor.OnDestroyCalled.Should().BeTrue("OnDestroy가 호출되어야 함");
    }

    [Fact(DisplayName = "Actor 인증 시 OnAuthenticate가 호출됨")]
    public async Task AuthenticateActor_CallsOnAuthenticate()
    {
        // Given (전제조건)
        var fakeActor = new FakeActor
        {
            ActorSender = new FakeActorSender { AccountId = 1001, SessionId = 2001 }
        };
        var authData = new SimplePacket(new Empty());

        // When (행동)
        await fakeActor.OnAuthenticate(authData);

        // Then (결과)
        fakeActor.OnAuthenticateCalled.Should().BeTrue("OnAuthenticate가 호출되어야 함");
        fakeActor.LastAuthData.Should().BeSameAs(authData, "전달한 authData가 기록되어야 함");
    }

    [Fact(DisplayName = "Actor 인증 시 authData가 null일 수 있음")]
    public async Task AuthenticateActor_WithNullAuthData_Succeeds()
    {
        // Given (전제조건)
        var fakeActor = new FakeActor
        {
            ActorSender = new FakeActorSender { AccountId = 1001, SessionId = 2001 }
        };

        // When (행동)
        await fakeActor.OnAuthenticate(null);

        // Then (결과)
        fakeActor.OnAuthenticateCalled.Should().BeTrue();
        fakeActor.LastAuthData.Should().BeNull();
    }

    #endregion

    #region 2. 응답 데이터 검증 (Response Validation)

    [Fact(DisplayName = "Actor의 IsConnected 속성이 연결 상태를 반영함")]
    public void Actor_IsConnected_ReflectsConnectionState()
    {
        // Given (전제조건)
        var fakeActor = new FakeActor
        {
            ActorSender = new FakeActorSender { AccountId = 1001, SessionId = 2001 },
            IsConnected = true
        };

        // When & Then (행동 및 결과)
        fakeActor.IsConnected.Should().BeTrue("연결된 상태여야 함");

        // When (행동) - 연결 끊김
        fakeActor.IsConnected = false;

        // Then (결과)
        fakeActor.IsConnected.Should().BeFalse("연결이 끊긴 상태여야 함");
    }

    [Fact(DisplayName = "ActorSender가 올바른 AccountId와 SessionId를 제공함")]
    public void ActorSender_ProvidesCorrectIdentifiers()
    {
        // Given (전제조건)
        var accountId = 1001L;
        var sessionId = 2001L;
        var actorSender = new FakeActorSender
        {
            AccountId = accountId,
            SessionId = sessionId
        };
        var fakeActor = new FakeActor { ActorSender = actorSender };

        // When & Then (행동 및 결과)
        fakeActor.ActorSender.AccountId.Should().Be(accountId);
        fakeActor.ActorSender.SessionId.Should().Be(sessionId);
    }

    #endregion

    #region 3. 입력 파라미터 검증 (Input Validation)

    [Fact(DisplayName = "인증 실패 시 예외를 발생시킬 수 있음")]
    public async Task Authenticate_WhenFails_ThrowsException()
    {
        // Given (전제조건)
        var fakeActor = new FakeActor
        {
            ActorSender = new FakeActorSender { AccountId = 1001, SessionId = 2001 },
            ThrowOnAuthenticate = true
        };

        // When & Then (행동 및 결과)
        var act = async () => await fakeActor.OnAuthenticate(null);
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Authentication failed");
    }

    [Fact(DisplayName = "OnCreate 콜백을 통해 초기화 로직 실행 가능")]
    public async Task OnCreate_WithCallback_ExecutesInitializationLogic()
    {
        // Given (전제조건)
        var initializationExecuted = false;
        var fakeActor = new FakeActor
        {
            ActorSender = new FakeActorSender { AccountId = 1001, SessionId = 2001 },
            OnCreateCallback = () =>
            {
                initializationExecuted = true;
                return Task.CompletedTask;
            }
        };

        // When (행동)
        await fakeActor.OnCreate();

        // Then (결과)
        initializationExecuted.Should().BeTrue("OnCreate 콜백이 실행되어야 함");
    }

    [Fact(DisplayName = "OnDestroy 콜백을 통해 정리 로직 실행 가능")]
    public async Task OnDestroy_WithCallback_ExecutesCleanupLogic()
    {
        // Given (전제조건)
        var cleanupExecuted = false;
        var fakeActor = new FakeActor
        {
            ActorSender = new FakeActorSender { AccountId = 1001, SessionId = 2001 },
            OnDestroyCallback = () =>
            {
                cleanupExecuted = true;
                return Task.CompletedTask;
            }
        };

        // When (행동)
        await fakeActor.OnDestroy();

        // Then (결과)
        cleanupExecuted.Should().BeTrue("OnDestroy 콜백이 실행되어야 함");
    }

    #endregion

    #region 4. 엣지 케이스 (Edge Cases)

    [Fact(DisplayName = "Actor Reset 후 상태가 초기화됨")]
    public async Task Reset_ResetsActorState()
    {
        // Given (전제조건)
        var fakeActor = new FakeActor
        {
            ActorSender = new FakeActorSender { AccountId = 1001, SessionId = 2001 }
        };
        var authData = new SimplePacket(new Empty());

        // When (행동) - 상태 변경
        await fakeActor.OnCreate();
        await fakeActor.OnAuthenticate(authData);
        fakeActor.IsConnected = false;

        // Then (결과) - Reset 전 확인
        fakeActor.OnCreateCalled.Should().BeTrue();
        fakeActor.OnAuthenticateCalled.Should().BeTrue();
        fakeActor.LastAuthData.Should().NotBeNull();
        fakeActor.IsConnected.Should().BeFalse();

        // When (행동) - Reset
        fakeActor.Reset();

        // Then (결과) - Reset 후 확인
        fakeActor.OnCreateCalled.Should().BeFalse("Reset 후 OnCreateCalled가 false여야 함");
        fakeActor.OnAuthenticateCalled.Should().BeFalse("Reset 후 OnAuthenticateCalled가 false여야 함");
        fakeActor.LastAuthData.Should().BeNull("Reset 후 LastAuthData가 null이어야 함");
        fakeActor.IsConnected.Should().BeTrue("Reset 후 IsConnected가 기본값 true여야 함");
    }

    [Fact(DisplayName = "DisposeAsync 호출 시 정상적으로 완료됨")]
    public async Task DisposeAsync_CompletesSuccessfully()
    {
        // Given (전제조건)
        var fakeActor = new FakeActor
        {
            ActorSender = new FakeActorSender { AccountId = 1001, SessionId = 2001 }
        };

        // When & Then (행동 및 결과)
        var act = async () => await fakeActor.DisposeAsync();
        await act.Should().NotThrowAsync("DisposeAsync는 예외 없이 완료되어야 함");
    }

    #endregion

    #region 5. 실무 활용 예제 (Usage Examples)

    [Fact(DisplayName = "실무 예제: Actor 전체 라이프사이클 - 생성 → 인증 → 소멸")]
    public async Task UsageExample_CompleteActorLifecycle()
    {
        // Given (전제조건)
        var fakeActor = new FakeActor
        {
            ActorSender = new FakeActorSender { AccountId = 1001, SessionId = 2001 }
        };
        var authData = new SimplePacket(new Empty());

        // When (행동 1) - 생성
        await fakeActor.OnCreate();

        // When (행동 2) - 인증
        await fakeActor.OnAuthenticate(authData);

        // When (행동 3) - 소멸
        await fakeActor.OnDestroy();
        await fakeActor.DisposeAsync();

        // Then (결과)
        fakeActor.OnCreateCalled.Should().BeTrue();
        fakeActor.OnAuthenticateCalled.Should().BeTrue();
        fakeActor.OnDestroyCalled.Should().BeTrue();
    }

    [Fact(DisplayName = "실무 예제: 연결 끊김 후 재연결 시나리오")]
    public async Task UsageExample_DisconnectAndReconnect()
    {
        // Given (전제조건)
        var fakeActor = new FakeActor
        {
            ActorSender = new FakeActorSender { AccountId = 1001, SessionId = 2001 },
            IsConnected = true
        };

        // When (행동 1) - 초기 연결 확인
        fakeActor.IsConnected.Should().BeTrue("초기 상태는 연결됨");

        // When (행동 2) - 연결 끊김
        fakeActor.IsConnected = false;
        fakeActor.IsConnected.Should().BeFalse("연결이 끊긴 상태");

        // When (행동 3) - 재연결
        fakeActor.IsConnected = true;

        // Then (결과)
        fakeActor.IsConnected.Should().BeTrue("재연결 후 연결됨");
    }

    [Fact(DisplayName = "실무 예제: 여러 Actor를 동시에 관리")]
    public async Task UsageExample_ManageMultipleActors()
    {
        // Given (전제조건)
        var actors = new List<FakeActor>
        {
            new FakeActor { ActorSender = new FakeActorSender { AccountId = 1001, SessionId = 2001 } },
            new FakeActor { ActorSender = new FakeActorSender { AccountId = 1002, SessionId = 2002 } },
            new FakeActor { ActorSender = new FakeActorSender { AccountId = 1003, SessionId = 2003 } }
        };

        // When (행동) - 모든 Actor 생성
        foreach (var actor in actors)
        {
            await actor.OnCreate();
            await actor.OnAuthenticate(null);
        }

        // Then (결과)
        actors.Should().AllSatisfy(actor =>
        {
            actor.OnCreateCalled.Should().BeTrue();
            actor.OnAuthenticateCalled.Should().BeTrue();
        });

        // When (행동) - 모든 Actor 소멸
        foreach (var actor in actors)
        {
            await actor.OnDestroy();
        }

        // Then (결과)
        actors.Should().AllSatisfy(actor =>
        {
            actor.OnDestroyCalled.Should().BeTrue();
        });
    }

    #endregion
}
