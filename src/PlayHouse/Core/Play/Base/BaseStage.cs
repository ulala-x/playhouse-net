#nullable enable

using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using PlayHouse.Abstractions;
using PlayHouse.Abstractions.Play;
using PlayHouse.Core.Play.EventLoop;
using PlayHouse.Core.Shared;
using PlayHouse.Runtime.Shared;
using PlayHouse.Runtime.ServerMesh.Message;
using PlayHouse.Runtime.Proto;
using PlayHouse.Runtime.ClientTransport;

// Alias to avoid conflict with System.Threading.TimerCallback
using TimerCallbackDelegate = PlayHouse.Abstractions.Play.TimerCallback;

namespace PlayHouse.Core.Play.Base;

/// <summary>
/// Base class that manages Stage lifecycle and event loop.
/// </summary>
/// <remarks>
/// BaseStage uses a global EventLoop for message processing.
/// Individual queues are removed to minimize memory and scheduling overhead.
/// </remarks>
internal sealed class BaseStage : IReplyPacketRegistry
{
    private readonly Dictionary<string, BaseActor> _actors = new();
    private readonly List<IDisposable> _pendingReplyPackets = new();
    private readonly ILogger? _logger;
    private BaseStageCmdHandler? _cmdHandler;
    private StageEventLoop _eventLoop = null!;  // PlayDispatcher에서 설정됨

    // Pool for ClientRouteMessage to avoid heap allocations
    internal static readonly Microsoft.Extensions.ObjectPool.ObjectPool<StageMessage.ClientRouteMessage> _clientRouteMessagePool = 
        new Microsoft.Extensions.ObjectPool.DefaultObjectPool<StageMessage.ClientRouteMessage>(
            new Microsoft.Extensions.ObjectPool.DefaultPooledObjectPolicy<StageMessage.ClientRouteMessage>());

    // AsyncLocal to track current Stage context for callback execution
    private static readonly AsyncLocal<BaseStage?> _currentStage = new();

    /// <summary>
    /// Gets the current Stage being processed (for callback execution optimization).
    /// </summary>
    internal static BaseStage? Current => _currentStage.Value;

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

    /// <summary>
    /// EventLoop을 설정합니다. PlayDispatcher에서 호출됩니다.
    /// </summary>
    /// <param name="eventLoop">Stage 메시지 처리를 담당할 EventLoop.</param>
    internal void SetEventLoop(StageEventLoop eventLoop)
    {
        _eventLoop = eventLoop;
    }

    /// <summary>
    /// Registers a reply packet for disposal after callback completion.
    /// </summary>
    public void RegisterReplyForDisposal(IDisposable packet)
    {
        _pendingReplyPackets.Add(packet);
    }

    /// <summary>
    /// Disposes all pending reply packets registered during message dispatch.
    /// </summary>
    private void DisposePendingReplies()
    {
        foreach (var packet in _pendingReplyPackets)
        {
            try
            {
                packet.Dispose();
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error disposing pending reply packet");
            }
        }
        _pendingReplyPackets.Clear();
    }

    #region Event Loop

    /// <summary>
    /// Posts a message to the Stage event loop.
    /// </summary>
    /// <param name="packet">The route packet to process.</param>
    public void Post(RoutePacket packet)
    {
        var msg = new StageMessage.RouteMessage(packet) { Stage = this };
        _eventLoop.Post(msg);
    }

    /// <summary>
    /// Posts a timer callback to the Stage event loop.
    /// </summary>
    /// <param name="timerId">Timer ID.</param>
    /// <param name="callback">Timer callback.</param>
    internal void PostTimerCallback(long timerId, TimerCallbackDelegate callback)
    {
        var msg = new StageMessage.TimerMessage(timerId, callback) { Stage = this };
        _eventLoop.Post(msg);
    }

    /// <summary>
    /// Posts an AsyncBlock result to the Stage event loop.
    /// </summary>
    /// <param name="asyncPacket">AsyncBlock packet.</param>
    internal void PostAsyncBlock(AsyncBlockPacket asyncPacket)
    {
        var msg = new StageMessage.AsyncMessage(asyncPacket) { Stage = this };
        _eventLoop.Post(msg);
    }

