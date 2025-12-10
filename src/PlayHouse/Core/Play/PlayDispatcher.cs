#nullable enable

using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using PlayHouse.Abstractions;
using PlayHouse.Abstractions.Play;
using PlayHouse.Core.Messaging;
using PlayHouse.Core.Play.Base;
using PlayHouse.Core.Shared;
using PlayHouse.Runtime.Communicator;
using PlayHouse.Runtime.Message;
using PlayHouse.Runtime.Proto;

// Alias to avoid conflict with System.Threading.TimerCallback
using TimerCallbackDelegate = PlayHouse.Abstractions.Play.TimerCallback;

namespace PlayHouse.Core.Play;

/// <summary>
/// Main dispatcher for Play server message routing.
/// </summary>
/// <remarks>
/// PlayDispatcher manages Stage lifecycle and routes messages to appropriate Stages.
/// It uses ConcurrentDictionary for thread-safe Stage management.
///
/// Key responsibilities:
/// - Stage creation and destruction
/// - Message routing to Stages
/// - Timer management
/// - AsyncBlock coordination
/// </remarks>
internal sealed class PlayDispatcher : IPlayDispatcher, IDisposable
{
    private readonly ConcurrentDictionary<long, BaseStage> _stages = new();
    private readonly PlayProducer _producer;
    private readonly IClientCommunicator _communicator;
    private readonly RequestCache _requestCache;
    private readonly TimerManager _timerManager;
    private readonly ushort _serviceId;
    private readonly string _nid;
    private readonly ILogger? _logger;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="PlayDispatcher"/> class.
    /// </summary>
    public PlayDispatcher(
        PlayProducer producer,
        IClientCommunicator communicator,
        RequestCache requestCache,
        ushort serviceId,
        string nid,
        ILogger? logger = null)
    {
        _producer = producer;
        _communicator = communicator;
        _requestCache = requestCache;
        _serviceId = serviceId;
        _nid = nid;
        _logger = logger;

        _timerManager = new TimerManager(OnTimerCallback, logger);
    }

    #region IPlayDispatcher Implementation

    /// <inheritdoc/>
    public void Post(RuntimeRoutePacket packet)
    {
        var stageId = packet.StageId;
        var msgId = packet.MsgId;

        // Handle Stage creation
        if (IsCreateStageRequest(msgId))
        {
            HandleCreateStage(stageId, packet);
            return;
        }

        // Route to existing Stage
        if (_stages.TryGetValue(stageId, out var baseStage))
        {
            baseStage.Post(packet);
        }
        else
        {
            _logger?.LogWarning("Stage {StageId} not found for message {MsgId}", stageId, msgId);
            SendErrorReply(packet, BaseErrorCode.StageNotFound);
            packet.Dispose();
        }
    }

    /// <inheritdoc/>
    public void PostTimer(TimerPacket timerPacket)
    {
        _timerManager.ProcessTimer(timerPacket);
    }

    /// <inheritdoc/>
    public void PostAsyncBlock(AsyncBlockPacket asyncPacket)
    {
        if (_stages.TryGetValue(asyncPacket.StageId, out var baseStage))
        {
            baseStage.PostAsyncBlock(asyncPacket);
        }
        else
        {
            _logger?.LogWarning("Stage {StageId} not found for AsyncBlock", asyncPacket.StageId);
        }
    }

    /// <inheritdoc/>
    public void PostDestroy(long stageId)
    {
        if (_stages.TryRemove(stageId, out var baseStage))
        {
            _timerManager.CancelAllForStage(stageId);
            baseStage.PostDestroy();
        }
    }

    #endregion

    #region Stage Creation

    private static bool IsCreateStageRequest(string msgId)
    {
        return msgId == nameof(CreateStageReq) ||
               msgId == "PlayHouse.Runtime.Proto.CreateStageReq";
    }

