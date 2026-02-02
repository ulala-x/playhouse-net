#nullable enable

using System;
using System.Collections.Concurrent;

namespace PlayHouse.Connector.Internal;

/// <summary>
/// 메인 스레드 콜백 관리자 (Unity 통합용)
/// </summary>
internal sealed class AsyncManager
{
    private readonly ConcurrentQueue<Action> _mainThreadActions = new();

    /// <summary>
    /// 메인 스레드에서 실행할 작업 추가
    /// </summary>
    /// <param name="action">실행할 작업</param>
    public void AddJob(Action action)
    {
        _mainThreadActions.Enqueue(action);
    }

    /// <summary>
    /// 메인 스레드에서 대기 중인 작업 실행
    /// </summary>
    public void MainThreadAction()
    {
        while (_mainThreadActions.TryDequeue(out var action))
        {
            try
            {
                action.Invoke();
            }
            catch (Exception ex)
            {
                // 개별 콜백 에러는 무시하고 계속 처리
                System.Diagnostics.Debug.WriteLine($"[PlayHouse] Callback error: {ex}");
            }
        }
    }

    /// <summary>
    /// 대기 중인 작업 수
    /// </summary>
    public int PendingCount => _mainThreadActions.Count;
}
