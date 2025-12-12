#nullable enable

using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using PlayHouse.Abstractions;
using PlayHouse.Abstractions.Play;
using PlayHouse.Core.Shared;
using PlayHouse.Runtime.Shared;
using PlayHouse.Runtime.ServerMesh.Message;
using PlayHouse.Runtime.Proto;

// Alias to avoid conflict with System.Threading.TimerCallback
using TimerCallbackDelegate = PlayHouse.Abstractions.Play.TimerCallback;

namespace PlayHouse.Core.Play.Base;

/// <summary>
/// Base class that manages Stage lifecycle and event loop.
/// </summary>
/// <remarks>
/// BaseStage implements a lock-free event loop using ConcurrentQueue and AtomicBoolean.
/// All messages are processed sequentially on the event loop to ensure thread safety
/// when accessing Stage state.
///
/// Event Loop Pattern:
/// 1. Messages are enqueued via Post()
/// 2. CAS operation ensures only one processing loop runs at a time
/// 3. Loop processes all queued messages
/// 4. Loop exits when queue is empty (double-check for race conditions)
/// </remarks>
internal sealed class BaseStage
{
    private readonly ConcurrentQueue<StageMessage> _messageQueue = new();
    private readonly AtomicBoolean _isProcessing = new(false);
    private readonly Dictionary<string, BaseActor> _actors = new();
    private readonly ILogger? _logger;
    private BaseStageCmdHandler? _cmdHandler;

    /// <summary>
    /// Gets the content-implemented Stage.
    /// </summary>
    public IStage Stage { get; }

    /// <summary>
    /// Gets the framework-provided StageSender.
    /// </summary>
    public XStageSender StageSender { get; }

    /// <summary>
    /// Gets whether the Stage has been created successfully.
    /// </summary>
    public bool IsCreated { get; private set; }

    /// <summary>
    /// Gets the Stage ID.
    /// </summary>
    public long StageId => StageSender.StageId;

    /// <summary>
    /// Gets the Stage type.
    /// </summary>
    public string StageType => StageSender.StageType;

    /// <summary>
    /// Initializes a new instance of the <see cref="BaseStage"/> class.
    /// </summary>
    /// <param name="stage">Content-implemented Stage.</param>
    /// <param name="stageSender">Framework StageSender.</param>
    /// <param name="logger">Optional logger.</param>
    public BaseStage(IStage stage, XStageSender stageSender, ILogger? logger = null)
    {
        Stage = stage;
        StageSender = stageSender;
        _logger = logger;
    }

    /// <summary>
    /// Sets the command handler for system messages.
    /// </summary>
    /// <param name="cmdHandler">The command handler.</param>
    internal void SetCmdHandler(BaseStageCmdHandler cmdHandler)
    {
        _cmdHandler = cmdHandler;
    }

    #region Event Loop

    /// <summary>
    /// Posts a message to the Stage event loop.
    /// </summary>
    /// <param name="packet">The route packet to process.</param>
    public void Post(RuntimeRoutePacket packet)
    {
        _messageQueue.Enqueue(new StageMessage.RouteMessage(packet));
        TryStartProcessing();
    }

    /// <summary>
    /// Posts a timer callback to the Stage event loop.
    /// </summary>
    /// <param name="timerId">Timer ID.</param>
    /// <param name="callback">Timer callback.</param>
    internal void PostTimerCallback(long timerId, TimerCallbackDelegate callback)
    {
        _messageQueue.Enqueue(new StageMessage.TimerMessage(timerId, callback));
        TryStartProcessing();
    }

    /// <summary>
    /// Posts an AsyncBlock result to the Stage event loop.
    /// </summary>
    /// <param name="asyncPacket">AsyncBlock packet.</param>
    internal void PostAsyncBlock(AsyncBlockPacket asyncPacket)
    {
        _messageQueue.Enqueue(new StageMessage.AsyncMessage(asyncPacket));
        TryStartProcessing();
    }

    private void TryStartProcessing()
    {
        // CAS: Only one thread can enter the processing loop
        if (_isProcessing.CompareAndSet(false, true))
        {
            _ = Task.Run(ProcessMessageLoopAsync);
        }
    }

    private async Task ProcessMessageLoopAsync()
    {
        do
        {
            // Process all queued messages
            while (_messageQueue.TryDequeue(out var message))
            {
                try
                {
                    await DispatchMessageAsync(message);
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Unhandled exception in Stage {StageId}", StageId);
                }
                finally
                {
                    message.Dispose();
                }
            }

            // Reset processing flag
            _isProcessing.Set(false);

            // Double-check: If new messages arrived during reset, restart processing
        } while (!_messageQueue.IsEmpty && _isProcessing.CompareAndSet(false, true));
    }

