#nullable enable

namespace PlayHouse.Abstractions.Play;

/// <summary>
/// Provides Actor-specific communication capabilities.
/// </summary>
/// <remarks>
/// IActorSender extends ISender with:
/// - AccountId property for user identification (must be set in OnAuthenticate)
/// - LeaveStage() to exit from current Stage
/// - SendToClient() for direct client messaging
/// </remarks>
public interface IActorSender : ISender
{
    /// <summary>
    /// Gets or sets the account identifier for this Actor.
    /// </summary>
    /// <remarks>
    /// MUST be set in IActor.OnAuthenticate() upon successful authentication.
    /// If empty ("") after OnAuthenticate completes, connection will be terminated.
    /// </remarks>
    string AccountId { get; set; }

    /// <summary>
    /// Removes this Actor from the current Stage.
    /// </summary>
    /// <remarks>
    /// This method:
    /// 1. Removes the Actor from BaseStage._actors
    /// 2. Calls IActor.OnDestroy()
    /// 3. Does NOT close the client connection (actor can join another stage)
    /// </remarks>
    void LeaveStage();

    /// <summary>
    /// Sends a message directly to the connected client.
    /// </summary>
    /// <param name="packet">The packet to send to the client.</param>
    void SendToClient(IPacket packet);
}
