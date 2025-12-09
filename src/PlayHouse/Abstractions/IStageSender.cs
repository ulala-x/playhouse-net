#nullable enable

namespace PlayHouse.Abstractions;

/// <summary>
/// Provides functionality for stages to send packets, manage timers, and control stage lifecycle.
/// </summary>
public interface IStageSender : ISender
{
    /// <summary>
    /// Gets the stage identifier.
    /// </summary>
    int StageId { get; }

    /// <summary>
    /// Gets the type name of the stage.
    /// </summary>
    string StageType { get; }

    /// <summary>
    /// Sends a packet to another stage.
    /// </summary>
    /// <param name="targetStageId">The identifier of the target stage.</param>
    /// <param name="packet">The packet to send.</param>
    /// <returns>A task representing the asynchronous send operation.</returns>
    ValueTask SendToStageAsync(int targetStageId, IPacket packet);

    /// <summary>
    /// Broadcasts a packet to all actors in the stage.
    /// </summary>
    /// <param name="packet">The packet to broadcast.</param>
    /// <returns>A task representing the asynchronous broadcast operation.</returns>
    ValueTask BroadcastAsync(IPacket packet);

    /// <summary>
    /// Broadcasts a packet to filtered actors in the stage.
    /// </summary>
    /// <param name="packet">The packet to broadcast.</param>
    /// <param name="filter">A predicate to filter which actors receive the packet.</param>
    /// <returns>A task representing the asynchronous broadcast operation.</returns>
    ValueTask BroadcastAsync(IPacket packet, Func<IActor, bool> filter);

    /// <summary>
    /// Adds a repeating timer that executes periodically.
    /// </summary>
    /// <param name="initialDelay">The delay before the first execution.</param>
    /// <param name="period">The period between subsequent executions.</param>
    /// <param name="callback">The callback to execute on each timer tick.</param>
    /// <returns>A unique timer identifier that can be used to cancel the timer.</returns>
    long AddRepeatTimer(TimeSpan initialDelay, TimeSpan period, Func<Task> callback);

    /// <summary>
    /// Adds a repeating timer that executes periodically with the same delay and period.
    /// </summary>
    /// <param name="period">The delay before first execution and period between subsequent executions.</param>
    /// <param name="callback">The callback to execute on each timer tick.</param>
    /// <returns>A unique timer identifier that can be used to cancel the timer.</returns>
    long AddRepeatTimer(TimeSpan period, Func<Task> callback)
        => AddRepeatTimer(period, period, callback);

    /// <summary>
    /// Adds a count-limited timer that executes a specific number of times.
    /// </summary>
    /// <param name="initialDelay">The delay before the first execution.</param>
    /// <param name="period">The period between subsequent executions.</param>
    /// <param name="count">The number of times to execute the callback.</param>
    /// <param name="callback">The callback to execute on each timer tick.</param>
    /// <returns>A unique timer identifier that can be used to cancel the timer.</returns>
    long AddCountTimer(TimeSpan initialDelay, TimeSpan period, int count, Func<Task> callback);

    /// <summary>
    /// Adds a count-limited timer that executes a specific number of times with the same delay and period.
    /// </summary>
    /// <param name="period">The delay before first execution and period between subsequent executions.</param>
    /// <param name="count">The number of times to execute the callback.</param>
    /// <param name="callback">The callback to execute on each timer tick.</param>
    /// <returns>A unique timer identifier that can be used to cancel the timer.</returns>
    long AddCountTimer(TimeSpan period, int count, Func<Task> callback)
        => AddCountTimer(period, period, count, callback);

    /// <summary>
    /// Cancels a previously registered timer.
    /// </summary>
    /// <param name="timerId">The identifier of the timer to cancel.</param>
    void CancelTimer(long timerId);

    /// <summary>
    /// Checks whether a timer with the specified identifier exists.
    /// </summary>
    /// <param name="timerId">The timer identifier to check.</param>
    /// <returns>True if the timer exists; otherwise, false.</returns>
    bool HasTimer(long timerId);

    /// <summary>
    /// Closes the stage, preventing new actors from joining and triggering cleanup.
    /// </summary>
    void CloseStage();

    /// <summary>
    /// Executes an asynchronous operation that blocks the stage's message processing.
    /// </summary>
    /// <param name="preCallback">The asynchronous operation to execute outside the stage context.</param>
    /// <param name="postCallback">An optional callback to process the result back in the stage context.</param>
    /// <remarks>
    /// This method allows executing operations that may block (e.g., database calls) without
    /// blocking the stage's message processing queue. The preCallback runs on a separate thread,
    /// and the postCallback runs back in the stage's context with the result.
    /// </remarks>
    void AsyncBlock(Func<Task<object?>> preCallback, Func<object?, Task>? postCallback = null);
}
