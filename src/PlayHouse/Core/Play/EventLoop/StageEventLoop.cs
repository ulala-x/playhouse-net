#nullable enable

using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace PlayHouse.Core.Play.EventLoop;

/// <summary>
/// Stage 전용 EventLoop - Task.Run 오버헤드 제거를 위한 전용 스레드 + ConcurrentQueue 기반 구현.
/// </summary>
/// <remarks>
/// - Task.Run의 스케줄링 오버헤드를 제거하기 위해 전용 스레드 사용
/// - ConcurrentQueue + AutoResetEvent를 사용하여 고성능 메시지 큐 구현
/// - StageSynchronizationContext로 async/await continuation을 같은 스레드에서 실행
/// </remarks>
internal sealed class StageEventLoop : IDisposable
{
    private readonly int _id;
    private readonly ConcurrentQueue<IEventLoopWorkItem> _workQueue = new();
    private readonly AutoResetEvent _wakeUp = new(false);
    private readonly Thread _thread;
    private readonly StageSynchronizationContext _syncContext;
    private readonly ILogger? _logger;
    private volatile bool _disposed;

    /// <summary>
    /// EventLoop의 전용 스레드를 반환합니다.
    /// SynchronizationContext에서 현재 스레드 비교에 사용됩니다.
    /// </summary>
    public Thread Thread => _thread;

    /// <summary>
    /// StageEventLoop의 새 인스턴스를 초기화합니다.
    /// </summary>
    /// <param name="id">EventLoop 식별자.</param>
    /// <param name="logger">선택적 로거.</param>
    public StageEventLoop(int id, ILogger? logger = null)
    {
        _id = id;
        _logger = logger;

        // SynchronizationContext 생성
        _syncContext = new StageSynchronizationContext(this);

        // 전용 스레드 생성 및 시작
        _thread = new Thread(Run)
        {
            Name = $"EventLoop-{id}",
            IsBackground = true
        };
        _thread.Start();

        _logger?.LogInformation("EventLoop-{Id} started on thread {ThreadId}", _id, _thread.ManagedThreadId);
    }

    /// <summary>
    /// EventLoop의 메인 실행 루프.
    /// ConcurrentQueue에서 작업을 읽어 순차적으로 실행합니다.
    /// </summary>
    private void Run()
    {
        // SynchronizationContext 설정 - await의 continuation이 이 스레드에서 실행됨
        SynchronizationContext.SetSynchronizationContext(_syncContext);

        try
        {
            while (!_disposed)
            {
                // 큐가 빌 때까지 모든 작업 처리 (배치)
                while (_workQueue.TryDequeue(out var item))
                {
                    ExecuteWorkItem(item);
                }

                // 큐가 비면 대기 (새 작업이 올 때까지)
                if (!_disposed)
                {
                    _wakeUp.WaitOne();
                }
            }
        }
        catch (Exception ex) when (!_disposed)
        {
            _logger?.LogError(ex, "EventLoop-{Id}: Fatal error in run loop", _id);
        }
        finally
        {
            _logger?.LogInformation("EventLoop-{Id} stopped", _id);
        }
    }

    /// <summary>
    /// 작업 항목을 실행합니다.
    /// 비동기 작업의 continuation은 SynchronizationContext를 통해 이 스레드로 마샬링됩니다.
    /// </summary>
    private void ExecuteWorkItem(IEventLoopWorkItem item)
    {
        try
        {
            var task = item.ExecuteAsync();

            // Task가 완료되지 않은 경우, async continuation이 SynchronizationContext를 통해
            // 다시 이 EventLoop으로 Post될 것이므로 여기서는 반환
            // Task가 이미 완료된 경우 GetAwaiter().GetResult()로 예외 처리
            if (task.IsCompleted)
            {
                task.GetAwaiter().GetResult();
            }
            // else: Task가 완료되지 않음 - continuation이 나중에 Queue로 Post될 것임
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "EventLoop-{Id}: Error executing work item", _id);
        }
    }

    /// <summary>
    /// EventLoop에 작업을 게시합니다.
    /// </summary>
    /// <param name="item">실행할 작업 항목.</param>
    public void Post(IEventLoopWorkItem item)
    {
        if (_disposed)
        {
            _logger?.LogWarning("EventLoop-{Id}: Attempted to post to disposed EventLoop", _id);
            return;
        }

        _workQueue.Enqueue(item);
        _wakeUp.Set();
    }

    /// <summary>
    /// EventLoop을 정리하고 스레드를 종료합니다.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        _wakeUp.Set();  // 대기 중인 스레드 깨움

        // EventLoop 스레드 종료 대기
        if (_thread.IsAlive)
        {
            _logger?.LogInformation("EventLoop-{Id}: Waiting for thread to complete...", _id);
            _thread.Join(TimeSpan.FromSeconds(5));
        }

        _wakeUp.Dispose();
        _logger?.LogInformation("EventLoop-{Id}: Disposed", _id);
    }
}
