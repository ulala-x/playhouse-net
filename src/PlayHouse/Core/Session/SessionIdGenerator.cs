#nullable enable

using System.Threading;

namespace PlayHouse.Core.Session;

/// <summary>
/// Generates unique session identifiers using thread-safe atomic operations.
/// </summary>
/// <remarks>
/// SessionIdGenerator uses Interlocked.Increment to ensure thread-safe,
/// monotonically increasing session IDs without requiring locks.
/// </remarks>
internal static class SessionIdGenerator
{
    private static long _sessionIdCounter = 0;

    /// <summary>
    /// Generates a unique session identifier.
    /// </summary>
    /// <returns>A unique session ID.</returns>
    /// <remarks>
    /// Session IDs start from 1 and increment monotonically.
    /// This method is thread-safe and can be called from multiple threads concurrently.
    /// </remarks>
    public static long Generate()
    {
        return Interlocked.Increment(ref _sessionIdCounter);
    }

    /// <summary>
    /// Gets the current session ID counter value for monitoring purposes.
    /// </summary>
    /// <returns>The current counter value.</returns>
    /// <remarks>
    /// This value represents the total number of sessions created since application start.
    /// Note that this is a snapshot and may change immediately after being read.
    /// </remarks>
    public static long GetCurrentCount()
    {
        return Interlocked.Read(ref _sessionIdCounter);
    }

    /// <summary>
    /// Resets the session ID counter to zero.
    /// </summary>
    /// <remarks>
    /// This should only be used in testing scenarios. In production, session IDs
    /// should continue incrementing throughout the application lifetime.
    /// </remarks>
    internal static void Reset()
    {
        Interlocked.Exchange(ref _sessionIdCounter, 0);
    }
}
