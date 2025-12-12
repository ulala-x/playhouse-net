#nullable enable

using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using PlayHouse.Abstractions;
using PlayHouse.Abstractions.Play;
using PlayHouse.Core.Messaging;
using PlayHouse.Core.Play.Base;
using PlayHouse.Core.Play.Base.Command;
using PlayHouse.Core.Shared;
using PlayHouse.Runtime.ServerMesh.Communicator;
using PlayHouse.Runtime.ServerMesh.Message;
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
    private readonly IClientReplyHandler? _clientReplyHandler;
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
        IClientReplyHandler? clientReplyHandler = null,
        ILogger? logger = null)
    {
        _producer = producer;
        _communicator = communicator;
        _requestCache = requestCache;
        _serviceId = serviceId;
        _nid = nid;
        _clientReplyHandler = clientReplyHandler;
        _logger = logger;

        _timerManager = new TimerManager(OnTimerCallback, logger);
    }

    #region IPlayDispatcher Implementation

    /// <inheritdoc/>
    public void OnPost(PlayMessage message)
    {
        switch (message)
        {
            case TimerMessage timerMsg:
                _timerManager.ProcessTimer(timerMsg.TimerPacket);
                break;

            case AsyncMessage asyncMsg:
                ProcessAsyncBlock(asyncMsg.AsyncBlockPacket);
                break;

            case DestroyMessage destroyMsg:
                ProcessDestroy(destroyMsg.StageId);
                break;

            case RouteMessage routeMsg:
                ProcessRoute(routeMsg.RoutePacket);
                break;

            case ClientRouteMessage clientRouteMsg:
                ProcessClientRoute(clientRouteMsg);
                break;

            case JoinActorMessage joinActorMsg:
                ProcessJoinActor(joinActorMsg);
                break;

            case DisconnectMessage disconnectMsg:
                ProcessDisconnect(disconnectMsg);
                break;

            default:
                _logger?.LogWarning("Unknown message type: {Type}", message.GetType().Name);
                message.Dispose();
                break;
        }
    }

    #endregion

    #region Message Processing

    private void ProcessRoute(RuntimeRoutePacket packet)
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

    private void ProcessAsyncBlock(AsyncBlockPacket asyncPacket)
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

    private void ProcessDestroy(long stageId)
    {
        if (_stages.TryRemove(stageId, out var baseStage))
        {
            _timerManager.CancelAllForStage(stageId);
            baseStage.PostDestroy();
        }
    }

    private void ProcessClientRoute(ClientRouteMessage message)
    {
        if (_stages.TryGetValue(message.StageId, out var baseStage))
        {
            baseStage.PostClientRoute(message.AccountId, message.MsgId, message.MsgSeq, message.Sid, message.Payload);
        }
        else
        {
            _logger?.LogWarning("Stage {StageId} not found for client message {MsgId}", message.StageId, message.MsgId);
        }
    }

    private void ProcessJoinActor(JoinActorMessage message)
    {
        if (_stages.TryGetValue(message.StageId, out var baseStage))
        {
            if (message.CompletionSource != null)
            {
                // Use async version when CompletionSource is provided
                _ = baseStage.PostJoinActorAsync(message.Actor).ContinueWith(t =>
                {
                    if (t.IsCompletedSuccessfully)
                        message.CompletionSource.TrySetResult(true);
                    else if (t.IsFaulted)
                        message.CompletionSource.TrySetException(t.Exception!);
                    else
                        message.CompletionSource.TrySetCanceled();
                }, TaskScheduler.Default);
            }
            else
            {
                baseStage.PostJoinActor(message.Actor);
            }
        }
        else
        {
            _logger?.LogWarning("Stage {StageId} not found for JoinActorMessage", message.StageId);
            message.CompletionSource?.TrySetException(new InvalidOperationException($"Stage {message.StageId} not found"));
        }
    }

    private void ProcessDisconnect(DisconnectMessage message)
    {
        if (_stages.TryGetValue(message.StageId, out var baseStage))
        {
            baseStage.PostDisconnect(message.AccountId);
        }
        else
        {
            _logger?.LogWarning("Stage {StageId} not found for DisconnectMessage", message.StageId);
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
            this,
            _clientReplyHandler);

        stageSender.SetStageType(stageType);

        var stage = _producer.GetStage(stageType, stageSender);
        var baseStage = new BaseStage(stage, stageSender, _logger);

        // Create and set command handler for system messages
        var cmdHandler = new BaseStageCmdHandler(_logger);
        RegisterCommands(cmdHandler);
        baseStage.SetCmdHandler(cmdHandler);

        return baseStage;
    }

    /// <summary>
    /// Registers all system message commands to the handler.
    /// </summary>
    private void RegisterCommands(BaseStageCmdHandler cmdHandler)
    {
        cmdHandler.Register(nameof(CreateStageReq), new CreateStageCmd(this, _logger));
        cmdHandler.Register(nameof(GetOrCreateStageReq), new GetOrCreateStageCmd(_logger));
        cmdHandler.Register(nameof(DisconnectNoticeMsg), new DisconnectNoticeCmd(_logger));
        cmdHandler.Register(nameof(ReconnectMsg), new ReconnectCmd(_logger));
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

    private void SendErrorReply(RuntimeRoutePacket packet, ushort errorCode)
    {
        if (packet.MsgSeq == 0) return; // Not a request

        var replyPacket = packet.CreateErrorReply(errorCode);
        _communicator.Send(packet.From, replyPacket);
        replyPacket.Dispose();
    }

    #endregion

    #region Stage Access

    /// <summary>
    /// Gets an existing Stage or creates a new one if it doesn't exist.
    /// </summary>
    /// <param name="stageId">The stage ID.</param>
    /// <param name="stageType">The stage type.</param>
    /// <returns>The BaseStage instance.</returns>
    public BaseStage? GetOrCreateStage(long stageId, string stageType)
    {
        if (!_producer.IsValidType(stageType))
        {
            _logger?.LogError("Invalid stage type: {StageType}", stageType);
            return null;
        }

        return _stages.GetOrAdd(stageId, id => CreateNewStage(id, stageType));
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
            ProcessDestroy(stageId);
        }
    }
}

