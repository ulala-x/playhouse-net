#nullable enable

using PlayHouse.Abstractions;
using PlayHouse.Abstractions.Play;
using PlayHouse.Core.Play.Base;
using PlayHouse.Runtime.ClientTransport;
using PlayHouse.Runtime.ServerMesh.Discovery;

namespace PlayHouse.Core.Play;

/// <summary>
/// Internal implementation of IActorSender.
/// </summary>
/// <remarks>
/// XActorSender delegates most operations to its parent BaseStage's StageSender.
/// It maintains Actor-specific session information for client communication.
/// </remarks>
internal sealed class XActorSender : IActorSender
{
    private readonly BaseStage _baseStage;
    private ITransportSession? _transportSession;

    private string _sessionServerId;
    private long _sid;
    private string _apiServerId;

    private XStageSender StageSender => _baseStage.StageSender;

    /// <inheritdoc/>
    public string AccountId { get; set; } = string.Empty;

    /// <inheritdoc/>
    public ServerType ServerType => _baseStage.StageSender.ServerType;

    /// <inheritdoc/>
    public ushort ServiceId => _baseStage.StageSender.ServiceId;

    /// <summary>
    /// Initializes a new instance of the <see cref="XActorSender"/> class.
    /// </summary>
    /// <param name="sessionNid">Session server NID.</param>
    /// <param name="sid">Session ID.</param>
    /// <param name="apiNid">API server NID.</param>
    /// <param name="baseStage">Parent BaseStage.</param>
    /// <param name="transportSession">Optional transport session for direct client communication.</param>
    public XActorSender(
        string sessionNid,
        long sid,
        string apiNid,
        BaseStage baseStage,
        ITransportSession? transportSession = null)
    {
        _sessionServerId = sessionNid;
        _sid = sid;
        _apiServerId = apiNid;
        _baseStage = baseStage;
        _transportSession = transportSession;
    }

    /// <summary>
    /// Gets the session server NID.
    /// </summary>
    internal string SessionNid => _sessionServerId;

    /// <summary>
    /// Gets the session ID.
    /// </summary>
    internal long Sid => _sid;

    /// <summary>
    /// Gets the API server NID.
    /// </summary>
    internal string ApiNid => _apiServerId;

    #region IActorSender Implementation

    /// <inheritdoc/>
    public async Task LeaveStageAsync()
    {
        await _baseStage.LeaveStage(AccountId, _sessionServerId, _sid);
    }

    /// <inheritdoc/>
    public void SendToClient(IPacket packet)
    {
        // For directly connected clients, use transport session directly
        if (_transportSession != null)
        {
            if (_transportSession.IsConnected)
            {
                _transportSession.SendResponse(
                    packet.MsgId,
                    0,  // msgSeq = 0 for push messages
                    _baseStage.StageId,
                    0,  // errorCode = 0
                    packet.Payload.DataSpan);
                return;
            }
            // Session exists but disconnected - skip sending
            Console.WriteLine($"[XActorSender] SendToClient skipped: session {_transportSession.SessionId} disconnected for stage {_baseStage.StageId}");
            return;
        }

        // For server-to-server (API → PlayServer), use the routing mechanism
        StageSender.SendToClient(_sessionServerId, _sid, packet);
    }

    #endregion

    #region ISender Delegation

    /// <inheritdoc/>
    public void SendToApi(string apiNid, IPacket packet)
    {
        StageSender.SendToApi(apiNid, packet);
    }

    /// <inheritdoc/>
    public void RequestToApi(string apiNid, IPacket packet, ReplyCallback replyCallback)
    {
        StageSender.RequestToApi(apiNid, packet, replyCallback);
    }

    /// <inheritdoc/>
    public async Task<IPacket> RequestToApi(string apiNid, IPacket packet)
    {
        return await StageSender.RequestToApi(apiNid, packet);
    }

    /// <inheritdoc/>
    public void SendToService(ServerType serverType, ushort serviceId, IPacket packet)
    {
        StageSender.SendToService(serverType, serviceId, packet);
    }

    /// <inheritdoc/>
    public void SendToService(ServerType serverType, ushort serviceId, IPacket packet, ServerSelectionPolicy policy)
    {
        StageSender.SendToService(serverType, serviceId, packet, policy);
    }

    /// <inheritdoc/>
    public void RequestToService(ServerType serverType, ushort serviceId, IPacket packet, ReplyCallback replyCallback)
    {
        StageSender.RequestToService(serverType, serviceId, packet, replyCallback);
    }

    /// <inheritdoc/>
    public void RequestToService(ServerType serverType, ushort serviceId, IPacket packet, ReplyCallback replyCallback, ServerSelectionPolicy policy)
    {
        StageSender.RequestToService(serverType, serviceId, packet, replyCallback, policy);
    }

    /// <inheritdoc/>
    public Task<IPacket> RequestToService(ServerType serverType, ushort serviceId, IPacket packet)
    {
        return StageSender.RequestToService(serverType, serviceId, packet);
    }

    /// <inheritdoc/>
    public Task<IPacket> RequestToService(ServerType serverType, ushort serviceId, IPacket packet, ServerSelectionPolicy policy)
    {
        return StageSender.RequestToService(serverType, serviceId, packet, policy);
    }

    /// <inheritdoc/>
    public void SendToStage(string playNid, long stageId, IPacket packet)
    {
        StageSender.SendToStage(playNid, stageId, packet);
    }

    /// <inheritdoc/>
    public void RequestToStage(string playNid, long stageId, IPacket packet, ReplyCallback replyCallback)
    {
        StageSender.RequestToStage(playNid, stageId, packet, replyCallback);
    }

    /// <inheritdoc/>
    public async Task<IPacket> RequestToStage(string playNid, long stageId, IPacket packet)
    {
        return await StageSender.RequestToStage(playNid, stageId, packet);
    }

    /// <inheritdoc/>
    public void Reply(ushort errorCode)
    {
        // For client requests, we don't use SendToClient for error codes
        // Just use the standard Reply mechanism
        StageSender.Reply(errorCode);
    }

    /// <inheritdoc/>
    public void Reply(IPacket reply)
    {
        _baseStage.Reply(reply);
    }

    #endregion

    #region Session Update

    /// <summary>
    /// Updates session information (used for reconnection).
    /// </summary>
    /// <param name="sessionNid">New session server NID.</param>
    /// <param name="sid">New session ID.</param>
    /// <param name="apiNid">New API server NID.</param>
    /// <param name="transportSession">Optional new transport session.</param>
    internal void Update(string sessionNid, long sid, string apiNid, ITransportSession? transportSession = null)
    {
        _sessionServerId = sessionNid;
        _sid = sid;
        _apiServerId = apiNid;
        _transportSession = transportSession;
    }

    #endregion
}
