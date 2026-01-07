#nullable enable

using System.Diagnostics;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;

namespace PlayHouse.Core.Shared.TaskPool;

/// <summary>
/// 동적으로 크기가 조절되는 전역 워커 Task 풀.
/// 부하에 따라 Task를 늘리고, 유휴 상태일 때 줄여서 효율을 극대화합니다.
/// </summary>
internal sealed class GlobalTaskPool : IDisposable
{
    private readonly Channel<ITaskPoolWorkItem> _workQueue;
    private readonly CancellationTokenSource _cts = new();
    private readonly ILogger? _logger;
    private bool _disposed;

    private readonly int _minPoolSize;
    private readonly int _maxPoolSize;
    private int _currentPoolSize;
    private int _idleWorkerCount; // 작업을 기다리고 있는 일꾼 수
    private long _lastIdleTimestamp; // 마지막으로 일꾼이 한가해졌던 시간

    /// <summary>
    /// GlobalTaskPool의 새 인스턴스를 초기화합니다.
    /// </summary>
    public GlobalTaskPool(int minPoolSize, int maxPoolSize, ILogger? logger = null)
    {
        _minPoolSize = minPoolSize;
        _maxPoolSize = maxPoolSize;
        _logger = logger;
        _lastIdleTimestamp = Stopwatch.GetTimestamp();
        
        _workQueue = Channel.CreateUnbounded<ITaskPoolWorkItem>(new UnboundedChannelOptions
        {
            SingleReader = false,
            SingleWriter = false
        });

        // 초기 최소 워커 생성
        for (int i = 0; i < _minPoolSize; i++)
        {
            SpawnWorker();
        }

        _logger?.LogInformation("GlobalTaskPool initialized (Min: {Min}, Max: {Max})", _minPoolSize, _maxPoolSize);
    }

    private void SpawnWorker()
    {
        var id = Interlocked.Increment(ref _currentPoolSize);
        if (id > _maxPoolSize)
        {
            Interlocked.Decrement(ref _currentPoolSize);
            return;
        }

        Task.Run(() => WorkerLoopAsync(id, _cts.Token));
    }

    /// <summary>
    /// 작업을 풀에 게시합니다. 
    /// 기존 워커들이 '긴 대기'에 빠져 기아 상태일 때만 워커를 확장합니다.
    /// </summary>
    public void Post(ITaskPoolWorkItem item)
    {
        if (!_workQueue.Writer.TryWrite(item))
        {
            _logger?.LogError("Failed to post work item to GlobalTaskPool");
            return;
        }

        // 기아 상태(Starvation) 감지: 
        // 유휴 일꾼이 없고, 마지막 유휴 발생 후 일정 시간(100ms)이 지났다면
        // 현재 일꾼들이 'Long Wait' 상태라고 판단하여 확장 시도.
        if (Volatile.Read(ref _idleWorkerCount) == 0 && _currentPoolSize < _maxPoolSize)
        {
            long now = Stopwatch.GetTimestamp();
            long lastIdle = Volatile.Read(ref _lastIdleTimestamp);
            
            // 100ms 동안 아무도 돌아오지 않았다면 보충군 투입
            if (now - lastIdle > (Stopwatch.Frequency / 10))
            {
                // 생성 전 다시 한번 체크 (Double-check)
                if (Interlocked.Read(ref _lastIdleTimestamp) == lastIdle)
                {
                    // 생성 시간을 갱신하여 연속 생성을 방지 (Throttling)
                    Interlocked.Exchange(ref _lastIdleTimestamp, now);
                    SpawnWorker();
                    _logger?.LogDebug("Worker expanded to {Count} due to starvation (Long Wait detected)", _currentPoolSize);
                }
            }
        }
    }

    private async Task WorkerLoopAsync(int id, CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                ITaskPoolWorkItem? item;

                // 1. 유휴 상태 진입 기록
                Interlocked.Increment(ref _idleWorkerCount);
                Interlocked.Exchange(ref _lastIdleTimestamp, Stopwatch.GetTimestamp());

                try
                {
                    // 작업을 기다림
                    if (_currentPoolSize > _minPoolSize)
                    {
                        // 최소 수량 초과 워커는 유휴 시간 체크
                        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                        timeoutCts.CancelAfter(TimeSpan.FromSeconds(30));
                        try
                        {
                            if (!await _workQueue.Reader.WaitToReadAsync(timeoutCts.Token)) break;
                        }
                        catch (OperationCanceledException)
                        {
                            // 30초 동안 일이 없으면 퇴장
                            if (Interlocked.Decrement(ref _currentPoolSize) >= _minPoolSize)
                            {
                                _logger?.LogDebug("Worker Task-{Id} retired", id);
                                return;
                            }
                            Interlocked.Increment(ref _currentPoolSize);
                        }
                    }
                    else
                    {
                        if (!await _workQueue.Reader.WaitToReadAsync(ct)) break;
                    }

                    if (!_workQueue.Reader.TryRead(out item)) continue;
                }
                finally
                {
                    // 작업 시작 전 유휴 상태 해제
                    Interlocked.Decrement(ref _idleWorkerCount);
                }

                // 2. 로직 실행
                if (item != null)
                {
                    await item.ExecuteAsync();
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error in Worker Task-{Id}", id);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _cts.Cancel();
        _workQueue.Writer.Complete();
        _cts.Dispose();
    }
}

/// <summary>
/// 워커 풀에서 실행할 작업 단위 인터페이스.
/// </summary>
internal interface ITaskPoolWorkItem
{
    Task ExecuteAsync();
}

/// <summary>
/// SynchronizationContext.Post에서 사용되는 작업을 나타내는 WorkItem.
/// </summary>
internal sealed class ContinuationWorkItem : ITaskPoolWorkItem
{
    private readonly SendOrPostCallback _callback;
    private readonly object? _state;

    public ContinuationWorkItem(SendOrPostCallback callback, object? state)
    {
        _callback = callback;
        _state = state;
    }

    public Task ExecuteAsync()
    {
        _callback(_state);
        return Task.CompletedTask;
    }
}
