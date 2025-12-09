#nullable enable

using System;
using Microsoft.Extensions.Logging;
using PlayHouse.Abstractions;
using PlayHouse.Core.Stage;

namespace PlayHouse.Core.Messaging;

/// <summary>
/// Routes packets to the appropriate stage based on routing information.
/// </summary>
/// <remarks>
/// PacketDispatcher is the central routing component that:
/// 1. Resolves target stages from packet headers
/// 2. Posts packets to the correct stage's message queue
/// 3. Handles routing errors and missing stages
/// </remarks>
public sealed class PacketDispatcher
{
    private readonly StagePool _stagePool;
    private readonly ILogger<PacketDispatcher> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="PacketDispatcher"/> class.
    /// </summary>
    /// <param name="stagePool">The stage pool for resolving stages.</param>
    /// <param name="logger">The logger instance.</param>
    public PacketDispatcher(StagePool stagePool, ILogger<PacketDispatcher> logger)
    {
        _stagePool = stagePool;
        _logger = logger;
    }

    /// <summary>
    /// Dispatches a route packet to its target stage.
    /// </summary>
    /// <param name="packet">The route packet to dispatch.</param>
    /// <returns>True if the packet was dispatched successfully; otherwise, false.</returns>
    public bool Dispatch(RoutePacket packet)
    {
        var stage = _stagePool.GetStage(packet.StageId);
        if (stage == null)
        {
            _logger.LogWarning("Stage {StageId} not found for packet {MsgId} (Type: {PacketType})",
                packet.StageId, packet.MsgId, packet.PacketType);
            return false;
        }

        try
        {
            stage.Post(packet);
            _logger.LogTrace("Dispatched {PacketType} to stage {StageId}: {MsgId}",
                packet.PacketType, packet.StageId, packet.MsgId);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error dispatching packet to stage {StageId}: {MsgId}",
                packet.StageId, packet.MsgId);
            return false;
        }
    }

    /// <summary>
    /// Dispatches a client packet to an actor in a stage.
    /// </summary>
    /// <param name="stageId">The target stage identifier.</param>
    /// <param name="accountId">The target account identifier.</param>
    /// <param name="packet">The packet to dispatch.</param>
    /// <returns>True if the packet was dispatched successfully; otherwise, false.</returns>
    public bool DispatchToActor(int stageId, long accountId, IPacket packet)
    {
        var routePacket = RoutePacket.ClientPacketOf(stageId, accountId, packet);
        return Dispatch(routePacket);
    }

    /// <summary>
    /// Dispatches a stage-level packet.
    /// </summary>
    /// <param name="stageId">The target stage identifier.</param>
    /// <param name="packet">The packet to dispatch.</param>
    /// <returns>True if the packet was dispatched successfully; otherwise, false.</returns>
    public bool DispatchToStage(int stageId, IPacket packet)
    {
        var routePacket = RoutePacket.StagePacketOf(stageId, packet);
        return Dispatch(routePacket);
    }

    /// <summary>
    /// Dispatches a timer callback to a stage.
    /// </summary>
    /// <param name="stageId">The target stage identifier.</param>
    /// <param name="timerId">The timer identifier.</param>
    /// <param name="callback">The timer callback to execute.</param>
    /// <returns>True if the timer was dispatched successfully; otherwise, false.</returns>
    public bool DispatchTimer(int stageId, long timerId, Func<System.Threading.Tasks.Task> callback)
    {
        var routePacket = RoutePacket.TimerPacketOf(stageId, timerId, callback);
        return Dispatch(routePacket);
    }

    /// <summary>
    /// Dispatches an async block result to a stage.
    /// </summary>
    /// <param name="stageId">The target stage identifier.</param>
    /// <param name="postCallback">The post-callback to execute with the result.</param>
    /// <param name="result">The result from the async operation.</param>
    /// <returns>True if the result was dispatched successfully; otherwise, false.</returns>
    public bool DispatchAsyncBlockResult(int stageId, Func<object?, System.Threading.Tasks.Task> postCallback, object? result)
    {
        var routePacket = RoutePacket.AsyncBlockResultOf(stageId, postCallback, result);
        return Dispatch(routePacket);
    }

    /// <summary>
    /// Gets statistics about dispatching for monitoring.
    /// </summary>
    public DispatcherStatistics GetStatistics()
    {
        var stages = _stagePool.GetAllStages();
        var totalQueueDepth = 0;
        var stagesProcessing = 0;

        foreach (var stage in stages)
        {
            totalQueueDepth += stage.QueueDepth;
            if (stage.IsProcessing)
            {
                stagesProcessing++;
            }
        }

        return new DispatcherStatistics
        {
            TotalStages = _stagePool.GetStageCount(),
            StagesProcessing = stagesProcessing,
            TotalQueueDepth = totalQueueDepth
        };
    }
}

/// <summary>
/// Statistics about packet dispatching.
/// </summary>
public sealed class DispatcherStatistics
{
    /// <summary>
    /// Gets the total number of stages.
    /// </summary>
    public int TotalStages { get; init; }

    /// <summary>
    /// Gets the number of stages currently processing messages.
    /// </summary>
    public int StagesProcessing { get; init; }

    /// <summary>
    /// Gets the total queue depth across all stages.
    /// </summary>
    public int TotalQueueDepth { get; init; }
}
