#nullable enable

namespace PlayHouse.Abstractions;

/// <summary>
/// Specifies the reason for leaving a stage or room.
/// </summary>
public enum LeaveReason
{
    /// <summary>
    /// Normal leave initiated by the actor or game logic.
    /// </summary>
    Normal,

    /// <summary>
    /// Leave due to user request (client initiated).
    /// </summary>
    UserRequest,

    /// <summary>
    /// Leave due to timeout (inactivity or operation timeout).
    /// </summary>
    Timeout,

    /// <summary>
    /// The actor was forcibly removed from the stage or room.
    /// </summary>
    Kicked,

    /// <summary>
    /// Leave due to server shutdown.
    /// </summary>
    ServerShutdown
}
