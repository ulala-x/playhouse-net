#nullable enable

using PlayHouse.Core.Play.Base;

namespace PlayHouse.Core.Play.EventLoop;

/// <summary>
/// EventLoop에서 처리할 작업 항목의 인터페이스.
/// </summary>
internal interface IEventLoopWorkItem
{
    /// <summary>
    /// 작업을 비동기로 실행합니다.
    /// </summary>
    /// <returns>작업 완료를 나타내는 Task.</returns>
    Task ExecuteAsync();
}

/// <summary>
/// Stage의 메시지 처리 작업을 나타내는 WorkItem.
/// BaseStage의 메시지 큐에서 하나의 메시지를 처리합니다.
/// </summary>
internal sealed class StageProcessingWorkItem : IEventLoopWorkItem
{
    private readonly BaseStage _stage;

    /// <summary>
    /// StageProcessingWorkItem의 새 인스턴스를 초기화합니다.
    /// </summary>
    /// <param name="stage">메시지를 처리할 BaseStage.</param>
    public StageProcessingWorkItem(BaseStage stage)
    {
        _stage = stage;
    }

    /// <summary>
    /// Stage의 메시지 하나를 처리합니다.
    /// </summary>
    public Task ExecuteAsync()
    {
        // BaseStage의 ProcessOneMessageAsync() 호출
        // 이 메서드는 나중에 BaseStage에 추가될 예정
        return _stage.ProcessOneMessageAsync();
    }
}

/// <summary>
/// SynchronizationContext.Post에서 사용되는 continuation 작업을 나타내는 WorkItem.
/// await 이후의 continuation 콜백을 EventLoop 스레드에서 실행합니다.
/// </summary>
internal sealed class ContinuationWorkItem : IEventLoopWorkItem
{
    private readonly SendOrPostCallback _callback;
    private readonly object? _state;

    /// <summary>
    /// ContinuationWorkItem의 새 인스턴스를 초기화합니다.
    /// </summary>
    /// <param name="callback">실행할 콜백 델리게이트.</param>
    /// <param name="state">콜백에 전달할 상태 객체.</param>
    public ContinuationWorkItem(SendOrPostCallback callback, object? state)
    {
        _callback = callback;
        _state = state;
    }

    /// <summary>
    /// Continuation 콜백을 실행합니다.
    /// </summary>
    public Task ExecuteAsync()
    {
        // SendOrPostCallback은 동기 메서드이므로 직접 호출
        _callback(_state);
        return Task.CompletedTask;
    }
}