    /// <summary>
    /// Posts a reply callback to the Stage event loop.
    /// </summary>
    /// <param name="callback">Reply callback.</param>
    /// <param name="errorCode">Error code.</param>
    /// <param name="packet">Reply packet.</param>
    internal void PostReplyCallback(ReplyCallback callback, ushort errorCode, IPacket? packet)
    {
        var msg = new StageMessage.ReplyCallbackMessage(callback, errorCode, packet) { Stage = this };
        _eventLoop.Post(msg);
    }

    /// <summary>
    /// Posts a client route message to the Stage event loop.
    /// </summary>
    internal void PostClientRoute(BaseActor actor, string accountId, string msgId, ushort msgSeq, long sid, IPayload payload)
    {
        var message = _clientRouteMessagePool.Get();
        message.Update(actor, accountId, msgId, msgSeq, sid, payload);
        message.Stage = this;
        _eventLoop.Post(message);
    }

    /// <summary>
    /// Posts a client route message to the Stage event loop.
    /// </summary>
    internal void PostClientRoute(string accountId, string msgId, ushort msgSeq, long sid, IPayload payload)
    {
        if (_actors.TryGetValue(accountId, out var actor))
        {
            var message = _clientRouteMessagePool.Get();
            message.Update(actor, accountId, msgId, msgSeq, sid, payload);
            message.Stage = this;
            _eventLoop.Post(message);
        }
        else
        {
            _logger?.LogWarning("Actor {AccountId} not found for client message {MsgId}", accountId, msgId);
            payload.Dispose();
        }
    }

    /// <summary>
    /// Posts a JoinActorMessage to the Stage event loop.
    /// </summary>
    internal void PostJoinActor(StageMessage.JoinActorMessage message)
    {
        message.Stage = this;
        _eventLoop.Post(message);
    }

    /// <summary>
    /// Posts a client disconnect notification to the Stage event loop.
    /// </summary>
    /// <param name="accountId">Account ID of disconnected client.</param>
    internal void PostDisconnect(string accountId)
    {
        var msg = new StageMessage.DisconnectMessage(accountId) { Stage = this };
        _eventLoop.Post(msg);
    }

    /// <summary>
    /// EventLoop에서 메시지를 처리할 때 컨텍스트를 설정합니다.
    /// </summary>
    internal static IDisposable SetCurrentContext(BaseStage stage)
    {
        _currentStage.Value = stage;
        return new ContextScope();
    }

    private sealed class ContextScope : IDisposable
    {
        public void Dispose() => _currentStage.Value = null;
    }

    /// <summary>
    /// Executes a batch of work items for this stage in a single context setup.
    /// This significantly reduces overhead by setting headers and cleaning up once per batch.
    /// </summary>
    internal async Task ExecuteBatchAsync(List<IEventLoopWorkItem> batch)
    {
        _currentStage.Value = this;
        try
        {
            for (int i = 0; i < batch.Count; i++)
            {
                var item = batch[i];
                try
                {
                    var task = item.ExecuteAsync();
                    if (task.IsCompleted)
                    {
                        task.GetAwaiter().GetResult();
                    }
                    else
                    {
                        await task;
                    }
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Error executing batched work item in Stage {StageId}", StageId);
                }
                finally
                {
                    if (item is IDisposable disposable)
                    {
                        disposable.Dispose();
                    }
                }
            }
        }
        finally
        {
            _currentStage.Value = null;
            // Batch level cleanup
            DisposePendingReplies();
        }
    }

    #endregion

    #region Message Handlers (Internal)

