#nullable enable

using System;
using System.Threading;

namespace PlayHouse.Core.Timer;

/// <summary>
/// Represents metadata for a timer instance.
/// </summary>
/// <remarks>
/// TimerEntry tracks the timer's configuration, execution state, and provides
/// lifecycle management for System.Threading.Timer instances.
/// </remarks>
public sealed class TimerEntry : IDisposable
{
    private readonly System.Threading.Timer _systemTimer;
    private int _executionCount;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="TimerEntry"/> class.
    /// </summary>
    /// <param name="timerId">The unique timer identifier.</param>
    /// <param name="stageId">The stage identifier this timer belongs to.</param>
    /// <param name="timerType">The type of timer (Repeat or Count).</param>
    /// <param name="initialDelay">The initial delay before first execution.</param>
    /// <param name="period">The period between executions.</param>
    /// <param name="maxExecutions">The maximum number of executions (null for infinite).</param>
    /// <param name="callback">The callback to execute on each timer tick.</param>
    /// <param name="systemTimer">The underlying System.Threading.Timer instance.</param>
    public TimerEntry(
        long timerId,
        int stageId,
        TimerType timerType,
        TimeSpan initialDelay,
        TimeSpan period,
        int? maxExecutions,
        Func<System.Threading.Tasks.Task> callback,
        System.Threading.Timer systemTimer)
    {
        TimerId = timerId;
        StageId = stageId;
        TimerType = timerType;
        InitialDelay = initialDelay;
        Period = period;
        MaxExecutions = maxExecutions;
        Callback = callback;
        _systemTimer = systemTimer;
        CreatedAt = DateTime.UtcNow;
        _executionCount = 0;
    }

    /// <summary>
    /// Gets the unique timer identifier.
    /// </summary>
    public long TimerId { get; }

    /// <summary>
    /// Gets the stage identifier this timer belongs to.
    /// </summary>
    public int StageId { get; }

    /// <summary>
    /// Gets the type of timer.
    /// </summary>
    public TimerType TimerType { get; }

    /// <summary>
    /// Gets the initial delay before first execution.
    /// </summary>
    public TimeSpan InitialDelay { get; }

    /// <summary>
    /// Gets the period between executions.
    /// </summary>
    public TimeSpan Period { get; }

    /// <summary>
    /// Gets the maximum number of executions (null for infinite).
    /// </summary>
    public int? MaxExecutions { get; }

    /// <summary>
    /// Gets the callback to execute on each timer tick.
    /// </summary>
    public Func<System.Threading.Tasks.Task> Callback { get; }

    /// <summary>
    /// Gets the timestamp when this timer was created.
    /// </summary>
    public DateTime CreatedAt { get; }

    /// <summary>
    /// Gets the timestamp when this timer was last executed (null if never executed).
    /// </summary>
    public DateTime? LastExecutedAt { get; private set; }

    /// <summary>
    /// Gets the number of times this timer has been executed.
    /// </summary>
    public int ExecutionCount => _executionCount;

    /// <summary>
    /// Gets a value indicating whether this timer has completed all executions.
    /// </summary>
    public bool IsCompleted => MaxExecutions.HasValue && _executionCount >= MaxExecutions.Value;

    /// <summary>
    /// Increments the execution count and updates last executed timestamp.
    /// </summary>
    /// <returns>The new execution count.</returns>
    public int IncrementExecutionCount()
    {
        LastExecutedAt = DateTime.UtcNow;
        return Interlocked.Increment(ref _executionCount);
    }

    /// <summary>
    /// Disposes the timer and releases resources.
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;

        _disposed = true;
        _systemTimer.Dispose();
    }
}

/// <summary>
/// Specifies the type of timer.
/// </summary>
public enum TimerType
{
    /// <summary>
    /// A timer that repeats indefinitely until cancelled.
    /// </summary>
    Repeat,

    /// <summary>
    /// A timer that executes a specific number of times then stops.
    /// </summary>
    Count
}
