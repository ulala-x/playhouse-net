#nullable enable

using System;
using PlayHouse.Abstractions;

namespace PlayHouse.Core.Session;

/// <summary>
/// Holds session data and connection state information.
/// </summary>
/// <remarks>
/// SessionInfo tracks the relationship between sessions, accounts, and stages,
/// along with connection state for pause-resume functionality.
/// </remarks>
public sealed class SessionInfo
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SessionInfo"/> class.
    /// </summary>
    /// <param name="sessionId">The unique session identifier.</param>
    public SessionInfo(long sessionId)
    {
        SessionId = sessionId;
        CreatedAt = DateTime.UtcNow;
        IsConnected = true; // Initially connected
    }

    /// <summary>
    /// Gets the unique session identifier.
    /// </summary>
    public long SessionId { get; }

    /// <summary>
    /// Gets or sets the account identifier associated with this session.
    /// </summary>
    /// <remarks>
    /// This is null until the session is authenticated and associated with an account.
    /// </remarks>
    public long? AccountId { get; set; }

    /// <summary>
    /// Gets or sets the stage identifier where this session's actor resides.
    /// </summary>
    /// <remarks>
    /// This is null until the actor joins a stage.
    /// </remarks>
    public int? StageId { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether this session is currently connected.
    /// </summary>
    public bool IsConnected { get; set; }

    /// <summary>
    /// Gets the timestamp when this session was created.
    /// </summary>
    public DateTime CreatedAt { get; }

    /// <summary>
    /// Gets or sets the timestamp when this session was authenticated.
    /// </summary>
    public DateTime? AuthenticatedAt { get; set; }

    /// <summary>
    /// Gets or sets the timestamp when this session was last connected.
    /// </summary>
    public DateTime? LastConnectedAt { get; set; }

    /// <summary>
    /// Gets or sets the timestamp when this session was last disconnected.
    /// </summary>
    public DateTime? LastDisconnectedAt { get; set; }

    /// <summary>
    /// Gets or sets the disconnection reason if disconnected.
    /// </summary>
    public DisconnectReason? DisconnectReason { get; set; }

    /// <summary>
    /// Gets or sets custom session metadata.
    /// </summary>
    /// <remarks>
    /// This can be used to store additional session-specific data such as
    /// client version, device type, geographic location, etc.
    /// </remarks>
    public object? Metadata { get; set; }

    /// <summary>
    /// Gets the duration of the current connection (if connected) or last connection (if disconnected).
    /// </summary>
    public TimeSpan? ConnectionDuration
    {
        get
        {
            if (LastConnectedAt.HasValue)
            {
                var endTime = IsConnected ? DateTime.UtcNow : (LastDisconnectedAt ?? DateTime.UtcNow);
                return endTime - LastConnectedAt.Value;
            }
            return null;
        }
    }

    /// <summary>
    /// Gets the duration since disconnection (only valid when IsConnected is false).
    /// </summary>
    public TimeSpan? DisconnectedDuration
    {
        get
        {
            if (!IsConnected && LastDisconnectedAt.HasValue)
            {
                return DateTime.UtcNow - LastDisconnectedAt.Value;
            }
            return null;
        }
    }

    /// <summary>
    /// Marks the session as authenticated with an account.
    /// </summary>
    /// <param name="accountId">The account identifier.</param>
    public void Authenticate(long accountId)
    {
        AccountId = accountId;
        AuthenticatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Marks the session as joined to a stage.
    /// </summary>
    /// <param name="stageId">The stage identifier.</param>
    public void JoinStage(int stageId)
    {
        StageId = stageId;
    }

    /// <summary>
    /// Marks the session as left from its current stage.
    /// </summary>
    public void LeaveStage()
    {
        StageId = null;
    }

    /// <summary>
    /// Updates the connection state.
    /// </summary>
    /// <param name="isConnected">True if connected; false if disconnected.</param>
    /// <param name="reason">The disconnection reason, if applicable.</param>
    public void UpdateConnectionState(bool isConnected, DisconnectReason? reason = null)
    {
        IsConnected = isConnected;

        if (isConnected)
        {
            LastConnectedAt = DateTime.UtcNow;
            DisconnectReason = null;
        }
        else
        {
            LastDisconnectedAt = DateTime.UtcNow;
            DisconnectReason = reason;
        }
    }

    /// <summary>
    /// Creates a summary string for logging and debugging.
    /// </summary>
    public override string ToString()
    {
        var account = AccountId.HasValue ? AccountId.Value.ToString() : "null";
        var stage = StageId.HasValue ? StageId.Value.ToString() : "null";
        var state = IsConnected ? "connected" : "disconnected";

        return $"Session[{SessionId}] Account={account} Stage={stage} State={state}";
    }
}
