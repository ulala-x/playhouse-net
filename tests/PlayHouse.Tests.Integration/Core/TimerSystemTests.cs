#nullable enable

using PlayHouse.Tests.Integration.TestHelpers;
using FluentAssertions;
using Xunit;

namespace PlayHouse.Tests.Integration.Core;

/// <summary>
/// 통합 테스트: 타이머 시스템 검증
/// RepeatTimer, CountTimer, 타이머 취소 등을 검증합니다.
/// </summary>
public class TimerSystemTests
{
    #region 1. 기본 동작 (Basic Operations)

    [Fact(DisplayName = "RepeatTimer 등록 시 timerId 반환")]
    public void AddRepeatTimer_ReturnsTimerId()
    {
        // Given (전제조건)
        var stageSender = new FakeStageSender { StageId = 1 };
        var initialDelay = TimeSpan.FromMilliseconds(100);
        var period = TimeSpan.FromMilliseconds(100);
        var callback = () => Task.CompletedTask;

        // When (행동)
        var timerId = stageSender.AddRepeatTimer(initialDelay, period, callback);

        // Then (결과)
        timerId.Should().BeGreaterThan(0, "유효한 timerId가 반환되어야 함");
        stageSender.RepeatTimers.Should().ContainSingle();
        stageSender.RepeatTimers[0].InitialDelay.Should().Be(initialDelay);
        stageSender.RepeatTimers[0].Period.Should().Be(period);
    }

    [Fact(DisplayName = "CountTimer 등록 시 timerId 반환")]
    public void AddCountTimer_ReturnsTimerId()
    {
        // Given (전제조건)
        var stageSender = new FakeStageSender { StageId = 1 };
        var initialDelay = TimeSpan.FromMilliseconds(100);
        var period = TimeSpan.FromMilliseconds(100);
        var count = 3;
        var callback = () => Task.CompletedTask;

        // When (행동)
        var timerId = stageSender.AddCountTimer(initialDelay, period, count, callback);

        // Then (결과)
        timerId.Should().BeGreaterThan(0, "유효한 timerId가 반환되어야 함");
        stageSender.CountTimers.Should().ContainSingle();
        stageSender.CountTimers[0].InitialDelay.Should().Be(initialDelay);
        stageSender.CountTimers[0].Period.Should().Be(period);
        stageSender.CountTimers[0].Count.Should().Be(count);
    }

    [Fact(DisplayName = "CancelTimer 호출 시 타이머 취소 기록")]
    public void CancelTimer_RecordsCancellation()
    {
        // Given (전제조건)
        var stageSender = new FakeStageSender { StageId = 1 };
        var timerId = stageSender.AddRepeatTimer(
            TimeSpan.FromMilliseconds(100),
            TimeSpan.FromMilliseconds(100),
            () => Task.CompletedTask
        );

        // When (행동)
        stageSender.CancelTimer(timerId);

        // Then (결과)
        stageSender.CancelledTimers.Should().ContainSingle();
        stageSender.CancelledTimers[0].Should().Be(timerId);
    }

    [Fact(DisplayName = "HasTimer - 등록된 타이머 확인")]
    public void HasTimer_ExistingTimer_ReturnsTrue()
    {
        // Given (전제조건)
        var stageSender = new FakeStageSender { StageId = 1 };
        var timerId = stageSender.AddRepeatTimer(
            TimeSpan.FromMilliseconds(100),
            TimeSpan.FromMilliseconds(100),
            () => Task.CompletedTask
        );

        // When & Then (행동 및 결과)
        stageSender.HasTimer(timerId).Should().BeTrue("등록된 타이머가 존재해야 함");
    }

    [Fact(DisplayName = "HasTimer - 취소된 타이머는 false 반환")]
    public void HasTimer_CancelledTimer_ReturnsFalse()
    {
        // Given (전제조건)
        var stageSender = new FakeStageSender { StageId = 1 };
        var timerId = stageSender.AddRepeatTimer(
            TimeSpan.FromMilliseconds(100),
            TimeSpan.FromMilliseconds(100),
            () => Task.CompletedTask
        );

        // When (행동)
        stageSender.CancelTimer(timerId);

        // Then (결과)
        stageSender.HasTimer(timerId).Should().BeFalse("취소된 타이머는 존재하지 않아야 함");
    }

