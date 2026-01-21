#nullable enable

using Microsoft.Extensions.ObjectPool;
using PlayHouse.Abstractions;
using PlayHouse.Core.Shared;
using PlayHouse.Runtime.ClientTransport;
using PlayHouse.Runtime.ServerMesh.Message;

// Alias to avoid conflict with System.Threading.TimerCallback
using TimerCallbackDelegate = PlayHouse.Abstractions.Play.TimerCallback;

namespace PlayHouse.Core.Play.Base;

/// <summary>
/// Stage 메시지 추상 클래스
/// </summary>
internal abstract class StageMessage : IDisposable
{
    // Pool for ClientRouteMessage to avoid heap allocations
    internal static readonly ObjectPool<ClientRouteMessage> ClientRouteMessagePool =
        new DefaultObjectPool<ClientRouteMessage>(
            new DefaultPooledObjectPolicy<ClientRouteMessage>());

    // Pool for ContinuationMessage to avoid heap allocations
    internal static readonly ObjectPool<ContinuationMessage> ContinuationMessagePool =
        new DefaultObjectPool<ContinuationMessage>(
            new DefaultPooledObjectPolicy<ContinuationMessage>());

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
        public override void Dispose() { Payload?.Dispose(); Payload = null; Actor = null; Stage = null; ClientRouteMessagePool.Return(this); }
        public override Task ExecuteAsync() => Stage!.ProcessClientRouteAsync(this);
    }

    public sealed class JoinActorMessage(BaseActor actor, ITransportSession session, ushort msgSeq, string authReplyMsgId, IPayload payload) : StageMessage
    {
        public BaseActor Actor => actor;
        public ITransportSession Session => session;
        public ushort MsgSeq => msgSeq;
        public string AuthReplyMsgId => authReplyMsgId;
        public IPacket? AuthReplyPacket { get; set; }  // OnAuthenticate에서 반환된 reply
        public override void Dispose() { payload.Dispose(); AuthReplyPacket?.Dispose(); }
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
            ContinuationMessagePool.Return(this);
        }
    }
}
