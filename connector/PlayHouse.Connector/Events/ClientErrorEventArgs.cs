namespace PlayHouse.Connector.Events;

/// <summary>
/// Event arguments for client errors.
/// </summary>
public sealed class ClientErrorEventArgs : EventArgs
{
    /// <summary>
    /// Gets the error that occurred.
    /// </summary>
    public Exception Error { get; }

    /// <summary>
    /// Gets the error context (e.g., "Connection", "Encoding", "Protocol").
    /// </summary>
    public string Context { get; }

    /// <summary>
    /// Gets whether the error is recoverable.
    /// </summary>
    public bool IsRecoverable { get; }

    /// <summary>
    /// Gets the timestamp when the error occurred.
    /// </summary>
    public DateTime Timestamp { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="ClientErrorEventArgs"/> class.
    /// </summary>
    /// <param name="error">The error that occurred</param>
    /// <param name="context">Error context</param>
    /// <param name="isRecoverable">Whether the error is recoverable</param>
    public ClientErrorEventArgs(Exception error, string context, bool isRecoverable = false)
    {
        Error = error ?? throw new ArgumentNullException(nameof(error));
        Context = context ?? throw new ArgumentNullException(nameof(context));
        IsRecoverable = isRecoverable;
        Timestamp = DateTime.UtcNow;
    }
}
