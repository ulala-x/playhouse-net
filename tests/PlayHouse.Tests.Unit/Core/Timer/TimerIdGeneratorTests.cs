#nullable enable

using PlayHouse.Core.Timer;
using FluentAssertions;
using Xunit;

namespace PlayHouse.Tests.Unit.Core.Timer;

/// <summary>
/// 단위 테스트: TimerIdGenerator의 고유 ID 생성 및 스레드 안전성 검증
/// </summary>
public class TimerIdGeneratorTests
{
    public TimerIdGeneratorTests()
    {
        // 각 테스트 전에 카운터 리셋
        TimerIdGenerator.Reset();
    }

    [Fact(DisplayName = "Generate - 첫 번째 호출 시 1을 반환")]
    public void Generate_FirstCall_ReturnsOne()
    {
        // Given (전제조건)
        TimerIdGenerator.Reset();

        // When (행동)
        var id = TimerIdGenerator.Generate();

        // Then (결과)
        id.Should().Be(1, "첫 번째 타이머 ID는 1이어야 함");
    }

    [Fact(DisplayName = "Generate - 연속 호출 시 순차적으로 증가")]
    public void Generate_SequentialCalls_ReturnsIncrementingValues()
    {
        // Given (전제조건)
        TimerIdGenerator.Reset();

        // When (행동)
        var id1 = TimerIdGenerator.Generate();
        var id2 = TimerIdGenerator.Generate();
        var id3 = TimerIdGenerator.Generate();

        // Then (결과)
        id1.Should().Be(1);
        id2.Should().Be(2);
        id3.Should().Be(3);
    }

    [Fact(DisplayName = "Generate - 모든 ID가 고유함")]
    public void Generate_MultipleGenerations_AllIdsAreUnique()
    {
        // Given (전제조건)
        TimerIdGenerator.Reset();
        const int count = 100;
        var ids = new List<long>();

        // When (행동)
        for (int i = 0; i < count; i++)
        {
            ids.Add(TimerIdGenerator.Generate());
        }

        // Then (결과)
        ids.Should().OnlyHaveUniqueItems("모든 타이머 ID는 고유해야 함");
        ids.Should().BeInAscendingOrder("타이머 ID는 단조 증가해야 함");
    }

    [Fact(DisplayName = "GetCurrentCount - 생성된 타이머 수를 정확히 반환")]
    public void GetCurrentCount_ReturnsCorrectValue()
    {
        // Given (전제조건)
        TimerIdGenerator.Reset();

        // When & Then (행동 및 결과)
        TimerIdGenerator.GetCurrentCount().Should().Be(0, "리셋 직후 카운트는 0이어야 함");

        TimerIdGenerator.Generate();
        TimerIdGenerator.GetCurrentCount().Should().Be(1);

        TimerIdGenerator.Generate();
        TimerIdGenerator.Generate();
        TimerIdGenerator.GetCurrentCount().Should().Be(3);
    }

    [Fact(DisplayName = "Reset - 카운터를 0으로 초기화")]
    public void Reset_ResetsCounterToZero()
    {
        // Given (전제조건)
        TimerIdGenerator.Generate();
        TimerIdGenerator.Generate();
        TimerIdGenerator.GetCurrentCount().Should().Be(2);

        // When (행동)
        TimerIdGenerator.Reset();

        // Then (결과)
        TimerIdGenerator.GetCurrentCount().Should().Be(0, "리셋 후 카운터는 0이어야 함");

        // 리셋 후 다시 1부터 시작
        var firstId = TimerIdGenerator.Generate();
        firstId.Should().Be(1, "리셋 후 첫 ID는 1이어야 함");
    }

