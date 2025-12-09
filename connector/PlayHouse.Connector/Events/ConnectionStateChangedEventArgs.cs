namespace PlayHouse.Connector.Events;

/// <summary>
/// Event arguments for connection state changes.
/// </summary>
public sealed class ConnectionStateChangedEventArgs : EventArgs
{
    /// <summary>
    /// Gets the previous connection state.
    /// </summary>
    public ConnectionState PreviousState { get; }

    /// <summary>
    /// Gets the new connection state.
    /// </summary>
    public ConnectionState NewState { get; }

    /// <summary>
    /// Gets the timestamp when the state changed.
    /// </summary>
    public DateTime Timestamp { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="ConnectionStateChangedEventArgs"/> class.
    /// </summary>
    /// <param name="previousState">Previous connection state</param>
    /// <param name="newState">New connection state</param>
    public ConnectionStateChangedEventArgs(ConnectionState previousState, ConnectionState newState)
    {
        PreviousState = previousState;
        NewState = newState;
        Timestamp = DateTime.UtcNow;
    }
}
