#nullable enable

using Google.Protobuf;
using Microsoft.Extensions.Logging;
using PlayHouse.Abstractions;
using PlayHouse.Abstractions.Play;
using PlayHouse.Core.Shared;
using PlayHouse.Runtime.ServerMesh.Message;
using PlayHouse.Runtime.Proto;

namespace PlayHouse.Core.Play.Base;

/// <summary>
/// Stage 시스템 메시지 핸들러.
/// </summary>
/// <remarks>
/// 내부 시스템 메시지를 처리합니다:
/// - JoinStageReq: 10단계 인증 플로우
/// - CreateJoinStageReq: Stage 생성 + 입장
/// - GetOrCreateStageReq: 기존 Stage 반환 또는 생성
/// - DisconnectNoticeMsg: 연결 끊김 알림
/// - ReconnectMsg: 재연결 처리
/// - CreateStageReq: Stage OnCreate/OnPostCreate 콜백
/// </remarks>
internal sealed class BaseStageCmdHandler
{
    private readonly BaseStage _baseStage;
    private readonly PlayProducer _producer;
    private readonly IPlayDispatcher _dispatcher;
    private readonly ILogger? _logger;

    /// <summary>
    /// 새 BaseStageCmdHandler 인스턴스를 생성합니다.
    /// </summary>
    public BaseStageCmdHandler(
        BaseStage baseStage,
        PlayProducer producer,
        IPlayDispatcher dispatcher,
        ILogger? logger = null)
    {
        _baseStage = baseStage;
        _producer = producer;
        _dispatcher = dispatcher;
        _logger = logger;
    }

    /// <summary>
    /// 시스템 메시지를 처리합니다.
    /// </summary>
    /// <param name="msgId">메시지 ID.</param>
    /// <param name="packet">라우트 패킷.</param>
    /// <returns>처리 성공 여부.</returns>
    public async Task<bool> HandleAsync(string msgId, RuntimeRoutePacket packet)
    {
        try
        {
            return msgId switch
            {
                nameof(CreateStageReq) or "PlayHouse.Runtime.Proto.CreateStageReq"
                    => await HandleCreateStageReqAsync(packet),

                nameof(JoinStageReq) or "PlayHouse.Runtime.Proto.JoinStageReq"
                    => await HandleJoinStageReqAsync(packet),

                nameof(CreateJoinStageReq) or "PlayHouse.Runtime.Proto.CreateJoinStageReq"
                    => await HandleCreateJoinStageReqAsync(packet),

                nameof(GetOrCreateStageReq) or "PlayHouse.Runtime.Proto.GetOrCreateStageReq"
                    => await HandleGetOrCreateStageReqAsync(packet),

                nameof(DisconnectNoticeMsg) or "PlayHouse.Runtime.Proto.DisconnectNoticeMsg"
                    => await HandleDisconnectNoticeMsgAsync(packet),

                nameof(ReconnectMsg) or "PlayHouse.Runtime.Proto.ReconnectMsg"
                    => await HandleReconnectMsgAsync(packet),

                _ => false
            };
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error handling system message {MsgId}", msgId);
            SendErrorReply(packet, BaseErrorCode.InternalError);
            return false;
        }
    }

    #region CreateStageReq - Stage OnCreate/OnPostCreate

