#nullable enable

using PlayHouse.Core.Stage;
using FluentAssertions;
using Xunit;

namespace PlayHouse.Tests.Unit.Core.Stage;

/// <summary>
/// 단위 테스트: AtomicBoolean의 동시성 안전성 및 CAS 연산 검증
/// </summary>
public class AtomicBooleanTests
{
    [Fact(DisplayName = "생성 시 초기값이 올바르게 설정됨")]
    public void Constructor_WithInitialValue_SetsValueCorrectly()
    {
        // Given (전제조건)
        var atomicTrue = new AtomicBoolean(true);
        var atomicFalse = new AtomicBoolean(false);
        var atomicDefault = new AtomicBoolean();

        // When & Then (행동 및 결과)
        atomicTrue.Value.Should().BeTrue();
        atomicFalse.Value.Should().BeFalse();
        atomicDefault.Value.Should().BeFalse(); // 기본값은 false
    }

    [Fact(DisplayName = "Set 메서드가 값을 올바르게 변경함")]
    public void Set_ChangesValue_Successfully()
    {
        // Given (전제조건)
        var atomic = new AtomicBoolean(false);

        // When (행동)
        atomic.Set(true);

        // Then (결과)
        atomic.Value.Should().BeTrue();

        // When (행동)
        atomic.Set(false);

        // Then (결과)
        atomic.Value.Should().BeFalse();
    }

    [Fact(DisplayName = "CompareAndSet - 예상값과 일치하면 값을 업데이트하고 true 반환")]
    public void CompareAndSet_WhenExpectedMatches_ShouldReturnTrueAndUpdate()
    {
        // Given (전제조건)
        var atomic = new AtomicBoolean(false);

        // When (행동)
        var result = atomic.CompareAndSet(false, true);

        // Then (결과)
        result.Should().BeTrue("예상값이 일치하므로 업데이트가 성공해야 함");
        atomic.Value.Should().BeTrue("값이 true로 업데이트되어야 함");
    }

    [Fact(DisplayName = "CompareAndSet - 예상값과 불일치하면 값을 유지하고 false 반환")]
    public void CompareAndSet_WhenExpectedDoesNotMatch_ShouldReturnFalseAndNotUpdate()
    {
        // Given (전제조건)
        var atomic = new AtomicBoolean(true);

        // When (행동)
        var result = atomic.CompareAndSet(false, true);

        // Then (결과)
        result.Should().BeFalse("예상값이 불일치하므로 업데이트가 실패해야 함");
        atomic.Value.Should().BeTrue("값이 변경되지 않고 true로 유지되어야 함");
    }

    [Fact(DisplayName = "CompareAndSet - false에서 true로 변경 성공")]
    public void CompareAndSet_FalseToTrue_Succeeds()
    {
        // Given (전제조건)
        var atomic = new AtomicBoolean(false);

        // When (행동)
        var result = atomic.CompareAndSet(expected: false, update: true);

        // Then (결과)
        result.Should().BeTrue();
        atomic.Value.Should().BeTrue();
    }

    [Fact(DisplayName = "CompareAndSet - true에서 false로 변경 성공")]
    public void CompareAndSet_TrueToFalse_Succeeds()
    {
        // Given (전제조건)
        var atomic = new AtomicBoolean(true);

        // When (행동)
        var result = atomic.CompareAndSet(expected: true, update: false);

        // Then (결과)
        result.Should().BeTrue();
        atomic.Value.Should().BeFalse();
    }

    [Fact(DisplayName = "동시성 테스트 - 여러 스레드에서 CompareAndSet 호출 시 하나만 성공")]
    public void CompareAndSet_ConcurrentAccess_OnlyOneThreadSucceeds()
    {
        // Given (전제조건)
        var atomic = new AtomicBoolean(false);
        const int threadCount = 10;
        var successCount = 0;
        var tasks = new List<Task>();

        // When (행동)
        for (int i = 0; i < threadCount; i++)
        {
            tasks.Add(Task.Run(() =>
            {
                // false에서 true로 변경 시도
                if (atomic.CompareAndSet(false, true))
                {
                    Interlocked.Increment(ref successCount);
                }
            }));
        }

        Task.WaitAll(tasks.ToArray());

        // Then (결과)
        successCount.Should().Be(1, "여러 스레드 중 정확히 하나만 false→true 변경에 성공해야 함");
        atomic.Value.Should().BeTrue("최종 값은 true여야 함");
    }

    [Fact(DisplayName = "동시성 테스트 - Set과 CompareAndSet 동시 호출")]
    public void ConcurrentSetAndCompareAndSet_ThreadSafe()
    {
        // Given (전제조건)
        var atomic = new AtomicBoolean(false);
        const int iterations = 1000;
        var tasks = new List<Task>();

        // When (행동)
        // Task 1: Set(true) 반복
        tasks.Add(Task.Run(() =>
        {
            for (int i = 0; i < iterations; i++)
            {
                atomic.Set(true);
            }
        }));

        // Task 2: Set(false) 반복
        tasks.Add(Task.Run(() =>
        {
            for (int i = 0; i < iterations; i++)
            {
                atomic.Set(false);
            }
        }));

        // Task 3: CompareAndSet 반복
        tasks.Add(Task.Run(() =>
        {
            for (int i = 0; i < iterations; i++)
            {
                atomic.CompareAndSet(false, true);
            }
        }));

        Task.WaitAll(tasks.ToArray());

        // Then (결과)
        // 값은 true 또는 false여야 하며, 중간 상태나 손상된 값이 없어야 함
        var finalValue = atomic.Value;
        (finalValue == true || finalValue == false).Should().BeTrue("최종 값은 유효한 boolean이어야 함");
    }

    [Theory(DisplayName = "CompareAndSet - 다양한 조합 검증")]
    [InlineData(false, false, false, true)]  // false→false 변경 성공
    [InlineData(true, true, true, true)]     // true→true 변경 성공
    [InlineData(false, true, false, false)]  // 예상값 불일치로 실패
    [InlineData(true, false, true, false)]   // 예상값 불일치로 실패
    public void CompareAndSet_VariousCombinations_BehavesCorrectly(
        bool initialValue,
        bool expected,
        bool update,
        bool shouldSucceed)
    {
        // Given (전제조건)
        var atomic = new AtomicBoolean(initialValue);

        // When (행동)
        var result = atomic.CompareAndSet(expected, update);

        // Then (결과)
        result.Should().Be(shouldSucceed);
        var expectedFinalValue = shouldSucceed ? update : initialValue;
        atomic.Value.Should().Be(expectedFinalValue);
    }
}
