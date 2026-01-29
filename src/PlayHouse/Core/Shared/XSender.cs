#nullable enable

using PlayHouse.Abstractions;
using PlayHouse.Core.Messaging;
using PlayHouse.Runtime.Shared;
using PlayHouse.Runtime.ServerMesh.Communicator;
using PlayHouse.Runtime.ServerMesh.Discovery;
using PlayHouse.Runtime.ServerMesh.Message;
using PlayHouse.Runtime.Proto;

namespace PlayHouse.Core.Shared;

/// <summary>
/// Base implementation of ISender providing common functionality.
/// </summary>
/// <remarks>
/// XSender handles the low-level details of message routing and request-reply
/// coordination. Derived classes (XActorSender, XStageSender, ApiSender) add
/// specialized functionality.
/// </remarks>
public abstract class XSender : ISender
{
    private readonly IClientCommunicator _communicator;
    private readonly RequestCache _requestCache;
    private readonly IServerInfoCenter _serverInfoCenter;
    private readonly int _requestTimeoutMs;

    // 전역 MsgSeq 카운터 - 모든 XSender 인스턴스가 공유하여 RequestCache 키 충돌 방지
    private static int _globalMsgSeqCounter;

    /// <summary>
    /// Gets the current RouteHeader for reply routing.
    /// </summary>
    internal RouteHeader? CurrentHeader { get; private set; }

    /// <inheritdoc/>
    public ushort ServiceId { get; }