    /// <summary>
    /// Stage 생성 요청 처리.
    /// 1. IStage.OnCreate(packet) 호출
    /// 2. IStage.OnPostCreate() 호출
    /// 3. 응답 전송
    /// </summary>
    private async Task<bool> HandleCreateStageReqAsync(RuntimeRoutePacket packet)
    {
        var req = CreateStageReq.Parser.ParseFrom(packet.Payload.DataSpan);
        _logger?.LogDebug("CreateStageReq: StageType={StageType}, PayloadId={PayloadId}",
            req.StageType, req.PayloadId);

        // Create content packet for OnCreate
        var contentPacket = CPacket.Of(req.PayloadId, req.Payload.ToByteArray());

        // 1. Call IStage.OnCreate
        var (result, replyPacket) = await _baseStage.Stage.OnCreate(contentPacket);

        if (!result)
        {
            _logger?.LogWarning("Stage.OnCreate failed for StageId={StageId}", _baseStage.StageId);

            var failRes = new CreateStageRes
            {
                Result = false,
                PayloadId = replyPacket?.MsgId ?? "",
                Payload = replyPacket != null
                    ? ByteString.CopyFrom(replyPacket.Payload.DataSpan)
                    : ByteString.Empty
            };
            SendReply(packet, failRes);
            return false;
        }

        // 2. Call IStage.OnPostCreate
        await _baseStage.Stage.OnPostCreate();
        _baseStage.MarkAsCreated();

        // 3. Send success response
        var successRes = new CreateStageRes
        {
            Result = true,
            PayloadId = replyPacket?.MsgId ?? "",
            Payload = replyPacket != null
                ? ByteString.CopyFrom(replyPacket.Payload.DataSpan)
                : ByteString.Empty
        };
        SendReply(packet, successRes);

        _logger?.LogInformation("Stage created: StageId={StageId}, StageType={StageType}",
            _baseStage.StageId, req.StageType);
        return true;
    }

    #endregion

    #region JoinStageReq - 10-step Authentication Flow

    /// <summary>
    /// Stage 입장 요청 처리 (10단계 인증 플로우).
    ///
    /// 1. XActorSender 생성
    /// 2. IActor 생성 (PlayProducer)
    /// 3. IActor.OnCreate() 호출
    /// 4. IActor.OnAuthenticate(authPacket) 호출
    /// 5. AccountId 검증 (빈 문자열 → 예외)
    /// 6. IActor.OnPostAuthenticate() 호출
    /// 7. IStage.OnJoinStage(actor) 호출
    /// 8. Actor 등록 (BaseStage.AddActor())
    /// 9. IStage.OnPostJoinStage(actor) 호출
    /// 10. 성공 응답 전송
    /// </summary>
    private async Task<bool> HandleJoinStageReqAsync(RuntimeRoutePacket packet)
    {
        var req = JoinStageReq.Parser.ParseFrom(packet.Payload.DataSpan);
        _logger?.LogDebug("JoinStageReq: SessionNid={SessionNid}, Sid={Sid}", req.SessionNid, req.Sid);

        // Stage가 생성되지 않았으면 실패
        if (!_baseStage.IsCreated)
        {
            _logger?.LogWarning("Stage not created: StageId={StageId}", _baseStage.StageId);
            SendJoinStageFailure(packet, BaseErrorCode.StageNotFound, "Stage not created");
            return false;
        }

        // 1. XActorSender 생성
        var routeAccountId = packet.AccountId;
        var actorSender = new XActorSender(
            routeAccountId,
            req.SessionNid,
            req.Sid,
            packet.From,  // apiNid
            _baseStage);

        // 2. IActor 생성
        IActor actor;
        try
        {
            actor = _producer.GetActor(_baseStage.StageType, actorSender);
        }
        catch (KeyNotFoundException ex)
        {
            _logger?.LogError(ex, "Actor type not registered for StageType={StageType}", _baseStage.StageType);
            SendJoinStageFailure(packet, BaseErrorCode.InvalidStageType, "Actor type not registered");
            return false;
        }

        // 3. IActor.OnCreate() 호출
        await actor.OnCreate();
        _logger?.LogDebug("Actor.OnCreate completed for AccountId routing={RouteAccountId}", routeAccountId);

        // 4. IActor.OnAuthenticate(authPacket) 호출
        var authPacket = CPacket.Of(req.PayloadId, req.Payload.ToByteArray());
        var authResult = await actor.OnAuthenticate(authPacket);

        if (!authResult)
        {
            _logger?.LogWarning("Actor.OnAuthenticate failed for AccountId routing={RouteAccountId}", routeAccountId);
            await actor.OnDestroy();
            SendJoinStageFailure(packet, BaseErrorCode.AuthenticationFailed, "Authentication failed");
            return false;
        }

        // 5. AccountId 검증 (빈 문자열 → 예외)
        if (string.IsNullOrEmpty(actorSender.AccountId))
        {
            _logger?.LogError("AccountId not set after OnAuthenticate for routing={RouteAccountId}", routeAccountId);
            await actor.OnDestroy();
            SendJoinStageFailure(packet, BaseErrorCode.InvalidAccountId,
                "AccountId must be set in OnAuthenticate");
            return false;
        }

        // 6. IActor.OnPostAuthenticate() 호출
        await actor.OnPostAuthenticate();
        _logger?.LogDebug("Actor.OnPostAuthenticate completed: AccountId={AccountId}", actorSender.AccountId);

        // 7. IStage.OnJoinStage(actor) 호출
        var joinResult = await _baseStage.Stage.OnJoinStage(actor);

        if (!joinResult)
        {
            _logger?.LogWarning("Stage.OnJoinStage rejected Actor: AccountId={AccountId}", actorSender.AccountId);
            await actor.OnDestroy();
            SendJoinStageFailure(packet, BaseErrorCode.JoinStageRejected, "Stage rejected join");
            return false;
        }

        // 8. Actor 등록
        var baseActor = new BaseActor(actor, actorSender);
        _baseStage.AddActor(baseActor);

        // 9. IStage.OnPostJoinStage(actor) 호출
        await _baseStage.Stage.OnPostJoinStage(actor);

        // 10. 성공 응답 전송
        var successRes = new JoinStageRes
        {
            Result = true,
            PayloadId = "",
            Payload = ByteString.Empty
        };
        SendReply(packet, successRes);

        _logger?.LogInformation("Actor joined Stage: StageId={StageId}, AccountId={AccountId}, RouteId={RouteId}",
            _baseStage.StageId, actorSender.AccountId, routeAccountId);
        return true;
    }

