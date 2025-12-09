#nullable enable

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using PlayHouse.Abstractions;

namespace PlayHouse.Core.Timer;

/// <summary>
/// Manages System.Threading.Timer instances for stages.
/// </summary>
/// <remarks>
/// TimerManager provides:
/// 1. Thread-safe timer creation and cancellation
/// 2. Support for repeat and count-limited timers
/// 3. Automatic cleanup of completed timers
/// 4. Stage-based timer grouping for bulk operations
/// 5. Integration with the event loop via callback dispatch
/// </remarks>
internal sealed class TimerManager : IDisposable
{
    private readonly ConcurrentDictionary<long, TimerEntry> _timers = new();
    private readonly Action<RoutePacket> _dispatchAction;
    private readonly ILogger<TimerManager> _logger;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="TimerManager"/> class.
    /// </summary>
    /// <param name="dispatchAction">The action to dispatch timer callbacks to stages.</param>
    /// <param name="logger">The logger instance.</param>
    public TimerManager(Action<RoutePacket> dispatchAction, ILogger<TimerManager> logger)
    {
        _dispatchAction = dispatchAction;
        _logger = logger;
    }

    /// <summary>
    /// Adds a repeating timer that executes indefinitely.
    /// </summary>
    /// <param name="stageId">The stage identifier.</param>
    /// <param name="initialDelay">The delay before the first execution.</param>
    /// <param name="period">The period between subsequent executions.</param>
    /// <param name="callback">The callback to execute on each timer tick.</param>
    /// <returns>A unique timer identifier that can be used to cancel the timer.</returns>
    public long AddRepeatTimer(int stageId, TimeSpan initialDelay, TimeSpan period, Func<Task> callback)
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(TimerManager));
        }

        var timerId = TimerIdGenerator.Generate();

        var systemTimer = new System.Threading.Timer(
            _ => OnTimerTick(timerId),
            null,
            initialDelay,
            period);

        var entry = new TimerEntry(
            timerId,
            stageId,
            TimerType.Repeat,
            initialDelay,
            period,
            null, // No max executions for repeat timer
            callback,
            systemTimer);

        if (_timers.TryAdd(timerId, entry))
        {
            _logger.LogDebug("Added repeat timer {TimerId} for stage {StageId} (delay: {Delay}ms, period: {Period}ms)",
                timerId, stageId, initialDelay.TotalMilliseconds, period.TotalMilliseconds);
            return timerId;
        }

        // Cleanup on failure
        systemTimer.Dispose();
        throw new InvalidOperationException($"Failed to add timer {timerId}");
    }

    /// <summary>
    /// Adds a count-limited timer that executes a specific number of times.
    /// </summary>
    /// <param name="stageId">The stage identifier.</param>
    /// <param name="initialDelay">The delay before the first execution.</param>
    /// <param name="period">The period between subsequent executions.</param>
    /// <param name="count">The number of times to execute the callback.</param>
    /// <param name="callback">The callback to execute on each timer tick.</param>
    /// <returns>A unique timer identifier that can be used to cancel the timer.</returns>
    public long AddCountTimer(int stageId, TimeSpan initialDelay, TimeSpan period, int count, Func<Task> callback)
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(TimerManager));
        }

        if (count <= 0)
        {
            throw new ArgumentException("Count must be greater than zero", nameof(count));
        }

        var timerId = TimerIdGenerator.Generate();

        var systemTimer = new System.Threading.Timer(
            _ => OnTimerTick(timerId),
            null,
            initialDelay,
            period);

        var entry = new TimerEntry(
            timerId,
            stageId,
            TimerType.Count,
            initialDelay,
            period,
            count,
            callback,
            systemTimer);

        if (_timers.TryAdd(timerId, entry))
        {
            _logger.LogDebug("Added count timer {TimerId} for stage {StageId} (delay: {Delay}ms, period: {Period}ms, count: {Count})",
                timerId, stageId, initialDelay.TotalMilliseconds, period.TotalMilliseconds, count);
            return timerId;
        }

        // Cleanup on failure
        systemTimer.Dispose();
        throw new InvalidOperationException($"Failed to add timer {timerId}");
    }

    /// <summary>
    /// Cancels a timer.
    /// </summary>
    /// <param name="timerId">The timer identifier.</param>
    /// <returns>True if the timer was found and cancelled; otherwise, false.</returns>
    public bool CancelTimer(long timerId)
    {
        if (_timers.TryRemove(timerId, out var entry))
        {
            entry.Dispose();
            _logger.LogDebug("Cancelled timer {TimerId}", timerId);
            return true;
        }

        _logger.LogWarning("Timer {TimerId} not found for cancellation", timerId);
        return false;
    }

    /// <summary>
    /// Checks if a timer exists.
    /// </summary>
    /// <param name="timerId">The timer identifier.</param>
    /// <returns>True if the timer exists; otherwise, false.</returns>
    public bool HasTimer(long timerId)
    {
        return _timers.ContainsKey(timerId);
    }

    /// <summary>
    /// Cancels all timers for a specific stage.
    /// </summary>
    /// <param name="stageId">The stage identifier.</param>
    /// <returns>The number of timers cancelled.</returns>
    public int CancelAllTimersForStage(int stageId)
    {
        var timersToCancel = _timers.Values
            .Where(t => t.StageId == stageId)
            .ToList();

        foreach (var timer in timersToCancel)
        {
            CancelTimer(timer.TimerId);
        }

        _logger.LogInformation("Cancelled {Count} timers for stage {StageId}",
            timersToCancel.Count, stageId);

        return timersToCancel.Count;
    }

    /// <summary>
    /// Gets all timers for a specific stage.
    /// </summary>
    /// <param name="stageId">The stage identifier.</param>
    /// <returns>A collection of timer entries.</returns>
    public IEnumerable<TimerEntry> GetTimersForStage(int stageId)
    {
        return _timers.Values
            .Where(t => t.StageId == stageId)
            .ToList();
    }

    /// <summary>
    /// Handles timer tick events.
    /// </summary>
    private void OnTimerTick(long timerId)
    {
        if (!_timers.TryGetValue(timerId, out var entry))
        {
            return; // Timer was cancelled
        }

        try
        {
            // Increment execution count
            var executionCount = entry.IncrementExecutionCount();

            // Create and dispatch timer packet
            var routePacket = RoutePacket.TimerPacketOf(entry.StageId, timerId, entry.Callback);
            _dispatchAction(routePacket);

            _logger.LogTrace("Timer {TimerId} tick {Count} dispatched to stage {StageId}",
                timerId, executionCount, entry.StageId);

            // Check if count timer is completed
            if (entry.IsCompleted)
            {
                _logger.LogDebug("Count timer {TimerId} completed after {Count} executions",
                    timerId, executionCount);
                CancelTimer(timerId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling timer tick for timer {TimerId}", timerId);
        }
    }

    /// <summary>
    /// Gets statistics about active timers for monitoring.
    /// </summary>
    public TimerStatistics GetStatistics()
    {
        var timers = _timers.Values.ToList();

        return new TimerStatistics
        {
            TotalTimers = timers.Count,
            RepeatTimers = timers.Count(t => t.TimerType == TimerType.Repeat),
            CountTimers = timers.Count(t => t.TimerType == TimerType.Count),
            TimersByStage = timers
                .GroupBy(t => t.StageId)
                .ToDictionary(g => g.Key, g => g.Count())
        };
    }

    /// <summary>
    /// Disposes the timer manager and all active timers.
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;

        _disposed = true;

        var timers = _timers.Values.ToList();
        _timers.Clear();

        foreach (var timer in timers)
        {
            try
            {
                timer.Dispose();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error disposing timer {TimerId}", timer.TimerId);
            }
        }

        _logger.LogInformation("Disposed {Count} timers", timers.Count);
    }
}

/// <summary>
/// Statistics about active timers.
/// </summary>
public sealed class TimerStatistics
{
    /// <summary>
    /// Gets the total number of active timers.
    /// </summary>
    public int TotalTimers { get; init; }

    /// <summary>
    /// Gets the number of repeat timers.
    /// </summary>
    public int RepeatTimers { get; init; }

    /// <summary>
    /// Gets the number of count timers.
    /// </summary>
    public int CountTimers { get; init; }

    /// <summary>
    /// Gets the number of timers per stage.
    /// </summary>
    public Dictionary<int, int> TimersByStage { get; init; } = new();
}
