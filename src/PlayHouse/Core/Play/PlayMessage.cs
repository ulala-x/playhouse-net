#nullable enable

using PlayHouse.Runtime.ServerMesh.Message;

namespace PlayHouse.Core.Play;

/// <summary>
/// Base abstract type for all messages dispatched to PlayDispatcher.
/// </summary>
/// <remarks>
/// PlayMessage provides a unified abstraction for different message types:
/// - RouteMessage: Server-to-server route packets
/// - TimerMessage: Timer callback notifications
/// - AsyncMessage: AsyncBlock post-callback results
/// - DestroyMessage: Stage destruction requests
/// </remarks>
internal abstract class PlayMessage : IDisposable
{
    /// <summary>
    /// Gets the target stage ID.
    /// </summary>
    public long StageId { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="PlayMessage"/> class.
    /// </summary>
    /// <param name="stageId">Target stage ID.</param>
    protected PlayMessage(long stageId)
    {
        StageId = stageId;
    }

    /// <inheritdoc/>
    public virtual void Dispose()
    {
        // Base implementation does nothing; override if needed
    }
}

/// <summary>
/// Message wrapping a RuntimeRoutePacket for stage processing.
/// </summary>
internal sealed class RouteMessage : PlayMessage
{
    /// <summary>
    /// Gets the wrapped route packet.
    /// </summary>
    public RuntimeRoutePacket RoutePacket { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="RouteMessage"/> class.
    /// </summary>
    /// <param name="routePacket">The route packet to wrap.</param>
    public RouteMessage(RuntimeRoutePacket routePacket)
        : base(routePacket.StageId)
    {
        RoutePacket = routePacket;
    }

    /// <inheritdoc/>
    public override void Dispose()
    {
        RoutePacket.Dispose();
    }
}

/// <summary>
/// Message wrapping a TimerPacket for timer callback execution.
/// </summary>
internal sealed class TimerMessage : PlayMessage
{
    /// <summary>
    /// Gets the wrapped timer packet.
    /// </summary>
    public TimerPacket TimerPacket { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="TimerMessage"/> class.
    /// </summary>
    /// <param name="timerPacket">The timer packet to wrap.</param>
    public TimerMessage(TimerPacket timerPacket)
        : base(timerPacket.StageId)
    {
        TimerPacket = timerPacket;
    }
}

/// <summary>
/// Message wrapping an AsyncBlockPacket for post-callback execution.
/// </summary>
internal sealed class AsyncMessage : PlayMessage
{
    /// <summary>
    /// Gets the wrapped async block packet.
    /// </summary>
    public AsyncBlockPacket AsyncBlockPacket { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="AsyncMessage"/> class.
    /// </summary>
    /// <param name="asyncBlockPacket">The async block packet to wrap.</param>
    public AsyncMessage(AsyncBlockPacket asyncBlockPacket)
        : base(asyncBlockPacket.StageId)
    {
        AsyncBlockPacket = asyncBlockPacket;
    }
}

/// <summary>
/// Message requesting stage destruction.
/// </summary>
internal sealed class DestroyMessage : PlayMessage
{
    /// <summary>
    /// Initializes a new instance of the <see cref="DestroyMessage"/> class.
    /// </summary>
    /// <param name="stageId">The stage ID to destroy.</param>
    public DestroyMessage(long stageId)
        : base(stageId)
    {
    }
}
