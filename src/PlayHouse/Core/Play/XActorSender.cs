#nullable enable

using PlayHouse.Abstractions;
using PlayHouse.Abstractions.Play;
using PlayHouse.Core.Play.Base;

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
    private readonly long _routeAccountId;

    private string _sessionNid;
    private long _sid;
    private string _apiNid;

    /// <inheritdoc/>
    public string AccountId { get; set; } = "";

    /// <inheritdoc/>
    public ushort ServiceId => _baseStage.StageSender.ServiceId;

    /// <summary>
    /// Initializes a new instance of the <see cref="XActorSender"/> class.
    /// </summary>
    /// <param name="routeAccountId">Internal routing account ID (from RouteHeader).</param>
    /// <param name="sessionNid">Session server NID.</param>
    /// <param name="sid">Session ID.</param>
    /// <param name="apiNid">API server NID.</param>
    /// <param name="baseStage">Parent BaseStage.</param>
    public XActorSender(
        long routeAccountId,
        string sessionNid,
        long sid,
        string apiNid,
        BaseStage baseStage)
    {
        _routeAccountId = routeAccountId;
        _sessionNid = sessionNid;
        _sid = sid;
        _apiNid = apiNid;
        _baseStage = baseStage;
    }

    /// <summary>
    /// Gets the internal routing account ID.
    /// </summary>
    internal long RouteAccountId => _routeAccountId;

    /// <summary>
    /// Gets the session server NID.
    /// </summary>
    internal string SessionNid => _sessionNid;

    /// <summary>
    /// Gets the session ID.
    /// </summary>
    internal long Sid => _sid;

    /// <summary>
    /// Gets the API server NID.
    /// </summary>
    internal string ApiNid => _apiNid;

    #region IActorSender Implementation

    /// <inheritdoc/>
    public void LeaveStage()
    {
        _baseStage.LeaveStage(_routeAccountId, _sessionNid, _sid);
    }

    /// <inheritdoc/>
    public void SendToClient(IPacket packet)
    {
        _baseStage.StageSender.SendToClient(_sessionNid, _sid, packet);
    }

    #endregion

    #region ISender Delegation

    /// <inheritdoc/>
    public void SendToApi(string apiNid, IPacket packet)
    {
        _baseStage.StageSender.SendToApi(apiNid, packet);
    }

    /// <inheritdoc/>
    public void RequestToApi(string apiNid, IPacket packet, ReplyCallback replyCallback)
    {
        _baseStage.StageSender.RequestToApi(apiNid, packet, replyCallback);
    }

    /// <inheritdoc/>
    public async Task<IPacket> RequestToApi(string apiNid, IPacket packet)
    {
        return await _baseStage.StageSender.RequestToApi(apiNid, packet);
    }

    /// <inheritdoc/>
    public void SendToStage(string playNid, long stageId, IPacket packet)
    {
        _baseStage.StageSender.SendToStage(playNid, stageId, packet);
    }

    /// <inheritdoc/>
    public void RequestToStage(string playNid, long stageId, IPacket packet, ReplyCallback replyCallback)
    {
        _baseStage.StageSender.RequestToStage(playNid, stageId, packet, replyCallback);
    }

    /// <inheritdoc/>
    public async Task<IPacket> RequestToStage(string playNid, long stageId, IPacket packet)
    {
        return await _baseStage.StageSender.RequestToStage(playNid, stageId, packet);
    }

    /// <inheritdoc/>
    public void Reply(ushort errorCode)
    {
        _baseStage.StageSender.Reply(errorCode);
    }

    /// <inheritdoc/>
    public void Reply(IPacket reply)
    {
        _baseStage.StageSender.Reply(reply);
    }

    #endregion

    #region Session Update

    /// <summary>
    /// Updates session information (used for reconnection).
    /// </summary>
    /// <param name="sessionNid">New session server NID.</param>
    /// <param name="sid">New session ID.</param>
    /// <param name="apiNid">New API server NID.</param>
    internal void Update(string sessionNid, long sid, string apiNid)
    {
        _sessionNid = sessionNid;
        _sid = sid;
        _apiNid = apiNid;
    }

    #endregion
}
