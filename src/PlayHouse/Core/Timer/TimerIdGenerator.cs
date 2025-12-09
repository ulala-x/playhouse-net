#nullable enable

using System.Threading;

namespace PlayHouse.Core.Timer;

/// <summary>
/// Generates unique timer identifiers using thread-safe atomic operations.
/// </summary>
/// <remarks>
/// TimerIdGenerator uses Interlocked.Increment to ensure thread-safe,
/// monotonically increasing timer IDs without requiring locks.
/// </remarks>
internal static class TimerIdGenerator
{
    private static long _timerIdCounter = 0;

    /// <summary>
    /// Generates a unique timer identifier.
    /// </summary>
    /// <returns>A unique timer ID.</returns>
    /// <remarks>
    /// Timer IDs start from 1 and increment monotonically.
    /// This method is thread-safe and can be called from multiple threads concurrently.
    /// </remarks>
    public static long Generate()
    {
        return Interlocked.Increment(ref _timerIdCounter);
    }

    /// <summary>
    /// Gets the current timer ID counter value for monitoring purposes.
    /// </summary>
    /// <returns>The current counter value.</returns>
    /// <remarks>
    /// This value represents the total number of timers created since application start.
    /// Note that this is a snapshot and may change immediately after being read.
    /// </remarks>
    public static long GetCurrentCount()
    {
        return Interlocked.Read(ref _timerIdCounter);
    }

    /// <summary>
    /// Resets the timer ID counter to zero.
    /// </summary>
    /// <remarks>
    /// This should only be used in testing scenarios. In production, timer IDs
    /// should continue incrementing throughout the application lifetime.
    /// </remarks>
    internal static void Reset()
    {
        Interlocked.Exchange(ref _timerIdCounter, 0);
    }
}
