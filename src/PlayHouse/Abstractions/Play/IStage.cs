#nullable enable

namespace PlayHouse.Abstractions.Play;

/// <summary>
/// Interface for Stage lifecycle and message handling.
/// </summary>
/// <remarks>
/// Content developers implement this interface to define Stage behavior.
/// The framework calls these methods in the appropriate order:
///
/// Stage Creation:
/// 1. OnCreate() - Initialize stage state
/// 2. OnPostCreate() - Setup timers, load data, etc.
///
/// Actor Join:
/// 1. OnJoinStage() - Validate and accept actor
/// 2. OnPostJoinStage() - Post-join processing
///
/// Message Dispatch:
/// - OnDispatch(IActor, IPacket) - Client messages (AccountId != 0)
/// - OnDispatch(IPacket) - Server-to-server messages (AccountId == 0)
///
/// Connection State:
/// - OnConnectionChanged() - Client connect/disconnect notifications
///
/// Cleanup:
/// - OnDestroy() - Final cleanup when stage closes
/// </remarks>
public interface IStage
{
    /// <summary>
    /// Gets the sender for this Stage.
    /// </summary>
    IStageSender StageSender { get; }

    #region Stage Lifecycle

    /// <summary>
    /// Called when the Stage is being created.
    /// </summary>
    /// <param name="packet">The creation request packet.</param>
    /// <returns>
    /// A tuple containing:
    /// - result: true if creation succeeds, false otherwise
    /// - reply: Response packet to send back to the requester
    /// </returns>
    Task<(bool result, IPacket reply)> OnCreate(IPacket packet);

    /// <summary>
    /// Called after OnCreate succeeds.
    /// Use for setting up timers, loading initial data, etc.
    /// </summary>
    Task OnPostCreate();

    /// <summary>
    /// Called when the Stage is being destroyed.
    /// Perform cleanup operations here.
    /// </summary>
    Task OnDestroy();

    #endregion

    #region Actor Management

    /// <summary>
    /// Called when an Actor attempts to join this Stage.
    /// </summary>
    /// <param name="actor">The Actor attempting to join.</param>
    /// <returns>true to allow join, false to reject.</returns>
    Task<bool> OnJoinStage(IActor actor);

    /// <summary>
    /// Called after an Actor successfully joins the Stage.
    /// </summary>
    /// <param name="actor">The Actor that joined.</param>
    Task OnPostJoinStage(IActor actor);

    #endregion

    #region Connection State

    /// <summary>
    /// Called when an Actor's connection state changes.
    /// </summary>
    /// <param name="actor">The Actor whose connection changed.</param>
    /// <param name="isConnected">true if connected, false if disconnected.</param>
    ValueTask OnConnectionChanged(IActor actor, bool isConnected);

    #endregion

    #region Message Dispatch

    /// <summary>
    /// Called when a message is received from a client (Actor present).
    /// </summary>
    /// <param name="actor">The Actor that sent the message.</param>
    /// <param name="packet">The received packet.</param>
    /// <remarks>
    /// This overload is called when RouteHeader.AccountId != 0
    /// and the corresponding Actor exists in the Stage.
    /// </remarks>
    Task OnDispatch(IActor actor, IPacket packet);

    /// <summary>
    /// Called when a server-to-server message is received (no Actor context).
    /// </summary>
    /// <param name="packet">The received packet.</param>
    /// <remarks>
    /// This overload is called when:
    /// - RouteHeader.AccountId == 0 (server-to-server communication)
    /// - Or the Actor for the given AccountId doesn't exist
    /// </remarks>
    Task OnDispatch(IPacket packet);

    #endregion
}
