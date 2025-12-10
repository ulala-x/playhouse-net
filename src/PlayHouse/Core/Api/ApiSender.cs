#nullable enable

using Google.Protobuf;
using PlayHouse.Abstractions;
using PlayHouse.Abstractions.Api;
using PlayHouse.Core.Messaging;
using PlayHouse.Core.Shared;
using PlayHouse.Runtime.Communicator;
using PlayHouse.Runtime.Message;
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

    /// <summary>
    /// Sets the session context from the incoming packet header.
    /// </summary>
    /// <param name="header">The route header containing session information.</param>
    public void SetSessionContext(RouteHeader header)
    {
        SetCurrentHeader(header);
        _sessionNid = header.From;
        _sid = header.Sid;
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

        var routePacket = CPacket.Of(nameof(CreateStageReq), req.ToByteArray());

        try
        {
            var reply = await RequestToStage(playNid, stageId, routePacket);
            var res = CreateStageRes.Parser.ParseFrom(reply.Payload.DataSpan);

            return new CreateStageResult(
                BaseErrorCode.Success,
                CPacket.Of(res.PayloadId, res.Payload.ToByteArray()));
        }
        catch (TimeoutException)
        {
            return new CreateStageResult(BaseErrorCode.RequestTimeout, CPacket.Empty("Error"));
        }
        catch (Exception)
        {
            return new CreateStageResult(BaseErrorCode.SystemError, CPacket.Empty("Error"));
        }
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

        var routePacket = CPacket.Of(nameof(GetOrCreateStageReq), req.ToByteArray());

        try
        {
            var reply = await RequestToStage(playNid, stageId, routePacket);
            var res = GetOrCreateStageRes.Parser.ParseFrom(reply.Payload.DataSpan);

            return new GetOrCreateStageResult(
                BaseErrorCode.Success,
                res.IsCreated,
                CPacket.Of(res.PayloadId, res.Payload.ToByteArray()));
        }
        catch (TimeoutException)
        {
            return new GetOrCreateStageResult(BaseErrorCode.RequestTimeout, false, CPacket.Empty("Error"));
        }
        catch (Exception)
        {
            return new GetOrCreateStageResult(BaseErrorCode.SystemError, false, CPacket.Empty("Error"));
        }
    }

    /// <inheritdoc/>
    public async Task<JoinStageResult> JoinStage(
        string playNid,
        long stageId,
        IPacket packet)
    {
        var req = new JoinStageReq
        {
            SessionNid = _sessionNid,
            Sid = _sid,
            PayloadId = packet.MsgId,
            Payload = ByteString.CopyFrom(packet.Payload.DataSpan)
        };

        var routePacket = CPacket.Of(nameof(JoinStageReq), req.ToByteArray());

        try
        {
            var reply = await RequestToStage(playNid, stageId, routePacket);
            var res = JoinStageRes.Parser.ParseFrom(reply.Payload.DataSpan);

            return new JoinStageResult(
                BaseErrorCode.Success,
                CPacket.Of(res.PayloadId, res.Payload.ToByteArray()));
        }
        catch (TimeoutException)
        {
            return new JoinStageResult(BaseErrorCode.RequestTimeout, CPacket.Empty("Error"));
        }
        catch (Exception)
        {
            return new JoinStageResult(BaseErrorCode.SystemError, CPacket.Empty("Error"));
        }
    }

    /// <inheritdoc/>
    public async Task<CreateJoinStageResult> CreateJoinStage(
        string playNid,
        string stageType,
        long stageId,
        IPacket createPacket,
        IPacket joinPacket)
    {
        var req = new CreateJoinStageReq
        {
            StageType = stageType,
            CreatePayloadId = createPacket.MsgId,
            CreatePayload = ByteString.CopyFrom(createPacket.Payload.DataSpan),
            JoinPayloadId = joinPacket.MsgId,
            JoinPayload = ByteString.CopyFrom(joinPacket.Payload.DataSpan),
            SessionNid = _sessionNid,
            Sid = _sid
        };

        var routePacket = CPacket.Of(nameof(CreateJoinStageReq), req.ToByteArray());

        try
        {
            var reply = await RequestToStage(playNid, stageId, routePacket);
            var res = CreateJoinStageRes.Parser.ParseFrom(reply.Payload.DataSpan);

            return new CreateJoinStageResult(
                BaseErrorCode.Success,
                res.IsCreated,
                CPacket.Of(res.CreatePayloadId, res.CreatePayload.ToByteArray()),
                CPacket.Of(res.JoinPayloadId, res.JoinPayload.ToByteArray()));
        }
        catch (TimeoutException)
        {
            return new CreateJoinStageResult(
                BaseErrorCode.RequestTimeout,
                false,
                CPacket.Empty("Error"),
                CPacket.Empty("Error"));
        }
        catch (Exception)
        {
            return new CreateJoinStageResult(
                BaseErrorCode.SystemError,
                false,
                CPacket.Empty("Error"),
                CPacket.Empty("Error"));
        }
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
            From = Nid,
            Sid = sid
        };

        // This would be sent via the session server's client communication path
        // For now, we use the API communication path to send to session server
        SendToApi(sessionNid, packet);
    }

    #endregion
}

