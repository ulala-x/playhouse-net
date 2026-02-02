#nullable enable

namespace PlayHouse.Runtime.Shared;

/// <summary>
/// A thread-safe boolean value using atomic operations.
/// </summary>
/// <remarks>
/// Used for lock-free event loop coordination where a single thread
/// must be guaranteed to process messages at a time.
/// </remarks>
public sealed class AtomicBoolean
{
    private int _value;

    /// <summary>
    /// Initializes a new instance of the <see cref="AtomicBoolean"/> class.
    /// </summary>
    /// <param name="initialValue">Initial value.</param>
    public AtomicBoolean(bool initialValue = false)
    {
        _value = initialValue ? 1 : 0;
    }

    /// <summary>
    /// Gets the current value.
    /// </summary>
    public bool Value => Volatile.Read(ref _value) == 1;

    /// <summary>
    /// Sets the value.
    /// </summary>
    /// <param name="newValue">New value to set.</param>
    public void Set(bool newValue)
    {
        Volatile.Write(ref _value, newValue ? 1 : 0);
    }

    /// <summary>
    /// Atomically sets the value to the given updated value
    /// if the current value equals the expected value.
    /// </summary>
    /// <param name="expected">Expected current value.</param>
    /// <param name="newValue">New value to set if expected matches.</param>
    /// <returns>true if successful (current value was equal to expected).</returns>
    public bool CompareAndSet(bool expected, bool newValue)
    {
        var expectedInt = expected ? 1 : 0;
        var newInt = newValue ? 1 : 0;
        return Interlocked.CompareExchange(ref _value, newInt, expectedInt) == expectedInt;
    }

    /// <summary>
    /// Atomically sets the value to true and returns the previous value.
    /// </summary>
    /// <returns>Previous value.</returns>
    public bool GetAndSet(bool newValue)
    {
        var newInt = newValue ? 1 : 0;
        return Interlocked.Exchange(ref _value, newInt) == 1;
    }

    /// <summary>
    /// Implicit conversion to bool.
    /// </summary>
    public static implicit operator bool(AtomicBoolean atomicBoolean) => atomicBoolean.Value;

    /// <inheritdoc/>
    public override string ToString() => Value.ToString();
}
