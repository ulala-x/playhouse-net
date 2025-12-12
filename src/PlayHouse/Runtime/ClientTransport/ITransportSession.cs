#nullable enable

namespace PlayHouse.Runtime.ClientTransport;

/// <summary>
/// Transport session interface for client connections.
/// </summary>
/// <remarks>
/// Provides a unified interface for different transport protocols (TCP, WebSocket, etc.).
/// Implementations handle protocol-specific details while exposing a common API.
/// </remarks>
public interface ITransportSession : IAsyncDisposable
{
    /// <summary>
    /// Gets the unique session identifier.
    /// </summary>
    long SessionId { get; }

    /// <summary>
    /// Gets or sets whether the session is authenticated.
    /// </summary>
    bool IsAuthenticated { get; set; }

    /// <summary>
    /// Gets whether the session is currently connected.
    /// </summary>
    bool IsConnected { get; }

    /// <summary>
    /// Sends data to the remote endpoint.
    /// </summary>
    /// <param name="data">The data to send.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    ValueTask SendAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gracefully disconnects the session.
    /// </summary>
    ValueTask DisconnectAsync();
}

/// <summary>
/// Callback delegate for received messages.
/// </summary>
/// <param name="session">The session that received the message.</param>
/// <param name="msgId">Message identifier.</param>
/// <param name="msgSeq">Message sequence number.</param>
/// <param name="stageId">Target stage ID.</param>
/// <param name="payload">Message payload.</param>
public delegate void MessageReceivedCallback(
    ITransportSession session,
    string msgId,
    ushort msgSeq,
    long stageId,
    ReadOnlyMemory<byte> payload);

/// <summary>
/// Callback delegate for session disconnection.
/// </summary>
/// <param name="session">The disconnected session.</param>
/// <param name="exception">Exception if disconnection was due to an error; otherwise null.</param>
public delegate void SessionDisconnectedCallback(ITransportSession session, Exception? exception);
