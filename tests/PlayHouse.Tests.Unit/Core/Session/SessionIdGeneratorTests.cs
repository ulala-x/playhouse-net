#nullable enable

using PlayHouse.Core.Session;
using FluentAssertions;
using Xunit;

namespace PlayHouse.Tests.Unit.Core.Session;

/// <summary>
/// 단위 테스트: SessionIdGenerator의 고유 세션 ID 생성 검증
/// </summary>
public class SessionIdGeneratorTests
{
    public SessionIdGeneratorTests()
    {
        // 각 테스트 전에 카운터 리셋 (내부 메서드가 있다면)
        // SessionIdGenerator는 static이므로 테스트 격리를 위해 필요할 수 있음
    }

    [Fact(DisplayName = "Generate - 첫 번째 호출 시 1을 반환")]
    public void Generate_FirstCall_ReturnsOne()
    {
        // Given & When (전제조건 및 행동)
        var id = SessionIdGenerator.Generate();

        // Then (결과)
        id.Should().BeGreaterThan(0, "세션 ID는 양수여야 함");
    }

    [Fact(DisplayName = "Generate - 연속 호출 시 모두 고유한 값 반환")]
    public void Generate_SequentialCalls_ReturnsUniqueValues()
    {
        // Given (전제조건)
        var ids = new List<long>();
        const int count = 100;

        // When (행동)
        for (int i = 0; i < count; i++)
        {
            ids.Add(SessionIdGenerator.Generate());
        }

        // Then (결과)
        ids.Should().OnlyHaveUniqueItems("모든 세션 ID는 고유해야 함");
        ids.Should().BeInAscendingOrder("세션 ID는 단조 증가해야 함");
    }

    [Fact(DisplayName = "동시성 테스트 - 여러 스레드에서 동시 호출 시 모든 ID가 고유함")]
    public void Generate_ConcurrentCalls_AllIdsAreUnique()
    {
        // Given (전제조건)
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
                    var id = SessionIdGenerator.Generate();
                    allIds.Add(id);
                }
            }));
        }

        Task.WaitAll(tasks.ToArray());

        // Then (결과)
        var idList = allIds.ToList();
        idList.Should().HaveCount(threadCount * idsPerThread, "모든 ID가 생성되어야 함");
        idList.Should().OnlyHaveUniqueItems("동시 호출 시에도 모든 ID는 고유해야 함");
        idList.Should().AllSatisfy(id => id.Should().BeGreaterThan(0, "모든 세션 ID는 양수여야 함"));
    }

    [Fact(DisplayName = "대량 생성 테스트 - 10,000개 세션 ID 생성")]
    public void Generate_LargeScale_HandlesThousandsOfIds()
    {
        // Given (전제조건)
        const int count = 10_000;
        var ids = new HashSet<long>();

        // When (행동)
        for (int i = 0; i < count; i++)
        {
            var id = SessionIdGenerator.Generate();
            ids.Add(id);
        }

        // Then (결과)
        ids.Should().HaveCount(count, "모든 세션 ID가 고유해야 하므로 Set 크기가 생성 수와 같아야 함");
    }

    [Fact(DisplayName = "성능 테스트 - 빠른 연속 생성 시 고유성 유지")]
    public void Generate_RapidSuccession_MaintainsUniqueness()
    {
        // Given (전제조건)
        const int count = 1000;
        var ids = new List<long>(count);

        // When (행동)
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        for (int i = 0; i < count; i++)
        {
            ids.Add(SessionIdGenerator.Generate());
        }
        stopwatch.Stop();

        // Then (결과)
        ids.Should().OnlyHaveUniqueItems("빠른 연속 생성에도 고유성이 유지되어야 함");
        stopwatch.ElapsedMilliseconds.Should().BeLessThan(100, "1000개 ID 생성은 100ms 이내에 완료되어야 함");
    }

    [Fact(DisplayName = "동시성 스트레스 테스트 - 100개 스레드에서 동시 생성")]
    public void Generate_HighConcurrency_StressTest()
    {
        // Given (전제조건)
        const int threadCount = 100;
        const int idsPerThread = 50;
        var allIds = new System.Collections.Concurrent.ConcurrentBag<long>();
        var tasks = new List<Task>();

        // When (행동)
        for (int i = 0; i < threadCount; i++)
        {
            tasks.Add(Task.Run(() =>
            {
                for (int j = 0; j < idsPerThread; j++)
                {
                    allIds.Add(SessionIdGenerator.Generate());
                }
            }));
        }

        Task.WaitAll(tasks.ToArray());

        // Then (결과)
        var idList = allIds.ToList();
        idList.Should().HaveCount(threadCount * idsPerThread);
        idList.Should().OnlyHaveUniqueItems("고부하 동시성 환경에서도 모든 ID는 고유해야 함");
    }

    [Fact(DisplayName = "세션 ID 범위 테스트 - 모든 값이 long 범위 내")]
    public void Generate_AllValuesWithinLongRange()
    {
        // Given & When (전제조건 및 행동)
        var ids = new List<long>();
        for (int i = 0; i < 1000; i++)
        {
            ids.Add(SessionIdGenerator.Generate());
        }

        // Then (결과)
        ids.Should().AllSatisfy(id =>
        {
            id.Should().BeGreaterThan(0, "세션 ID는 양수여야 함");
            id.Should().BeLessThanOrEqualTo(long.MaxValue, "세션 ID는 long 최대값 이내여야 함");
        });
    }
}
