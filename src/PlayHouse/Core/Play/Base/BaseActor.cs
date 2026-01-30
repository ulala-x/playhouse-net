#nullable enable

using Microsoft.Extensions.DependencyInjection;
using PlayHouse.Abstractions.Play;

namespace PlayHouse.Core.Play.Base;

/// <summary>
/// Wrapper class that links an IActor with its XActorLink and DI scope.
/// </summary>
/// <remarks>
/// BaseActor is managed by BaseStage and provides the association
/// between the content-implemented IActor and the framework's XActorLink.
/// It also manages the IServiceScope for proper Scoped dependency lifecycle.
/// </remarks>
internal sealed class BaseActor
{
    private readonly IServiceScope? _serviceScope;

    /// <summary>
    /// Gets the content-implemented Actor.
    /// </summary>
    public IActor Actor { get; }

    /// <summary>
    /// Gets the framework-provided ActorLink.
    /// </summary>
    public XActorLink ActorLink { get; }

    /// <summary>
    /// Gets the account ID for this Actor.
    /// </summary>
    public string AccountId => ActorLink.AccountId;

    /// <summary>
    /// Initializes a new instance of the <see cref="BaseActor"/> class.
    /// </summary>
    /// <param name="actor">Content-implemented Actor.</param>
    /// <param name="actorLink">Framework ActorLink.</param>
    /// <param name="serviceScope">Optional DI scope for Scoped dependency management.</param>
    public BaseActor(IActor actor, XActorLink actorLink, IServiceScope? serviceScope = null)
    {
        Actor = actor;
        ActorLink = actorLink;
        _serviceScope = serviceScope;
    }

    /// <summary>
    /// Disposes the Actor's DI scope, releasing all Scoped dependencies.
    /// </summary>
    /// <remarks>
    /// This method should be called when the Actor leaves the Stage
    /// to ensure proper cleanup of Scoped services.
    /// </remarks>
    internal void Dispose()
    {
        _serviceScope?.Dispose();
    }
}
