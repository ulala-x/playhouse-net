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

/// <summary>
/// Message routing client messages to Actor via AccountId.
/// </summary>
internal sealed class ClientRouteMessage : PlayMessage
{
    /// <summary>
    /// Gets the account ID for actor routing.
    /// </summary>
    public string AccountId { get; }

    /// <summary>
    /// Gets the message ID.
    /// </summary>
    public string MsgId { get; }

    /// <summary>
    /// Gets the message sequence number.
    /// </summary>
    public ushort MsgSeq { get; }

    /// <summary>
    /// Gets the session ID.
    /// </summary>
    public long Sid { get; }

    /// <summary>
    /// Gets the message payload.
    /// </summary>
    public ReadOnlyMemory<byte> Payload { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="ClientRouteMessage"/> class.
    /// </summary>
    /// <param name="stageId">Target stage ID.</param>
    /// <param name="accountId">Account ID for actor routing.</param>
    /// <param name="msgId">Message ID.</param>
    /// <param name="msgSeq">Message sequence number.</param>
    /// <param name="sid">Session ID.</param>
    /// <param name="payload">Message payload.</param>
    public ClientRouteMessage(
        long stageId,
        string accountId,
        string msgId,
        ushort msgSeq,
        long sid,
        ReadOnlyMemory<byte> payload)
        : base(stageId)
    {
        AccountId = accountId;
        MsgId = msgId;
        MsgSeq = msgSeq;
        Sid = sid;
        Payload = payload;
    }
}
