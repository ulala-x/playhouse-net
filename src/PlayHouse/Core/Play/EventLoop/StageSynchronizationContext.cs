namespace PlayHouse.Core.Play.EventLoop;

/// <summary>
/// Stage EventLoop에서 async/await의 continuation을 같은 스레드에서 실행하도록 보장하는 SynchronizationContext
/// </summary>
internal sealed class StageSynchronizationContext : SynchronizationContext
{
    private readonly StageEventLoop _eventLoop;

    public StageSynchronizationContext(StageEventLoop eventLoop)
    {
        _eventLoop = eventLoop;
    }

    /// <summary>
    /// 비동기 메시지를 EventLoop에 게시
    /// await의 continuation이 EventLoop 큐에 추가됨
    /// </summary>
    public override void Post(SendOrPostCallback d, object? state)
    {
        // 현재 실행 중인 Stage 정보를 함께 전달 (배치 처리 그룹화용)
        var currentStage = Base.BaseStage.Current;
        _eventLoop.Post(new ContinuationWorkItem(currentStage, d, state));
    }

    /// <summary>
    /// 동기 메시지를 EventLoop에 전송
    /// 현재 스레드가 EventLoop 스레드면 직접 실행, 아니면 대기
    /// </summary>
    public override void Send(SendOrPostCallback d, object? state)
    {
        // 현재 스레드가 EventLoop 스레드면 직접 실행
        if (Thread.CurrentThread == _eventLoop.Thread)
        {
            d(state);
            return;
        }

        // 다른 스레드에서 호출된 경우 대기하며 실행
        using var resetEvent = new ManualResetEventSlim(false);
        Exception? exception = null;

        var currentStage = Base.BaseStage.Current;
        _eventLoop.Post(new ContinuationWorkItem(currentStage, _ =>
        {
            try
            {
                d(state);
            }
            catch (Exception ex)
            {
                exception = ex;
            }
            finally
            {
                resetEvent.Set();
            }
        }, null));

        resetEvent.Wait();

        if (exception != null)
        {
            throw exception;
        }
    }

    /// <summary>
    /// SynchronizationContext의 복사본 생성
    /// EventLoop는 단일 인스턴스이므로 자기 자신을 반환
    /// </summary>
    public override SynchronizationContext CreateCopy()
    {
        return this;
    }
}
