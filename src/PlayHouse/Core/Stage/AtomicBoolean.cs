#nullable enable

using System.Threading;

namespace PlayHouse.Core.Stage;

/// <summary>
/// Provides atomic compare-and-swap (CAS) operations for boolean values.
/// </summary>
/// <remarks>
/// AtomicBoolean is a lock-free synchronization primitive that uses Interlocked
/// operations to provide thread-safe boolean operations with minimal overhead.
/// This is essential for the lock-free event loop pattern in BaseStage.
/// </remarks>
public sealed class AtomicBoolean
{
    private int _value;

    /// <summary>
    /// Initializes a new instance of the <see cref="AtomicBoolean"/> class.
    /// </summary>
    /// <param name="initialValue">The initial boolean value.</param>
    public AtomicBoolean(bool initialValue = false)
    {
        _value = initialValue ? 1 : 0;
    }

    /// <summary>
    /// Gets the current value.
    /// </summary>
    public bool Value => _value == 1;

    /// <summary>
    /// Sets the value atomically.
    /// </summary>
    /// <param name="value">The value to set.</param>
    public void Set(bool value)
    {
        Interlocked.Exchange(ref _value, value ? 1 : 0);
    }

    /// <summary>
    /// Atomically compares the current value with the expected value and,
    /// if they are equal, updates the value.
    /// </summary>
    /// <param name="expected">The expected value to compare against.</param>
    /// <param name="update">The value to set if the comparison succeeds.</param>
    /// <returns>True if the comparison succeeded and the value was updated; otherwise, false.</returns>
    /// <remarks>
    /// This is the core operation for lock-free algorithms. It ensures that only one
    /// thread can successfully change the value from false to true, preventing race conditions.
    /// </remarks>
    public bool CompareAndSet(bool expected, bool update)
    {
        int expectedInt = expected ? 1 : 0;
        int updateInt = update ? 1 : 0;
        return Interlocked.CompareExchange(ref _value, updateInt, expectedInt) == expectedInt;
    }
}
