namespace PlayHouse.Connector;

using Google.Protobuf;
using PlayHouse.Connector.Events;

/// <summary>
/// Main client interface for PlayHouse server communication.
/// Provides connection management, request/response patterns, and event-driven messaging.
/// </summary>
public interface IPlayHouseClient : IAsyncDisposable
{
    /// <summary>
    /// Gets the current connection state.
    /// </summary>
    ConnectionState State { get; }

    /// <summary>
    /// Gets the stage ID assigned after successful connection.
    /// </summary>
    int StageId { get; }

    /// <summary>
    /// Gets the account ID for the connected user.
    /// </summary>
    long AccountId { get; }

    /// <summary>
    /// Gets whether the client is currently connected to the server.
    /// </summary>
    bool IsConnected { get; }

    /// <summary>
    /// Connects to the PlayHouse server and joins a room.
    /// </summary>
    /// <param name="endpoint">Server endpoint (e.g., "tcp://localhost:8080" or "ws://localhost:8080")</param>
    /// <param name="roomToken">Room authentication token</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Join room result with success status and assigned stage ID</returns>
    Task<JoinRoomResult> ConnectAsync(string endpoint, string roomToken, CancellationToken cancellationToken = default);

    /// <summary>
    /// Disconnects from the server gracefully.
    /// </summary>
    /// <param name="reason">Optional disconnection reason</param>
    Task DisconnectAsync(string? reason = null);

    /// <summary>
    /// Attempts to reconnect to the server using the last endpoint and token.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if reconnection was successful</returns>
    Task<bool> ReconnectAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends a request and waits for a typed response.
    /// </summary>
    /// <typeparam name="TRequest">Request message type</typeparam>
    /// <typeparam name="TResponse">Response message type</typeparam>
    /// <param name="request">Request message</param>
    /// <param name="timeout">Optional timeout (default: 30 seconds)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Response wrapper with success status and data</returns>
    Task<Response<TResponse>> RequestAsync<TRequest, TResponse>(
        TRequest request,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
        where TRequest : IMessage
        where TResponse : IMessage, new();

    /// <summary>
    /// Sends a one-way message to the server (no response expected).
    /// </summary>
    /// <typeparam name="T">Message type</typeparam>
    /// <param name="message">Message to send</param>
    ValueTask SendAsync<T>(T message) where T : IMessage;

    /// <summary>
    /// Leaves the current room gracefully.
    /// </summary>
    /// <param name="reason">Optional leave reason</param>
    /// <returns>Leave room result with success status</returns>
    Task<LeaveRoomResult> LeaveRoomAsync(string? reason = null);

    /// <summary>
    /// Raised when connection state changes.
    /// </summary>
    event EventHandler<ConnectionStateChangedEventArgs>? ConnectionStateChanged;

    /// <summary>
    /// Raised when a message is received from the server.
    /// </summary>
    event EventHandler<MessageReceivedEventArgs>? MessageReceived;

    /// <summary>
    /// Raised when a client error occurs.
    /// </summary>
    event EventHandler<ClientErrorEventArgs>? ErrorOccurred;

    /// <summary>
    /// Raised when disconnected from the server.
    /// </summary>
    event EventHandler<DisconnectedEventArgs>? Disconnected;

    /// <summary>
    /// Registers a synchronous message handler for a specific message type.
    /// </summary>
    /// <typeparam name="T">Message type to handle</typeparam>
    /// <param name="handler">Handler action</param>
    /// <returns>Disposable to unregister the handler</returns>
    IDisposable On<T>(Action<T> handler) where T : IMessage, new();

    /// <summary>
    /// Registers an asynchronous message handler for a specific message type.
    /// </summary>
    /// <typeparam name="T">Message type to handle</typeparam>
    /// <param name="handler">Async handler function</param>
    /// <returns>Disposable to unregister the handler</returns>
    IDisposable On<T>(Func<T, Task> handler) where T : IMessage, new();
}

/// <summary>
/// Represents the connection state of the PlayHouse client.
/// </summary>
public enum ConnectionState
{
    /// <summary>Client is disconnected from the server.</summary>
    Disconnected,

    /// <summary>Client is in the process of connecting.</summary>
    Connecting,

    /// <summary>Client is connected and ready for communication.</summary>
    Connected,

    /// <summary>Client is attempting to reconnect after disconnection.</summary>
    Reconnecting,

    /// <summary>Client is in the process of disconnecting.</summary>
    Disconnecting
}

/// <summary>
/// Response wrapper for request/response operations.
/// </summary>
/// <typeparam name="T">Response message type</typeparam>
public readonly record struct Response<T>(
    bool Success,
    ushort ErrorCode,
    T? Data,
    string? ErrorMessage = null) where T : IMessage;

/// <summary>
/// Result of a room join operation.
/// </summary>
public readonly record struct JoinRoomResult(
    bool Success,
    ushort ErrorCode,
    int StageId,
    string? ErrorMessage = null);

/// <summary>
/// Result of a leave room operation.
/// </summary>
public readonly record struct LeaveRoomResult(
    bool Success,
    ushort ErrorCode,
    string? ErrorMessage = null);
