#nullable enable

namespace PlayHouse.Abstractions;

/// <summary>
/// Provides functionality for actors to send packets and identify themselves.
/// </summary>
/// <remarks>
/// The ActorSender is associated with a specific actor and provides context
/// about the actor's identity through AccountId and SessionId.
/// </remarks>
public interface IActorSender : ISender
{
    /// <summary>
    /// Gets the account identifier of the actor.
    /// </summary>
    long AccountId { get; }

    /// <summary>
    /// Gets the session identifier of the actor's current connection.
    /// </summary>
    /// <remarks>
    /// The session ID changes each time the actor reconnects, while AccountId remains constant.
    /// </remarks>
    long SessionId { get; }
}
