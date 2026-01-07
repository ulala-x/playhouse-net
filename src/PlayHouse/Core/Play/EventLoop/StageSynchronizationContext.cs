using PlayHouse.Core.Play.Base;
using PlayHouse.Core.Shared.TaskPool;

namespace PlayHouse.Core.Play.EventLoop;

/// <summary>
/// Stage의 비동기 Continuation을 워커 Task 풀에서 실행하도록 관리하는 컨텍스트.
/// </summary>
internal sealed class StageSynchronizationContext : SynchronizationContext
{
    private readonly GlobalTaskPool _taskPool;

    public StageSynchronizationContext(GlobalTaskPool taskPool)
    {
        _taskPool = taskPool;
    }

    public override void Post(SendOrPostCallback d, object? state)
    {
        var currentStage = BaseStage.Current;
        if (currentStage != null)
        {
            // Stage 컨텍스트가 있으면 해당 Stage의 메일박스에 넣어서 순차성 유지
            currentStage.PostContinuation(d, state);
        }
        else
        {
            // 컨텍스트가 없으면(드문 경우) 직접 풀에 게시
            _taskPool.Post(new ContinuationWorkItem(d, state));
        }
    }

    public override void Send(SendOrPostCallback d, object? state)
    {
        // 동기 실행은 직접 수행
        d(state);
    }

    public override SynchronizationContext CreateCopy() => this;
}
