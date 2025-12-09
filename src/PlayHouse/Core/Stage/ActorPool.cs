#nullable enable

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using PlayHouse.Abstractions;

namespace PlayHouse.Core.Stage;

/// <summary>
/// Manages actors within a stage.
/// </summary>
/// <remarks>
/// ActorPool provides thread-safe actor management using ConcurrentDictionary.
/// All operations are thread-safe and can be called from multiple threads.
/// </remarks>
internal sealed class ActorPool
{
    private readonly ConcurrentDictionary<long, ActorContext> _actors = new();
    private readonly ILogger _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="ActorPool"/> class.
    /// </summary>
    /// <param name="logger">The logger instance.</param>
    public ActorPool(ILogger logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Adds an actor to the pool.
    /// </summary>
    /// <param name="actorContext">The actor context to add.</param>
    /// <returns>True if the actor was added; false if an actor with the same account ID already exists.</returns>
    public bool AddActor(ActorContext actorContext)
    {
        if (_actors.TryAdd(actorContext.AccountId, actorContext))
        {
            _logger.LogDebug("Actor {AccountId} added to pool", actorContext.AccountId);
            return true;
        }

        _logger.LogWarning("Actor {AccountId} already exists in pool", actorContext.AccountId);
        return false;
    }

    /// <summary>
    /// Removes an actor from the pool.
    /// </summary>
    /// <param name="accountId">The account identifier of the actor to remove.</param>
    /// <returns>The removed actor context, or null if not found.</returns>
    public ActorContext? RemoveActor(long accountId)
    {
        if (_actors.TryRemove(accountId, out var actorContext))
        {
            _logger.LogDebug("Actor {AccountId} removed from pool", accountId);
            return actorContext;
        }

        _logger.LogWarning("Actor {AccountId} not found in pool for removal", accountId);
        return null;
    }

    /// <summary>
    /// Gets an actor from the pool.
    /// </summary>
    /// <param name="accountId">The account identifier.</param>
    /// <returns>The actor context, or null if not found.</returns>
    public ActorContext? GetActor(long accountId)
    {
        _actors.TryGetValue(accountId, out var actorContext);
        return actorContext;
    }

    /// <summary>
    /// Checks if an actor exists in the pool.
    /// </summary>
    /// <param name="accountId">The account identifier.</param>
    /// <returns>True if the actor exists; otherwise, false.</returns>
    public bool HasActor(long accountId)
    {
        return _actors.ContainsKey(accountId);
    }

    /// <summary>
    /// Gets the number of actors in the pool.
    /// </summary>
    public int Count => _actors.Count;

    /// <summary>
    /// Gets all actors in the pool.
    /// </summary>
    /// <returns>A collection of all actor contexts.</returns>
    public IEnumerable<ActorContext> GetAllActors()
    {
        return _actors.Values.ToList();
    }

    /// <summary>
    /// Gets all connected actors.
    /// </summary>
    /// <returns>A collection of connected actor contexts.</returns>
    public IEnumerable<ActorContext> GetConnectedActors()
    {
        return _actors.Values.Where(a => a.IsConnected).ToList();
    }

    /// <summary>
    /// Gets all disconnected actors.
    /// </summary>
    /// <returns>A collection of disconnected actor contexts.</returns>
    public IEnumerable<ActorContext> GetDisconnectedActors()
    {
        return _actors.Values.Where(a => !a.IsConnected).ToList();
    }

    /// <summary>
    /// Finds actors that have been disconnected longer than the specified timeout.
    /// </summary>
    /// <param name="timeout">The disconnection timeout.</param>
    /// <returns>A collection of timed-out actor contexts.</returns>
    public IEnumerable<ActorContext> FindTimedOutActors(TimeSpan timeout)
    {
        var cutoffTime = DateTime.UtcNow - timeout;
        return _actors.Values
            .Where(a => !a.IsConnected && a.DisconnectedAt.HasValue && a.DisconnectedAt.Value < cutoffTime)
            .ToList();
    }

    /// <summary>
    /// Removes and disposes all actors that match the specified predicate.
    /// </summary>
    /// <param name="predicate">The predicate to match actors for removal.</param>
    /// <returns>The number of actors removed.</returns>
    public async Task<int> RemoveActorsAsync(Func<ActorContext, bool> predicate)
    {
        var actorsToRemove = _actors.Values.Where(predicate).ToList();

        foreach (var actor in actorsToRemove)
        {
            if (_actors.TryRemove(actor.AccountId, out var removed))
            {
                await removed.DisposeAsync();
            }
        }

        return actorsToRemove.Count;
    }

    /// <summary>
    /// Disposes all actors in the pool.
    /// </summary>
    public async Task DisposeAllAsync()
    {
        var actors = _actors.Values.ToList();
        _actors.Clear();

        foreach (var actor in actors)
        {
            try
            {
                await actor.DisposeAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error disposing actor {AccountId}", actor.AccountId);
            }
        }

        _logger.LogInformation("Disposed {Count} actors", actors.Count);
    }
}
