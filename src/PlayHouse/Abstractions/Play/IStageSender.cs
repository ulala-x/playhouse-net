#nullable enable

namespace PlayHouse.Abstractions.Play;

/// <summary>
/// Delegate for asynchronous pre-callback in AsyncBlock.
/// Executed on a thread pool thread (outside the event loop).
/// </summary>
/// <returns>Result to be passed to the post-callback.</returns>
public delegate Task<object?> AsyncPreCallback();

/// <summary>
/// Delegate for asynchronous post-callback in AsyncBlock.
/// Executed on the Stage event loop (safe to access Stage state).
/// </summary>
/// <param name="result">Result from the pre-callback.</param>
public delegate Task AsyncPostCallback(object? result);

/// <summary>
/// Delegate for timer callbacks.
/// </summary>
public delegate Task TimerCallback();

/// <summary>
/// Provides Stage-specific communication and management capabilities.
/// </summary>
/// <remarks>
/// IStageSender extends ISender with:
/// - Timer management (repeat, count, cancel)
/// - Stage lifecycle management (close)
/// - AsyncBlock for safe external I/O operations
/// - Client messaging with StageId context
/// </remarks>
public interface IStageSender : ISender
{
    /// <summary>
    /// Gets the unique identifier for this Stage.
    /// </summary>
    long StageId { get; }

    /// <summary>
    /// Gets the type identifier for this Stage.
    /// </summary>
    string StageType { get; }

    #region Timer Management

    /// <summary>
    /// Adds a repeating timer that fires indefinitely.
    /// </summary>
    /// <param name="initialDelay">Time before the first callback.</param>
    /// <param name="period">Interval between subsequent callbacks.</param>
    /// <param name="callback">The callback to execute on each timer tick.</param>
    /// <returns>A unique timer ID that can be used to cancel the timer.</returns>
    long AddRepeatTimer(TimeSpan initialDelay, TimeSpan period, TimerCallback callback);

    /// <summary>
    /// Adds a timer that fires a specified number of times.
    /// </summary>
    /// <param name="initialDelay">Time before the first callback.</param>
    /// <param name="period">Interval between subsequent callbacks.</param>
    /// <param name="count">Number of times to fire before auto-canceling.</param>
    /// <param name="callback">The callback to execute on each timer tick.</param>
    /// <returns>A unique timer ID that can be used to cancel the timer.</returns>
    long AddCountTimer(TimeSpan initialDelay, TimeSpan period, int count, TimerCallback callback);

    /// <summary>
    /// Cancels an active timer.
    /// </summary>
    /// <param name="timerId">The ID of the timer to cancel.</param>
    void CancelTimer(long timerId);

    /// <summary>
    /// Checks if a timer is still active.
    /// </summary>
    /// <param name="timerId">The ID of the timer to check.</param>
    /// <returns>True if the timer exists and is active; otherwise, false.</returns>
    bool HasTimer(long timerId);

    #endregion

    #region Stage Management

    /// <summary>
    /// Closes this Stage, canceling all timers and triggering cleanup.
    /// </summary>
    /// <remarks>
    /// This method:
    /// 1. Cancels all active timers
    /// 2. Sends a destroy message to the dispatcher
    /// 3. Triggers IStage.OnDestroy()
    /// </remarks>
    void CloseStage();

    #endregion

    #region AsyncBlock

    /// <summary>
    /// Executes an asynchronous operation outside the event loop,
    /// then optionally processes the result back on the event loop.
    /// </summary>
    /// <param name="preCallback">
    /// Executed on a thread pool thread. Use for external I/O
    /// (database queries, HTTP calls, etc.).
    /// </param>
    /// <param name="postCallback">
    /// Executed on the Stage event loop after preCallback completes.
    /// Safe to access Stage state here.
    /// </param>
    /// <remarks>
    /// AsyncBlock provides a safe pattern for performing blocking I/O
    /// without blocking the Stage event loop:
    /// 1. preCallback runs on ThreadPool (can block)
    /// 2. Result is captured
    /// 3. postCallback runs on Stage event loop (safe state access)
    /// </remarks>
    void AsyncBlock(AsyncPreCallback preCallback, AsyncPostCallback? postCallback = null);

    #endregion

    #region Client Communication

    /// <summary>
    /// Sends a message to a specific client.
    /// </summary>
    /// <param name="sessionServerId">The session server ID.</param>
    /// <param name="sid">The session ID.</param>
    /// <param name="packet">The packet to send.</param>
    void SendToClient(string sessionServerId, long sid, IPacket packet);

    #endregion
}
