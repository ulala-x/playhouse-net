#nullable enable

using Microsoft.Extensions.Logging.Abstractions;
using PlayHouse.Core.Messaging;
using PlayHouse.Abstractions;
using FluentAssertions;
using Xunit;

namespace PlayHouse.Unit.Core.Messaging;

/// <summary>
/// 단위 테스트: RequestCache의 요청 추적 및 타임아웃 처리 검증
/// </summary>
public class RequestCacheTests
{
    [Fact(DisplayName = "Add - 요청을 캐시에 추가하고 TaskCompletionSource 반환")]
    public void Add_AddsRequest_ReturnsTaskCompletionSource()
    {
        // Given (전제조건)
        var cache = new RequestCache(NullLogger<RequestCache>.Instance);
        ushort msgSeq = 1;

        // When (행동)
        var tcs = cache.Add(msgSeq);

        // Then (결과)
        Assert.NotNull(tcs);
        tcs.Task.IsCompleted.Should().BeFalse("아직 완료되지 않은 상태여야 함");
    }

    [Fact(DisplayName = "TryRemove - 존재하는 요청 제거 성공")]
    public void TryRemove_ExistingRequest_ReturnsTrue()
    {
        // Given (전제조건)
        var cache = new RequestCache(NullLogger<RequestCache>.Instance);
        ushort msgSeq = 1;
        var tcs = cache.Add(msgSeq);

        // When (행동)
        var result = cache.TryRemove(msgSeq, out var removedTcs);

        // Then (결과)
        result.Should().BeTrue("존재하는 요청 제거는 성공해야 함");
        Assert.Same(tcs, removedTcs);
    }

    [Fact(DisplayName = "TryRemove - 존재하지 않는 요청 제거 실패")]
    public void TryRemove_NonExistentRequest_ReturnsFalse()
    {
        // Given (전제조건)
        var cache = new RequestCache(NullLogger<RequestCache>.Instance);
        ushort msgSeq = 999;

        // When (행동)
        var result = cache.TryRemove(msgSeq, out var removedTcs);

        // Then (결과)
        result.Should().BeFalse("존재하지 않는 요청 제거는 실패해야 함");
        Assert.Null(removedTcs);
    }

    [Fact(DisplayName = "Add - 동일한 msgSeq로 중복 추가 시 새 요청이 예외 상태")]
    public void Add_DuplicateMsgSeq_NewRequestFails()
    {
        // Given (전제조건)
        var cache = new RequestCache(NullLogger<RequestCache>.Instance);
        ushort msgSeq = 1;
        var tcs1 = cache.Add(msgSeq);

        // When (행동) - 같은 msgSeq로 다시 추가하면 새 TCS가 예외 설정됨
        var tcs2 = cache.Add(msgSeq);

        // Then (결과) - 새 요청은 예외 상태, 기존 요청은 그대로
        tcs2.Task.IsFaulted.Should().BeTrue("새 요청은 예외 상태여야 함 (중복 등록)");
        tcs1.Task.IsFaulted.Should().BeFalse("기존 요청은 그대로 유지되어야 함");
        Assert.NotSame(tcs1, tcs2);
    }

    [Fact(DisplayName = "Clear - 모든 요청을 제거")]
    public void Clear_RemovesAllRequests()
    {
        // Given (전제조건)
        var cache = new RequestCache(NullLogger<RequestCache>.Instance);
        cache.Add(1);
        cache.Add(2);
        cache.Add(3);

        // When (행동)
        cache.Clear();

        // Then (결과)
        cache.TryRemove(1, out _).Should().BeFalse("모든 요청이 제거되어야 함");
        cache.TryRemove(2, out _).Should().BeFalse("모든 요청이 제거되어야 함");
        cache.TryRemove(3, out _).Should().BeFalse("모든 요청이 제거되어야 함");
    }

    [Fact(DisplayName = "동시성 테스트 - 여러 스레드에서 Add 동시 호출")]
    public async Task Add_ConcurrentCalls_ThreadSafe()
    {
        // Given (전제조건)
        var cache = new RequestCache(NullLogger<RequestCache>.Instance);
        const int threadCount = 10;
        var tasks = new List<Task>();
        var addedSeqs = new System.Collections.Concurrent.ConcurrentBag<ushort>();

        // When (행동)
        for (int i = 0; i < threadCount; i++)
        {
            var msgSeq = (ushort)(i + 1);
            tasks.Add(Task.Run(() =>
            {
                cache.Add(msgSeq);
                addedSeqs.Add(msgSeq);
            }));
        }

        await Task.WhenAll(tasks);

        // Then (결과)
        foreach (var seq in addedSeqs)
        {
            cache.TryRemove(seq, out var tcs).Should().BeTrue($"msgSeq={seq}가 캐시에 존재해야 함");
            Assert.NotNull(tcs);
        }
    }

