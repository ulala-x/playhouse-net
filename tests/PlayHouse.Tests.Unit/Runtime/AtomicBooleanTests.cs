#nullable enable

using FluentAssertions;
using PlayHouse.Runtime;
using Xunit;

namespace PlayHouse.Tests.Unit.Runtime;

/// <summary>
/// 단위 테스트: AtomicBoolean의 스레드 안전 연산 검증
/// </summary>
public class AtomicBooleanTests
{
    [Fact(DisplayName = "기본 생성자는 false로 초기화한다")]
    public void DefaultConstructor_InitializesToFalse()
    {
        // Given (전제조건)
        // When (행동)
        var atomic = new AtomicBoolean();

        // Then (결과)
        atomic.Value.Should().BeFalse("기본값은 false여야 함");
    }

    [Fact(DisplayName = "생성자에 true를 전달하면 true로 초기화된다")]
    public void Constructor_WithTrue_InitializesToTrue()
    {
        // Given (전제조건)
        // When (행동)
        var atomic = new AtomicBoolean(true);

        // Then (결과)
        atomic.Value.Should().BeTrue("생성자 인자가 true이면 초기값도 true여야 함");
    }

    [Fact(DisplayName = "Set(true)는 값을 true로 변경한다")]
    public void Set_True_ChangesValueToTrue()
    {
        // Given (전제조건)
        var atomic = new AtomicBoolean(false);

        // When (행동)
        atomic.Set(true);

        // Then (결과)
        atomic.Value.Should().BeTrue("Set(true) 호출 후 값이 true여야 함");
    }

    [Fact(DisplayName = "Set(false)는 값을 false로 변경한다")]
    public void Set_False_ChangesValueToFalse()
    {
        // Given (전제조건)
        var atomic = new AtomicBoolean(true);

        // When (행동)
        atomic.Set(false);

        // Then (결과)
        atomic.Value.Should().BeFalse("Set(false) 호출 후 값이 false여야 함");
    }

    [Fact(DisplayName = "CompareAndSet - 기대값이 일치하면 값을 변경하고 true를 반환한다")]
    public void CompareAndSet_ExpectedMatches_ChangesValueAndReturnsTrue()
    {
        // Given (전제조건)
        var atomic = new AtomicBoolean(false);

        // When (행동)
        var result = atomic.CompareAndSet(false, true);

        // Then (결과)
        result.Should().BeTrue("기대값이 일치하면 true를 반환해야 함");
        atomic.Value.Should().BeTrue("값이 새 값으로 변경되어야 함");
    }

    [Fact(DisplayName = "CompareAndSet - 기대값이 불일치하면 값을 유지하고 false를 반환한다")]
    public void CompareAndSet_ExpectedMismatch_KeepsValueAndReturnsFalse()
    {
        // Given (전제조건)
        var atomic = new AtomicBoolean(true);

        // When (행동)
        var result = atomic.CompareAndSet(false, true);

        // Then (결과)
        result.Should().BeFalse("기대값이 불일치하면 false를 반환해야 함");
        atomic.Value.Should().BeTrue("값이 변경되지 않아야 함");
    }

    [Fact(DisplayName = "GetAndSet - 이전 값을 반환하고 새 값으로 변경한다")]
    public void GetAndSet_ReturnsPreviousValue_AndSetsNewValue()
    {
        // Given (전제조건)
        var atomic = new AtomicBoolean(false);

        // When (행동)
        var previousValue = atomic.GetAndSet(true);

        // Then (결과)
        previousValue.Should().BeFalse("이전 값(false)을 반환해야 함");
        atomic.Value.Should().BeTrue("새 값(true)으로 변경되어야 함");
    }

    [Fact(DisplayName = "동시성 테스트 - 여러 스레드에서 CompareAndSet 경쟁")]
    public async Task CompareAndSet_ConcurrentRace_OnlyOneSucceeds()
    {
        // Given (전제조건)
        var atomic = new AtomicBoolean(false);
        var successCount = 0;
        const int threadCount = 100;
        var startSignal = new ManualResetEventSlim(false);

        // When (행동)
        var tasks = Enumerable.Range(0, threadCount).Select(_ => Task.Run(() =>
        {
            startSignal.Wait();
            if (atomic.CompareAndSet(false, true))
            {
                Interlocked.Increment(ref successCount);
            }
        })).ToArray();

        startSignal.Set();
        await Task.WhenAll(tasks);

        // Then (결과)
        successCount.Should().Be(1, "오직 하나의 스레드만 CAS 성공해야 함");
        atomic.Value.Should().BeTrue("최종 값은 true여야 함");
    }

    [Fact(DisplayName = "동시성 테스트 - 토글 연산이 정확히 수행된다")]
    public async Task ConcurrentToggle_CorrectFinalState()
    {
        // Given (전제조건)
        var atomic = new AtomicBoolean(false);
        const int toggleCount = 1000;
        var completedToggles = 0;

        // When (행동)
        var tasks = Enumerable.Range(0, toggleCount).Select(_ => Task.Run(() =>
        {
            bool toggled;
            do
            {
                var current = atomic.Value;
                toggled = atomic.CompareAndSet(current, !current);
            } while (!toggled);

            Interlocked.Increment(ref completedToggles);
        })).ToArray();

        await Task.WhenAll(tasks);

        // Then (결과)
        completedToggles.Should().Be(toggleCount, "모든 토글이 완료되어야 함");
        // 1000번 토글했으므로 최종 값은 false (짝수 번 토글)
        atomic.Value.Should().BeFalse("짝수 번 토글 후 값은 false여야 함");
    }
}
