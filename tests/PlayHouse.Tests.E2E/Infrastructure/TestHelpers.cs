#nullable enable

using PlayHouse.Connector;
using ClientConnector = PlayHouse.Connector.Connector;

namespace PlayHouse.Tests.E2E.Infrastructure;

/// <summary>
/// E2E 테스트 성능 개선을 위한 헬퍼 메서드 모음.
/// Task.Delay 폴링을 조건 기반 대기로 변경하여 테스트 속도를 향상시킵니다.
/// </summary>
public static class TestHelpers
{
    /// <summary>
    /// 조건이 충족될 때까지 대기합니다 (폴링 방식).
    /// Task.Delay 반복 대신 짧은 간격으로 조건을 확인하여 성능을 개선합니다.
    /// </summary>
    /// <param name="condition">확인할 조건</param>
    /// <param name="timeoutMs">타임아웃 (밀리초), 기본값 5000ms</param>
    /// <param name="pollIntervalMs">폴링 간격 (밀리초), 기본값 10ms</param>
    /// <returns>조건이 충족되면 true, 타임아웃시 false</returns>
    public static async Task<bool> WaitForConditionAsync(
        Func<bool> condition,
        int timeoutMs = 5000,
        int pollIntervalMs = 10)
    {
        var startTime = DateTime.UtcNow;
        while (!condition())
        {
            if ((DateTime.UtcNow - startTime).TotalMilliseconds >= timeoutMs)
            {
                return false;
            }
            await Task.Delay(pollIntervalMs);
        }
        return true;
    }

    /// <summary>
    /// 카운터가 목표값에 도달할 때까지 대기합니다.
    /// </summary>
    /// <param name="counter">카운터 값을 반환하는 함수</param>
    /// <param name="target">목표값</param>
    /// <param name="timeoutMs">타임아웃 (밀리초), 기본값 5000ms</param>
    /// <returns>목표값에 도달하면 true, 타임아웃시 false</returns>
    public static Task<bool> WaitForCountAsync(
        Func<int> counter,
        int target,
        int timeoutMs = 5000)
    {
        return WaitForConditionAsync(() => counter() >= target, timeoutMs);
    }

    /// <summary>
    /// Connector 콜백을 처리하면서 조건이 충족될 때까지 대기합니다.
    /// 콜백 타이머를 사용하지 않는 테스트에서 수동으로 MainThreadAction()을 호출하면서 대기합니다.
    /// </summary>
    /// <param name="connector">Connector 인스턴스</param>
    /// <param name="condition">확인할 조건</param>
    /// <param name="timeoutMs">타임아웃 (밀리초), 기본값 5000ms</param>
    /// <returns>조건이 충족되면 true, 타임아웃시 false</returns>
    public static async Task<bool> ProcessCallbacksUntilAsync(
        ClientConnector connector,
        Func<bool> condition,
        int timeoutMs = 5000)
    {
        var startTime = DateTime.UtcNow;
        while (!condition())
        {
            if ((DateTime.UtcNow - startTime).TotalMilliseconds >= timeoutMs)
            {
                return false;
            }
            connector.MainThreadAction();
            await Task.Delay(10);
        }
        return true;
    }

    /// <summary>
    /// Connector 콜백을 일정 횟수만큼 처리합니다.
    /// 비동기 처리가 완료될 시간을 주기 위한 용도입니다.
    /// </summary>
    /// <param name="connector">Connector 인스턴스</param>
    /// <param name="iterations">콜백 처리 횟수, 기본값 3</param>
    /// <param name="delayMs">각 처리 사이의 대기 시간 (밀리초), 기본값 50ms</param>
    public static async Task ProcessCallbacksAsync(
        ClientConnector connector,
        int iterations = 3,
        int delayMs = 50)
    {
        for (int i = 0; i < iterations; i++)
        {
            connector.MainThreadAction();
            await Task.Delay(delayMs);
        }
    }
}
