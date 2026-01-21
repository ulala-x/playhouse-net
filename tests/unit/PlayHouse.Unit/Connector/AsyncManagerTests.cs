#nullable enable

using FluentAssertions;
using PlayHouse.Connector.Internal;
using Xunit;

namespace PlayHouse.Unit.Connector;

/// <summary>
/// 단위 테스트: AsyncManager의 메인 스레드 콜백 관리 기능 검증
/// </summary>
public class AsyncManagerTests
{
    [Fact(DisplayName = "AddJob - 작업을 큐에 추가한다")]
    public void AddJob_AddsActionToQueue()
    {
        // Given (전제조건)
        var manager = new AsyncManager();

        // When (행동)
        manager.AddJob(() => { /* 빈 작업 */ });

        // Then (결과)
        manager.PendingCount.Should().Be(1, "작업이 큐에 추가되어야 함");
    }

    [Fact(DisplayName = "MainThreadAction - 큐의 모든 작업을 실행한다")]
    public void MainThreadAction_ExecutesAllQueuedActions()
    {
        // Given (전제조건)
        var manager = new AsyncManager();
        var counter = 0;
        manager.AddJob(() => counter++);
        manager.AddJob(() => counter++);
        manager.AddJob(() => counter++);

        // When (행동)
        manager.MainThreadAction();

        // Then (결과)
        counter.Should().Be(3, "3개의 작업이 모두 실행되어야 함");
        manager.PendingCount.Should().Be(0, "실행 후 큐가 비어있어야 함");
    }

    [Fact(DisplayName = "MainThreadAction - 빈 큐에서 호출해도 예외가 발생하지 않는다")]
    public void MainThreadAction_EmptyQueue_NoException()
    {
        // Given (전제조건)
        var manager = new AsyncManager();

        // When (행동)
        var action = () => manager.MainThreadAction();

        // Then (결과)
        action.Should().NotThrow("빈 큐에서 호출해도 예외가 없어야 함");
    }

    [Fact(DisplayName = "MainThreadAction - 작업 실행 중 예외가 발생해도 나머지 작업은 실행된다")]
    public void MainThreadAction_ExceptionInAction_ContinuesProcessing()
    {
        // Given (전제조건)
        var manager = new AsyncManager();
        var beforeException = false;
        var afterException = false;

        manager.AddJob(() => beforeException = true);
        manager.AddJob(() => throw new InvalidOperationException("테스트 예외"));
        manager.AddJob(() => afterException = true);

        // When (행동)
        var action = () => manager.MainThreadAction();

        // Then (결과)
        action.Should().NotThrow("예외가 있어도 다른 작업 처리 중 예외가 전파되지 않아야 함");
        beforeException.Should().BeTrue("예외 전 작업이 실행되어야 함");
        afterException.Should().BeTrue("예외 후 작업도 실행되어야 함");
    }

    [Fact(DisplayName = "AddJob - 작업 실행 순서가 FIFO를 따른다")]
    public void AddJob_ExecutionOrder_IsFIFO()
    {
        // Given (전제조건)
        var manager = new AsyncManager();
        var order = new List<int>();

        manager.AddJob(() => order.Add(1));
        manager.AddJob(() => order.Add(2));
        manager.AddJob(() => order.Add(3));

        // When (행동)
        manager.MainThreadAction();

        // Then (결과)
        order.Should().Equal(new[] { 1, 2, 3 }, "FIFO 순서로 실행되어야 함");
    }

    [Fact(DisplayName = "동시성 테스트 - 여러 스레드에서 AddJob 호출")]
    public async Task AddJob_ConcurrentCalls_AllJobsQueued()
    {
        // Given (전제조건)
        var manager = new AsyncManager();
        var counter = 0;
        const int threadCount = 100;
        var startSignal = new ManualResetEventSlim(false);

        // When (행동)
        var tasks = Enumerable.Range(0, threadCount).Select(_ => Task.Run(() =>
        {
            startSignal.Wait();
            manager.AddJob(() => Interlocked.Increment(ref counter));
        })).ToArray();

        startSignal.Set();
        await Task.WhenAll(tasks);
        manager.MainThreadAction();

        // Then (결과)
        counter.Should().Be(threadCount, $"{threadCount}개의 작업이 모두 실행되어야 함");
    }

    [Fact(DisplayName = "PendingCount - 대기 중인 작업 수를 정확히 반환한다")]
    public void PendingCount_ReturnsCorrectCount()
    {
        // Given (전제조건)
        var manager = new AsyncManager();

        // When (행동)
        manager.AddJob(() => { });
        manager.AddJob(() => { });
        var countAfterAdd = manager.PendingCount;
        manager.MainThreadAction();
        var countAfterExecute = manager.PendingCount;

        // Then (결과)
        countAfterAdd.Should().Be(2, "2개의 작업이 대기 중이어야 함");
        countAfterExecute.Should().Be(0, "실행 후 대기 작업이 없어야 함");
    }
}
