#nullable enable

namespace PlayHouse.Runtime.ClientTransport;

/// <summary>
/// Transport server interface for accepting client connections.
/// </summary>
/// <remarks>
/// Provides a unified interface for different transport protocols (TCP, WebSocket, etc.).
/// </remarks>
public interface ITransportServer : IAsyncDisposable
{
    /// <summary>
    /// Gets the number of active sessions.
    /// </summary>
    int SessionCount { get; }

    /// <summary>
    /// Starts the server and begins accepting connections.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task StartAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Stops the server and disconnects all sessions.
    /// </summary>
    Task StopAsync();

    /// <summary>
    /// Gets a session by its identifier.
    /// </summary>
    /// <param name="sessionId">The session identifier.</param>
    /// <returns>The session if found; otherwise null.</returns>
    ITransportSession? GetSession(long sessionId);

    /// <summary>
    /// Disconnects a specific session.
    /// </summary>
    /// <param name="sessionId">The session identifier.</param>
    ValueTask DisconnectSessionAsync(long sessionId);

    /// <summary>
    /// Disconnects all active sessions.
    /// </summary>
    Task DisconnectAllSessionsAsync();

    /// <summary>
    /// Gets all active sessions.
    /// </summary>
    /// <returns>An enumerable of all active sessions.</returns>
    IEnumerable<ITransportSession> GetAllSessions();
}
