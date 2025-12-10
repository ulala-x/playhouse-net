#nullable enable

using PlayHouse.Abstractions.Play;

namespace PlayHouse.Core.Play.Base;

/// <summary>
/// Wrapper class that links an IActor with its XActorSender.
/// </summary>
/// <remarks>
/// BaseActor is managed by BaseStage and provides the association
/// between the content-implemented IActor and the framework's XActorSender.
/// </remarks>
internal sealed class BaseActor
{
    /// <summary>
    /// Gets the content-implemented Actor.
    /// </summary>
    public IActor Actor { get; }

    /// <summary>
    /// Gets the framework-provided ActorSender.
    /// </summary>
    public XActorSender ActorSender { get; }

    /// <summary>
    /// Gets the account ID for this Actor.
    /// </summary>
    public string AccountId => ActorSender.AccountId;

    /// <summary>
    /// Gets the route account ID (internal routing).
    /// </summary>
    public long RouteAccountId => ActorSender.RouteAccountId;

    /// <summary>
    /// Initializes a new instance of the <see cref="BaseActor"/> class.
    /// </summary>
    /// <param name="actor">Content-implemented Actor.</param>
    /// <param name="actorSender">Framework ActorSender.</param>
    public BaseActor(IActor actor, XActorSender actorSender)
    {
        Actor = actor;
        ActorSender = actorSender;
    }
}