    [Fact(DisplayName = "동시성 테스트 - 여러 스레드에서 동시 호출 시 모든 ID가 고유함")]
    public async Task Generate_ConcurrentCalls_AllIdsAreUnique()
    {
        // Given (전제조건)
        TimerIdGenerator.Reset();
        const int threadCount = 10;
        const int idsPerThread = 100;
        var allIds = new System.Collections.Concurrent.ConcurrentBag<long>();
        var tasks = new List<Task>();

        // When (행동)
        for (int i = 0; i < threadCount; i++)
        {
            tasks.Add(Task.Run(() =>
            {
                for (int j = 0; j < idsPerThread; j++)
                {
                    var id = TimerIdGenerator.Generate();
                    allIds.Add(id);
                }
            }));
        }

        await Task.WhenAll(tasks);

        // Then (결과)
        var idList = allIds.ToList();
        idList.Should().HaveCount(threadCount * idsPerThread, "모든 ID가 생성되어야 함");
        idList.Should().OnlyHaveUniqueItems("동시 호출 시에도 모든 ID는 고유해야 함");
        idList.Min().Should().Be(1, "최소 ID는 1이어야 함");
        idList.Max().Should().Be(threadCount * idsPerThread, "최대 ID는 총 생성 수와 같아야 함");
    }

    [Fact(DisplayName = "동시성 테스트 - Generate와 GetCurrentCount 동시 호출")]
    public async Task ConcurrentGenerateAndGetCount_ThreadSafe()
    {
        // Given (전제조건)
        TimerIdGenerator.Reset();
        const int iterations = 1000;
        var tasks = new List<Task>();
        var ids = new System.Collections.Concurrent.ConcurrentBag<long>();

        // When (행동)
        // Task 1: Generate 반복
        tasks.Add(Task.Run(() =>
        {
            for (int i = 0; i < iterations; i++)
            {
                ids.Add(TimerIdGenerator.Generate());
            }
        }));

        // Task 2: GetCurrentCount 반복 (읽기 작업)
        tasks.Add(Task.Run(() =>
        {
            for (int i = 0; i < iterations; i++)
            {
                _ = TimerIdGenerator.GetCurrentCount();
            }
        }));

        await Task.WhenAll(tasks);

        // Then (결과)
        var idList = ids.ToList();
        idList.Should().HaveCount(iterations);
        idList.Should().OnlyHaveUniqueItems("동시 읽기가 있어도 모든 ID는 고유해야 함");
        TimerIdGenerator.GetCurrentCount().Should().Be(iterations);
    }

    [Fact(DisplayName = "대량 생성 테스트 - 10,000개 ID 생성")]
    public void Generate_LargeScale_HandlesThousandsOfIds()
    {
        // Given (전제조건)
        TimerIdGenerator.Reset();
        const int count = 10_000;
        var ids = new List<long>(count);

        // When (행동)
        for (int i = 0; i < count; i++)
        {
            ids.Add(TimerIdGenerator.Generate());
        }

        // Then (결과)
        ids.Should().HaveCount(count);
        ids.Should().OnlyHaveUniqueItems();
        ids[0].Should().Be(1);
        ids[^1].Should().Be(count);
        TimerIdGenerator.GetCurrentCount().Should().Be(count);
    }

    [Fact(DisplayName = "스레드 안전성 테스트 - Reset과 Generate 동시 호출")]
    public async Task ConcurrentResetAndGenerate_ThreadSafe()
    {
        // Given (전제조건)
        const int iterations = 100;
        var tasks = new List<Task>();
        var ids = new System.Collections.Concurrent.ConcurrentBag<long>();

        // When (행동)
        // Task 1: Generate 반복
        tasks.Add(Task.Run(() =>
        {
            for (int i = 0; i < iterations; i++)
            {
                ids.Add(TimerIdGenerator.Generate());
                Thread.Sleep(1); // 약간의 지연
            }
        }));

        // Task 2: 주기적으로 Reset 호출
        tasks.Add(Task.Run(() =>
        {
            for (int i = 0; i < 5; i++)
            {
                Thread.Sleep(10);
                TimerIdGenerator.Reset();
            }
        }));

        await Task.WhenAll(tasks);

        // Then (결과)
        // Reset이 있어도 데드락이나 예외가 발생하지 않아야 함
        ids.Should().NotBeEmpty("일부 ID가 생성되어야 함");
        // 모든 ID는 양수여야 함
        ids.Should().AllSatisfy(id => id.Should().BeGreaterThan(0));
    }
}
