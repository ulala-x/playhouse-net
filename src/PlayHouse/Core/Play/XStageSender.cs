#nullable enable

using PlayHouse.Abstractions;
using PlayHouse.Abstractions.Play;
using PlayHouse.Core.Messaging;
using PlayHouse.Core.Shared;
using PlayHouse.Runtime.ServerMesh.Communicator;
using PlayHouse.Runtime.ServerMesh.Message;
using PlayHouse.Runtime.Proto;

// Alias to avoid conflict with System.Threading.TimerCallback
using TimerCallbackDelegate = PlayHouse.Abstractions.Play.TimerCallback;

namespace PlayHouse.Core.Play;

/// <summary>
/// Internal implementation of IStageSender.
/// </summary>
/// <remarks>
/// XStageSender extends XSender with Stage-specific functionality:
/// - Timer management (repeat, count, cancel)
/// - AsyncBlock for external I/O operations
/// - CloseStage for stage lifecycle management
/// - SendToClient with StageId context
/// </remarks>
internal sealed class XStageSender : XSender, IStageSender
{
    private readonly IPlayDispatcher _dispatcher;
    private readonly IClientCommunicator _communicator;
    private readonly IClientReplyHandler? _clientReplyHandler;
    private readonly HashSet<long> _timerIds = new();
    private long _timerIdCounter;
    private object? _baseStage;  // BaseStage instance (stored as object to avoid circular dependency)

    /// <inheritdoc/>
    public long StageId { get; }

    /// <inheritdoc/>
    public string StageType { get; private set; } = "";

    /// <summary>
    /// Gets the ServerId of this stage sender.
    /// </summary>
    public new string ServerId => base.ServerId;

    /// <summary>
    /// Initializes a new instance of the <see cref="XStageSender"/> class.
    /// </summary>
    public XStageSender(
        IClientCommunicator communicator,
        RequestCache requestCache,
        ushort serviceId,
        string serverId,
        long stageId,
        IPlayDispatcher dispatcher,
        IClientReplyHandler? clientReplyHandler = null,
        int requestTimeoutMs = 30000)
        : base(communicator, requestCache, serviceId, serverId, requestTimeoutMs)
    {
        StageId = stageId;
        _dispatcher = dispatcher;
        _communicator = communicator;
        _clientReplyHandler = clientReplyHandler;
    }

    /// <summary>
    /// Sets the stage type identifier.
    /// </summary>
    /// <param name="stageType">Stage type.</param>
    internal void SetStageType(string stageType)
    {
        StageType = stageType;
    }

    /// <summary>
    /// Sets the BaseStage instance for callback queueing.
    /// </summary>
    /// <param name="baseStage">BaseStage instance.</param>
    internal void SetBaseStage(object baseStage)
    {
        _baseStage = baseStage;
    }

    /// <summary>
    /// Gets the sender's Stage ID for Stage-to-Stage communication.
    /// </summary>
    /// <returns>This Stage's ID.</returns>
    protected override long GetSenderStageId()
    {
        return StageId;
    }

    /// <summary>
    /// Gets the Stage context for callback queueing.
    /// </summary>
    /// <returns>BaseStage instance.</returns>
    protected override object? GetStageContext()
    {
        return _baseStage;
    }

    #region Timer Management

    /// <inheritdoc/>
    public long AddRepeatTimer(TimeSpan initialDelay, TimeSpan period, TimerCallbackDelegate callback)
    {
        var timerId = GenerateTimerId();
        var timerPacket = CreateTimerPacket(
            TimerMsg.Types.Type.Repeat,
            timerId,
            initialDelay,
            period,
            0,
            callback);

        _dispatcher.OnPost(new TimerMessage(timerPacket));
        _timerIds.Add(timerId);
        return timerId;
    }

    /// <inheritdoc/>
    public long AddCountTimer(TimeSpan initialDelay, TimeSpan period, int count, TimerCallbackDelegate callback)
    {
        var timerId = GenerateTimerId();
        var timerPacket = CreateTimerPacket(
            TimerMsg.Types.Type.Count,
            timerId,
            initialDelay,
            period,
            count,
            callback);

        _dispatcher.OnPost(new TimerMessage(timerPacket));
        _timerIds.Add(timerId);
        return timerId;
    }

    /// <inheritdoc/>
    public void CancelTimer(long timerId)
    {
        if (!_timerIds.Contains(timerId)) return;

        var timerPacket = CreateTimerPacket(
            TimerMsg.Types.Type.Cancel,
            timerId,
            TimeSpan.Zero,
            TimeSpan.Zero,
            0,
            () => Task.CompletedTask);

        _dispatcher.OnPost(new TimerMessage(timerPacket));
        _timerIds.Remove(timerId);
    }

    /// <inheritdoc/>
    public bool HasTimer(long timerId)
    {
        return _timerIds.Contains(timerId);
    }

    private long GenerateTimerId()
    {
        return Interlocked.Increment(ref _timerIdCounter);
    }

    private TimerPacket CreateTimerPacket(
        TimerMsg.Types.Type type,
        long timerId,
        TimeSpan initialDelay,
        TimeSpan period,
        int count,
        TimerCallbackDelegate callback)
    {
        return new TimerPacket(
            StageId,
            timerId,
            type,
            (long)initialDelay.TotalMilliseconds,
            (long)period.TotalMilliseconds,
            count,
            callback);
    }

    #endregion

    #region Stage Management

