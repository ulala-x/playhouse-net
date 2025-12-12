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
    private readonly HashSet<long> _timerIds = new();
    private long _timerIdCounter;

    /// <inheritdoc/>
    public long StageId { get; }

    /// <inheritdoc/>
    public string StageType { get; private set; } = "";

    /// <summary>
    /// Gets the NID of this stage sender.
    /// </summary>
    public new string Nid => base.Nid;

    /// <summary>
    /// Initializes a new instance of the <see cref="XStageSender"/> class.
    /// </summary>
    public XStageSender(
        IClientCommunicator communicator,
        RequestCache requestCache,
        ushort serviceId,
        string nid,
        long stageId,
        IPlayDispatcher dispatcher,
        int requestTimeoutMs = 30000)
        : base(communicator, requestCache, serviceId, nid, requestTimeoutMs)
    {
        StageId = stageId;
        _dispatcher = dispatcher;
        _communicator = communicator;
    }

    /// <summary>
    /// Sets the stage type identifier.
    /// </summary>
    /// <param name="stageType">Stage type.</param>
    internal void SetStageType(string stageType)
    {
        StageType = stageType;
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

        _dispatcher.PostTimer(timerPacket);
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

        _dispatcher.PostTimer(timerPacket);
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

        _dispatcher.PostTimer(timerPacket);
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
        _dispatcher.PostDestroy(StageId);
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
                    _dispatcher.PostAsyncBlock(asyncPacket);
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
    public void SendToClient(string sessionNid, long sid, IPacket packet)
    {
        var header = new RouteHeader
        {
            ServiceId = ServiceId,
            MsgId = packet.MsgId,
            From = Nid,
            StageId = StageId,
            Sid = sid
        };

        var routePacket = RuntimeRoutePacket.Of(header, packet.Payload.Data.ToArray());
        _communicator.Send(sessionNid, routePacket);
        routePacket.Dispose();
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
