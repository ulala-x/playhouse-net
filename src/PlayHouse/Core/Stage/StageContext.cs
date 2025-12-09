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
internal sealed class StageContext : BaseStage, IAsyncDisposable
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
