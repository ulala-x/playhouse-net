#nullable enable

using Microsoft.Extensions.DependencyInjection;

namespace PlayHouse.Abstractions.Play;

/// <summary>
/// Factory for creating Stage and Actor instances with dependency injection support.
/// </summary>
/// <remarks>
/// Content developers register their Stage and Actor factories using this class.
/// The framework uses these factories to create instances during Stage creation
/// and Actor join processes.
///
/// Usage with DI:
/// <code>
/// var serviceProvider = services.BuildServiceProvider();
/// var producer = new PlayProducer(stageTypes, actorTypes, serviceProvider);
/// </code>
///
/// Manual registration (legacy):
/// <code>
/// var producer = new PlayProducer();
/// producer.Register(
///     "battle",
///     stageSender => new BattleStage(stageSender),
///     actorSender => new BattleActor(actorSender)
/// );
/// </code>
/// </remarks>
public class PlayProducer
{
    private readonly IServiceProvider? _serviceProvider;
    private readonly Dictionary<string, Type> _stageTypes = new();
    private readonly Dictionary<string, Type> _actorTypes = new();
    private readonly Dictionary<string, Func<IStageSender, IStage>> _stageFactories = new();
    private readonly Dictionary<string, Func<IActorSender, IActor>> _actorFactories = new();

    /// <summary>
    /// Default constructor for manual registration.
    /// </summary>
    public PlayProducer()
    {
    }

    /// <summary>
    /// Constructor for Bootstrap pattern with Stage-specific Actor types and dependency injection support.
    /// </summary>
    /// <param name="stageTypes">Dictionary of stage type names to Stage implementation types.</param>
    /// <param name="actorTypes">Dictionary of stage type names to Actor implementation types.</param>
    /// <param name="serviceProvider">Service provider for dependency injection.</param>
    public PlayProducer(
        Dictionary<string, Type> stageTypes,
        Dictionary<string, Type> actorTypes,
        IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
        _stageTypes = stageTypes;
        _actorTypes = actorTypes;
    }

    /// <summary>
    /// Registers Stage and Actor factories for a given stage type.
    /// </summary>
    /// <param name="stageType">The unique identifier for this stage type.</param>
    /// <param name="stageFactory">Factory function to create IStage instances.</param>
    /// <param name="actorFactory">Factory function to create IActor instances.</param>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the stageType is already registered.
    /// </exception>
    public void Register(
        string stageType,
        Func<IStageSender, IStage> stageFactory,
        Func<IActorSender, IActor> actorFactory)
    {
        if (!_stageFactories.TryAdd(stageType, stageFactory))
        {
            throw new InvalidOperationException($"Stage type '{stageType}' is already registered");
        }

        _actorFactories[stageType] = actorFactory;
    }

    /// <summary>
    /// Creates a new Stage instance for the specified stage type.
    /// </summary>
    /// <param name="stageType">The stage type identifier.</param>
    /// <param name="stageSender">The sender to inject into the Stage.</param>
    /// <returns>A new IStage instance.</returns>
    /// <exception cref="KeyNotFoundException">
    /// Thrown when the stageType is not registered.
    /// </exception>
    internal IStage GetStage(string stageType, IStageSender stageSender)
    {
        // Manual registration (factory-based)
        if (_stageFactories.TryGetValue(stageType, out var factory))
        {
            return factory(stageSender);
        }

        // Type-based registration with DI
        if (_serviceProvider != null && _stageTypes.TryGetValue(stageType, out var type))
        {
            return (IStage)ActivatorUtilities.CreateInstance(
                _serviceProvider,
                type,
                stageSender);  // IStageSender is explicitly passed
        }

        throw new KeyNotFoundException($"Stage type '{stageType}' is not registered. Did you forget to call PlayProducer.Register()?");
    }

    /// <summary>
    /// Creates a new Actor instance for the specified stage type.
    /// </summary>
    /// <param name="stageType">The stage type identifier.</param>
    /// <param name="actorSender">The sender to inject into the Actor.</param>
    /// <returns>A new IActor instance.</returns>
    /// <exception cref="KeyNotFoundException">
    /// Thrown when the stageType is not registered.
    /// </exception>
    internal IActor GetActor(string stageType, IActorSender actorSender)
    {
        // Manual registration (factory-based)
        if (_actorFactories.TryGetValue(stageType, out var factory))
        {
            return factory(actorSender);
        }

        // Type-based registration with DI
        if (_serviceProvider != null && _actorTypes.TryGetValue(stageType, out var actorType))
        {
            return (IActor)ActivatorUtilities.CreateInstance(
                _serviceProvider,
                actorType,
                actorSender);  // IActorSender is explicitly passed
        }

        throw new KeyNotFoundException($"Actor type for stage '{stageType}' is not registered. Did you forget to call PlayProducer.Register()?");
    }

    /// <summary>
    /// Checks if the specified stage type is registered.
    /// </summary>
    /// <param name="stageType">The stage type to check.</param>
    /// <returns>true if registered, false otherwise.</returns>
    internal bool IsValidType(string stageType)
    {
        return _stageFactories.ContainsKey(stageType) || _stageTypes.ContainsKey(stageType);
    }

    /// <summary>
    /// Gets all registered stage types.
    /// </summary>
    /// <returns>Collection of registered stage type identifiers.</returns>
    public IReadOnlyCollection<string> GetRegisteredTypes()
    {
        var allTypes = new HashSet<string>(_stageFactories.Keys);
        foreach (var key in _stageTypes.Keys)
        {
            allTypes.Add(key);
        }
        return allTypes.ToList().AsReadOnly();
    }
}