    /// <inheritdoc/>
    public void CloseStage()
    {
        // Cancel all active timers
        foreach (var timerId in _timerIds.ToList())
        {
            CancelTimer(timerId);
        }
        _timerIds.Clear();

        // Post stage destroy request
        _dispatcher.OnPost(new DestroyMessage(StageId));
    }

    #endregion

    #region AsyncBlock

    /// <inheritdoc/>
    public void AsyncBlock(AsyncPreCallback preCallback, AsyncPostCallback? postCallback = null)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                // Execute preCallback on ThreadPool (can block)
                var result = await preCallback.Invoke();

                if (postCallback != null)
                {
                    // Post result back to Stage event loop
                    var asyncPacket = new AsyncBlockPacket(StageId, postCallback, result);
                    _dispatcher.OnPost(new AsyncMessage(asyncPacket));
                }
            }
            catch (Exception ex)
            {
                // Log error but don't crash the ThreadPool thread
                Console.Error.WriteLine($"AsyncBlock error in Stage {StageId}: {ex.Message}");
            }
        });
    }

    #endregion

    #region Client Communication

    /// <inheritdoc/>
    public void SendToClient(string sessionServerId, long sid, IPacket packet)
    {
        // For directly connected clients, use transport handler
        if (sid > 0 && _clientReplyHandler != null)
        {
            // Client push message - msgSeq = 0 (not a response to a request)
            _ = _clientReplyHandler.SendClientReplyAsync(
                sid,
                packet.MsgId,
                0,  // msgSeq = 0 for push messages
                StageId,
                0,  // errorCode = 0
                packet.Payload);
            return;
        }

        // Server-to-server communication (e.g., through API server)
        var header = new RouteHeader
        {
            ServiceId = ServiceId,
            MsgId = packet.MsgId,
            From = ServerId,
            StageId = StageId,
            Sid = sid
        };

        // Note: ProtoPayload now serializes eagerly in constructor (no lazy serialization).
        // Zero-copy: RoutePacket references the payload without copying
        // RoutePacket.Of(header, IPayload) sets ownsPayload=false, original packet retains ownership
        var routePacket = RoutePacket.Of(header, packet.Payload);
        _communicator.Send(sessionServerId, routePacket);
    }

    #endregion

    #region Client Reply Override

    /// <summary>
    /// Overrides Reply to detect client requests and route through transport.
    /// </summary>
    public new void Reply(ushort errorCode)
    {
        if (CurrentHeader?.Sid > 0 && _clientReplyHandler != null)
        {
            // Client request - route through transport
            _ = _clientReplyHandler.SendClientReplyAsync(
                CurrentHeader.Sid,
                CurrentHeader.MsgId,
                (ushort)CurrentHeader.MsgSeq,
                StageId,
                errorCode,
                Abstractions.EmptyPayload.Instance);
        }
        else
        {
            // Server-to-server request - use base implementation
            base.Reply(errorCode);
        }
    }

    /// <summary>
    /// Overrides Reply to detect client requests and route through transport.
    /// </summary>
    public new void Reply(IPacket reply)
    {
        if (CurrentHeader?.Sid > 0 && _clientReplyHandler != null)
        {
            // Capture header values before async execution (CurrentHeader may be cleared during await)
            var sid = CurrentHeader.Sid;
            var msgSeq = (ushort)CurrentHeader.MsgSeq;

            // Client request - route through transport with fire-and-forget disposal
            _ = ReplyAndDisposeAsync(reply, sid, msgSeq);
        }
        else
        {
            // Server-to-server request - use base implementation
            base.Reply(reply);
        }
    }

    /// <summary>
    /// Sends client reply and ensures packet disposal after transmission.
    /// </summary>
    private async Task ReplyAndDisposeAsync(IPacket reply, long sid, ushort msgSeq)
    {
        try
        {
            await _clientReplyHandler!.SendClientReplyAsync(
                sid,
                reply.MsgId,
                msgSeq,
                StageId,
                0,
                reply.Payload);
        }
        finally
        {
            reply.Dispose();  // ArrayPool.Return is called here
        }
    }

    #endregion

    /// <summary>
    /// Called when a timer is removed externally (e.g., timer expired).
    /// </summary>
    internal void OnTimerRemoved(long timerId)
    {
        _timerIds.Remove(timerId);
    }
}

/// <summary>
/// Packet for timer operations.
/// </summary>
internal sealed class TimerPacket
{
    public long StageId { get; }
    public long TimerId { get; }
    public TimerMsg.Types.Type Type { get; }
    public long InitialDelayMs { get; }
    public long PeriodMs { get; }
    public int Count { get; }
    public TimerCallbackDelegate Callback { get; }

    public TimerPacket(
        long stageId,
        long timerId,
        TimerMsg.Types.Type type,
        long initialDelayMs,
        long periodMs,
        int count,
        TimerCallbackDelegate callback)
    {
        StageId = stageId;
        TimerId = timerId;
        Type = type;
        InitialDelayMs = initialDelayMs;
        PeriodMs = periodMs;
        Count = count;
        Callback = callback;
    }
}

/// <summary>
/// Packet for AsyncBlock post-callback.
/// </summary>
internal sealed class AsyncBlockPacket
{
    public long StageId { get; }
    public AsyncPostCallback PostCallback { get; }
    public object? Result { get; }

    public AsyncBlockPacket(long stageId, AsyncPostCallback postCallback, object? result)
    {
        StageId = stageId;
        PostCallback = postCallback;
        Result = result;
    }
}
