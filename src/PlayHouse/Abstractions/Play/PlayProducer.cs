#nullable enable

namespace PlayHouse.Abstractions.Play;

/// <summary>
/// Factory for creating Stage and Actor instances.
/// </summary>
/// <remarks>
/// Content developers register their Stage and Actor factories using this class.
/// The framework uses these factories to create instances during Stage creation
/// and Actor join processes.
///
/// Usage:
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
    private readonly Dictionary<string, Func<IStageSender, IStage>> _stageFactories = new();
    private readonly Dictionary<string, Func<IActorSender, IActor>> _actorFactories = new();
    private readonly Type? _defaultActorType;

    /// <summary>
    /// Default constructor for manual registration.
    /// </summary>
    public PlayProducer()
    {
    }

    /// <summary>
    /// Constructor for Bootstrap pattern with Type-based registration.
    /// </summary>
    /// <param name="stageTypes">Dictionary of stage type names to Stage implementation types.</param>
    /// <param name="actorType">Default Actor implementation type.</param>
    public PlayProducer(Dictionary<string, Type> stageTypes, Type actorType)
    {
        _defaultActorType = actorType;

        foreach (var (stageType, type) in stageTypes)
        {
            var stageT = type;
            _stageFactories[stageType] = stageSender =>
            {
                var stage = (IStage)Activator.CreateInstance(stageT)!;
                SetStageSender(stage, stageSender);
                return stage;
            };

            _actorFactories[stageType] = actorSender =>
            {
                var actor = (IActor)Activator.CreateInstance(_defaultActorType)!;
                SetActorSender(actor, actorSender);
                return actor;
            };
        }
    }

    private static void SetStageSender(IStage stage, IStageSender stageSender)
    {
        // Use reflection to set the StageSender property
        var prop = stage.GetType().GetProperty("StageSender");
        prop?.SetValue(stage, stageSender);
    }

    private static void SetActorSender(IActor actor, IActorSender actorSender)
    {
        // Use reflection to set the ActorSender property
        var prop = actor.GetType().GetProperty("ActorSender");
        prop?.SetValue(actor, actorSender);
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
        if (_stageFactories.TryGetValue(stageType, out var factory))
        {
            return factory(stageSender);
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
        if (_actorFactories.TryGetValue(stageType, out var factory))
        {
            return factory(actorSender);
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
        return _stageFactories.ContainsKey(stageType);
    }

    /// <summary>
    /// Gets all registered stage types.
    /// </summary>
    /// <returns>Collection of registered stage type identifiers.</returns>
    public IReadOnlyCollection<string> GetRegisteredTypes()
    {
        return _stageFactories.Keys.ToList().AsReadOnly();
    }
}
