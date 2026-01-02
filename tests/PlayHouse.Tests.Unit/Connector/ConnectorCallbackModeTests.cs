#nullable enable

using FluentAssertions;
using PlayHouse.Connector;
using System.Threading;
using Xunit;

namespace PlayHouse.Tests.Unit.Connector;

/// <summary>
/// 단위 테스트: Connector SynchronizationContext 기반 콜백 검증
/// </summary>
public class ConnectorCallbackModeTests
{
    [Fact(DisplayName = "ImmediateSynchronizationContext - Post는 즉시 실행")]
    public void ImmediateSynchronizationContext_Post_ExecutesImmediately()
    {
        // Given (전제조건)
        var syncContext = new ImmediateSynchronizationContext();
        var executed = false;

        // When (행동)
        syncContext.Post(_ => executed = true, null);

        // Then (결과)
        executed.Should().BeTrue("Post는 즉시 동일 스레드에서 실행되어야 함");
    }

    [Fact(DisplayName = "ImmediateSynchronizationContext - Send는 즉시 실행")]
    public void ImmediateSynchronizationContext_Send_ExecutesImmediately()
    {
        // Given (전제조건)
        var syncContext = new ImmediateSynchronizationContext();
        var executed = false;

        // When (행동)
        syncContext.Send(_ => executed = true, null);

        // Then (결과)
        executed.Should().BeTrue("Send는 즉시 동일 스레드에서 실행되어야 함");
    }

    [Fact(DisplayName = "SynchronizationContext.Current - null일 때 큐 모드로 동작")]
    public void ClientNetwork_NoSyncContext_UsesQueueMode()
    {
        // Given (전제조건) - SynchronizationContext.Current가 null인 상태
        SynchronizationContext.SetSynchronizationContext(null);

        // Then (결과)
        SynchronizationContext.Current.Should().BeNull(
            "SynchronizationContext가 없으면 큐 모드로 동작 (MainThreadAction 필요)");
    }

    [Fact(DisplayName = "SynchronizationContext.Current - 설정되었을 때 즉시 실행")]
    public void ClientNetwork_WithSyncContext_ExecutesImmediately()
    {
        // Given (전제조건)
        var originalContext = SynchronizationContext.Current;
        var syncContext = new ImmediateSynchronizationContext();
        SynchronizationContext.SetSynchronizationContext(syncContext);

        try
        {
            // Then (결과)
            SynchronizationContext.Current.Should().NotBeNull(
                "SynchronizationContext가 설정되면 즉시 실행 모드로 동작");
            SynchronizationContext.Current.Should().BeSameAs(syncContext);
        }
        finally
        {
            // Cleanup
            SynchronizationContext.SetSynchronizationContext(originalContext);
        }
    }
}
