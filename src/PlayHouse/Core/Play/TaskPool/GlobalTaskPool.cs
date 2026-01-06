#nullable enable

using System.Threading.Channels;
using Microsoft.Extensions.Logging;

namespace PlayHouse.Core.Play.TaskPool;

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
    private int _busyWorkerCount;

    /// <summary>
    /// GlobalTaskPool의 새 인스턴스를 초기화합니다.
    /// </summary>
    /// <param name="minPoolSize">최소 유지할 워커 수.</param>
    /// <param name="maxPoolSize">최대 확장 가능한 워커 수.</param>
    /// <param name="logger">로거 인스턴스.</param>
    public GlobalTaskPool(int minPoolSize, int maxPoolSize, ILogger? logger = null)
    {
        _minPoolSize = minPoolSize;
        _maxPoolSize = maxPoolSize;
        _logger = logger;
        
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
    /// 작업을 풀에 게시합니다. 필요시 워커를 동적으로 확장합니다.
    /// </summary>
    public void Post(ITaskPoolWorkItem item)
    {
        if (!_workQueue.Writer.TryWrite(item))
        {
            _logger?.LogError("Failed to post work item to GlobalTaskPool");
            return;
        }

        // 모든 워커가 일하고 있고, 최대치에 도달하지 않았다면 추가 워커 생성
        // (큐에 쌓이는 속도가 처리 속도보다 빠를 때 대응)
        if (_busyWorkerCount >= _currentPoolSize && _currentPoolSize < _maxPoolSize)
        {
            SpawnWorker();
        }
    }

    private async Task WorkerLoopAsync(int id, CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                ITaskPoolWorkItem? item;

                // 1. 비동기로 작업 대기 (유휴 시간 체크 포함)
                if (!_workQueue.Reader.TryRead(out item))
                {
                    // 최소 수량을 초과한 워커는 일정 시간 대기 후 종료 (Retirement)
                    if (_currentPoolSize > _minPoolSize)
                    {
                        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                        timeoutCts.CancelAfter(TimeSpan.FromSeconds(30)); // 30초 유휴 시 종료

                        try
                        {
                            if (!await _workQueue.Reader.WaitToReadAsync(timeoutCts.Token)) break;
                            if (!_workQueue.Reader.TryRead(out item)) continue;
                        }
                        catch (OperationCanceledException)
                        {
                            // 유휴 시간 초과 - 워커 종료
                            if (Interlocked.Decrement(ref _currentPoolSize) >= _minPoolSize)
                            {
                                _logger?.LogDebug("Worker Task-{Id} retired due to inactivity", id);
                                return;
                            }
                            // 만약 그 사이 최소 수량 밑으로 내려갔다면 다시 복구
                            Interlocked.Increment(ref _currentPoolSize);
                        }
                    }
                    else
                    {
                        // 최소 수량 워커는 무한 대기
                        if (!await _workQueue.Reader.WaitToReadAsync(ct)) break;
                        if (!_workQueue.Reader.TryRead(out item)) continue;
                    }
                }

                if (item == null) continue;

                // 2. 작업 실행
                Interlocked.Increment(ref _busyWorkerCount);
                try
                {
                    await item.ExecuteAsync();
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Error executing work item in Worker Task-{Id}", id);
                }
                finally
                {
                    Interlocked.Decrement(ref _busyWorkerCount);
                }
            }
        }
        catch (OperationCanceledException) { }
        finally
        {
            // 예기치 않은 종료 시 카운트 보정
            // Interlocked.Decrement(ref _currentPoolSize); // 정상 retirement와 중복될 수 있어 주의 필요
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
