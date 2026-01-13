using PlayHouse.Core.Play.Base;

namespace PlayHouse.Core.Play.EventLoop;

/// <summary>
/// Stage의 비동기 Continuation을 처리하는 SynchronizationContext.
/// GlobalTaskPool 대신 ThreadPool을 직접 사용하여 효율성을 높입니다.
/// </summary>
/// <remarks>
/// AsyncLocal&lt;BaseStage.Current&gt;를 유지하기 위해 ThreadPool.QueueUserWorkItem()을 사용합니다.
/// UnsafeQueueUserWorkItem은 ExecutionContext를 전파하지 않아 순차성이 깨질 수 있습니다.
/// </remarks>
internal sealed class StageSynchronizationContext : SynchronizationContext
{
    /// <summary>
    /// 싱글톤 인스턴스. GlobalTaskPool 의존성이 없으므로 공유 가능.
    /// </summary>
    public static readonly StageSynchronizationContext Instance = new();

    private StageSynchronizationContext() { }

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
            // 컨텍스트가 없으면(드문 경우) ThreadPool에서 직접 실행
            // QueueUserWorkItem을 사용하여 ExecutionContext/AsyncLocal 유지
            ThreadPool.QueueUserWorkItem(_ => d(state));
        }
    }

    public override void Send(SendOrPostCallback d, object? state)
    {
        // 동기 실행은 직접 수행
        d(state);
    }

    public override SynchronizationContext CreateCopy() => this;
}
