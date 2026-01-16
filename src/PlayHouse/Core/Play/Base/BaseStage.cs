#nullable enable

using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using PlayHouse.Abstractions;
using PlayHouse.Abstractions.Play;
using PlayHouse.Core.Shared;
using PlayHouse.Runtime.ServerMesh.Message;
using PlayHouse.Runtime.Proto;
using PlayHouse.Runtime.ClientTransport;

// Alias to avoid conflict with System.Threading.TimerCallback
using TimerCallbackDelegate = PlayHouse.Abstractions.Play.TimerCallback;

namespace PlayHouse.Core.Play.Base;

/// <summary>
/// Base class that manages Stage lifecycle and mailbox-based scheduling using ThreadPool.
/// </summary>
internal sealed class BaseStage : IReplyPacketRegistry
{
    private readonly Dictionary<string, BaseActor> _actors = new();
    private readonly ConcurrentQueue<StageMessage> _mailbox = new();
    private readonly List<IDisposable> _pendingReplyPackets = new();
    private readonly ILogger _logger;
    private BaseStageCmdHandler? _cmdHandler;
    private int _isScheduled;

    // Pool for ClientRouteMessage to avoid heap allocations
    internal static readonly Microsoft.Extensions.ObjectPool.ObjectPool<StageMessage.ClientRouteMessage> _clientRouteMessagePool = 
        new Microsoft.Extensions.ObjectPool.DefaultObjectPool<StageMessage.ClientRouteMessage>(
            new Microsoft.Extensions.ObjectPool.DefaultPooledObjectPolicy<StageMessage.ClientRouteMessage>());

    // Pool for ContinuationMessage to avoid heap allocations
    internal static readonly Microsoft.Extensions.ObjectPool.ObjectPool<StageMessage.ContinuationMessage> _continuationMessagePool = 
        new Microsoft.Extensions.ObjectPool.DefaultObjectPool<StageMessage.ContinuationMessage>(
            new Microsoft.Extensions.ObjectPool.DefaultPooledObjectPolicy<StageMessage.ContinuationMessage>());

    // AsyncLocal to track current Stage context
    private static readonly AsyncLocal<BaseStage?> _currentStage = new();

    /// <summary>
    /// Gets the current Stage being processed.
    /// </summary>
    internal static BaseStage? Current => _currentStage.Value;

    public IStage Stage { get; }
    public XStageSender StageSender { get; }
    public bool IsCreated { get; private set; }
    public long StageId => StageSender.StageId;
    public string StageType => StageSender.StageType;

    public BaseStage(IStage stage, XStageSender stageSender, ILogger logger)
    {
        Stage = stage;
        StageSender = stageSender;
        _logger = logger;
    }

    internal void SetCmdHandler(BaseStageCmdHandler cmdHandler) => _cmdHandler = cmdHandler;

    public void RegisterReplyForDisposal(IDisposable packet) => _pendingReplyPackets.Add(packet);

    private void DisposePendingReplies()
    {
        foreach (var packet in _pendingReplyPackets)
        {
            try { packet.Dispose(); }
            catch (Exception ex) { _logger.LogError(ex, "Error disposing pending reply packet"); }
        }
        _pendingReplyPackets.Clear();
    }

    /// <summary>
    /// ThreadPool에서 실행될 핵심 로직.
    /// 메일박스의 메시지를 순차적으로 배치 처리합니다.
    /// </summary>
    private async Task ExecuteAsync()
    {
        _currentStage.Value = this;
        try
        {
            int processed = 0;
            while (processed < 100 && _mailbox.TryDequeue(out var message))
            {
                try
                {
                    var task = message.ExecuteAsync();
                    if (task.IsCompleted) task.GetAwaiter().GetResult();
                    else await task;
                }
                catch (Exception ex) 
                {
                    _logger.LogError(ex, "Error executing message in Stage {StageId}", StageId); 
                }
                finally
                {
                    message.Dispose();
                    DisposePendingReplies();
                }
                processed++;
            }
        }
        finally
        {
            _currentStage.Value = null;
            Interlocked.Exchange(ref _isScheduled, 0);
            if (!_mailbox.IsEmpty) ScheduleExecution();
        }
    }

    private void ScheduleExecution()
    {
        if (Interlocked.CompareExchange(ref _isScheduled, 1, 0) == 0)
        {
            // ThreadPool.QueueUserWorkItem을 사용하여 ExecutionContext/AsyncLocal 유지
            ThreadPool.QueueUserWorkItem(_ => _ = ExecuteAsync());
        }
    }

    #region Post Methods

