#nullable enable

namespace PlayHouse.Abstractions;

/// <summary>
/// Represents a game room or logic container that manages actors and game state.
/// </summary>
/// <remarks>
/// Stages are the core execution context for game logic in PlayHouse. Each stage
/// runs on a single thread, ensuring all operations are serialized and thread-safe.
/// Stages can contain multiple actors and manage game state, timers, and inter-stage
/// communication.
/// </remarks>
public interface IStage : IAsyncDisposable
{
    /// <summary>
    /// Gets the sender interface for this stage to send packets and manage stage operations.
    /// </summary>
    IStageSender StageSender { get; }

    /// <summary>
    /// Called when the stage is first created.
    /// </summary>
    /// <param name="packet">The creation packet containing initialization data.</param>
    /// <returns>
    /// A tuple containing an error code (0 for success) and an optional reply packet.
    /// </returns>
    /// <remarks>
    /// Use this method to initialize stage state and validate creation parameters.
    /// Return a non-zero error code to prevent stage creation.
    /// </remarks>
    Task<(ushort errorCode, IPacket? reply)> OnCreate(IPacket packet);

    /// <summary>
    /// Called after the stage is successfully created and ready to accept actors.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    /// <remarks>
    /// This is called after <see cref="OnCreate"/> succeeds. Use this method to
    /// perform additional initialization that requires the stage to be fully set up.
    /// </remarks>
    Task OnPostCreate();

    /// <summary>
    /// Called when an actor attempts to join the stage or room.
    /// </summary>
    /// <param name="actor">The actor attempting to join.</param>
    /// <param name="userInfo">User information packet provided during join.</param>
    /// <returns>
    /// A tuple containing an error code (0 for success) and an optional reply packet.
    /// </returns>
    /// <remarks>
    /// Use this method to validate join conditions (e.g., room capacity, permissions)
    /// and initialize actor-specific state. Return a non-zero error code to reject the join.
    /// </remarks>
    Task<(ushort errorCode, IPacket? reply)> OnJoinRoom(IActor actor, IPacket userInfo);

    /// <summary>
    /// Called after an actor successfully joins the room.
    /// </summary>
    /// <param name="actor">The actor that joined.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    /// <remarks>
    /// This is called after <see cref="OnJoinRoom"/> succeeds. Use this method to
    /// notify other actors, send initial state, or trigger join-related game logic.
    /// </remarks>
    Task OnPostJoinRoom(IActor actor);

    /// <summary>
    /// Called when an actor leaves the stage or room.
    /// </summary>
    /// <param name="actor">The actor that is leaving.</param>
    /// <param name="reason">The reason for leaving.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    /// <remarks>
    /// Use this method to clean up actor-specific state, notify other actors,
    /// and handle any leave-related game logic.
    /// </remarks>
    ValueTask OnLeaveRoom(IActor actor, LeaveReason reason);

    /// <summary>
    /// Called when an actor's connection status changes.
    /// </summary>
    /// <param name="actor">The actor whose connection status changed.</param>
    /// <param name="isConnected">True if the actor is now connected; false if disconnected.</param>
    /// <param name="reason">The disconnection reason, if applicable.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    /// <remarks>
    /// Use this method to handle pause/resume logic when actors disconnect and reconnect.
    /// Disconnected actors remain in the stage until they timeout or explicitly leave.
    /// </remarks>
    ValueTask OnActorConnectionChanged(IActor actor, bool isConnected, DisconnectReason? reason);

    /// <summary>
    /// Called when a message packet is dispatched to an actor.
    /// </summary>
    /// <param name="actor">The target actor for the message.</param>
    /// <param name="packet">The message packet to process.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    /// <remarks>
    /// This is the main message handler for actor-specific game logic. The packet
    /// should be routed to the appropriate handler based on its MsgId.
    /// </remarks>
    ValueTask OnDispatch(IActor actor, IPacket packet);
}
