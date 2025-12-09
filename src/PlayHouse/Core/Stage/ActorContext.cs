#nullable enable

using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using PlayHouse.Abstractions;

namespace PlayHouse.Core.Stage;

/// <summary>
/// Wraps a user-defined IActor with connection state and lifecycle management.
/// </summary>
/// <remarks>
/// ActorContext provides:
/// 1. Connection state management (connected/disconnected)
/// 2. Lifecycle hooks (OnCreate, OnDestroy, OnAuthenticate)
/// 3. Integration with session management
/// </remarks>
internal sealed class ActorContext : IAsyncDisposable
{
    private readonly IActor _userActor;
    private readonly ILogger _logger;
    private bool _isConnected;
    private bool _isDisposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="ActorContext"/> class.
    /// </summary>
    /// <param name="accountId">The account identifier for this actor.</param>
    /// <param name="sessionId">The session identifier for this actor.</param>
    /// <param name="userActor">The user-defined actor implementation.</param>
    /// <param name="logger">The logger instance.</param>
    public ActorContext(
        long accountId,
        long sessionId,
        IActor userActor,
        ILogger logger)
    {
        AccountId = accountId;
        SessionId = sessionId;
        _userActor = userActor;
        _logger = logger;
        _isConnected = true; // Initially connected
        CreatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Gets the account identifier.
    /// </summary>
    public long AccountId { get; }

    /// <summary>
    /// Gets the session identifier.
    /// </summary>
    public long SessionId { get; }

    /// <summary>
    /// Gets the user-defined actor implementation.
    /// </summary>
    public IActor UserActor => _userActor;

    /// <summary>
    /// Gets a value indicating whether the actor is currently connected.
    /// </summary>
    public bool IsConnected => _isConnected;

    /// <summary>
    /// Gets the timestamp when this actor was created.
    /// </summary>
    public DateTime CreatedAt { get; }

    /// <summary>
    /// Gets the timestamp when this actor was last disconnected (null if never disconnected).
    /// </summary>
    public DateTime? DisconnectedAt { get; private set; }

    /// <summary>
    /// Sets the connection state of the actor.
    /// </summary>
    /// <param name="isConnected">True if connected; false if disconnected.</param>
    public void SetConnectionState(bool isConnected)
    {
        _isConnected = isConnected;
        if (!isConnected)
        {
            DisconnectedAt = DateTime.UtcNow;
        }
    }

    /// <summary>
    /// Initializes the actor by calling user's OnCreate.
    /// </summary>
    public async Task OnCreateAsync()
    {
        try
        {
            await _userActor.OnCreate();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in actor OnCreate for account {AccountId}", AccountId);
            throw;
        }
    }

    /// <summary>
    /// Authenticates the actor by calling user's OnAuthenticate.
    /// </summary>
    /// <param name="authData">Optional authentication data.</param>
    public async Task OnAuthenticateAsync(IPacket? authData)
    {
        try
        {
            await _userActor.OnAuthenticate(authData);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in actor OnAuthenticate for account {AccountId}", AccountId);
            throw;
        }
    }

    /// <summary>
    /// Destroys the actor by calling user's OnDestroy.
    /// </summary>
    public async Task OnDestroyAsync()
    {
        if (_isDisposed) return;

        try
        {
            await _userActor.OnDestroy();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in actor OnDestroy for account {AccountId}", AccountId);
        }
    }

    /// <summary>
    /// Disposes the actor and releases resources.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (_isDisposed) return;

        _isDisposed = true;

        try
        {
            await OnDestroyAsync();
            await _userActor.DisposeAsync();

            _logger.LogDebug("Actor {AccountId} disposed", AccountId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error disposing actor {AccountId}", AccountId);
        }
    }
}