    internal Task DispatchRoutePacketAsync(RoutePacket packet)
    {
        // Set current header for reply routing
        StageSender.SetCurrentHeader(packet.Header);

        try
        {
            var msgId = packet.MsgId;
            var accountIdString = packet.AccountId.ToString();

            Task task;
            // Check if this is a system message
            if (IsSystemMessage(msgId))
            {
                task = HandleSystemMessageAsync(msgId, packet);
            }
            else if (packet.AccountId != 0 && _actors.TryGetValue(accountIdString, out var baseActor))
            {
                // Client message (Actor exists)
                var contentPacket = CreateContentPacket(packet);
                task = Stage.OnDispatch(baseActor.Actor, contentPacket);
            }
            else
            {
                // Server-to-server message (no Actor context)
                var contentPacket = CreateContentPacket(packet);
                task = Stage.OnDispatch(contentPacket);
            }

            if (!task.IsCompleted)
            {
                return task.ContinueWith(t => 
                {
                    StageSender.ClearCurrentHeader();
                    DisposePendingReplies();
                    if (t.IsFaulted) throw t.Exception!;
                }, TaskScheduler.Default);
            }

            StageSender.ClearCurrentHeader();
            DisposePendingReplies();
            return task;
        }
        catch
        {
            StageSender.ClearCurrentHeader();
            DisposePendingReplies();
            throw;
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

    internal async Task HandleDestroyAsync()
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
    internal Task ProcessClientRouteAsync(StageMessage.ClientRouteMessage message)
    {
        var baseActor = message.Actor;

        // If actor is not pre-resolved (slow path), look it up now
        if (baseActor == null)
        {
            _actors.TryGetValue(message.AccountId, out baseActor);
        }

        if (baseActor != null)
        {
            // Create RouteHeader for client message reply routing
            var header = new RouteHeader
            {
                MsgSeq = message.MsgSeq,
                ServiceId = 1, // TODO: Get from config
                MsgId = message.MsgId,
                From = "client", // From client transport
                StageId = StageId,
                AccountId = 0, // Will be set by ActorSender
                Sid = message.Sid // Session ID for reply routing
            };

            // Set current header on StageSender for reply routing
            StageSender.SetCurrentHeader(header);

            try
            {
                // Take ownership of payload from message (zero-copy optimization)
                var payload = message.TakePayload();
                if (payload != null)
                {
                    // Pass payload directly to CPacket - ownership transferred
                    using var packet = CPacket.Of(message.MsgId, payload);
                    var task = Stage.OnDispatch(baseActor.Actor, packet);
                    
                    // 만약 동기적으로 완료되지 않았다면, 헤더 정리를 위한 continuation이 필요함
                    if (!task.IsCompleted)
                    {
                        return task.ContinueWith(t => 
                        {
                            StageSender.ClearCurrentHeader();
                            DisposePendingReplies();
                            if (t.IsFaulted) throw t.Exception!;
                        }, TaskScheduler.Default);
                    }
                    
                    // 동기 완료 시 즉시 헤더 정리
                    StageSender.ClearCurrentHeader();
                    DisposePendingReplies();
                    return task;
                }
            }
            catch
            {
                StageSender.ClearCurrentHeader();
                DisposePendingReplies();
                throw;
            }
        }
        else
        {
            _logger?.LogWarning("Actor {AccountId} not found for client message {MsgId}", message.AccountId, message.MsgId);
        }
        
        DisposePendingReplies();
        return Task.CompletedTask;
    }

    /// <summary>
    /// Processes actor join by checking for existing actor (reconnection) or new join.
    /// Sends auth reply after processing.
    /// </summary>
    internal async Task ProcessJoinActorAsync(StageMessage.JoinActorMessage message)
    {
        var actor = message.Actor;
        var accountId = actor.AccountId;
        ushort errorCode = (ushort)ErrorCode.Success;

        try
        {
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
            }
            else
            {
                // New actor join
                var joinResult = await Stage.OnJoinStage(actor.Actor);
                if (!joinResult)
                {
                    _logger?.LogWarning("Stage {StageId} rejected actor {AccountId}", StageId, accountId);
                    errorCode = (ushort)ErrorCode.JoinStageFailed;
                    try
                    {
                        await actor.Actor.OnDestroy();
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogError(ex, "Error destroying rejected actor {AccountId}", accountId);
                    }
                }
                else
                {
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
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error in ProcessJoinActorAsync for actor {AccountId}", accountId);
            errorCode = (ushort)ErrorCode.InternalError;
        }
        finally
        {
            // Send auth reply
            message.Session.SendResponse(
                message.AuthReplyMsgId,
                message.MsgSeq,
                StageId,
                errorCode,
                ReadOnlySpan<byte>.Empty);
            
            DisposePendingReplies();
        }
    }

    /// <summary>
    /// Processes client disconnect by calling OnConnectionChanged(false).
    /// Does not remove or destroy the Actor.
    /// </summary>
    internal async Task ProcessDisconnectAsync(string accountId)
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
        DisposePendingReplies();
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

        // 7. Create BaseActor (JoinStage is handled in ProcessJoinActorAsync)
        var baseActor = new BaseActor(actor, actorSender);

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
        _eventLoop.Post(new StageMessage.DestroyMessage() { Stage = this });
    }

    #endregion
}

/// <summary>
/// Base class for Stage event loop messages.
/// </summary>
internal abstract class StageMessage : IDisposable, IEventLoopWorkItem
{
    public BaseStage? Stage { get; internal set; }

    public virtual void Dispose() { }

    public abstract Task ExecuteAsync();

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

        public override Task ExecuteAsync() => Stage!.DispatchRoutePacketAsync(Packet);
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

        public override Task ExecuteAsync() => Callback.Invoke();
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

        public override Task ExecuteAsync() => AsyncPacket.PostCallback.Invoke(AsyncPacket.Result);
    }

    /// <summary>
    /// Message for reply callbacks.
    /// </summary>
    public sealed class ReplyCallbackMessage : StageMessage
    {
        public ReplyCallback Callback { get; }
        public ushort ErrorCode { get; }
        public IPacket? Packet { get; }

        public ReplyCallbackMessage(ReplyCallback callback, ushort errorCode, IPacket? packet)
        {
            Callback = callback;
            ErrorCode = errorCode;
            Packet = packet;
        }

        public override Task ExecuteAsync()
        {
            Callback(ErrorCode, Packet);
            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// Message to destroy the Stage.
    /// </summary>
    public sealed class DestroyMessage : StageMessage
    {
        public override Task ExecuteAsync() => Stage!.HandleDestroyAsync();
    }

    /// <summary>
    /// Message for client route to actor.
    /// </summary>
    public sealed class ClientRouteMessage : StageMessage
    {
        public BaseActor? Actor { get; private set; }
        public string AccountId { get; private set; } = "";
        public string MsgId { get; private set; } = "";
        public ushort MsgSeq { get; private set; }
        public long Sid { get; private set; }
        public IPayload? Payload { get; private set; }

        public ClientRouteMessage() { }

        public ClientRouteMessage(BaseActor? actor, string accountId, string msgId, ushort msgSeq, long sid, IPayload payload)
        {
            Update(actor, accountId, msgId, msgSeq, sid, payload);
        }

        internal void Update(BaseActor? actor, string accountId, string msgId, ushort msgSeq, long sid, IPayload payload)
        {
            Actor = actor;
            AccountId = accountId;
            MsgId = msgId;
            MsgSeq = msgSeq;
            Sid = sid;
            Payload = payload;
        }

        /// <summary>
        /// Takes ownership of the payload, preventing automatic disposal.
        /// </summary>
        /// <returns>The payload, or null if already taken.</returns>
        public IPayload? TakePayload()
        {
            var p = Payload;
            Payload = null;  // Transfer ownership - Dispose will not clean up
            return p;
        }

        public override void Dispose()
        {
            Payload?.Dispose();
            Payload = null;
            Actor = null;
            Stage = null;
            
            // Return to pool
            BaseStage._clientRouteMessagePool.Return(this);
        }

        public override Task ExecuteAsync() => Stage!.ProcessClientRouteAsync(this);
    }

    /// <summary>
    /// Message for authenticated actor joining stage.
    /// </summary>
    public sealed class JoinActorMessage : StageMessage
    {
        public BaseActor Actor { get; }
        public ITransportSession Session { get; }
        public ushort MsgSeq { get; }
        public string AuthReplyMsgId { get; }
        public IPayload Payload { get; }

        public JoinActorMessage(
            BaseActor actor,
            ITransportSession session,
            ushort msgSeq,
            string authReplyMsgId,
            IPayload payload)
        {
            Actor = actor;
            Session = session;
            MsgSeq = msgSeq;
            AuthReplyMsgId = authReplyMsgId;
            Payload = payload;
        }

        public override void Dispose()
        {
            Payload?.Dispose();
        }

        public override Task ExecuteAsync() => Stage!.ProcessJoinActorAsync(this);
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

        public override Task ExecuteAsync() => Stage!.ProcessDisconnectAsync(AccountId);
    }
}