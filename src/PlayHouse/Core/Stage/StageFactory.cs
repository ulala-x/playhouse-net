#nullable enable

using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using PlayHouse.Abstractions;
using PlayHouse.Core.Messaging;
using PlayHouse.Core.Session;
using PlayHouse.Core.Timer;

namespace PlayHouse.Core.Stage;

/// <summary>
/// Factory for creating and managing stage instances.
/// </summary>
/// <remarks>
/// StageFactory provides:
/// 1. Stage type registration and instantiation
/// 2. Stage lifecycle management (Create, PostCreate, Destroy)
/// 3. Integration with StagePool for stage storage
/// 4. Dependency injection for stage components
/// </remarks>
public sealed class StageFactory
{
    private readonly StagePool _stagePool;
    private readonly PacketDispatcher _dispatcher;
    private readonly TimerManager _timerManager;
    private readonly SessionManager _sessionManager;
    private readonly ILoggerFactory _loggerFactory;
    private readonly StageTypeRegistry _registry;
    private readonly ILogger<StageFactory> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="StageFactory"/> class.
    /// </summary>
    /// <param name="stagePool">The stage pool for storing created stages.</param>
    /// <param name="dispatcher">The packet dispatcher.</param>
    /// <param name="timerManager">The timer manager.</param>
    /// <param name="sessionManager">The session manager.</param>
    /// <param name="loggerFactory">The logger factory.</param>
    public StageFactory(
        StagePool stagePool,
        PacketDispatcher dispatcher,
        TimerManager timerManager,
        SessionManager sessionManager,
        ILoggerFactory loggerFactory)
    {
        _stagePool = stagePool;
        _dispatcher = dispatcher;
        _timerManager = timerManager;
        _sessionManager = sessionManager;
        _loggerFactory = loggerFactory;
        _registry = new StageTypeRegistry();
        _logger = loggerFactory.CreateLogger<StageFactory>();
    }

    /// <summary>
    /// Gets the stage type registry for registering stage types.
    /// </summary>
    public StageTypeRegistry Registry => _registry;

    /// <summary>
    /// Creates a new stage instance.
    /// </summary>
    /// <param name="stageType">The stage type name.</param>
    /// <param name="creationPacket">The creation packet with initialization data.</param>
    /// <returns>
    /// A tuple containing the created stage context, error code, and optional reply packet.
    /// If error code is non-zero, the stage was not created.
    /// </returns>
    public async Task<(StageContext? stageContext, ushort errorCode, IPacket? reply)> CreateStageAsync(
        string stageType,
        IPacket creationPacket)
    {
        try
        {
            // Get registered stage type
            var userStageType = _registry.GetStageType(stageType);
            if (userStageType == null)
            {
                _logger.LogWarning("Stage type {StageType} not registered", stageType);
                return (null, ErrorCode.StageNotFound, null);
            }

            // Generate stage ID
            var stageId = _stagePool.GenerateStageId();

            // Create user stage instance
            var userStage = CreateUserStage(userStageType, stageId, stageType);

            // Create stage sender
            var stageSender = new StageSenderImpl(
                stageId,
                stageType,
                _dispatcher,
                _timerManager,
                _sessionManager,
                () => GetStageActorPool(stageId),
                _loggerFactory.CreateLogger<StageSenderImpl>());

            // Set stage sender on user stage
            SetStageSender(userStage, stageSender);

            // Create stage context
            var stageContext = new StageContext(
                userStage,
                stageSender,
                _loggerFactory.CreateLogger<StageContext>());

            // Call OnCreate lifecycle hook
            var (errorCode, reply) = await stageContext.OnCreateAsync(creationPacket);

            if (errorCode != ErrorCode.Success)
            {
                _logger.LogWarning("Stage {StageId} ({StageType}) creation failed with error {ErrorCode}",
                    stageId, stageType, errorCode);

                await stageContext.DisposeAsync();
                return (null, errorCode, reply);
            }

            // Add to stage pool
            if (!_stagePool.AddStage(stageContext))
            {
                _logger.LogError("Failed to add stage {StageId} to pool", stageId);
                await stageContext.DisposeAsync();
                return (null, ErrorCode.InternalError, null);
            }

            // Call OnPostCreate lifecycle hook
            await stageContext.OnPostCreateAsync();

            _logger.LogInformation("Created stage {StageId} ({StageType})", stageId, stageType);

            return (stageContext, ErrorCode.Success, reply);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating stage of type {StageType}", stageType);
            return (null, ErrorCode.InternalError, null);
        }
    }