    private async Task DispatchMessageAsync(StageMessage message)
    {
        switch (message)
        {
            case StageMessage.RouteMessage routeMessage:
                await DispatchRoutePacketAsync(routeMessage.Packet);
                break;

            case StageMessage.TimerMessage timerMessage:
                await timerMessage.Callback.Invoke();
                break;

            case StageMessage.AsyncMessage asyncMessage:
                await asyncMessage.AsyncPacket.PostCallback.Invoke(asyncMessage.AsyncPacket.Result);
                break;

            case StageMessage.DestroyMessage:
                await HandleDestroyAsync();
                break;
        }
    }

    private async Task DispatchRoutePacketAsync(RuntimeRoutePacket packet)
    {
        // Set current header for reply routing
        StageSender.SetCurrentHeader(packet.Header);

        try
        {
            var msgId = packet.MsgId;
            var accountIdString = packet.AccountId.ToString();

            // Check if this is a system message
            if (IsSystemMessage(msgId))
            {
                await HandleSystemMessageAsync(msgId, packet);
            }
            else if (packet.AccountId != 0 && _actors.TryGetValue(accountIdString, out var baseActor))
            {
                // Client message (Actor exists)
                var contentPacket = CreateContentPacket(packet);
                await Stage.OnDispatch(baseActor.Actor, contentPacket);
            }
            else
            {
                // Server-to-server message (no Actor context)
                var contentPacket = CreateContentPacket(packet);
                await Stage.OnDispatch(contentPacket);
            }
        }
        finally
        {
            StageSender.ClearCurrentHeader();
        }
    }

    private static bool IsSystemMessage(string msgId)
    {
        return msgId.StartsWith("PlayHouse.Runtime.Proto.") ||
               msgId == nameof(CreateStageReq) ||
               msgId == nameof(JoinStageReq) ||
               msgId == nameof(GetOrCreateStageReq) ||
               msgId == nameof(DestroyStageReq) ||
               msgId == nameof(DisconnectNoticeMsg) ||
               msgId == nameof(ReconnectMsg);
    }

    private async Task HandleSystemMessageAsync(string msgId, RuntimeRoutePacket packet)
    {
        if (_cmdHandler == null)
        {
            _logger?.LogWarning("CmdHandler not set for Stage {StageId}, cannot handle system message: {MsgId}",
                StageId, msgId);
            return;
        }

        var handled = await _cmdHandler.HandleAsync(msgId, packet, this);
        if (!handled)
        {
            _logger?.LogWarning("Unhandled system message: {MsgId} for Stage {StageId}", msgId, StageId);
        }
    }

    private async Task HandleDestroyAsync()
    {
        // Destroy all actors
        foreach (var baseActor in _actors.Values.ToList())
        {
            try
            {
                await baseActor.Actor.OnDestroy();
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error destroying Actor {AccountId}", baseActor.AccountId);
            }
        }
        _actors.Clear();

        // Call Stage.OnDestroy
        try
        {
            await Stage.OnDestroy();
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error destroying Stage {StageId}", StageId);
        }
    }

    private static IPacket CreateContentPacket(RuntimeRoutePacket packet)
    {
        return CPacket.Of(packet.MsgId, packet.Payload.DataSpan.ToArray());
    }

    #endregion

    #region Actor Management

    /// <summary>
    /// Adds an Actor to this Stage.
    /// </summary>
    /// <param name="baseActor">The actor to add.</param>
    public void AddActor(BaseActor baseActor)
    {
        _actors[baseActor.AccountId] = baseActor;
    }

    /// <summary>
    /// Removes an Actor from this Stage.
    /// </summary>
    /// <param name="accountId">Account ID of the actor.</param>
    /// <returns>true if removed, false if not found.</returns>
    public bool RemoveActor(string accountId)
    {
        return _actors.Remove(accountId);
    }

    /// <summary>
    /// Gets an Actor by account ID.
    /// </summary>
    /// <param name="accountId">Account ID.</param>
    /// <returns>BaseActor if found, null otherwise.</returns>
    public BaseActor? GetActor(string accountId)
    {
        return _actors.GetValueOrDefault(accountId);
    }

    /// <summary>
    /// Gets the number of actors in this Stage.
    /// </summary>
    public int ActorCount => _actors.Count;

    /// <summary>
    /// Handles an Actor leaving the Stage.
    /// </summary>
    /// <param name="accountId">Account ID.</param>
    /// <param name="sessionNid">Session server NID.</param>
    /// <param name="sid">Session ID.</param>
    public async void LeaveStage(string accountId, string sessionNid, long sid)
    {
        if (_actors.TryGetValue(accountId, out var baseActor))
        {
            _actors.Remove(accountId);

            try
            {
                await baseActor.Actor.OnDestroy();
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error in Actor.OnDestroy for {AccountId}", accountId);
            }

            // Note: Connection is NOT closed here - actor can join another stage
        }
    }

    #endregion

    #region Reply

    /// <summary>
    /// Sends an error reply to the current request.
    /// </summary>
    public void Reply(ushort errorCode) => StageSender.Reply(errorCode);

