#nullable enable

using PlayHouse.Core.Play.Base;

namespace PlayHouse.Core.Play.EventLoop;

/// <summary>
/// EventLoop에서 처리할 작업 항목의 인터페이스.
/// </summary>
internal interface IEventLoopWorkItem
{
    /// <summary>
    /// 이 작업이 속한 Stage를 반환합니다. 
    /// 전역 작업인 경우 null을 반환할 수 있습니다.
    /// </summary>
    BaseStage? Stage { get; }

    /// <summary>
    /// 작업을 비동기로 실행합니다.
    /// </summary>
    /// <returns>작업 완료를 나타내는 Task.</returns>
    Task ExecuteAsync();
}

/// <summary>
/// SynchronizationContext.Post에서 사용되는 작업을 나타내는 WorkItem.
/// </summary>
internal sealed class ContinuationWorkItem : IEventLoopWorkItem
{
    private readonly SendOrPostCallback _callback;
    private readonly object? _state;
    public BaseStage? Stage { get; }

    public ContinuationWorkItem(BaseStage? stage, SendOrPostCallback callback, object? state)
    {
        Stage = stage;
        _callback = callback;
        _state = state;
    }

    public Task ExecuteAsync()
    {
        _callback(_state);
        return Task.CompletedTask;
    }
}
