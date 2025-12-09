namespace PlayHouse.Connector.Connection;

/// <summary>
/// Abstraction for network connection (TCP or WebSocket).
/// </summary>
public interface IConnection : IAsyncDisposable
{
    /// <summary>
    /// Gets whether the connection is currently active.
    /// </summary>
    bool IsConnected { get; }

    /// <summary>
    /// Raised when data is received from the server.
    /// </summary>
    event EventHandler<byte[]>? DataReceived;

    /// <summary>
    /// Raised when the connection is disconnected.
    /// </summary>
    event EventHandler<Exception?>? Disconnected;

    /// <summary>
    /// Connects to the specified server endpoint.
    /// </summary>
    /// <param name="host">Server hostname or IP address</param>
    /// <param name="port">Server port</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task ConnectAsync(string host, int port, CancellationToken cancellationToken = default);

    /// <summary>
    /// Disconnects from the server gracefully.
    /// </summary>
    Task DisconnectAsync();

    /// <summary>
    /// Sends data to the server.
    /// </summary>
    /// <param name="data">Data to send</param>
    /// <param name="cancellationToken">Cancellation token</param>
    ValueTask SendAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default);
}