    /// <summary>
    /// Sends a reply packet to the current request.
    /// </summary>
    public void Reply(IPacket packet) => StageSender.Reply(packet);

    #endregion

    #region Stage Lifecycle Helpers

    /// <summary>
    /// Creates the stage by calling IStage.OnCreate.
    /// </summary>
    /// <param name="stageType">Stage type identifier.</param>
    /// <param name="packet">Content packet for OnCreate.</param>
    /// <returns>Error code and optional reply packet.</returns>
    public async Task<(bool success, IPacket? reply)> CreateStage(string stageType, IPacket packet)
    {
        StageSender.SetStageType(stageType);
        var (result, replyPacket) = await Stage.OnCreate(packet);

        if (result)
        {
            MarkAsCreated();
        }

        return (result, replyPacket);
    }

    /// <summary>
    /// Handles actor join flow (10-step authentication).
    /// </summary>
    /// <param name="sessionNid">Session server NID.</param>
    /// <param name="sid">Session ID.</param>
    /// <param name="apiNid">API server NID.</param>
    /// <param name="authPacket">Authentication packet.</param>
    /// <param name="producer">Play producer for actor creation.</param>
    /// <returns>Tuple of (success, errorCode, actor).</returns>
    public async Task<(bool success, ushort errorCode, BaseActor? actor)> JoinActor(
        string sessionNid,
        long sid,
        string apiNid,
        IPacket authPacket,
        PlayProducer producer)
    {
        // 1. Create XActorSender
        var actorSender = new XActorSender(
            sessionNid,
            sid,
            apiNid,
            this);

        // 2. Create IActor
        IActor actor;
        try
        {
            actor = producer.GetActor(StageType, actorSender);
        }
        catch (KeyNotFoundException)
        {
            return (false, BaseErrorCode.InvalidStageType, null);
        }

        // 3. OnCreate
        await actor.OnCreate();

        // 4. OnAuthenticate
        var authResult = await actor.OnAuthenticate(authPacket);
        if (!authResult)
        {
            await actor.OnDestroy();
            return (false, BaseErrorCode.AuthenticationFailed, null);
        }

        // 5. Validate AccountId
        if (string.IsNullOrEmpty(actorSender.AccountId))
        {
            await actor.OnDestroy();
            return (false, BaseErrorCode.InvalidAccountId, null);
        }

        // 6. OnPostAuthenticate
        await actor.OnPostAuthenticate();

        // 7. OnJoinStage
        var joinResult = await Stage.OnJoinStage(actor);
        if (!joinResult)
        {
            await actor.OnDestroy();
            return (false, BaseErrorCode.JoinStageRejected, null);
        }

        // 8. Add actor
        var baseActor = new BaseActor(actor, actorSender);
        AddActor(baseActor);

        // 9. OnPostJoinStage
        await Stage.OnPostJoinStage(actor);

        return (true, BaseErrorCode.Success, baseActor);
    }

    /// <summary>
    /// Calls OnPostCreate on the stage.
    /// </summary>
    public async Task OnPostCreate()
    {
        await Stage.OnPostCreate();
    }

    #endregion

    #region Lifecycle

    /// <summary>
    /// Marks the Stage as created.
    /// </summary>
    public void MarkAsCreated()
    {
        IsCreated = true;
    }

    /// <summary>
    /// Posts a destroy request to the event loop.
    /// </summary>
    internal void PostDestroy()
    {
        _messageQueue.Enqueue(new StageMessage.DestroyMessage());
        TryStartProcessing();
    }

    #endregion
}

/// <summary>
/// Base class for Stage event loop messages.
/// </summary>
internal abstract class StageMessage : IDisposable
{
    public virtual void Dispose() { }

    /// <summary>
    /// Message containing a route packet.
    /// </summary>
    public sealed class RouteMessage : StageMessage
    {
        public RuntimeRoutePacket Packet { get; }

        public RouteMessage(RuntimeRoutePacket packet)
        {
            Packet = packet;
        }

        public override void Dispose()
        {
            Packet.Dispose();
        }
    }

    /// <summary>
    /// Message for timer callbacks.
    /// </summary>
    public sealed class TimerMessage : StageMessage
    {
        public long TimerId { get; }
        public TimerCallbackDelegate Callback { get; }

        public TimerMessage(long timerId, TimerCallbackDelegate callback)
        {
            TimerId = timerId;
            Callback = callback;
        }
    }

    /// <summary>
    /// Message for AsyncBlock results.
    /// </summary>
    public sealed class AsyncMessage : StageMessage
    {
        public AsyncBlockPacket AsyncPacket { get; }

        public AsyncMessage(AsyncBlockPacket asyncPacket)
        {
            AsyncPacket = asyncPacket;
        }
    }

    /// <summary>
    /// Message to destroy the Stage.
    /// </summary>
    public sealed class DestroyMessage : StageMessage { }
}
