#nullable enable

using System.Threading.Channels;
using Microsoft.Extensions.Logging;

namespace PlayHouse.Core.Play.TaskPool;

/// <summary>
/// 고정된 개수의 워커 Task를 관리하는 전역 풀.
/// 10,000 CCU 상황에서도 시스템 전체의 Task 개수를 일정하게 유지합니다.
/// </summary>
internal sealed class GlobalTaskPool : IDisposable
{
    private readonly Channel<ITaskPoolWorkItem> _workQueue;
    private readonly Task[] _workers;
    private readonly CancellationTokenSource _cts = new();
    private readonly ILogger? _logger;
    private bool _disposed;

    /// <summary>
    /// GlobalTaskPool의 새 인스턴스를 초기화합니다.
    /// </summary>
    /// <param name="poolSize">고정할 워커 Task의 개수 (예: 200).</param>
    /// <param name="logger">로거 인스턴스.</param>
    public GlobalTaskPool(int poolSize, ILogger? logger = null)
    {
        _logger = logger;
        
        // 다중 생산자 - 다중 소비자 채널 생성
        _workQueue = Channel.CreateUnbounded<ITaskPoolWorkItem>(new UnboundedChannelOptions
        {
            SingleReader = false,
            SingleWriter = false
        });

        _workers = new Task[poolSize];
        for (int i = 0; i < poolSize; i++)
        {
            int workerId = i;
            _workers[i] = Task.Run(() => WorkerLoopAsync(workerId, _cts.Token));
        }

        _logger?.LogInformation("GlobalTaskPool initialized with {PoolSize} worker tasks", poolSize);
    }

    /// <summary>
    /// 작업을 풀에 게시합니다.
    /// </summary>
    public void Post(ITaskPoolWorkItem item)
    {
        if (!_workQueue.Writer.TryWrite(item))
        {
            _logger?.LogError("Failed to post work item to GlobalTaskPool");
        }
    }

    /// <summary>
    /// 각 워커 Task의 메인 루프.
    /// </summary>
    private async Task WorkerLoopAsync(int id, CancellationToken ct)
    {
        _logger?.LogDebug("Worker Task-{Id} started", id);

        try
        {
            // 채널에서 작업을 하나씩 꺼내어 실행
            await foreach (var item in _workQueue.Reader.ReadAllAsync(ct))
            {
                try
                {
                    await item.ExecuteAsync();
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Error executing work item in Worker Task-{Id}", id);
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Fatal error in Worker Task-{Id} loop", id);
        }
        finally
        {
            _logger?.LogDebug("Worker Task-{Id} stopped", id);
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