    #endregion

    #region 2. 응답 데이터 검증 (Response Validation)

    [Fact(DisplayName = "여러 타이머 등록 시 각각 고유한 timerId 반환")]
    public void AddMultipleTimers_ReturnsUniqueTimerIds()
    {
        // Given (전제조건)
        var stageSender = new FakeStageSender { StageId = 1 };
        var callback = () => Task.CompletedTask;

        // When (행동)
        var timerId1 = stageSender.AddRepeatTimer(TimeSpan.FromMilliseconds(100), TimeSpan.FromMilliseconds(100), callback);
        var timerId2 = stageSender.AddRepeatTimer(TimeSpan.FromMilliseconds(200), TimeSpan.FromMilliseconds(200), callback);
        var timerId3 = stageSender.AddCountTimer(TimeSpan.FromMilliseconds(100), TimeSpan.FromMilliseconds(100), 3, callback);

        // Then (결과)
        var timerIds = new[] { timerId1, timerId2, timerId3 };
        timerIds.Should().OnlyHaveUniqueItems("모든 timerId는 고유해야 함");
        timerIds.Should().AllSatisfy(id => id.Should().BeGreaterThan(0));
    }

    [Fact(DisplayName = "타이머 등록 시 콜백이 올바르게 저장됨")]
    public void AddTimer_CallbackIsStoredCorrectly()
    {
        // Given (전제조건)
        var stageSender = new FakeStageSender { StageId = 1 };
        var callbackExecuted = false;
        var callback = () =>
        {
            callbackExecuted = true;
            return Task.CompletedTask;
        };

        // When (행동)
        stageSender.AddRepeatTimer(TimeSpan.Zero, TimeSpan.FromMilliseconds(100), callback);

        // Then (결과)
        stageSender.RepeatTimers.Should().ContainSingle();

        // 콜백 실행 테스트
        var storedCallback = stageSender.RepeatTimers[0].Callback;
        storedCallback.Invoke().Wait();
        callbackExecuted.Should().BeTrue("저장된 콜백이 실행되어야 함");
    }

    #endregion

    #region 3. 입력 파라미터 검증 (Input Validation)

    [Fact(DisplayName = "RepeatTimer - 다양한 시간 간격 설정 가능")]
    public void AddRepeatTimer_VariousIntervals_AcceptedCorrectly()
    {
        // Given (전제조건)
        var stageSender = new FakeStageSender { StageId = 1 };
        var callback = () => Task.CompletedTask;

        // When (행동)
        var timer1 = stageSender.AddRepeatTimer(TimeSpan.Zero, TimeSpan.FromMilliseconds(10), callback);
        var timer2 = stageSender.AddRepeatTimer(TimeSpan.FromMilliseconds(50), TimeSpan.FromMilliseconds(100), callback);
        var timer3 = stageSender.AddRepeatTimer(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1), callback);