    private void SendJoinStageFailure(RuntimeRoutePacket packet, ushort errorCode, string reason)
    {
        _logger?.LogDebug("JoinStage failure: ErrorCode={ErrorCode}, Reason={Reason}", errorCode, reason);
        var res = new JoinStageRes
        {
            Result = false,
            PayloadId = "",
            Payload = ByteString.Empty
        };
        SendReply(packet, res, errorCode);
    }

    #endregion

    #region CreateJoinStageReq - Stage 생성 + 입장 동시 처리

    /// <summary>
    /// Stage 생성 + 입장 동시 처리.
    /// </summary>
    private async Task<bool> HandleCreateJoinStageReqAsync(RuntimeRoutePacket packet)
    {
        var req = CreateJoinStageReq.Parser.ParseFrom(packet.Payload.DataSpan);
        _logger?.LogDebug("CreateJoinStageReq: StageType={StageType}", req.StageType);

        bool isCreated = false;

        // Stage가 아직 생성되지 않았으면 생성
        if (!_baseStage.IsCreated)
        {
            var createPacket = CPacket.Of(req.CreatePayloadId, req.CreatePayload.ToByteArray());
            var (createResult, createReply) = await _baseStage.Stage.OnCreate(createPacket);

            if (!createResult)
            {
                _logger?.LogWarning("Stage.OnCreate failed in CreateJoinStage");
                var failRes = new CreateJoinStageRes
                {
                    Result = false,
                    IsCreated = false,
                    CreatePayloadId = createReply?.MsgId ?? "",
                    CreatePayload = createReply != null
                        ? ByteString.CopyFrom(createReply.Payload.DataSpan)
                        : ByteString.Empty,
                    JoinPayloadId = "",
                    JoinPayload = ByteString.Empty
                };
                SendReply(packet, failRes);
                return false;
            }

            await _baseStage.Stage.OnPostCreate();
            _baseStage.MarkAsCreated();
            isCreated = true;
        }

        // Join 처리 (JoinStageReq와 동일한 10단계 플로우)
        var routeAccountId = packet.AccountId;
        var actorSender = new XActorSender(
            routeAccountId,
            req.SessionNid,
            req.Sid,
            packet.From,
            _baseStage);

        IActor actor;
        try
        {
            actor = _producer.GetActor(_baseStage.StageType, actorSender);
        }
        catch (KeyNotFoundException ex)
        {
            _logger?.LogError(ex, "Actor type not registered");
            SendCreateJoinStageFailure(packet, isCreated, BaseErrorCode.InvalidStageType);
            return false;
        }

        await actor.OnCreate();

        var authPacket = CPacket.Of(req.JoinPayloadId, req.JoinPayload.ToByteArray());
        var authResult = await actor.OnAuthenticate(authPacket);

        if (!authResult)
        {
            await actor.OnDestroy();
            SendCreateJoinStageFailure(packet, isCreated, BaseErrorCode.AuthenticationFailed);
            return false;
        }

        if (string.IsNullOrEmpty(actorSender.AccountId))
        {
            await actor.OnDestroy();
            SendCreateJoinStageFailure(packet, isCreated, BaseErrorCode.InvalidAccountId);
            return false;
        }

        await actor.OnPostAuthenticate();

        var joinResult = await _baseStage.Stage.OnJoinStage(actor);
        if (!joinResult)
        {
            await actor.OnDestroy();
            SendCreateJoinStageFailure(packet, isCreated, BaseErrorCode.JoinStageRejected);
            return false;
        }

        var baseActor = new BaseActor(actor, actorSender);
        _baseStage.AddActor(baseActor);

        await _baseStage.Stage.OnPostJoinStage(actor);

        var successRes = new CreateJoinStageRes
        {
            Result = true,
            IsCreated = isCreated,
            CreatePayloadId = "",
            CreatePayload = ByteString.Empty,
            JoinPayloadId = "",
            JoinPayload = ByteString.Empty
        };
        SendReply(packet, successRes);

        _logger?.LogInformation("CreateJoinStage success: StageId={StageId}, IsCreated={IsCreated}, AccountId={AccountId}",
            _baseStage.StageId, isCreated, actorSender.AccountId);
        return true;
    }

