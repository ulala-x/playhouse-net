#nullable enable

using Google.Protobuf;
using PlayHouse.Abstractions;
using PlayHouse.Abstractions.Api;
using PlayHouse.Core.Messaging;
using PlayHouse.Core.Shared;
using PlayHouse.Runtime.ServerMesh.Communicator;
using PlayHouse.Runtime.ServerMesh.Message;
using PlayHouse.Runtime.Proto;

namespace PlayHouse.Core.Api;

/// <summary>
/// Implementation of IApiSender for API server handlers.
/// </summary>
/// <remarks>
/// ApiSender extends XSender with API-specific functionality:
/// - Stage creation and management
/// - Client session information access
/// - Authentication context management
/// </remarks>
internal class ApiSender : XSender, IApiSender
{
    private string _accountId = string.Empty;
    private string _sessionNid = string.Empty;
    private long _sid;
    private long _stageId;

    /// <summary>
    /// Initializes a new instance of the <see cref="ApiSender"/> class.
    /// </summary>
    /// <param name="communicator">The communicator for sending messages.</param>
    /// <param name="requestCache">The request cache for tracking pending requests.</param>
    /// <param name="serviceId">The service ID of this API server.</param>
    /// <param name="nid">The NID of this API server.</param>
    public ApiSender(
        IClientCommunicator communicator,
        RequestCache requestCache,
        ushort serviceId,
        string nid)
        : base(communicator, requestCache, serviceId, nid)
    {
    }

    /// <inheritdoc/>
    public string AccountId
    {
        get => _accountId;
        set => _accountId = value;
    }

    /// <inheritdoc/>
    public string SessionNid => _sessionNid;

    /// <inheritdoc/>
    public long Sid => _sid;

    /// <inheritdoc/>
    public long StageId => _stageId;

    /// <summary>
    /// Sets the session context from the incoming packet header.
    /// </summary>
    /// <param name="header">The route header containing session information.</param>
    public void SetSessionContext(RouteHeader header)
    {
        SetCurrentHeader(header);
        _sessionNid = header.From;
        _sid = header.Sid;
        _stageId = header.StageId;
        _accountId = header.AccountId.ToString();
    }

    /// <summary>
    /// Clears the session context.
    /// </summary>
    public void ClearSessionContext()
    {
        ClearCurrentHeader();
        _sessionNid = string.Empty;
        _sid = 0;
        _stageId = 0;
        _accountId = string.Empty;
    }

    #region Stage Creation

    /// <inheritdoc/>
    public async Task<CreateStageResult> CreateStage(
        string playNid,
        string stageType,
        long stageId,
        IPacket packet)
    {
        var req = new CreateStageReq
        {
            StageType = stageType,
            PayloadId = packet.MsgId,
            Payload = ByteString.CopyFrom(packet.Payload.DataSpan)
        };

        var routePacket = CPacket.Of(req);
        var reply = await RequestToStage(playNid, stageId, routePacket);
        var res = CreateStageRes.Parser.ParseFrom(reply.Payload.DataSpan);

        // Zero-copy: use ByteString.Memory directly
        return new CreateStageResult(
            res.Result,
            CPacket.Of(res.PayloadId, new MemoryPayload(res.Payload.Memory)));
    }

    /// <inheritdoc/>
    public async Task<GetOrCreateStageResult> GetOrCreateStage(
        string playNid,
        string stageType,
        long stageId,
        IPacket createPacket,
        IPacket joinPacket)
    {
        var req = new GetOrCreateStageReq
        {
            StageType = stageType,
            CreatePayloadId = createPacket.MsgId,
            CreatePayload = ByteString.CopyFrom(createPacket.Payload.DataSpan),
            JoinPayloadId = joinPacket.MsgId,
            JoinPayload = ByteString.CopyFrom(joinPacket.Payload.DataSpan)
        };

        var routePacket = CPacket.Of(req);
        var reply = await RequestToStage(playNid, stageId, routePacket);
        var res = GetOrCreateStageRes.Parser.ParseFrom(reply.Payload.DataSpan);

        // Result/IsCreated combinations:
        // - Result=true, IsCreated=false: Stage already existed
        // - Result=true, IsCreated=true: New stage created successfully
        // - Result=false, IsCreated=false: Creation failed
        // Zero-copy: use ByteString.Memory directly
        return new GetOrCreateStageResult(
            res.Result,
            res.IsCreated,
            CPacket.Of(res.PayloadId, new MemoryPayload(res.Payload.Memory)));
    }

    #endregion

    #region Client Communication

    /// <inheritdoc/>
    public void SendToClient(IPacket packet)
    {
        if (string.IsNullOrEmpty(_sessionNid))
        {
            throw new InvalidOperationException("Session context not set - cannot send to client");
        }

        SendToClient(_sessionNid, _sid, packet);
    }

    /// <inheritdoc/>
    public void SendToClient(string sessionNid, long sid, IPacket packet)
    {
        // Send to session server which will forward to the client
        var header = new RouteHeader
        {
            ServiceId = ServiceId,
            MsgId = packet.MsgId,
            From = ServerId,
            Sid = sid
        };

        // This would be sent via the session server's client communication path
        // For now, we use the API communication path to send to session server
        SendToApi(sessionNid, packet);
    }

    #endregion
}

