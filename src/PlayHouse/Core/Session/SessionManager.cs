#nullable enable

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging;
using PlayHouse.Abstractions;

namespace PlayHouse.Core.Session;

/// <summary>
/// Manages session lifecycle and account-to-session mapping.
/// </summary>
/// <remarks>
/// SessionManager provides thread-safe session management using ConcurrentDictionary.
/// It maintains bidirectional mapping between sessions and accounts to support
/// pause-resume functionality and reconnection.
/// </remarks>
internal sealed class SessionManager
{
    private readonly ConcurrentDictionary<long, SessionInfo> _sessions = new();
    private readonly ConcurrentDictionary<long, long> _accountToSession = new();
    private readonly ILogger<SessionManager> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="SessionManager"/> class.
    /// </summary>
    /// <param name="logger">The logger instance.</param>
    public SessionManager(ILogger<SessionManager> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Creates a new session.
    /// </summary>
    /// <param name="sessionId">The unique session identifier.</param>
    /// <returns>The created session info.</returns>
    public SessionInfo CreateSession(long sessionId)
    {
        var sessionInfo = new SessionInfo(sessionId);

        if (_sessions.TryAdd(sessionId, sessionInfo))
        {
            _logger.LogDebug("Created session {SessionId}", sessionId);
            return sessionInfo;
        }

        _logger.LogWarning("Session {SessionId} already exists", sessionId);
        return _sessions[sessionId];
    }

    /// <summary>
    /// Gets a session by its identifier.
    /// </summary>
    /// <param name="sessionId">The session identifier.</param>
    /// <returns>The session info, or null if not found.</returns>
    public SessionInfo? GetSession(long sessionId)
    {
        _sessions.TryGetValue(sessionId, out var sessionInfo);
        return sessionInfo;
    }

    /// <summary>
    /// Removes a session.
    /// </summary>
    /// <param name="sessionId">The session identifier.</param>
    /// <returns>The removed session info, or null if not found.</returns>
    public SessionInfo? RemoveSession(long sessionId)
    {
        if (_sessions.TryRemove(sessionId, out var sessionInfo))
        {
            // Remove account mapping if exists
            if (sessionInfo.AccountId.HasValue)
            {
                _accountToSession.TryRemove(sessionInfo.AccountId.Value, out _);
            }

            _logger.LogDebug("Removed session {SessionId}", sessionId);
            return sessionInfo;
        }

        _logger.LogWarning("Session {SessionId} not found for removal", sessionId);
        return null;
    }

    /// <summary>
    /// Maps an account ID to a session.
    /// </summary>
    /// <param name="sessionId">The session identifier.</param>
    /// <param name="accountId">The account identifier.</param>
    /// <returns>True if the mapping was successful; otherwise, false.</returns>
    public bool MapAccountId(long sessionId, long accountId)
    {
        var sessionInfo = GetSession(sessionId);
        if (sessionInfo == null)
        {
            _logger.LogWarning("Cannot map account {AccountId} to non-existent session {SessionId}",
                accountId, sessionId);
            return false;
        }

        // Update session info
        sessionInfo.Authenticate(accountId);

        // Add or update account-to-session mapping
        _accountToSession.AddOrUpdate(accountId, sessionId, (_, _) => sessionId);

        _logger.LogDebug("Mapped account {AccountId} to session {SessionId}", accountId, sessionId);
        return true;
    }

    /// <summary>
    /// Gets the session ID associated with an account.
    /// </summary>
    /// <param name="accountId">The account identifier.</param>
    /// <returns>The session ID, or null if not found.</returns>
    public long? GetSessionIdByAccount(long accountId)
    {
        return _accountToSession.TryGetValue(accountId, out var sessionId) ? sessionId : null;
    }

    /// <summary>
    /// Gets the session associated with an account.
    /// </summary>
    /// <param name="accountId">The account identifier.</param>
    /// <returns>The session info, or null if not found.</returns>
    public SessionInfo? GetSessionByAccount(long accountId)
    {
        var sessionId = GetSessionIdByAccount(accountId);
        return sessionId.HasValue ? GetSession(sessionId.Value) : null;
    }

    /// <summary>
    /// Checks if a session exists.
    /// </summary>
    /// <param name="sessionId">The session identifier.</param>
    /// <returns>True if the session exists; otherwise, false.</returns>
    public bool HasSession(long sessionId)
    {
        return _sessions.ContainsKey(sessionId);
    }

    /// <summary>
    /// Updates the connection state of a session.
    /// </summary>
    /// <param name="sessionId">The session identifier.</param>
    /// <param name="isConnected">True if connected; false if disconnected.</param>
    /// <param name="reason">The disconnection reason, if applicable.</param>
    /// <returns>True if the update was successful; otherwise, false.</returns>
    public bool UpdateConnectionState(long sessionId, bool isConnected, DisconnectReason? reason = null)
    {
        var sessionInfo = GetSession(sessionId);
        if (sessionInfo == null)
        {
            _logger.LogWarning("Cannot update connection state for non-existent session {SessionId}", sessionId);
            return false;
        }

        sessionInfo.UpdateConnectionState(isConnected, reason);

        _logger.LogDebug("Updated session {SessionId} connection state to {State}",
            sessionId, isConnected ? "connected" : "disconnected");

        return true;
    }

    /// <summary>
    /// Gets all sessions.
    /// </summary>
    /// <returns>A collection of all session infos.</returns>
    public IEnumerable<SessionInfo> GetAllSessions()
    {
        return _sessions.Values.ToList();
    }

    /// <summary>
    /// Gets all connected sessions.
    /// </summary>
    /// <returns>A collection of connected session infos.</returns>
    public IEnumerable<SessionInfo> GetConnectedSessions()
    {
        return _sessions.Values.Where(s => s.IsConnected).ToList();
    }

    /// <summary>
    /// Gets all disconnected sessions.
    /// </summary>
    /// <returns>A collection of disconnected session infos.</returns>
    public IEnumerable<SessionInfo> GetDisconnectedSessions()
    {
        return _sessions.Values.Where(s => !s.IsConnected).ToList();
    }

    /// <summary>
    /// Finds sessions that have been disconnected longer than the specified timeout.
    /// </summary>
    /// <param name="timeout">The disconnection timeout.</param>
    /// <returns>A collection of timed-out session infos.</returns>
    public IEnumerable<SessionInfo> FindTimedOutSessions(TimeSpan timeout)
    {
        var cutoffTime = DateTime.UtcNow - timeout;
        return _sessions.Values
            .Where(s => !s.IsConnected &&
                       s.LastDisconnectedAt.HasValue &&
                       s.LastDisconnectedAt.Value < cutoffTime)
            .ToList();
    }

    /// <summary>
    /// Removes sessions that match the specified predicate.
    /// </summary>
    /// <param name="predicate">The predicate to match sessions for removal.</param>
    /// <returns>The number of sessions removed.</returns>
    public int RemoveSessions(Func<SessionInfo, bool> predicate)
    {
        var sessionsToRemove = _sessions.Values.Where(predicate).ToList();

        foreach (var session in sessionsToRemove)
        {
            RemoveSession(session.SessionId);
        }

        _logger.LogInformation("Removed {Count} sessions matching predicate", sessionsToRemove.Count);
        return sessionsToRemove.Count;
    }

    /// <summary>
    /// Gets statistics about sessions for monitoring.
    /// </summary>
    /// <returns>Session statistics.</returns>
    public SessionStatistics GetStatistics()
    {
        var sessions = _sessions.Values.ToList();
        var now = DateTime.UtcNow;

        return new SessionStatistics
        {
            TotalSessions = sessions.Count,
            ConnectedSessions = sessions.Count(s => s.IsConnected),
            DisconnectedSessions = sessions.Count(s => !s.IsConnected),
            AuthenticatedSessions = sessions.Count(s => s.AccountId.HasValue),
            AverageSessionAge = sessions.Count > 0
                ? TimeSpan.FromMilliseconds(sessions.Average(s => (now - s.CreatedAt).TotalMilliseconds))
                : TimeSpan.Zero,
            SessionsByStage = sessions
                .Where(s => s.StageId.HasValue)
                .GroupBy(s => s.StageId!.Value)
                .ToDictionary(g => g.Key, g => g.Count())
        };
    }

    /// <summary>
    /// Clears all sessions.
    /// </summary>
    public void ClearAll()
    {
        var count = _sessions.Count;
        _sessions.Clear();
        _accountToSession.Clear();

        _logger.LogInformation("Cleared all {Count} sessions", count);
    }

    /// <summary>
    /// Gets the total number of sessions.
    /// </summary>
    public int SessionCount => _sessions.Count;
}

/// <summary>
/// Statistics about sessions.
/// </summary>
public sealed class SessionStatistics
{
    /// <summary>
    /// Gets the total number of sessions.
    /// </summary>
    public int TotalSessions { get; init; }

    /// <summary>
    /// Gets the number of connected sessions.
    /// </summary>
    public int ConnectedSessions { get; init; }

    /// <summary>
    /// Gets the number of disconnected sessions.
    /// </summary>
    public int DisconnectedSessions { get; init; }

    /// <summary>
    /// Gets the number of authenticated sessions.
    /// </summary>
    public int AuthenticatedSessions { get; init; }

    /// <summary>
    /// Gets the average session age.
    /// </summary>
    public TimeSpan AverageSessionAge { get; init; }

    /// <summary>
    /// Gets the number of sessions per stage.
    /// </summary>
    public Dictionary<int, int> SessionsByStage { get; init; } = new();
}
