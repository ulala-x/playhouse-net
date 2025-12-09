#nullable enable

using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using PlayHouse.Abstractions;
using PlayHouse.Core.Messaging;
using PlayHouse.Core.Session;
using PlayHouse.Core.Timer;

namespace PlayHouse.Core.Stage;

/// <summary>
/// Implementation of IStageSender that provides stage-level message sending and timer management.
/// </summary>
internal sealed class StageSenderImpl : IStageSender
{
    private readonly int _stageId;
    private readonly string _stageType;
    private readonly PacketDispatcher _dispatcher;
    private readonly TimerManager _timerManager;
    private readonly SessionManager _sessionManager;
    private readonly Func<ActorPool> _getActorPool;
    private readonly ILogger _logger;
    private RequestContext? _requestContext;
    private bool _stageClosed;

    /// <summary>
    /// Initializes a new instance of the <see cref="StageSenderImpl"/> class.
    /// </summary>
    /// <param name="stageId">The stage identifier.</param>
    /// <param name="stageType">The stage type name.</param>
    /// <param name="dispatcher">The packet dispatcher.</param>
    /// <param name="timerManager">The timer manager.</param>
    /// <param name="sessionManager">The session manager.</param>
    /// <param name="getActorPool">Function to get the current actor pool.</param>
    /// <param name="logger">The logger instance.</param>
    public StageSenderImpl(
        int stageId,
        string stageType,
        PacketDispatcher dispatcher,
        TimerManager timerManager,
        SessionManager sessionManager,
        Func<ActorPool> getActorPool,
        ILogger logger)
    {
        _stageId = stageId;
        _stageType = stageType;
        _dispatcher = dispatcher;
        _timerManager = timerManager;
        _sessionManager = sessionManager;
        _getActorPool = getActorPool;
        _logger = logger;
    }

    /// <inheritdoc/>
    public int StageId => _stageId;

    /// <inheritdoc/>
    public string StageType => _stageType;

    /// <summary>
    /// Sets the request context for the current operation.
    /// </summary>
    /// <param name="context">The request context containing session and message information.</param>
    internal void SetRequestContext(RequestContext? context)
    {
        _requestContext = context;
    }

    /// <inheritdoc/>
    public void Reply(ushort errorCode)
    {
        if (_requestContext == null)
        {
            _logger.LogWarning("Reply called without request context in stage {StageId}", _stageId);
            return;
        }

        if (_requestContext.MsgSeq == 0)
        {
            _logger.LogWarning("Reply called for non-request message (MsgSeq=0) in stage {StageId}", _stageId);
            return;
        }

        // Create reply packet with error code
        var replyPacket = new SimplePacket(
            _requestContext.MsgId,
            _requestContext.MsgSeq,
            _stageId,
            errorCode,
            EmptyPayload.Instance);

        SendToSession(_requestContext.SessionId, replyPacket);
    }

    /// <inheritdoc/>
    public void Reply(IPacket packet)
    {
        if (_requestContext == null)
        {
            _logger.LogWarning("Reply called without request context in stage {StageId}", _stageId);
            return;
        }

        if (_requestContext.MsgSeq == 0)
        {
            _logger.LogWarning("Reply called for non-request message (MsgSeq=0) in stage {StageId}", _stageId);
            return;
        }

        SendToSession(_requestContext.SessionId, packet);
    }

