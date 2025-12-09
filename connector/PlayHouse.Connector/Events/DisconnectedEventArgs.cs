namespace PlayHouse.Connector.Events;

/// <summary>
/// Event arguments for disconnection events.
/// </summary>
public sealed class DisconnectedEventArgs : EventArgs
{
    /// <summary>
    /// Gets the reason for disconnection.
    /// </summary>
    public string? Reason { get; }

    /// <summary>
    /// Gets the exception that caused the disconnection, if any.
    /// </summary>
    public Exception? Exception { get; }

    /// <summary>
    /// Gets whether the disconnection was initiated by the client.
    /// </summary>
    public bool WasIntentional { get; }

    /// <summary>
    /// Gets whether reconnection should be attempted.
    /// </summary>
    public bool ShouldReconnect { get; }

    /// <summary>
    /// Gets the timestamp when disconnection occurred.
    /// </summary>
    public DateTime Timestamp { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="DisconnectedEventArgs"/> class.
    /// </summary>
    /// <param name="reason">Disconnection reason</param>
    /// <param name="exception">Exception that caused disconnection</param>
    /// <param name="wasIntentional">Whether disconnection was client-initiated</param>
    /// <param name="shouldReconnect">Whether reconnection should be attempted</param>
    public DisconnectedEventArgs(
        string? reason = null,
        Exception? exception = null,
        bool wasIntentional = false,
        bool shouldReconnect = false)
    {
        Reason = reason;
        Exception = exception;
        WasIntentional = wasIntentional;
        ShouldReconnect = shouldReconnect;
        Timestamp = DateTime.UtcNow;
    }
}
