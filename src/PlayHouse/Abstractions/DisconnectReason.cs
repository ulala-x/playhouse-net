#nullable enable

namespace PlayHouse.Abstractions;

/// <summary>
/// Specifies the reason for a client disconnection.
/// </summary>
public enum DisconnectReason
{
    /// <summary>
    /// Normal disconnection initiated by the client or server.
    /// </summary>
    Normal,

    /// <summary>
    /// Disconnection due to a network error or connection failure.
    /// </summary>
    NetworkError,

    /// <summary>
    /// Disconnection due to timeout (no activity within the configured period).
    /// </summary>
    Timeout,

    /// <summary>
    /// The client was forcibly disconnected by the server.
    /// </summary>
    Kicked,

    /// <summary>
    /// Disconnection due to server shutdown.
    /// </summary>
    ServerShutdown,

    /// <summary>
    /// Disconnection due to duplicate login detection.
    /// </summary>
    DuplicateLogin
}