    private void SendCreateJoinStageFailure(RuntimeRoutePacket packet, bool isCreated, ushort errorCode)
    {
        var res = new CreateJoinStageRes
        {
            Result = false,
            IsCreated = isCreated,
            CreatePayloadId = "",
            CreatePayload = ByteString.Empty,
            JoinPayloadId = "",
            JoinPayload = ByteString.Empty
        };
        SendReply(packet, res, errorCode);
    }

    #endregion

    #region GetOrCreateStageReq - 기존 Stage 반환 또는 생성

    /// <summary>
    /// 기존 Stage 반환 또는 생성.
    /// </summary>
    private async Task<bool> HandleGetOrCreateStageReqAsync(RuntimeRoutePacket packet)
    {
        var req = GetOrCreateStageReq.Parser.ParseFrom(packet.Payload.DataSpan);
        _logger?.LogDebug("GetOrCreateStageReq: StageType={StageType}", req.StageType);

        bool isCreated = false;

        // Stage가 아직 생성되지 않았으면 생성
        if (!_baseStage.IsCreated)
        {
            var createPacket = CPacket.Of(req.CreatePayloadId, req.CreatePayload.ToByteArray());
            var (createResult, createReply) = await _baseStage.Stage.OnCreate(createPacket);

            if (!createResult)
            {
                var failRes = new GetOrCreateStageRes
                {
                    Result = false,
                    IsCreated = false,
                    PayloadId = createReply?.MsgId ?? "",
                    Payload = createReply != null
                        ? ByteString.CopyFrom(createReply.Payload.DataSpan)
                        : ByteString.Empty
                };
                SendReply(packet, failRes);
                return false;
            }

            await _baseStage.Stage.OnPostCreate();
            _baseStage.MarkAsCreated();
            isCreated = true;
        }

        // 성공 응답
        var successRes = new GetOrCreateStageRes
        {
            Result = true,
            IsCreated = isCreated,
            PayloadId = req.JoinPayloadId,
            Payload = req.JoinPayload
        };
        SendReply(packet, successRes);

        _logger?.LogInformation("GetOrCreateStage success: StageId={StageId}, IsCreated={IsCreated}",
            _baseStage.StageId, isCreated);
        return true;
    }