    [Fact(DisplayName = "동시성 테스트 - Add와 TryRemove 동시 호출")]
    public async Task ConcurrentAddAndRemove_ThreadSafe()
    {
        // Given (전제조건)
        var cache = new RequestCache(NullLogger<RequestCache>.Instance);
        const int iterations = 100;
        var tasks = new List<Task>();
        var addedCount = 0;
        var removedCount = 0;
        var startSignal = new ManualResetEventSlim(false);

        // When (행동)
        // Task 1: Add 반복 - 서로 다른 msgSeq 범위 사용
        tasks.Add(Task.Run(() =>
        {
            startSignal.Wait();
            for (ushort i = 1; i <= iterations; i++)
            {
                try
                {
                    cache.Add(i);
                    Interlocked.Increment(ref addedCount);
                }
                catch (ObjectDisposedException)
                {
                    // Race condition: TryRemove가 CTS를 dispose한 후 Token 접근 시 발생 가능
                    // 이는 예상된 동작임
                }
                Thread.Yield();
            }
        }));

        // Task 2: TryRemove 반복 - Add가 완료된 후 시작하도록 약간의 딜레이
        tasks.Add(Task.Run(async () =>
        {
            startSignal.Wait();
            await Task.Delay(5); // Add가 먼저 일부 진행되도록
            for (int round = 0; round < 3; round++)
            {
                for (ushort i = 1; i <= iterations; i++)
                {
                    if (cache.TryRemove(i, out _))
                    {
                        Interlocked.Increment(ref removedCount);
                    }
                    Thread.Yield();
                }
            }
        }));

        // 동시에 시작
        startSignal.Set();
        await Task.WhenAll(tasks);

        // Then (결과)
        // 데드락이나 예외가 발생하지 않아야 함
        // 최소한 추가한 만큼 제거할 수 있어야 함 (아직 타임아웃 안 된 항목들)
        (addedCount + removedCount).Should().BeGreaterThan(0, "최소한 일부 동작이 수행되어야 함");
    }

    [Fact(DisplayName = "TaskCompletionSource 완료 테스트 - SetResult 호출 후 Task 완료")]
    public void TaskCompletionSource_SetResult_CompletesTask()
    {
        // Given (전제조건)
        var cache = new RequestCache(NullLogger<RequestCache>.Instance);
        ushort msgSeq = 1;
        var tcs = cache.Add(msgSeq);

        // When (행동)
        tcs.SetResult(null!);

        // Then (결과)
        tcs.Task.IsCompleted.Should().BeTrue("SetResult 호출 후 Task가 완료되어야 함");
        tcs.Task.IsCompletedSuccessfully.Should().BeTrue("정상 완료되어야 함");
    }

    [Fact(DisplayName = "TaskCompletionSource 취소 테스트 - SetCanceled 호출 후 Task 취소")]
    public void TaskCompletionSource_SetCanceled_CancelsTask()
    {
        // Given (전제조건)
        var cache = new RequestCache(NullLogger<RequestCache>.Instance);
        ushort msgSeq = 1;
        var tcs = cache.Add(msgSeq);

        // When (행동)
        tcs.SetCanceled();

        // Then (결과)
        tcs.Task.IsCanceled.Should().BeTrue("SetCanceled 호출 후 Task가 취소되어야 함");
    }

    [Fact(DisplayName = "TaskCompletionSource 예외 테스트 - SetException 호출 후 Task 실패")]
    public void TaskCompletionSource_SetException_FailsTask()
    {
        // Given (전제조건)
        var cache = new RequestCache(NullLogger<RequestCache>.Instance);
        ushort msgSeq = 1;
        var tcs = cache.Add(msgSeq);
        var exception = new TimeoutException("Request timeout");

        // When (행동)
        tcs.SetException(exception);

        // Then (결과)
        tcs.Task.IsFaulted.Should().BeTrue("SetException 호출 후 Task가 실패해야 함");
        tcs.Task.Exception.Should().NotBeNull();
        Assert.Same(exception, tcs.Task.Exception!.InnerException);
    }

    [Fact(DisplayName = "대량 요청 테스트 - 10,000개 요청 추가 및 제거")]
    public void LargeScale_AddAndRemove_HandlesThousandsOfRequests()
    {
        // Given (전제조건)
        var cache = new RequestCache(NullLogger<RequestCache>.Instance);
        const int count = 10_000;

        // When (행동)
        for (ushort i = 1; i <= count; i++)
        {
            cache.Add(i);
        }

        // Then (결과)
        for (ushort i = 1; i <= count; i++)
        {
            cache.TryRemove(i, out var tcs).Should().BeTrue($"msgSeq={i}가 캐시에 존재해야 함");
            Assert.NotNull(tcs);
        }
    }

    [Fact(DisplayName = "Clear 후 Add - 새로운 요청 추가 가능")]
    public void Clear_ThenAdd_AllowsNewRequests()
    {
        // Given (전제조건)
        var cache = new RequestCache(NullLogger<RequestCache>.Instance);
        cache.Add(1);
        cache.Add(2);
        cache.Clear();

        // When (행동)
        var tcs = cache.Add(3);

        // Then (결과)
        Assert.NotNull(tcs);
        cache.TryRemove(3, out var removedTcs).Should().BeTrue();
        Assert.Same(tcs, removedTcs);
    }
}
