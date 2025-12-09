#nullable enable

using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using PlayHouse.Abstractions;

namespace PlayHouse.Core.Stage;

/// <summary>
/// Wraps a user-defined IStage with lock-free event loop processing.
/// </summary>
/// <remarks>
/// StageContext provides:
/// 1. A lock-free event loop using CAS operations
/// 2. Lifecycle management (Create, PostCreate, Destroy)
/// 3. Actor management via ActorPool
/// 4. Message routing to actors and stage handlers
/// </remarks>
public sealed class StageContext : IAsyncDisposable
{
    private readonly IStage _userStage;
    private readonly IStageSender _stageSender;
    private readonly ActorPool _actorPool;
    private readonly ILogger<StageContext> _logger;
    private readonly ConcurrentQueue<RoutePacket> _msgQueue = new();
    private readonly AtomicBoolean _isProcessing = new(false);
    private readonly Func<long, long, IActor>? _actorFactory;
    private bool _isDisposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="StageContext"/> class.
    /// </summary>
    /// <param name="userStage">The user-defined stage implementation.</param>
    /// <param name="stageSender">The sender interface for this stage.</param>
    /// <param name="logger">The logger instance.</param>
    /// <param name="actorFactory">Optional factory function for creating user actors. Receives (accountId, sessionId).</param>
    public StageContext(
        IStage userStage,
        IStageSender stageSender,
        ILogger<StageContext> logger,
        Func<long, long, IActor>? actorFactory = null)
    {
        _userStage = userStage;
        _stageSender = stageSender;
        _logger = logger;
        _actorFactory = actorFactory;
        _actorPool = new ActorPool(logger);
    }

    /// <summary>
    /// Gets the stage identifier.
    /// </summary>
    public int StageId => _stageSender.StageId;

    /// <summary>
    /// Gets the stage type name.
    /// </summary>
    public string StageType => _stageSender.StageType;

    /// <summary>
    /// Gets the actor pool for this stage.
    /// </summary>
    public ActorPool ActorPool => _actorPool;

    /// <summary>
    /// Gets the current queue depth for monitoring purposes.
    /// </summary>
    public int QueueDepth => _msgQueue.Count;

    /// <summary>
    /// Gets a value indicating whether this stage is currently processing messages.
    /// </summary>
    public bool IsProcessing => _isProcessing.Value;