    /// <summary>
    /// Destroys a stage and removes it from the pool.
    /// </summary>
    /// <param name="stageId">The stage identifier.</param>
    /// <returns>True if the stage was destroyed successfully; otherwise, false.</returns>
    public async Task<bool> DestroyStageAsync(int stageId)
    {
        try
        {
            var stageContext = _stagePool.RemoveStage(stageId);
            if (stageContext == null)
            {
                _logger.LogWarning("Stage {StageId} not found for destruction", stageId);
                return false;
            }

            await stageContext.DisposeAsync();

            _logger.LogInformation("Destroyed stage {StageId} ({StageType})",
                stageId, stageContext.StageType);

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error destroying stage {StageId}", stageId);
            return false;
        }
    }

    /// <summary>
    /// Creates a user stage instance using reflection.
    /// </summary>
    private IStage CreateUserStage(Type stageType, int stageId, string stageTypeName)
    {
        try
        {
            var instance = Activator.CreateInstance(stageType);
            if (instance is IStage stage)
            {
                return stage;
            }

            throw new InvalidOperationException(
                $"Type {stageType.FullName} does not implement IStage");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating user stage instance for type {StageType}", stageTypeName);
            throw;
        }
    }

    /// <summary>
    /// Sets the stage sender on a user stage instance.
    /// </summary>
    private void SetStageSender(IStage userStage, IStageSender stageSender)
    {
        // Use reflection to set the StageSender property
        var property = userStage.GetType().GetProperty(nameof(IStage.StageSender));
        if (property != null && property.CanWrite)
        {
            property.SetValue(userStage, stageSender);
        }
        else
        {
            _logger.LogWarning("Unable to set StageSender on user stage type {StageType}",
                userStage.GetType().Name);
        }
    }

    /// <summary>
    /// Gets the actor pool for a stage.
    /// </summary>
    private ActorPool GetStageActorPool(int stageId)
    {
        var stageContext = _stagePool.GetStage(stageId);
        if (stageContext == null)
        {
            throw new InvalidOperationException($"Stage {stageId} not found");
        }

        return stageContext.ActorPool;
    }
}

/// <summary>
/// Registry for stage type mappings.
/// </summary>
public sealed class StageTypeRegistry
{
    private readonly ConcurrentDictionary<string, Type> _stageTypes = new();

    /// <summary>
    /// Registers a stage type.
    /// </summary>
    /// <typeparam name="TStage">The stage type to register.</typeparam>
    /// <param name="stageTypeName">The stage type name (identifier).</param>
    /// <returns>True if registration succeeded; false if the type was already registered.</returns>
    public bool RegisterStageType<TStage>(string stageTypeName) where TStage : IStage
    {
        return RegisterStageType(stageTypeName, typeof(TStage));
    }

    /// <summary>
    /// Registers a stage type.
    /// </summary>
    /// <param name="stageTypeName">The stage type name (identifier).</param>
    /// <param name="stageType">The stage type to register.</param>
    /// <returns>True if registration succeeded; false if the type was already registered.</returns>
    public bool RegisterStageType(string stageTypeName, Type stageType)
    {
        if (!typeof(IStage).IsAssignableFrom(stageType))
        {
            throw new ArgumentException(
                $"Type {stageType.FullName} does not implement IStage",
                nameof(stageType));
        }

        return _stageTypes.TryAdd(stageTypeName, stageType);
    }

    /// <summary>
    /// Gets a registered stage type.
    /// </summary>
    /// <param name="stageTypeName">The stage type name.</param>
    /// <returns>The stage type, or null if not registered.</returns>
    public Type? GetStageType(string stageTypeName)
    {
        _stageTypes.TryGetValue(stageTypeName, out var stageType);
        return stageType;
    }

    /// <summary>
    /// Checks if a stage type is registered.
    /// </summary>
    /// <param name="stageTypeName">The stage type name.</param>
    /// <returns>True if the stage type is registered; otherwise, false.</returns>
    public bool IsRegistered(string stageTypeName)
    {
        return _stageTypes.ContainsKey(stageTypeName);
    }

    /// <summary>
    /// Gets the number of registered stage types.
    /// </summary>
    public int Count => _stageTypes.Count;
}