        // Then (결과)
        stageSender.RepeatTimers.Should().HaveCount(3);
        stageSender.RepeatTimers[0].InitialDelay.Should().Be(TimeSpan.Zero);
        stageSender.RepeatTimers[1].InitialDelay.Should().Be(TimeSpan.FromMilliseconds(50));
        stageSender.RepeatTimers[2].InitialDelay.Should().Be(TimeSpan.FromSeconds(1));
    }

    [Fact(DisplayName = "CountTimer - 다양한 count 값 설정 가능")]
    public void AddCountTimer_VariousCounts_AcceptedCorrectly()
    {
        // Given (전제조건)
        var stageSender = new FakeStageSender { StageId = 1 };
        var callback = () => Task.CompletedTask;

        // When (행동)
        stageSender.AddCountTimer(TimeSpan.Zero, TimeSpan.FromMilliseconds(100), 1, callback);
        stageSender.AddCountTimer(TimeSpan.Zero, TimeSpan.FromMilliseconds(100), 3, callback);
        stageSender.AddCountTimer(TimeSpan.Zero, TimeSpan.FromMilliseconds(100), 10, callback);

        // Then (결과)
        stageSender.CountTimers.Should().HaveCount(3);
        stageSender.CountTimers[0].Count.Should().Be(1);
        stageSender.CountTimers[1].Count.Should().Be(3);
        stageSender.CountTimers[2].Count.Should().Be(10);
    }

    #endregion

    #region 4. 엣지 케이스 (Edge Cases)

    [Fact(DisplayName = "여러 타이머 동시 취소 가능")]
    public void CancelTimer_MultipleTimers_AllCancelled()
    {
        // Given (전제조건)
        var stageSender = new FakeStageSender { StageId = 1 };
        var callback = () => Task.CompletedTask;
        var timerId1 = stageSender.AddRepeatTimer(TimeSpan.Zero, TimeSpan.FromMilliseconds(100), callback);
        var timerId2 = stageSender.AddRepeatTimer(TimeSpan.Zero, TimeSpan.FromMilliseconds(100), callback);
        var timerId3 = stageSender.AddRepeatTimer(TimeSpan.Zero, TimeSpan.FromMilliseconds(100), callback);

        // When (행동)
        stageSender.CancelTimer(timerId1);
        stageSender.CancelTimer(timerId2);
        stageSender.CancelTimer(timerId3);

        // Then (결과)
        stageSender.CancelledTimers.Should().HaveCount(3);
        stageSender.HasTimer(timerId1).Should().BeFalse();
        stageSender.HasTimer(timerId2).Should().BeFalse();
        stageSender.HasTimer(timerId3).Should().BeFalse();
    }

    [Fact(DisplayName = "존재하지 않는 timerId 취소 시도 - 무시됨")]
    public void CancelTimer_NonExistentTimerId_Ignored()
    {
        // Given (전제조건)
        var stageSender = new FakeStageSender { StageId = 1 };
        var nonExistentTimerId = 999L;

        // When (행동)
        stageSender.CancelTimer(nonExistentTimerId);

        // Then (결과)
        stageSender.CancelledTimers.Should().ContainSingle();
        stageSender.CancelledTimers[0].Should().Be(nonExistentTimerId);
    }

    [Fact(DisplayName = "동일한 타이머를 여러 번 취소 시도 - 모두 기록됨")]
    public void CancelTimer_SameTimerMultipleTimes_AllRecorded()
    {
        // Given (전제조건)
        var stageSender = new FakeStageSender { StageId = 1 };
        var timerId = stageSender.AddRepeatTimer(
            TimeSpan.Zero,
            TimeSpan.FromMilliseconds(100),
            () => Task.CompletedTask
        );

        // When (행동)
        stageSender.CancelTimer(timerId);
        stageSender.CancelTimer(timerId);
        stageSender.CancelTimer(timerId);

        // Then (결과)
        stageSender.CancelledTimers.Should().HaveCount(3, "모든 취소 시도가 기록됨");
        stageSender.HasTimer(timerId).Should().BeFalse();
    }

    [Fact(DisplayName = "Reset 후 타이머 상태 초기화")]
    public void Reset_ClearsAllTimerState()
    {
        // Given (전제조건)
        var stageSender = new FakeStageSender { StageId = 1 };
        var callback = () => Task.CompletedTask;
        var timerId1 = stageSender.AddRepeatTimer(TimeSpan.Zero, TimeSpan.FromMilliseconds(100), callback);
        var timerId2 = stageSender.AddCountTimer(TimeSpan.Zero, TimeSpan.FromMilliseconds(100), 3, callback);
        stageSender.CancelTimer(timerId1);

        // When (행동)
        stageSender.Reset();

        // Then (결과)
        stageSender.RepeatTimers.Should().BeEmpty("Reset 후 RepeatTimers가 비어야 함");
        stageSender.CountTimers.Should().BeEmpty("Reset 후 CountTimers가 비어야 함");
        stageSender.CancelledTimers.Should().BeEmpty("Reset 후 CancelledTimers가 비어야 함");
    }

    #endregion

    #region 5. 실무 활용 예제 (Usage Examples)

    [Fact(DisplayName = "실무 예제: 게임 틱 타이머 (RepeatTimer)")]
    public void UsageExample_GameTickTimer()
    {
        // Given (전제조건)
        var stageSender = new FakeStageSender { StageId = 1, StageType = "BattleRoom" };
        var tickCount = 0;
        var gameTickCallback = () =>
        {
            tickCount++;
            // 게임 로직 업데이트 (위치, 충돌 등)
            return Task.CompletedTask;
        };

        // When (행동) - 100ms마다 게임 틱 실행
        var tickTimerId = stageSender.AddRepeatTimer(
            TimeSpan.Zero,
            TimeSpan.FromMilliseconds(100),
            gameTickCallback
        );

        // Then (결과)
        stageSender.RepeatTimers.Should().ContainSingle();
        tickTimerId.Should().BeGreaterThan(0);

        // 시뮬레이션: 콜백 3회 실행
        for (int i = 0; i < 3; i++)
        {
            stageSender.RepeatTimers[0].Callback.Invoke().Wait();
        }
        tickCount.Should().Be(3, "게임 틱이 3회 실행되어야 함");
    }

    [Fact(DisplayName = "실무 예제: 카운트다운 타이머 (CountTimer)")]
    public void UsageExample_CountdownTimer()
    {
        // Given (전제조건)
        var stageSender = new FakeStageSender { StageId = 1, StageType = "LobbyRoom" };
        var countdown = 3;
        var countdownCallback = () =>
        {
            countdown--;
            // 카운트다운 UI 업데이트
            return Task.CompletedTask;
        };

        // When (행동) - 3초 카운트다운 (1초 간격, 3회)
        var countdownTimerId = stageSender.AddCountTimer(
            TimeSpan.FromSeconds(1),
            TimeSpan.FromSeconds(1),
            3,
            countdownCallback
        );

        // Then (결과)
        stageSender.CountTimers.Should().ContainSingle();
        stageSender.CountTimers[0].Count.Should().Be(3);

        // 시뮬레이션: 콜백 3회 실행
        for (int i = 0; i < 3; i++)
        {
            stageSender.CountTimers[0].Callback.Invoke().Wait();
        }
        countdown.Should().Be(0, "카운트다운이 0이 되어야 함");
    }

    [Fact(DisplayName = "실무 예제: 버프 지속 시간 타이머")]
    public void UsageExample_BuffDurationTimer()
    {
        // Given (전제조건)
        var stageSender = new FakeStageSender { StageId = 1, StageType = "BattleRoom" };
        var buffActive = true;
        var buffExpireCallback = () =>
        {
            buffActive = false;
            // 버프 제거 로직
            return Task.CompletedTask;
        };

        // When (행동) - 5초 후 버프 제거 (1회만 실행)
        var buffTimerId = stageSender.AddCountTimer(
            TimeSpan.FromSeconds(5),
            TimeSpan.Zero,
            1,
            buffExpireCallback
        );

        // Then (결과)
        stageSender.CountTimers.Should().ContainSingle();
        buffActive.Should().BeTrue("버프가 활성화 상태여야 함");

        // 시뮬레이션: 타이머 만료
        stageSender.CountTimers[0].Callback.Invoke().Wait();
        buffActive.Should().BeFalse("버프가 제거되어야 함");
    }

    [Fact(DisplayName = "실무 예제: 여러 타이머 관리 및 정리")]
    public void UsageExample_ManageMultipleTimers()
    {
        // Given (전제조건)
        var stageSender = new FakeStageSender { StageId = 1, StageType = "GameStage" };
        var callback = () => Task.CompletedTask;

        // When (행동) - 여러 타이머 등록
        var tickTimerId = stageSender.AddRepeatTimer(TimeSpan.Zero, TimeSpan.FromMilliseconds(100), callback);
        var spawnTimerId = stageSender.AddRepeatTimer(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5), callback);
        var gameTimerId = stageSender.AddCountTimer(TimeSpan.Zero, TimeSpan.FromSeconds(1), 60, callback); // 60초 게임

        // Then (결과)
        stageSender.RepeatTimers.Should().HaveCount(2);
        stageSender.CountTimers.Should().HaveCount(1);

        // When (행동) - 게임 종료 시 모든 타이머 정리
        stageSender.CancelTimer(tickTimerId);
        stageSender.CancelTimer(spawnTimerId);
        stageSender.CancelTimer(gameTimerId);

        // Then (결과)
        stageSender.CancelledTimers.Should().HaveCount(3, "모든 타이머가 취소되어야 함");
        stageSender.HasTimer(tickTimerId).Should().BeFalse();
        stageSender.HasTimer(spawnTimerId).Should().BeFalse();
        stageSender.HasTimer(gameTimerId).Should().BeFalse();
    }

    #endregion
}