    /// <inheritdoc/>
    public ValueTask SendAsync(IPacket packet)
    {
        if (_requestContext == null)
        {
            _logger.LogWarning("SendAsync called without request context in stage {StageId}", _stageId);
            return ValueTask.CompletedTask;
        }

        SendToSession(_requestContext.SessionId, packet);
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc/>
    public ValueTask SendToStageAsync(int targetStageId, IPacket packet)
    {
        _dispatcher.DispatchToStage(targetStageId, packet);
        _logger.LogTrace("Sent packet from stage {StageId} to stage {TargetStageId}: {MsgId}",
            _stageId, targetStageId, packet.MsgId);
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc/>
    public ValueTask BroadcastAsync(IPacket packet)
    {
        var actorPool = _getActorPool();
        var actors = actorPool.GetConnectedActors();

        foreach (var actorContext in actors)
        {
            var session = _sessionManager.GetSession(actorContext.SessionId);
            if (session != null)
            {
                SendToSession(session.SessionId, packet);
            }
        }

        _logger.LogTrace("Broadcast packet in stage {StageId}: {MsgId} to {Count} actors",
            _stageId, packet.MsgId, actorPool.Count);

        return ValueTask.CompletedTask;
    }

    /// <inheritdoc/>
    public ValueTask BroadcastAsync(IPacket packet, Func<IActor, bool> filter)
    {
        var actorPool = _getActorPool();
        var actors = actorPool.GetConnectedActors();
        var count = 0;

        foreach (var actorContext in actors)
        {
            if (filter(actorContext.UserActor))
            {
                var session = _sessionManager.GetSession(actorContext.SessionId);
                if (session != null)
                {
                    SendToSession(session.SessionId, packet);
                    count++;
                }
            }
        }

        _logger.LogTrace("Broadcast packet in stage {StageId}: {MsgId} to {Count} filtered actors",
            _stageId, packet.MsgId, count);

        return ValueTask.CompletedTask;
    }

    /// <inheritdoc/>
    public long AddRepeatTimer(TimeSpan initialDelay, TimeSpan period, Func<Task> callback)
    {
        if (_stageClosed)
        {
            _logger.LogWarning("Cannot add timer to closed stage {StageId}", _stageId);
            return 0;
        }

        return _timerManager.AddRepeatTimer(_stageId, initialDelay, period, callback);
    }

    /// <inheritdoc/>
    public long AddCountTimer(TimeSpan initialDelay, TimeSpan period, int count, Func<Task> callback)
    {
        if (_stageClosed)
        {
            _logger.LogWarning("Cannot add timer to closed stage {StageId}", _stageId);
            return 0;
        }

        return _timerManager.AddCountTimer(_stageId, initialDelay, period, count, callback);
    }

    /// <inheritdoc/>
    public void CancelTimer(long timerId)
    {
        _timerManager.CancelTimer(timerId);
    }

    /// <inheritdoc/>
    public bool HasTimer(long timerId)
    {
        return _timerManager.HasTimer(timerId);
    }

    /// <inheritdoc/>
    public void CloseStage()
    {
        if (_stageClosed)
        {
            _logger.LogWarning("Stage {StageId} is already closed", _stageId);
            return;
        }

        _stageClosed = true;

        // Cancel all timers for this stage
        _timerManager.CancelAllTimersForStage(_stageId);

        _logger.LogInformation("Stage {StageId} ({StageType}) closed", _stageId, _stageType);
    }

    /// <inheritdoc/>
    public void AsyncBlock(Func<Task<object?>> preCallback, Func<object?, Task>? postCallback = null)
    {
        if (_stageClosed)
        {
            _logger.LogWarning("Cannot execute AsyncBlock on closed stage {StageId}", _stageId);
            return;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                // Execute the pre-callback outside stage context
                var result = await preCallback();

                // Dispatch the post-callback back to the stage if provided
                if (postCallback != null)
                {
                    _dispatcher.DispatchAsyncBlockResult(_stageId, postCallback, result);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in AsyncBlock pre-callback for stage {StageId}", _stageId);
            }
        });
    }

    /// <summary>
    /// Sends a packet to a specific session.
    /// </summary>
    /// <param name="sessionId">The session identifier.</param>
    /// <param name="packet">The packet to send.</param>
    private void SendToSession(long sessionId, IPacket packet)
    {
        var session = _sessionManager.GetSession(sessionId);
        if (session == null)
        {
            _logger.LogWarning("Session {SessionId} not found for sending packet", sessionId);
            return;
        }

        // TODO: Serialize and send packet to session's transport layer
        // For now, just log the intent
        _logger.LogTrace("Sending packet to session {SessionId}: {MsgId}", sessionId, packet.MsgId);
    }

    /// <summary>
    /// Simple implementation of IPacket for internal use.
    /// </summary>
    private sealed class SimplePacket : IPacket
    {
        public string MsgId { get; }
        public ushort MsgSeq { get; }
        public int StageId { get; }
        public ushort ErrorCode { get; }
        public IPayload Payload { get; }

        public SimplePacket(string msgId, ushort msgSeq, int stageId, ushort errorCode, IPayload payload)
        {
            MsgId = msgId;
            MsgSeq = msgSeq;
            StageId = stageId;
            ErrorCode = errorCode;
            Payload = payload;
        }

        public void Dispose()
        {
            Payload?.Dispose();
        }
    }
}

/// <summary>
/// Contains context information for a request being processed in a stage.
/// </summary>
internal sealed class RequestContext
{
    /// <summary>
    /// Gets the session identifier for the request.
    /// </summary>
    public long SessionId { get; }

    /// <summary>
    /// Gets the message identifier.
    /// </summary>
    public string MsgId { get; }

    /// <summary>
    /// Gets the message sequence number (0 for notifications, >0 for requests).
    /// </summary>
    public ushort MsgSeq { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="RequestContext"/> class.
    /// </summary>
    /// <param name="sessionId">The session identifier.</param>
    /// <param name="msgId">The message identifier.</param>
    /// <param name="msgSeq">The message sequence number.</param>
    public RequestContext(long sessionId, string msgId, ushort msgSeq)
    {
        SessionId = sessionId;
        MsgId = msgId;
        MsgSeq = msgSeq;
    }
}