    /// <summary>
    /// Posts a packet to this stage's message queue for processing.
    /// </summary>
    /// <param name="routePacket">The route packet to process.</param>
    public void Post(RoutePacket routePacket)
    {
        _msgQueue.Enqueue(routePacket);

        // Only start processing if we're not already processing
        if (_isProcessing.CompareAndSet(false, true))
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await ProcessMessageLoopAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Unhandled exception in stage {StageId} message loop", StageId);
                }
            });
        }
    }

    /// <summary>
    /// Processes the message queue in a loop until empty.
    /// </summary>
    private async Task ProcessMessageLoopAsync()
    {
        do
        {
            // Process all available messages
            while (_msgQueue.TryDequeue(out var packet))
            {
                try
                {
                    using (packet)
                    {
                        await DispatchAsync(packet);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex,
                        "Error dispatching packet in stage {StageId}: MsgId={MsgId}, Type={PacketType}",
                        StageId, packet.MsgId, packet.PacketType);
                }
            }

            // Mark that we're done processing
            _isProcessing.Set(false);

            // Double-check: if new messages arrived, resume processing
        } while (!_msgQueue.IsEmpty && _isProcessing.CompareAndSet(false, true));
    }

    /// <summary>
    /// Dispatches a route packet based on its type.
    /// </summary>
    /// <param name="packet">The route packet to dispatch.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    private async Task DispatchAsync(RoutePacket packet)
    {
        if (_isDisposed)
        {
            _logger.LogWarning("Attempted to dispatch packet to disposed stage {StageId}", StageId);
            return;
        }

        try
        {
            switch (packet.PacketType)
            {
                case RoutePacketType.ClientPacket:
                    await DispatchClientPacketAsync(packet);
                    break;

                case RoutePacketType.StagePacket:
                    await DispatchStagePacketAsync(packet);
                    break;

                case RoutePacketType.Timer:
                    await DispatchTimerAsync(packet);
                    break;

                case RoutePacketType.AsyncBlockResult:
                    await DispatchAsyncBlockResultAsync(packet);
                    break;

                default:
                    _logger.LogWarning("Unknown packet type {PacketType} in stage {StageId}",
                        packet.PacketType, StageId);
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error dispatching {PacketType} in stage {StageId}",
                packet.PacketType, StageId);
        }
    }

    /// <summary>
    /// Dispatches a client packet to the target actor.
    /// </summary>
    private async Task DispatchClientPacketAsync(RoutePacket packet)
    {
        var actorContext = _actorPool.GetActor(packet.AccountId);
        if (actorContext == null)
        {
            _logger.LogWarning("Actor {AccountId} not found in stage {StageId}",
                packet.AccountId, StageId);
            return;
        }

        // Set request context for reply/send operations
        var stageSenderImpl = _stageSender as StageSenderImpl;
        if (stageSenderImpl != null)
        {
            var requestContext = new RequestContext(actorContext.SessionId, packet.MsgId, packet.MsgSeq);
            stageSenderImpl.SetRequestContext(requestContext);
        }

        try
        {
            // Convert RoutePacket to IPacket for user stage
            var userPacket = CreateUserPacket(packet);
            await _userStage.OnDispatch(actorContext.UserActor, userPacket);
        }
        finally
        {
            // Clear request context after dispatch
            stageSenderImpl?.SetRequestContext(null);
        }
    }

    /// <summary>
    /// Dispatches a stage-level packet.
    /// </summary>
    private async Task DispatchStagePacketAsync(RoutePacket packet)
    {
        // Stage-level packets are handled by the MessageHandler or specific stage logic
        // For now, log as info - this will be extended with stage message handlers
        _logger.LogInformation("Stage packet received in stage {StageId}: {MsgId}",
            StageId, packet.MsgId);
        await Task.CompletedTask;
    }

    /// <summary>
    /// Dispatches a timer callback.
    /// </summary>
    private async Task DispatchTimerAsync(RoutePacket packet)
    {
        if (packet.TimerCallback != null)
        {
            await packet.TimerCallback();
        }
    }

    /// <summary>
    /// Dispatches an async block result callback.
    /// </summary>
    private async Task DispatchAsyncBlockResultAsync(RoutePacket packet)
    {
        if (packet.Payload is AsyncBlockPayload asyncPayload)
        {
            await asyncPayload.PostCallback(asyncPayload.Result);
        }
    }

    /// <summary>
    /// Creates a user packet from a route packet.
    /// </summary>
    private IPacket CreateUserPacket(RoutePacket routePacket)
    {
        return new UserPacket(
            routePacket.MsgId,
            routePacket.MsgSeq,
            routePacket.StageId,
            routePacket.ErrorCode,
            routePacket.Payload);
    }

    /// <summary>
    /// Initializes the stage by calling user's OnCreate.
    /// </summary>
    /// <param name="packet">The creation packet.</param>
    /// <returns>A tuple containing error code and optional reply packet.</returns>
    public async Task<(ushort errorCode, IPacket? reply)> OnCreateAsync(IPacket packet)
    {
        return await _userStage.OnCreate(packet);
    }

    /// <summary>
    /// Completes stage initialization by calling user's OnPostCreate.
    /// </summary>
    public async Task OnPostCreateAsync()
    {
        await _userStage.OnPostCreate();
    }

    /// <summary>
    /// Joins an actor to this stage.
    /// </summary>
    /// <param name="accountId">The account identifier.</param>
    /// <param name="sessionId">The session identifier.</param>
    /// <param name="userInfo">User information packet provided during join.</param>
    /// <returns>
    /// A tuple containing an error code (0 for success), optional reply packet, and the created actor context.
    /// </returns>
    public async Task<(ushort errorCode, IPacket? reply, ActorContext? actorContext)> JoinActorAsync(
        long accountId,
        long sessionId,
        IPacket userInfo)
    {
        if (_isDisposed)
        {
            _logger.LogWarning("Attempted to join actor to disposed stage {StageId}", StageId);
            return (ErrorCode.InvalidState, null, null);
        }

        try
        {
            // Check if actor already exists
            if (_actorPool.HasActor(accountId))
            {
                _logger.LogWarning("Actor {AccountId} already exists in stage {StageId}", accountId, StageId);
                return (ErrorCode.DuplicateLogin, null, null);
            }

            // Create actor instance using factory or default
            var userActor = CreateUserActor(accountId, sessionId);

            // Create actor context
            var actorContext = new ActorContext(
                accountId,
                sessionId,
                userActor,
                _logger);

            // Initialize actor
            await actorContext.OnCreateAsync();

            // Call stage's OnJoinRoom
            var (errorCode, reply) = await _userStage.OnJoinRoom(userActor, userInfo);

            if (errorCode != ErrorCode.Success)
            {
                _logger.LogWarning("Actor {AccountId} join rejected by stage {StageId} with error {ErrorCode}",
                    accountId, StageId, errorCode);

                await actorContext.DisposeAsync();
                return (errorCode, reply, null);
            }

            // Add actor to pool
            if (!_actorPool.AddActor(actorContext))
            {
                _logger.LogError("Failed to add actor {AccountId} to pool in stage {StageId}", accountId, StageId);
                await actorContext.DisposeAsync();
                return (ErrorCode.InternalError, null, null);
            }

            // Call stage's OnPostJoinRoom
            await _userStage.OnPostJoinRoom(userActor);

            _logger.LogInformation("Actor {AccountId} joined stage {StageId}", accountId, StageId);

            return (ErrorCode.Success, reply, actorContext);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error joining actor {AccountId} to stage {StageId}", accountId, StageId);
            return (ErrorCode.InternalError, null, null);
        }
    }

    /// <summary>
    /// Removes an actor from this stage.
    /// </summary>
    /// <param name="accountId">The account identifier.</param>
    /// <param name="reason">The reason for leaving.</param>
    /// <returns>True if the actor was removed successfully; otherwise, false.</returns>
    public async Task<bool> LeaveActorAsync(long accountId, LeaveReason reason)
    {
        if (_isDisposed)
        {
            _logger.LogWarning("Attempted to remove actor from disposed stage {StageId}", StageId);
            return false;
        }

        try
        {
            var actorContext = _actorPool.RemoveActor(accountId);
            if (actorContext == null)
            {
                _logger.LogWarning("Actor {AccountId} not found in stage {StageId} for removal", accountId, StageId);
                return false;
            }

            // Call stage's OnLeaveRoom
            await _userStage.OnLeaveRoom(actorContext.UserActor, reason);

            // Dispose actor context
            await actorContext.DisposeAsync();

            _logger.LogInformation("Actor {AccountId} left stage {StageId} (reason: {Reason})",
                accountId, StageId, reason);

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error removing actor {AccountId} from stage {StageId}", accountId, StageId);
            return false;
        }
    }

    /// <summary>
    /// Updates the connection state of an actor.
    /// </summary>
    /// <param name="accountId">The account identifier.</param>
    /// <param name="isConnected">True if connected; false if disconnected.</param>
    /// <param name="reason">The disconnection reason, if applicable.</param>
    /// <returns>True if the update was successful; otherwise, false.</returns>
    public async Task<bool> UpdateActorConnectionAsync(
        long accountId,
        bool isConnected,
        DisconnectReason? reason = null)
    {
        if (_isDisposed)
        {
            _logger.LogWarning("Attempted to update actor connection in disposed stage {StageId}", StageId);
            return false;
        }

        try
        {
            var actorContext = _actorPool.GetActor(accountId);
            if (actorContext == null)
            {
                _logger.LogWarning("Actor {AccountId} not found in stage {StageId} for connection update",
                    accountId, StageId);
                return false;
            }

            // Update actor connection state
            actorContext.SetConnectionState(isConnected);

            // Notify stage of connection change
            await _userStage.OnActorConnectionChanged(actorContext.UserActor, isConnected, reason);

            _logger.LogInformation("Actor {AccountId} connection updated in stage {StageId}: {State}",
                accountId, StageId, isConnected ? "connected" : "disconnected");

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating actor {AccountId} connection in stage {StageId}",
                accountId, StageId);
            return false;
        }
    }

    /// <summary>
    /// Creates a user actor instance.
    /// </summary>
    /// <param name="accountId">The account identifier.</param>
    /// <param name="sessionId">The session identifier.</param>
    /// <returns>A new IActor instance.</returns>
    /// <remarks>
    /// If an actor factory was provided during construction, it will be used to create
    /// a custom actor type. Otherwise, a default actor implementation will be used.
    /// </remarks>
    private IActor CreateUserActor(long accountId, long sessionId)
    {
        // Use the actor factory if provided
        if (_actorFactory != null)
        {
            return _actorFactory(accountId, sessionId);
        }

        // Get the stage sender implementation for default actor
        var stageSender = _stageSender as StageSenderImpl;
        if (stageSender == null)
        {
            throw new InvalidOperationException("StageSender is not a StageSenderImpl");
        }

        // Create actor sender for default actor
        var actorSender = new ActorSenderImpl(accountId, sessionId, stageSender);

        // Create a default actor implementation
        return new DefaultActor(actorSender);
    }

    /// <summary>
    /// Disposes the stage and all its actors.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (_isDisposed) return;

        _isDisposed = true;

        try
        {
            // Dispose all actors
            await _actorPool.DisposeAllAsync();

            // Dispose user stage
            await _userStage.DisposeAsync();

            _logger.LogInformation("Stage {StageId} ({StageType}) disposed", StageId, StageType);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error disposing stage {StageId}", StageId);
        }
    }

    /// <summary>
    /// Default actor implementation used when no custom actor is provided.
    /// </summary>
    private sealed class DefaultActor : IActor
    {
        public IActorSender ActorSender { get; }
        public bool IsConnected => true;

        public DefaultActor(IActorSender actorSender)
        {
            ActorSender = actorSender;
        }

        public Task OnCreate() => Task.CompletedTask;
        public Task OnDestroy() => Task.CompletedTask;
        public Task OnAuthenticate(IPacket? authData) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    /// <summary>
    /// Simple implementation of IPacket for user stage consumption.
    /// </summary>
    private sealed class UserPacket : IPacket
    {
        public string MsgId { get; }
        public ushort MsgSeq { get; }
        public int StageId { get; }
        public ushort ErrorCode { get; }
        public IPayload Payload { get; }

        public UserPacket(string msgId, ushort msgSeq, int stageId, ushort errorCode, IPayload payload)
        {
            MsgId = msgId;
            MsgSeq = msgSeq;
            StageId = stageId;
            ErrorCode = errorCode;
            Payload = payload;
        }

        public void Dispose()
        {
            // Payload disposal is handled by RoutePacket
        }
    }
}
