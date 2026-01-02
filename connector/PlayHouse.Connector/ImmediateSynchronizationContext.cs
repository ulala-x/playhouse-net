#nullable enable

using System.Threading;

namespace PlayHouse.Connector;

/// <summary>
/// 테스트 및 벤치마크용 즉시 실행 SynchronizationContext
/// Post/Send 호출 시 즉시 동일 스레드에서 실행합니다.
/// </summary>
public class ImmediateSynchronizationContext : SynchronizationContext
{
    public override void Post(SendOrPostCallback d, object? state) => d(state);
    public override void Send(SendOrPostCallback d, object? state) => d(state);
}