    public void Post(RoutePacket packet)
    {
        _mailbox.Enqueue(new StageMessage.RouteMessage(packet) { Stage = this });
        ScheduleExecution();
    }

    internal void PostTimerCallback(long timerId, TimerCallbackDelegate callback)
    {
        _mailbox.Enqueue(new StageMessage.TimerMessage(timerId, callback) { Stage = this });
        ScheduleExecution();
    }

    internal void PostAsyncBlock(AsyncBlockPacket asyncPacket)
    {
        _mailbox.Enqueue(new StageMessage.AsyncMessage(asyncPacket) { Stage = this });
        ScheduleExecution();
    }

    internal void PostReplyCallback(ReplyCallback callback, ushort errorCode, IPacket? packet)
    {
        _mailbox.Enqueue(new StageMessage.ReplyCallbackMessage(callback, errorCode, packet) { Stage = this });
        ScheduleExecution();
    }

    internal void PostClientRoute(BaseActor actor, string accountId, string msgId, ushort msgSeq, long sid, IPayload payload)
    {
        var message = _clientRouteMessagePool.Get();
        message.Update(actor, accountId, msgId, msgSeq, sid, payload);
        message.Stage = this;
        _mailbox.Enqueue(message);
        ScheduleExecution();
    }

    internal void PostClientRoute(string accountId, string msgId, ushort msgSeq, long sid, IPayload payload)
    {
        if (_actors.TryGetValue(accountId, out var actor))
        {
            var message = _clientRouteMessagePool.Get();
            message.Update(actor, accountId, msgId, msgSeq, sid, payload);
            message.Stage = this;
            _mailbox.Enqueue(message);
            ScheduleExecution();
        }
        else
        {
            _logger.LogWarning("Actor {AccountId} not found for client message {MsgId}", accountId, msgId);
            payload.Dispose();
        }
    }

    internal void PostJoinActor(StageMessage.JoinActorMessage message)
    {
        message.Stage = this;
        _mailbox.Enqueue(message);
        ScheduleExecution();
    }

    internal void PostDisconnect(string accountId)
    {
        _mailbox.Enqueue(new StageMessage.DisconnectMessage(accountId) { Stage = this });
        ScheduleExecution();
    }

    /// <summary>
    /// 비동기 Continuation을 메일박스에 게시하여 순차성을 보장합니다.
    /// </summary>
    internal void PostContinuation(SendOrPostCallback callback, object? state)
    {
        var msg = _continuationMessagePool.Get();
        msg.Update(callback, state);
        msg.Stage = this;
        _mailbox.Enqueue(msg);
        ScheduleExecution();
    }

    internal static IDisposable SetCurrentContext(BaseStage stage)
    {
        _currentStage.Value = stage;
        return new ContextScope();
    }

    private sealed class ContextScope : IDisposable
    {
        public void Dispose() => _currentStage.Value = null;
    }

    #endregion

    #region Message Handlers

    internal Task DispatchRoutePacketAsync(RoutePacket packet)
    {
        StageSender.SetCurrentHeader(packet.Header);
        try
        {
            var msgId = packet.MsgId;
            var accountIdString = packet.AccountId.ToString();
            Task task;
            if (IsSystemMessage(msgId)) task = HandleSystemMessageAsync(msgId, packet);
            else if (packet.AccountId != 0 && _actors.TryGetValue(accountIdString, out var baseActor))
            {
                var contentPacket = CreateContentPacket(packet);
                task = Stage.OnDispatch(baseActor.Actor, contentPacket);
            }
            else
            {
                var contentPacket = CreateContentPacket(packet);
                task = Stage.OnDispatch(contentPacket);
            }

            if (!task.IsCompleted)
            {
                return task.ContinueWith(t => {
                    StageSender.ClearCurrentHeader();
                    DisposePendingReplies();
                    if (t.IsFaulted) throw t.Exception!;
                }, TaskScheduler.Default);
            }
            StageSender.ClearCurrentHeader();
            return task;
        }
        catch { StageSender.ClearCurrentHeader(); throw; }
    }

    private static bool IsSystemMessage(string msgId) =>
        msgId.StartsWith("PlayHouse.Runtime.Proto.") ||
        msgId == nameof(CreateStageReq) || msgId == nameof(GetOrCreateStageReq) ||
        msgId == nameof(DestroyStageReq) || msgId == nameof(DisconnectNoticeMsg) || msgId == nameof(ReconnectMsg);

    private async Task HandleSystemMessageAsync(string msgId, RoutePacket packet)
    {
        if (_cmdHandler != null) await _cmdHandler.HandleAsync(msgId, packet, this);
    }