    private void HandleCreateStage(long stageId, RuntimeRoutePacket packet)
    {
        try
        {
            var req = CreateStageReq.Parser.ParseFrom(packet.Payload.DataSpan);

            if (!_producer.IsValidType(req.StageType))
            {
                _logger?.LogError("Invalid stage type: {StageType}", req.StageType);
                SendErrorReply(packet, BaseErrorCode.InvalidStageType);
                packet.Dispose();
                return;
            }

            // Create Stage atomically
            var created = false;
            var baseStage = _stages.GetOrAdd(stageId, id =>
            {
                created = true;
                return CreateNewStage(id, req.StageType);
            });

            if (!created)
            {
                // Stage already exists
                _logger?.LogWarning("Stage {StageId} already exists", stageId);
                SendErrorReply(packet, BaseErrorCode.StageAlreadyExists);
                packet.Dispose();
                return;
            }

            // Post create request to Stage event loop
            baseStage.Post(packet);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to create Stage {StageId}", stageId);
            SendErrorReply(packet, BaseErrorCode.InternalError);
            packet.Dispose();
        }
    }

    private BaseStage CreateNewStage(long stageId, string stageType)
    {
        var stageSender = new XStageSender(
            _communicator,
            _requestCache,
            _serviceId,
            _nid,
            stageId,
            this);

        stageSender.SetStageType(stageType);

        var stage = _producer.GetStage(stageType, stageSender);
        return new BaseStage(stage, stageSender, _logger);
    }

    #endregion

    #region Timer Callback

    private void OnTimerCallback(long stageId, long timerId, TimerCallbackDelegate callback)
    {
        if (_stages.TryGetValue(stageId, out var baseStage))
        {
            baseStage.PostTimerCallback(timerId, callback);
        }
        else
        {
            // Stage was destroyed, cancel the timer
            _timerManager.CancelAllForStage(stageId);
        }
    }

    #endregion

    #region Error Handling

    private void SendErrorReply(RuntimeRoutePacket packet, BaseErrorCode errorCode)
    {
        if (packet.MsgSeq == 0) return; // Not a request

        var replyPacket = packet.CreateErrorReply((ushort)errorCode);
        _communicator.Send(packet.From, replyPacket);
        replyPacket.Dispose();
    }

    #endregion

    #region Metrics

    /// <summary>
    /// Gets the number of active Stages.
    /// </summary>
    public int StageCount => _stages.Count;

    /// <summary>
    /// Gets the total number of Actors across all Stages.
    /// </summary>
    public int TotalActorCount => _stages.Values.Sum(s => s.ActorCount);

    /// <summary>
    /// Gets the number of active timers.
    /// </summary>
    public int ActiveTimerCount => _timerManager.ActiveTimerCount;

    #endregion

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _timerManager.Dispose();

        foreach (var stageId in _stages.Keys.ToList())
        {
            PostDestroy(stageId);
        }
    }
}

/// <summary>
/// Base error codes for PlayHouse framework.
/// </summary>
public enum BaseErrorCode : ushort
{
    /// <summary>
    /// Success (no error).
    /// </summary>
    Success = 0,

    /// <summary>
    /// Request timed out.
    /// </summary>
    RequestTimeout = 100,

    /// <summary>
    /// Stage not found.
    /// </summary>
    StageNotFound = 200,

    /// <summary>
    /// Stage already exists.
    /// </summary>
    StageAlreadyExists = 201,

    /// <summary>
    /// Invalid stage type.
    /// </summary>
    InvalidStageType = 202,

    /// <summary>
    /// Stage creation failed.
    /// </summary>
    StageCreateFailed = 203,

    /// <summary>
    /// Authentication failed.
    /// </summary>
    AuthenticationFailed = 300,

    /// <summary>
    /// Join stage failed.
    /// </summary>
    JoinStageFailed = 301,

    /// <summary>
    /// Internal server error.
    /// </summary>
    InternalError = 500,

    /// <summary>
    /// Unchecked content error.
    /// </summary>
    UncheckedContentsError = 501
}
