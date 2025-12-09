#nullable enable

namespace PlayHouse.Abstractions;

/// <summary>
/// Represents a player or entity within a stage.
/// </summary>
/// <remarks>
/// Actors are the fundamental unit of player interaction in the PlayHouse framework.
/// Each actor is associated with an account and maintains state across disconnections
/// through the pause-resume mechanism. All actor operations are executed serially
/// within the stage's context to ensure thread safety.
/// </remarks>
public interface IActor : IAsyncDisposable
{
    /// <summary>
    /// Gets the sender interface for this actor to send packets and replies.
    /// </summary>
    IActorSender ActorSender { get; }

    /// <summary>
    /// Gets a value indicating whether the actor is currently connected to a client.
    /// </summary>
    /// <remarks>
    /// Actors can be disconnected but remain in the stage. When disconnected,
    /// the actor enters a paused state until reconnection or timeout.
    /// </remarks>
    bool IsConnected { get; }

    /// <summary>
    /// Called when the actor is first created and joined to a stage.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    /// <remarks>
    /// Use this method to initialize actor state and load persistent data.
    /// This is called before <see cref="OnAuthenticate"/>.
    /// </remarks>
    Task OnCreate();

    /// <summary>
    /// Called when the actor is being destroyed and removed from the stage.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    /// <remarks>
    /// Use this method to clean up resources and save persistent data.
    /// This is guaranteed to be called before the actor is removed.
    /// </remarks>
    Task OnDestroy();

    /// <summary>
    /// Called to authenticate the actor, typically after initial creation or reconnection.
    /// </summary>
    /// <param name="authData">Optional authentication data provided by the client.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    /// <remarks>
    /// Implementations should verify the authentication data and set up actor state.
    /// Throw an exception if authentication fails.
    /// </remarks>
    Task OnAuthenticate(IPacket? authData);
}