    /// <summary>
    /// Gets the ServerId of this sender.
    /// </summary>
    protected string ServerId { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="XSender"/> class.
    /// </summary>
    /// <param name="communicator">Communicator for sending messages.</param>
    /// <param name="requestCache">Cache for tracking pending requests.</param>
    /// <param name="serverInfoCenter">Server information center for service discovery.</param>
    /// <param name="serviceId">Service ID of this sender.</param>
    /// <param name="serverId">ServerId of this sender.</param>
    /// <param name="requestTimeoutMs">Request timeout in milliseconds.</param>
    protected XSender(
        IClientCommunicator communicator,
        RequestCache requestCache,
        IServerInfoCenter serverInfoCenter,
        ushort serviceId,
        string serverId,
        int requestTimeoutMs = 30000)
    {
        _communicator = communicator;
        _requestCache = requestCache;
        _serverInfoCenter = serverInfoCenter;
        ServiceId = serviceId;
        ServerId = serverId;
        _requestTimeoutMs = requestTimeoutMs;
    }

    /// <summary>
    /// Sets the current header for reply routing.
    /// </summary>
    /// <param name="header">The incoming request header.</param>
    public void SetCurrentHeader(RouteHeader header)
    {
        CurrentHeader = header;
    }

    /// <summary>
    /// Clears the current header.
    /// </summary>
    public void ClearCurrentHeader()
    {
        CurrentHeader = null;
    }

    /// <summary>
    /// Gets the sender's Stage ID for Stage-to-Stage communication.
    /// Override this in XStageSender to return the actual StageId.
    /// </summary>
    /// <returns>Stage ID for the sender (0 if not a Stage).</returns>
    protected virtual long GetSenderStageId()
    {
        return 0;
    }

    /// <summary>
    /// Gets the delegate for posting callbacks to Stage event loop.
    /// Override this in XStageSender to return the post callback delegate.
    /// </summary>
    /// <returns>Callback delegate (null if not a Stage).</returns>
    protected virtual Action<ReplyCallback, ushort, IPacket?>? GetPostToStageCallback()
    {
        return null;
    }

    /// <summary>
    /// Gets the next message sequence number.
    /// </summary>
    /// <returns>A new sequence number (1-65535).</returns>
    protected ushort NextMsgSeq()
    {
        var seq = Interlocked.Increment(ref _globalMsgSeqCounter);
        // Wrap around at 65535 and skip 0 (0 means no reply expected)
        if (seq > 65535)
        {
            Interlocked.CompareExchange(ref _globalMsgSeqCounter, 1, seq);
            seq = Interlocked.Increment(ref _globalMsgSeqCounter);
        }
        if (seq == 0) seq = Interlocked.Increment(ref _globalMsgSeqCounter);
        return (ushort)seq;
    }

    #region API Communication

    /// <inheritdoc/>
    public void SendToApi(string apiServerId, IPacket packet)
    {
        var stageId = GetSenderStageId();
        var accountId = CurrentHeader?.AccountId ?? 0;
        var sid = CurrentHeader?.Sid ?? 0;

        var header = CreateHeader(packet.MsgId, 0, stageId, accountId, sid);
        SendInternal(apiServerId, header, packet.Payload);
    }

    /// <inheritdoc/>
    public void RequestToApi(string apiServerId, IPacket packet, ReplyCallback replyCallback)
    {
        var msgSeq = NextMsgSeq();
        var stageId = GetSenderStageId();
        var accountId = CurrentHeader?.AccountId ?? 0;
        var sid = CurrentHeader?.Sid ?? 0;

        var header = CreateHeader(packet.MsgId, msgSeq, stageId, accountId, sid);

        var replyObject = ReplyObject.CreateCallback(msgSeq, replyCallback, GetPostToStageCallback());
        RegisterReply(replyObject);

        SendInternal(apiServerId, header, packet.Payload);
    }

    /// <inheritdoc/>
    public async Task<IPacket> RequestToApi(string apiServerId, IPacket packet)
    {
        var msgSeq = NextMsgSeq();
        var stageId = GetSenderStageId();
        var accountId = CurrentHeader?.AccountId ?? 0;
        var sid = CurrentHeader?.Sid ?? 0;

        var header = CreateHeader(packet.MsgId, msgSeq, stageId, accountId, sid);

        var (replyObject, task) = ReplyObject.CreateAsync(msgSeq);
        RegisterReply(replyObject);

        SendInternal(apiServerId, header, packet.Payload);

        return await task;
    }

    #endregion

    #region Service Communication

    /// <inheritdoc/>
    public void SendToService(ushort serviceId, IPacket packet)
    {
        SendToService(serviceId, packet, ServerSelectionPolicy.RoundRobin);
    }

    /// <inheritdoc/>
    public void SendToService(ushort serviceId, IPacket packet, ServerSelectionPolicy policy)
    {
        var server = _serverInfoCenter.GetServerByService(serviceId, policy)
            ?? throw new InvalidOperationException($"No available server for service {serviceId}");

        SendToApi(server.ServerId, packet);
    }

    /// <inheritdoc/>
    public void RequestToService(ushort serviceId, IPacket packet, ReplyCallback replyCallback)
    {
        RequestToService(serviceId, packet, replyCallback, ServerSelectionPolicy.RoundRobin);
    }

    /// <inheritdoc/>
    public void RequestToService(ushort serviceId, IPacket packet, ReplyCallback replyCallback, ServerSelectionPolicy policy)
    {
        var server = _serverInfoCenter.GetServerByService(serviceId, policy)
            ?? throw new InvalidOperationException($"No available server for service {serviceId}");

        RequestToApi(server.ServerId, packet, replyCallback);
    }

    /// <inheritdoc/>
    public Task<IPacket> RequestToService(ushort serviceId, IPacket packet)
    {
        return RequestToService(serviceId, packet, ServerSelectionPolicy.RoundRobin);
    }

    /// <inheritdoc/>
    public async Task<IPacket> RequestToService(ushort serviceId, IPacket packet, ServerSelectionPolicy policy)
    {
        var server = _serverInfoCenter.GetServerByService(serviceId, policy)
            ?? throw new InvalidOperationException($"No available server for service {serviceId}");

        return await RequestToApi(server.ServerId, packet);
    }

    #endregion

    #region Stage Communication

    /// <inheritdoc/>
    public void SendToStage(string playServerId, long stageId, IPacket packet)
    {
        // Determine AccountId for reply routing:
        // - If CurrentHeader exists with AccountId (Actor context), preserve it
        // - Otherwise, use sender's StageId (Stage-to-Stage direct communication)
        var accountId = (CurrentHeader != null && CurrentHeader.AccountId != 0)
            ? CurrentHeader.AccountId
            : GetSenderStageId();
        // Stage-to-Stage communication: Sid should be 0 (not a client session)
        var header = CreateHeader(packet.MsgId, 0, stageId, accountId, sid: 0);
        SendInternal(playServerId, header, packet.Payload);
    }

    /// <inheritdoc/>
    public void RequestToStage(string playServerId, long stageId, IPacket packet, ReplyCallback replyCallback)
    {
        var msgSeq = NextMsgSeq();
        // Determine AccountId for reply routing:
        // - If CurrentHeader exists with AccountId (Actor context), preserve it
        // - Otherwise, use sender's StageId (Stage-to-Stage direct communication)
        var accountId = (CurrentHeader != null && CurrentHeader.AccountId != 0)
            ? CurrentHeader.AccountId
            : GetSenderStageId();
        // Stage-to-Stage communication: Sid should be 0 (not a client session)
        var header = CreateHeader(packet.MsgId, msgSeq, stageId, accountId, sid: 0);

        var replyObject = ReplyObject.CreateCallback(msgSeq, replyCallback, GetPostToStageCallback());
        RegisterReply(replyObject);

        SendInternal(playServerId, header, packet.Payload);
    }

    /// <inheritdoc/>
    public async Task<IPacket> RequestToStage(string playServerId, long stageId, IPacket packet)
    {
        var msgSeq = NextMsgSeq();
        // Determine AccountId for reply routing:
        // - If CurrentHeader exists with AccountId (Actor context), preserve it
        // - Otherwise, use sender's StageId (Stage-to-Stage direct communication)
        var accountId = (CurrentHeader != null && CurrentHeader.AccountId != 0)
            ? CurrentHeader.AccountId
            : GetSenderStageId();

        // Stage-to-Stage communication: Sid should be 0 (not a client session)
        var header = CreateHeader(packet.MsgId, msgSeq, stageId, accountId, sid: 0);

        var (replyObject, task) = ReplyObject.CreateAsync(msgSeq);
        RegisterReply(replyObject);

        SendInternal(playServerId, header, packet.Payload);

        return await task;
    }

    #endregion

    #region Reply

    /// <inheritdoc/>
    public void Reply(ushort errorCode)
    {
        if (CurrentHeader == null)
        {
            throw new InvalidOperationException("Cannot reply without a current request context");
        }

        if (CurrentHeader.MsgSeq == 0)
        {
            return; // Not a request, no reply needed
        }

        var replyHeader = new RouteHeader
        {
            MsgSeq = CurrentHeader.MsgSeq,
            ServiceId = ServiceId,
            MsgId = CurrentHeader.MsgId,
            From = ServerId,
            ErrorCode = errorCode,
            IsReply = true
        };

        SendReplyInternal(CurrentHeader.From, replyHeader, EmptyPayload.Instance);
    }

    /// <inheritdoc/>
    public void Reply(IPacket reply)
    {
        if (CurrentHeader == null)
        {
            throw new InvalidOperationException("Cannot reply without a current request context");
        }

        if (CurrentHeader.MsgSeq == 0)
        {
            return; // Not a request, no reply needed
        }

        // For Stage-to-Stage communication: AccountId in request contains the sender's StageId
        // Reply should be routed back to that Stage
        var replyStageId = CurrentHeader.AccountId;
        var replyAccountId = CurrentHeader.AccountId;

        var replyHeader = new RouteHeader
        {
            MsgSeq = CurrentHeader.MsgSeq,
            ServiceId = ServiceId,
            MsgId = reply.MsgId,
            From = ServerId,
            ErrorCode = 0,
            IsReply = true,
            StageId = replyStageId,
            AccountId = replyAccountId
        };

        SendReplyInternal(CurrentHeader.From, replyHeader, reply.Payload);
    }

    #endregion

    #region Internal Methods

    private RouteHeader CreateHeader(string msgId, ushort msgSeq, long stageId = 0, long accountId = 0, long sid = 0)
    {
        return new RouteHeader
        {
            MsgSeq = msgSeq,
            ServiceId = ServiceId,
            MsgId = msgId,
            From = ServerId,
            StageId = stageId,
            AccountId = accountId,
            Sid = sid
        };
    }

    private void SendInternal(string targetServerId, RouteHeader header, IPayload payload)
    {
        // Note: ProtoPayload now serializes eagerly in constructor (no lazy serialization).
        // Packet ownership is transferred to the queue.
        // ZmqPlaySocket.Send() will dispose the packet after sending.
        var packet = RoutePacket.Of(header, payload);
        _communicator.Send(targetServerId, packet);
    }

    private void SendReplyInternal(string targetServerId, RouteHeader header, IPayload payload)
    {
        // Note: ProtoPayload now serializes eagerly in constructor (no lazy serialization).
        // Packet ownership is transferred to the queue.
        // ZmqPlaySocket.Send() will dispose the packet after sending.
        var packet = RoutePacket.Of(header, payload);
        _communicator.Send(targetServerId, packet);
    }

    private void RegisterReply(ReplyObject replyObject)
    {
        var tcs = new TaskCompletionSource<IPacket>();
        _requestCache.Register(replyObject.MsgSeq, tcs, TimeSpan.FromMilliseconds(_requestTimeoutMs));

        Console.WriteLine($"[XSender] Registered Reply: MsgSeq={replyObject.MsgSeq}");

        // Bridge ReplyObject to RequestCache
        tcs.Task.ContinueWith(t =>
        {
            if (t.IsCompletedSuccessfully)
            {
                Console.WriteLine($"[XSender] Reply Received: MsgSeq={replyObject.MsgSeq}, Success");
                replyObject.Complete(t.Result);
            }
            else
            {
                Console.WriteLine($"[XSender] Reply Error: MsgSeq={replyObject.MsgSeq}, Exception={t.Exception?.Message}");
                replyObject.CompleteWithError((ushort)ErrorCode.RequestTimeout);
            }
        }, TaskContinuationOptions.ExecuteSynchronously);
    }

    #endregion
}