    internal async Task HandleDestroyAsync()
    {
        foreach (var baseActor in _actors.Values.ToList()) await baseActor.Actor.OnDestroy();
        _actors.Clear();
        try { await Stage.OnDestroy(); }
        catch (Exception ex) { _logger?.LogError(ex, "Error destroying Stage {StageId}", StageId); }
    }

    private static IPacket CreateContentPacket(RoutePacket packet) => CPacket.Of(packet.MsgId, packet.Payload);

    internal Task ProcessClientRouteAsync(StageMessage.ClientRouteMessage message)
    {
        var baseActor = message.Actor;
        if (baseActor == null) _actors.TryGetValue(message.AccountId, out baseActor);

        if (baseActor != null)
        {
            var header = new RouteHeader { MsgSeq = message.MsgSeq, ServiceId = 1, MsgId = message.MsgId, From = "client", StageId = StageId, AccountId = 0, Sid = message.Sid };
            StageSender.SetCurrentHeader(header);
            try
            {
                var payload = message.TakePayload();
                if (payload != null)
                {
                    using var packet = CPacket.Of(message.MsgId, payload);
                    var task = Stage.OnDispatch(baseActor.Actor, packet);
                    if (!task.IsCompleted)
                    {
                        return task.ContinueWith(t => {
                            StageSender.ClearCurrentHeader();
                            DisposePendingReplies();
                            if (t.IsFaulted) throw t.Exception!;
                        }, TaskScheduler.Default);
                    }
                    StageSender.ClearCurrentHeader();
                    return task;
                }
            }
            catch { StageSender.ClearCurrentHeader(); throw; }
        }
        return Task.CompletedTask;
    }

    internal async Task ProcessJoinActorAsync(StageMessage.JoinActorMessage message)
    {
        var actor = message.Actor;
        var accountId = actor.AccountId;
        ushort errorCode = (ushort)ErrorCode.Success;
        try
        {
            if (_actors.TryGetValue(accountId, out var existingActor))
            {
                await actor.Actor.OnDestroy();
                existingActor.ActorSender.Update(actor.ActorSender.SessionNid, actor.ActorSender.Sid, actor.ActorSender.ApiNid);
                await Stage.OnConnectionChanged(existingActor.Actor, true);
            }
            else
            {
                var joinResult = await Stage.OnJoinStage(actor.Actor);
                if (!joinResult)
                {
                    errorCode = (ushort)ErrorCode.JoinStageFailed;
                    await actor.Actor.OnDestroy();
                }
                else
                {
                    AddActor(actor);
                    await Stage.OnPostJoinStage(actor.Actor);
                }
            }
        }
        catch (Exception ex) { _logger?.LogError(ex, "Error in ProcessJoinActorAsync"); errorCode = (ushort)ErrorCode.InternalError; }
        finally { message.Session.SendResponse(message.AuthReplyMsgId, message.MsgSeq, StageId, errorCode, ReadOnlySpan<byte>.Empty); }
    }

    internal async Task ProcessDisconnectAsync(string accountId)
    {
        if (_actors.TryGetValue(accountId, out var baseActor)) await Stage.OnConnectionChanged(baseActor.Actor, false);
    }

    #endregion

    #region Actor Management
    public void AddActor(BaseActor baseActor) => _actors[baseActor.AccountId] = baseActor;
    public bool RemoveActor(string accountId) => _actors.Remove(accountId);
    public BaseActor? GetActor(string accountId) => _actors.GetValueOrDefault(accountId);
    public int ActorCount => _actors.Count;
    public async void LeaveStage(string accountId, string sessionNid, long sid)
    {
        if (_actors.TryGetValue(accountId, out var baseActor))
        {
            _actors.Remove(accountId);
            await baseActor.Actor.OnDestroy();
        }
    }
    #endregion

    #region Reply / Helpers
    public void Reply(ushort errorCode) => StageSender.Reply(errorCode);
    public void Reply(IPacket packet) => StageSender.Reply(packet);

    public async Task<(bool success, IPacket? reply)> CreateStage(string stageType, IPacket packet)
    {
        StageSender.SetStageType(stageType);
        var (result, replyPacket) = await Stage.OnCreate(packet);
        if (result) IsCreated = true;
        return (result, replyPacket);
    }

