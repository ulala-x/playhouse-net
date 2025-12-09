#nullable enable

using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using PlayHouse.Abstractions;

namespace PlayHouse.Core.Stage;

/// <summary>
/// Wraps a user-defined IStage with lock-free event loop processing.
/// </summary>
/// <remarks>
/// StageContext provides:
/// 1. A lock-free event loop via BaseStage
/// 2. Lifecycle management (Create, PostCreate, Destroy)
/// 3. Actor management via ActorPool
/// 4. Message routing to actors and stage handlers
/// </remarks>
public sealed class StageContext : BaseStage, IAsyncDisposable
{
    private readonly IStage _userStage;
    private readonly ActorPool _actorPool;
    private bool _isDisposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="StageContext"/> class.
    /// </summary>
    /// <param name="userStage">The user-defined stage implementation.</param>
    /// <param name="stageSender">The sender interface for this stage.</param>
    /// <param name="logger">The logger instance.</param>
    public StageContext(
        IStage userStage,
        IStageSender stageSender,
        ILogger<StageContext> logger)
        : base(stageSender, logger)
    {
        _userStage = userStage;
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
    /// Dispatches a route packet based on its type.
    /// </summary>
    /// <param name="packet">The route packet to dispatch.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    protected override async Task DispatchAsync(RoutePacket packet)
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

        // Convert RoutePacket to IPacket for user stage
        var userPacket = CreateUserPacket(packet);
        await _userStage.OnDispatch(actorContext.UserActor, userPacket);
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

            // Create actor instance using reflection (simplified - user should provide factory)
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
    /// This is a simplified implementation. In production, this should use a proper
    /// actor factory pattern that allows dependency injection and custom actor types.
    /// </remarks>
    private IActor CreateUserActor(long accountId, long sessionId)
    {
        // Get the stage sender implementation
        var stageSender = _stageSender as StageSenderImpl;
        if (stageSender == null)
        {
            throw new InvalidOperationException("StageSender is not a StageSenderImpl");
        }

        // Create actor sender
        var actorSender = new ActorSenderImpl(accountId, sessionId, stageSender);

        // Create a default actor implementation
        // TODO: This should be provided by user through actor factory
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