    #endregion

    #region DisconnectNoticeMsg - 연결 끊김 알림

    /// <summary>
    /// 연결 끊김 알림 처리.
    /// IStage.OnConnectionChanged(actor, false) 호출.
    /// </summary>
    private async Task<bool> HandleDisconnectNoticeMsgAsync(RuntimeRoutePacket packet)
    {
        var msg = DisconnectNoticeMsg.Parser.ParseFrom(packet.Payload.DataSpan);
        _logger?.LogDebug("DisconnectNoticeMsg: AccountId={AccountId}, Sid={Sid}", msg.AccountId, msg.Sid);

        var baseActor = _baseStage.GetActor(msg.AccountId);
        if (baseActor == null)
        {
            _logger?.LogWarning("Actor not found for disconnect: AccountId={AccountId}", msg.AccountId);
            return false;
        }

        // IStage.OnConnectionChanged(actor, false) 호출
        await _baseStage.Stage.OnConnectionChanged(baseActor.Actor, false);

        _logger?.LogInformation("Actor disconnected: StageId={StageId}, AccountId={AccountId}",
            _baseStage.StageId, baseActor.AccountId);
        return true;
    }

    #endregion

    #region ReconnectMsg - 재연결 처리

    /// <summary>
    /// 재연결 처리.
    /// 세션 정보 업데이트 후 IStage.OnConnectionChanged(actor, true) 호출.
    /// </summary>
    private async Task<bool> HandleReconnectMsgAsync(RuntimeRoutePacket packet)
    {
        var msg = ReconnectMsg.Parser.ParseFrom(packet.Payload.DataSpan);
        _logger?.LogDebug("ReconnectMsg: AccountId={AccountId}, Sid={Sid}, SessionNid={SessionNid}",
            msg.AccountId, msg.Sid, msg.SessionNid);

        var baseActor = _baseStage.GetActor(msg.AccountId);
        if (baseActor == null)
        {
            _logger?.LogWarning("Actor not found for reconnect: AccountId={AccountId}", msg.AccountId);
            SendErrorReply(packet, BaseErrorCode.ActorNotFound);
            return false;
        }

        // 세션 정보 업데이트
        baseActor.ActorSender.Update(msg.SessionNid, msg.Sid, msg.ApiNid);

        // IStage.OnConnectionChanged(actor, true) 호출
        await _baseStage.Stage.OnConnectionChanged(baseActor.Actor, true);

        _logger?.LogInformation("Actor reconnected: StageId={StageId}, AccountId={AccountId}",
            _baseStage.StageId, baseActor.AccountId);
        return true;
    }

    #endregion

    #region Helper Methods

    /// <summary>
    /// 시스템 메시지인지 확인합니다.
    /// </summary>
    public static bool IsSystemMessage(string msgId)
    {
        return msgId.StartsWith("PlayHouse.Runtime.Proto.") ||
               msgId == nameof(CreateStageReq) ||
               msgId == nameof(JoinStageReq) ||
               msgId == nameof(CreateJoinStageReq) ||
               msgId == nameof(GetOrCreateStageReq) ||
               msgId == nameof(DisconnectNoticeMsg) ||
               msgId == nameof(ReconnectMsg);
    }

    private void SendReply(RuntimeRoutePacket packet, IMessage response, ushort errorCode = 0)
    {
        if (packet.MsgSeq == 0) return; // Not a request

        var replyPacket = RuntimeRoutePacket.CreateReply(
            packet,
            _baseStage.StageSender.Nid,
            _baseStage.StageSender.ServiceId,
            response.GetType().Name,
            response.ToByteArray(),
            errorCode);

        _baseStage.StageSender.SendReply(packet.From, replyPacket);
    }

    private void SendErrorReply(RuntimeRoutePacket packet, ushort errorCode)
    {
        if (packet.MsgSeq == 0) return;

        var replyPacket = packet.CreateErrorReply(errorCode);
        _baseStage.StageSender.SendReply(packet.From, replyPacket);
    }

    #endregion
}