    public async Task<(bool success, ushort errorCode, BaseActor? actor)> JoinActor(string sessionNid, long sid, string apiNid, IPacket authPacket, PlayProducer producer)
    {
        var actorSender = new XActorSender(sessionNid, sid, apiNid, this);
        IActor actor;
        try { actor = producer.GetActor(StageType, actorSender); }
        catch { return (false, (ushort)ErrorCode.InvalidStageType, null); }
        await actor.OnCreate();
        if (!await actor.OnAuthenticate(authPacket)) { await actor.OnDestroy(); return (false, (ushort)ErrorCode.AuthenticationFailed, null); }
        if (string.IsNullOrEmpty(actorSender.AccountId)) { await actor.OnDestroy(); return (false, (ushort)ErrorCode.InvalidAccountId, null); }
        await actor.OnPostAuthenticate();
        return (true, (ushort)ErrorCode.Success, new BaseActor(actor, actorSender));
    }

    public async Task OnPostCreate() => await Stage.OnPostCreate();
    public void MarkAsCreated() => IsCreated = true;
    internal void PostDestroy()
    {
        _mailbox.Enqueue(new StageMessage.DestroyMessage() { Stage = this });
        ScheduleExecution();
    }
    #endregion
}

/// <summary>
/// Stage 메시지 추상 클래스
/// </summary>
internal abstract class StageMessage : IDisposable
{
    public BaseStage? Stage { get; internal set; }
    public virtual void Dispose() { }
    public abstract Task ExecuteAsync();

    public sealed class RouteMessage(RoutePacket packet) : StageMessage
    {
        public override void Dispose() => packet.Dispose();
        public override Task ExecuteAsync() => Stage!.DispatchRoutePacketAsync(packet);
    }

    public sealed class TimerMessage(long timerId, TimerCallbackDelegate callback) : StageMessage
    {
        public long TimerId => timerId;
        public override Task ExecuteAsync() => callback.Invoke();
    }

    public sealed class AsyncMessage(AsyncBlockPacket asyncPacket) : StageMessage
    {
        public override Task ExecuteAsync() => asyncPacket.PostCallback.Invoke(asyncPacket.Result);
    }

    public sealed class ReplyCallbackMessage(ReplyCallback callback, ushort errorCode, IPacket? packet) : StageMessage
    {
        public override Task ExecuteAsync() { callback(errorCode, packet); return Task.CompletedTask; }
    }

    public sealed class DestroyMessage : StageMessage
    {
        public override Task ExecuteAsync() => Stage!.HandleDestroyAsync();
    }

    public sealed class ClientRouteMessage : StageMessage
    {
        public BaseActor? Actor { get; private set; }
        public string AccountId { get; private set; } = "";
        public string MsgId { get; private set; } = "";
        public ushort MsgSeq { get; private set; }
        public long Sid { get; private set; }
        public IPayload? Payload { get; private set; }

        public ClientRouteMessage() { }
        internal void Update(BaseActor? actor, string accountId, string msgId, ushort msgSeq, long sid, IPayload payload)
        {
            Actor = actor; AccountId = accountId; MsgId = msgId; MsgSeq = msgSeq; Sid = sid; Payload = payload;
        }
        public IPayload? TakePayload() { var p = Payload; Payload = null; return p; }
        public override void Dispose() { Payload?.Dispose(); Payload = null; Actor = null; Stage = null; BaseStage._clientRouteMessagePool.Return(this); }
        public override Task ExecuteAsync() => Stage!.ProcessClientRouteAsync(this);
    }

    public sealed class JoinActorMessage(BaseActor actor, ITransportSession session, ushort msgSeq, string authReplyMsgId, IPayload payload) : StageMessage
    {
        public BaseActor Actor => actor;
        public ITransportSession Session => session;
        public ushort MsgSeq => msgSeq;
        public string AuthReplyMsgId => authReplyMsgId;
        public override void Dispose() => payload.Dispose();
        public override Task ExecuteAsync() => Stage!.ProcessJoinActorAsync(this);
    }

    public sealed class DisconnectMessage(string accountId) : StageMessage
    {
        public override Task ExecuteAsync() => Stage!.ProcessDisconnectAsync(accountId);
    }

    /// <summary>
    /// 비동기 Continuation을 래핑하는 메시지
    /// </summary>
    public sealed class ContinuationMessage : StageMessage
    {
        private SendOrPostCallback? _callback;
        private object? _state;

        public ContinuationMessage() { }

        internal void Update(SendOrPostCallback callback, object? state)
        {
            _callback = callback;
            _state = state;
        }

        public override Task ExecuteAsync()
        {
            _callback?.Invoke(_state);
            return Task.CompletedTask;
        }

        public override void Dispose()
        {
            _callback = null;
            _state = null;
            Stage = null;
            BaseStage._continuationMessagePool.Return(this);
        }
    }
}