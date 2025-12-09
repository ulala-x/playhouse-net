#nullable enable

namespace PlayHouse.Abstractions;

/// <summary>
/// Represents a packet with routing information used internally for message dispatch.
/// </summary>
/// <remarks>
/// RoutePacket is an internal value object that wraps IPacket with additional routing
/// metadata such as account ID and packet type. This allows the routing system to
/// efficiently dispatch messages to the correct stage and actor.
/// </remarks>
public sealed class RoutePacket : IDisposable
{
    /// <summary>
    /// Gets the message identifier.
    /// </summary>
    public string MsgId { get; }

    /// <summary>
    /// Gets the message sequence number.
    /// </summary>
    public ushort MsgSeq { get; }

    /// <summary>
    /// Gets the target stage identifier.
    /// </summary>
    public int StageId { get; }

    /// <summary>
    /// Gets the target account identifier (0 for stage-level packets).
    /// </summary>
    public long AccountId { get; }

    /// <summary>
    /// Gets the error code.
    /// </summary>
    public ushort ErrorCode { get; }

    /// <summary>
    /// Gets the payload data.
    /// </summary>
    public IPayload Payload { get; }

    /// <summary>
    /// Gets the type of routing packet.
    /// </summary>
    public RoutePacketType PacketType { get; }

    /// <summary>
    /// Gets the timer callback for timer packets.
    /// </summary>
    public Func<Task>? TimerCallback { get; }

    /// <summary>
    /// Gets the timer identifier for timer packets.
    /// </summary>
    public long TimerId { get; }

    private bool _disposed;

    private RoutePacket(
        string msgId,
        ushort msgSeq,
        int stageId,
        long accountId,
        ushort errorCode,
        IPayload payload,
        RoutePacketType packetType,
        Func<Task>? timerCallback = null,
        long timerId = 0)
    {
        MsgId = msgId;
        MsgSeq = msgSeq;
        StageId = stageId;
        AccountId = accountId;
        ErrorCode = errorCode;
        Payload = payload;
        PacketType = packetType;
        TimerCallback = timerCallback;
        TimerId = timerId;
    }

    /// <summary>
    /// Creates a route packet for a client message targeting a specific actor.
    /// </summary>
    /// <param name="stageId">The target stage identifier.</param>
    /// <param name="accountId">The target account identifier.</param>
    /// <param name="packet">The source packet.</param>
    /// <returns>A new route packet configured for client message routing.</returns>
    public static RoutePacket ClientPacketOf(int stageId, long accountId, IPacket packet) => new(
        packet.MsgId, packet.MsgSeq, stageId, accountId, packet.ErrorCode, packet.Payload,
        RoutePacketType.ClientPacket);

    /// <summary>
    /// Creates a route packet for a stage-level message.
    /// </summary>
    /// <param name="stageId">The target stage identifier.</param>
    /// <param name="packet">The source packet.</param>
    /// <returns>A new route packet configured for stage message routing.</returns>
    public static RoutePacket StagePacketOf(int stageId, IPacket packet) => new(
        packet.MsgId, packet.MsgSeq, stageId, 0, packet.ErrorCode, packet.Payload,
        RoutePacketType.StagePacket);

    /// <summary>
    /// Creates a route packet for a timer callback.
    /// </summary>
    /// <param name="stageId">The target stage identifier.</param>
    /// <param name="timerId">The timer identifier.</param>
    /// <param name="callback">The timer callback to execute.</param>
    /// <returns>A new route packet configured for timer execution.</returns>
    public static RoutePacket TimerPacketOf(int stageId, long timerId, Func<Task> callback) => new(
        string.Empty, 0, stageId, 0, 0, EmptyPayload.Instance,
        RoutePacketType.Timer, callback, timerId);

    /// <summary>
    /// Creates a route packet for an async block result.
    /// </summary>
    /// <param name="stageId">The target stage identifier.</param>
    /// <param name="postCallback">The post-callback to execute with the result.</param>
    /// <param name="result">The result from the async operation.</param>
    /// <returns>A new route packet configured for async block result processing.</returns>
    public static RoutePacket AsyncBlockResultOf(int stageId, Func<object?, Task> postCallback, object? result) => new(
        string.Empty, 0, stageId, 0, 0, new AsyncBlockPayload(postCallback, result),
        RoutePacketType.AsyncBlockResult);

    /// <summary>
    /// Releases resources used by the route packet.
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Payload?.Dispose();
    }
}

/// <summary>
/// Specifies the type of route packet for internal routing logic.
/// </summary>
public enum RoutePacketType
{
    /// <summary>
    /// A packet from a client targeting a specific actor.
    /// </summary>
    ClientPacket,

    /// <summary>
    /// A packet targeting the stage itself (not a specific actor).
    /// </summary>
    StagePacket,

    /// <summary>
    /// A timer callback execution packet.
    /// </summary>
    Timer,

    /// <summary>
    /// A result from an async block operation.
    /// </summary>
    AsyncBlockResult
}
