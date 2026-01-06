#nullable enable

using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using PlayHouse.Core.Play.Base;

namespace PlayHouse.Core.Play.EventLoop;

/// <summary>
/// Stage 전용 EventLoop - Task.Run 오버헤드 제거를 위한 전용 스레드 + Channel 기반 구현.
/// </summary>
/// <remarks>
/// - Task.Run의 스케줄링 오버헤드를 제거하기 위해 전용 스레드 사용
/// - Channel&lt;T&gt;를 사용하여 고성능 메시지 큐 구현
/// - StageSynchronizationContext로 async/await continuation을 같은 스레드에서 실행
/// </remarks>
internal sealed class StageEventLoop : IDisposable
{
    private readonly int _id;
    private readonly Channel<IEventLoopWorkItem> _channel;
    private readonly Thread _thread;
    private readonly StageSynchronizationContext _syncContext;
    private readonly ILogger? _logger;
    private readonly ManualResetEventSlim _shutdownEvent = new(false);
    private bool _disposed;

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

        // UnboundedChannel 생성 - SingleReader 옵션으로 성능 최적화
        _channel = Channel.CreateUnbounded<IEventLoopWorkItem>(new UnboundedChannelOptions
        {
            SingleReader = true
        });

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
    /// Channel에서 작업을 읽어 순차적으로 실행하며, 같은 Stage의 작업은 배치로 처리합니다.
    /// </summary>
    private void Run()
    {
        // SynchronizationContext 설정 - await의 continuation이 이 스레드에서 실행됨
        SynchronizationContext.SetSynchronizationContext(_syncContext);

        // 배치 처리를 위한 임시 버퍼 (할당 최소화)
        var workItems = new List<IEventLoopWorkItem>(1024);
        var stageGroups = new Dictionary<BaseStage, List<IEventLoopWorkItem>>();
        var orderOfStages = new List<BaseStage>();

        try
        {
            while (!_disposed)
            {
                try
                {
                    // 1. 최대한 많이 긁어모음 (Aggregation)
                    if (_channel.Reader.TryRead(out var item))
                    {
                        workItems.Add(item);
                        while (workItems.Count < 1024 && _channel.Reader.TryRead(out item))
                        {
                            workItems.Add(item);
                        }
                    }
                    else
                    {
                        var waitTask = _channel.Reader.WaitToReadAsync();
                        if (waitTask.IsCompletedSuccessfully)
                        {
                            if (!waitTask.Result) break;
                        }
                        else
                        {
                            if (!waitTask.AsTask().ConfigureAwait(false).GetAwaiter().GetResult()) break;
                        }
                        continue;
                    }

                    // 2. Stage별 그룹화 (순서 보장하며 묶음)
                    for (int i = 0; i < workItems.Count; i++)
                    {
                        var work = workItems[i];
                        var stage = work.Stage;

                        if (stage == null)
                        {
                            // 전역 작업은 즉시 실행하거나 별도 처리 (순차성 위해 즉시 실행 추천)
                            ExecuteWorkItem(work);
                            continue;
                        }

                        if (!stageGroups.TryGetValue(stage, out var group))
                        {
                            group = new List<IEventLoopWorkItem>();
                            stageGroups[stage] = group;
                            orderOfStages.Add(stage);
                        }
                        group.Add(work);
                    }

                    // 3. 그룹별 일괄 실행 (Bundle Execution)
                    for (int i = 0; i < orderOfStages.Count; i++)
                    {
                        var stage = orderOfStages[i];
                        var batch = stageGroups[stage];
                        
                        // Stage에게 일괄 처리를 맡김 (Context Switch 1번으로 단축)
                        var task = stage.ExecuteBatchAsync(batch);
                        if (task.IsCompleted)
                        {
                            task.GetAwaiter().GetResult();
                        }
                        else
                        {
                            task.GetAwaiter().GetResult(); // EventLoop 스레드 유지하며 대기
                        }
                        
                        batch.Clear();
                    }
                    
                    workItems.Clear();
                    stageGroups.Clear();
                    orderOfStages.Clear();
                }
                catch (Exception) when (_disposed)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "EventLoop-{Id}: Error in message loop", _id);
                }
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "EventLoop-{Id}: Fatal error in run loop", _id);
        }
        finally
        {
            _shutdownEvent.Set();
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
            // else: Task가 완료되지 않음 - continuation이 나중에 Channel로 Post될 것임
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

        // Channel에 작업 추가 - TryWrite는 UnboundedChannel에서 항상 성공
        if (!_channel.Writer.TryWrite(item))
        {
            _logger?.LogError("EventLoop-{Id}: Failed to post work item to channel", _id);
        }
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

        // Channel Writer를 완료 처리 - ReadAllAsync가 종료됨
        _channel.Writer.Complete();

        // EventLoop 스레드 종료 대기
        if (_thread.IsAlive)
        {
            _logger?.LogInformation("EventLoop-{Id}: Waiting for thread to complete...", _id);
            _thread.Join();
        }

        // ManualResetEventSlim 정리
        _shutdownEvent.Dispose();

        _logger?.LogInformation("EventLoop-{Id}: Disposed", _id);
    }
}
