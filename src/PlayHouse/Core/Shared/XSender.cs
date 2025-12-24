#nullable enable

using PlayHouse.Abstractions;
using PlayHouse.Core.Messaging;
using PlayHouse.Runtime.Shared;
using PlayHouse.Runtime.ServerMesh.Communicator;
using PlayHouse.Runtime.ServerMesh.Message;
using PlayHouse.Runtime.Proto;
using IPayload = PlayHouse.Abstractions.IPayload;

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
    private readonly int _requestTimeoutMs;
    private int _msgSeqCounter;

    /// <summary>
    /// Gets the current RouteHeader for reply routing.
    /// </summary>
    public RouteHeader? CurrentHeader { get; private set; }

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
    /// <param name="serviceId">Service ID of this sender.</param>
    /// <param name="serverId">ServerId of this sender.</param>
    /// <param name="requestTimeoutMs">Request timeout in milliseconds.</param>
    protected XSender(
        IClientCommunicator communicator,
        RequestCache requestCache,
        ushort serviceId,
        string serverId,
        int requestTimeoutMs = 30000)
    {
        _communicator = communicator;
        _requestCache = requestCache;
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
    /// Gets the next message sequence number.
    /// </summary>
    /// <returns>A new sequence number (1-65535).</returns>
    protected ushort NextMsgSeq()
    {
        var seq = Interlocked.Increment(ref _msgSeqCounter);
        // Wrap around at 65535 and skip 0 (0 means no reply expected)
        if (seq > 65535)
        {
            Interlocked.CompareExchange(ref _msgSeqCounter, 1, seq);
            seq = Interlocked.Increment(ref _msgSeqCounter);
        }
        if (seq == 0) seq = Interlocked.Increment(ref _msgSeqCounter);
        return (ushort)seq;
    }

    #region API Communication

    /// <inheritdoc/>
    public void SendToApi(string apiServerId, IPacket packet)
    {
        var header = CreateHeader(packet.MsgId, 0);
        SendInternal(apiServerId, header, packet.Payload);
    }

    /// <inheritdoc/>
    public void RequestToApi(string apiServerId, IPacket packet, ReplyCallback replyCallback)
    {
        var msgSeq = NextMsgSeq();
        var header = CreateHeader(packet.MsgId, msgSeq);

        var replyObject = ReplyObject.CreateCallback(msgSeq, replyCallback);
        RegisterReply(replyObject);

        SendInternal(apiServerId, header, packet.Payload);
    }

    /// <inheritdoc/>
    public async Task<IPacket> RequestToApi(string apiServerId, IPacket packet)
    {
        var msgSeq = NextMsgSeq();
        var header = CreateHeader(packet.MsgId, msgSeq);

        var (replyObject, task) = ReplyObject.CreateAsync(msgSeq);
        RegisterReply(replyObject);

        SendInternal(apiServerId, header, packet.Payload);

        return await task;
    }

    #endregion

    #region Stage Communication

    /// <inheritdoc/>
    public void SendToStage(string playServerId, long stageId, IPacket packet)
    {
        var header = CreateHeader(packet.MsgId, 0, stageId);
        SendInternal(playServerId, header, packet.Payload);
    }

    /// <inheritdoc/>
    public void RequestToStage(string playServerId, long stageId, IPacket packet, ReplyCallback replyCallback)
    {
        var msgSeq = NextMsgSeq();
        var header = CreateHeader(packet.MsgId, msgSeq, stageId);

        var replyObject = ReplyObject.CreateCallback(msgSeq, replyCallback);
        RegisterReply(replyObject);

        SendInternal(playServerId, header, packet.Payload);
    }

    /// <inheritdoc/>
    public async Task<IPacket> RequestToStage(string playServerId, long stageId, IPacket packet)
    {
        var msgSeq = NextMsgSeq();
        var header = CreateHeader(packet.MsgId, msgSeq, stageId);

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

        SendReplyInternal(CurrentHeader.From, replyHeader, ReadOnlyMemory<byte>.Empty);
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

        var replyHeader = new RouteHeader
        {
            MsgSeq = CurrentHeader.MsgSeq,
            ServiceId = ServiceId,
            MsgId = reply.MsgId,
            From = ServerId,
            ErrorCode = 0,
            IsReply = true
        };

        SendReplyInternal(CurrentHeader.From, replyHeader, reply.Payload.Data);
    }

    #endregion

    #region Internal Methods

    private RouteHeader CreateHeader(string msgId, ushort msgSeq, long stageId = 0, long accountId = 0)
    {
        return new RouteHeader
        {
            MsgSeq = msgSeq,
            ServiceId = ServiceId,
            MsgId = msgId,
            From = ServerId,
            StageId = stageId,
            AccountId = accountId
        };
    }

    private void SendInternal(string targetServerId, RouteHeader header, IPayload payload)
    {
        // Create RuntimePayload from Abstractions.IPayload - zero-copy with MemoryPayload
        var runtimePayload = new Runtime.ServerMesh.Message.MemoryPayload(payload.Data);
        var packet = RoutePacket.Of(header, runtimePayload);
        _communicator.Send(targetServerId, packet);
        packet.Dispose();
    }

    private void SendReplyInternal(string targetServerId, RouteHeader header, ReadOnlyMemory<byte> payload)
    {
        // Create RuntimePayload from ReadOnlyMemory - zero-copy with MemoryPayload
        var runtimePayload = new Runtime.ServerMesh.Message.MemoryPayload(payload);
        var packet = RoutePacket.Of(header, runtimePayload);
        _communicator.Send(targetServerId, packet);
        packet.Dispose();
    }

    private void RegisterReply(ReplyObject replyObject)
    {
        var tcs = new TaskCompletionSource<IPacket>();
        _requestCache.Register(replyObject.MsgSeq, tcs, TimeSpan.FromMilliseconds(_requestTimeoutMs));

        // Bridge ReplyObject to RequestCache
        tcs.Task.ContinueWith(t =>
        {
            if (t.IsCompletedSuccessfully)
            {
                replyObject.Complete(t.Result);
            }
            else if (t.IsFaulted)
            {
                replyObject.CompleteWithError(BaseErrorCode.RequestTimeout);
            }
        }, TaskContinuationOptions.ExecuteSynchronously);
    }

    #endregion
}
