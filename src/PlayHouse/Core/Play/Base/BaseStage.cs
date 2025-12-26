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
    public void Post(RoutePacket packet)
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

    /// <summary>
    /// Posts a client route message to the Stage event loop.
    /// </summary>
    /// <param name="accountId">Account ID for actor routing.</param>
    /// <param name="msgId">Message ID.</param>
    /// <param name="msgSeq">Message sequence number.</param>
    /// <param name="sid">Session ID.</param>
    /// <param name="payload">Message payload.</param>
    internal void PostClientRoute(string accountId, string msgId, ushort msgSeq, long sid, ReadOnlyMemory<byte> payload)
    {
        _messageQueue.Enqueue(new StageMessage.ClientRouteMessage(accountId, msgId, msgSeq, sid, payload));
        TryStartProcessing();
    }

    /// <summary>
    /// Posts an authenticated Actor join to the Stage event loop.
    /// This completes the authentication flow by calling Stage callbacks.
    /// </summary>
    /// <param name="actor">The authenticated BaseActor.</param>
    internal void PostJoinActor(BaseActor actor)
    {
        _messageQueue.Enqueue(new StageMessage.JoinActorMessage(actor, null));
        TryStartProcessing();
    }

    /// <summary>
    /// Posts an authenticated Actor join to the Stage event loop and waits for completion.
    /// This is used during authentication to ensure the actor is fully joined before the client receives auth reply.
    /// </summary>
    /// <param name="actor">The authenticated BaseActor.</param>
    /// <returns>Task that completes when the actor has been joined to the stage.</returns>
    internal Task PostJoinActorAsync(BaseActor actor)
    {
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _messageQueue.Enqueue(new StageMessage.JoinActorMessage(actor, tcs));
        TryStartProcessing();
        return tcs.Task;
    }

    /// <summary>
    /// Posts a client disconnect notification to the Stage event loop.
    /// </summary>
    /// <param name="accountId">Account ID of disconnected client.</param>
    internal void PostDisconnect(string accountId)
    {
        _messageQueue.Enqueue(new StageMessage.DisconnectMessage(accountId));
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

            case StageMessage.ClientRouteMessage clientRouteMessage:
                await ProcessClientRouteAsync(clientRouteMessage.AccountId, clientRouteMessage.MsgId,
                    clientRouteMessage.MsgSeq, clientRouteMessage.Sid, clientRouteMessage.Payload);
                break;

            case StageMessage.JoinActorMessage joinActorMessage:
                await ProcessJoinActorAsync(joinActorMessage.Actor);
                joinActorMessage.CompletionSource?.TrySetResult(true);
                break;

            case StageMessage.DisconnectMessage disconnectMessage:
                await ProcessDisconnectAsync(disconnectMessage.AccountId);
                break;

            case StageMessage.DestroyMessage:
                await HandleDestroyAsync();
                break;
        }
    }

    private async Task DispatchRoutePacketAsync(RoutePacket packet)
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
               msgId == nameof(GetOrCreateStageReq) ||
               msgId == nameof(DestroyStageReq) ||
               msgId == nameof(DisconnectNoticeMsg) ||
               msgId == nameof(ReconnectMsg);
    }

    private async Task HandleSystemMessageAsync(string msgId, RoutePacket packet)
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

    private static IPacket CreateContentPacket(RoutePacket packet)
    {
        // Zero-copy: Share payload reference - handler reads synchronously before RoutePacket disposal
        return CPacket.Of(packet.MsgId, packet.Payload);
    }

    /// <summary>
    /// Processes client route message by finding actor and dispatching to Stage.OnDispatch.
    /// </summary>
    private async Task ProcessClientRouteAsync(string accountId, string msgId, ushort msgSeq, long sid, ReadOnlyMemory<byte> payload)
    {
        if (_actors.TryGetValue(accountId, out var baseActor))
        {
            // Create RouteHeader for client message reply routing
            var header = new Runtime.Proto.RouteHeader
            {
                MsgSeq = msgSeq,
                ServiceId = 1, // TODO: Get from config
                MsgId = msgId,
                From = "client", // From client transport
                StageId = StageId,
                AccountId = 0, // Will be set by ActorSender
                Sid = sid // Session ID for reply routing
            };

            // Set current header on StageSender for reply routing
            StageSender.SetCurrentHeader(header);

            try
            {
                // Create packet from ReadOnlyMemory - zero-copy with MemoryPayload
                var packet = CPacket.Of(msgId, new MemoryPayload(payload));
                await Stage.OnDispatch(baseActor.Actor, packet);
            }
            finally
            {
                // Clear current header after dispatch
                StageSender.ClearCurrentHeader();
            }
        }
        else
        {
            _logger?.LogWarning("Actor {AccountId} not found for client message {MsgId}", accountId, msgId);
        }
    }

    /// <summary>
    /// Processes actor join by checking for existing actor (reconnection) or new join.
    /// </summary>
    private async Task ProcessJoinActorAsync(BaseActor actor)
    {
        var accountId = actor.AccountId;

        // Check if actor already exists (reconnection)
        if (_actors.TryGetValue(accountId, out var existingActor))
        {
            _logger?.LogInformation("Actor {AccountId} reconnecting to stage {StageId}", accountId, StageId);

            // Destroy the new actor instance
            try
            {
                await actor.Actor.OnDestroy();
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error destroying new actor instance during reconnection for {AccountId}", accountId);
            }

            // Update existing actor's session information
            existingActor.ActorSender.Update(
                actor.ActorSender.SessionNid,
                actor.ActorSender.Sid,
                actor.ActorSender.ApiNid);

            // Notify reconnection
            try
            {
                await Stage.OnConnectionChanged(existingActor.Actor, true);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error in OnConnectionChanged(true) for actor {AccountId}", accountId);
            }

            return;
        }

        // New actor join (existing logic)
        var joinResult = await Stage.OnJoinStage(actor.Actor);
        if (!joinResult)
        {
            _logger?.LogWarning("Stage {StageId} rejected actor {AccountId}", StageId, accountId);
            try
            {
                await actor.Actor.OnDestroy();
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error destroying rejected actor {AccountId}", accountId);
            }
            return;
        }

        // Add actor to stage
        AddActor(actor);

        // Call Stage.OnPostJoinStage
        try
        {
            await Stage.OnPostJoinStage(actor.Actor);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error in OnPostJoinStage for actor {AccountId}", accountId);
        }
    }

    /// <summary>
    /// Processes client disconnect by calling OnConnectionChanged(false).
    /// Does not remove or destroy the Actor.
    /// </summary>
    private async Task ProcessDisconnectAsync(string accountId)
    {
        if (_actors.TryGetValue(accountId, out var baseActor))
        {
            try
            {
                await Stage.OnConnectionChanged(baseActor.Actor, false);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error in OnConnectionChanged(false) for actor {AccountId}", accountId);
            }
        }
        else
        {
            _logger?.LogWarning("Actor {AccountId} not found for disconnect notification", accountId);
        }
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
            return (false, (ushort)ErrorCode.InvalidStageType, null);
        }

        // 3. OnCreate
        await actor.OnCreate();

        // 4. OnAuthenticate
        var authResult = await actor.OnAuthenticate(authPacket);
        if (!authResult)
        {
            await actor.OnDestroy();
            return (false, (ushort)ErrorCode.AuthenticationFailed, null);
        }

        // 5. Validate AccountId
        if (string.IsNullOrEmpty(actorSender.AccountId))
        {
            await actor.OnDestroy();
            return (false, (ushort)ErrorCode.InvalidAccountId, null);
        }

        // 6. OnPostAuthenticate
        await actor.OnPostAuthenticate();

        // 7. OnJoinStage
        var joinResult = await Stage.OnJoinStage(actor);
        if (!joinResult)
        {
            await actor.OnDestroy();
            return (false, (ushort)ErrorCode.JoinStageRejected, null);
        }

        // 8. Add actor
        var baseActor = new BaseActor(actor, actorSender);
        AddActor(baseActor);

        // 9. OnPostJoinStage
        await Stage.OnPostJoinStage(actor);

        return (true, (ushort)ErrorCode.Success, baseActor);
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
        public RoutePacket Packet { get; }

        public RouteMessage(RoutePacket packet)
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

    /// <summary>
    /// Message for client route to actor.
    /// </summary>
    public sealed class ClientRouteMessage : StageMessage
    {
        public string AccountId { get; }
        public string MsgId { get; }
        public ushort MsgSeq { get; }
        public long Sid { get; }
        public ReadOnlyMemory<byte> Payload { get; }

        public ClientRouteMessage(string accountId, string msgId, ushort msgSeq, long sid, ReadOnlyMemory<byte> payload)
        {
            AccountId = accountId;
            MsgId = msgId;
            MsgSeq = msgSeq;
            Sid = sid;
            Payload = payload;
        }
    }

    /// <summary>
    /// Message for authenticated actor joining stage.
    /// </summary>
    public sealed class JoinActorMessage : StageMessage
    {
        public BaseActor Actor { get; }
        public TaskCompletionSource<bool>? CompletionSource { get; }

        public JoinActorMessage(BaseActor actor, TaskCompletionSource<bool>? completionSource = null)
        {
            Actor = actor;
            CompletionSource = completionSource;
        }
    }

    /// <summary>
    /// Message for client disconnection notification.
    /// </summary>
    public sealed class DisconnectMessage : StageMessage
    {
        public string AccountId { get; }

        public DisconnectMessage(string accountId)
        {
            AccountId = accountId;
        }
    }
}
