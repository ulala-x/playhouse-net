#nullable enable

using Google.Protobuf;
using Microsoft.Extensions.Logging;
using PlayHouse.Abstractions;
using PlayHouse.Abstractions.Play;
using PlayHouse.Core.Shared;
using PlayHouse.Runtime.ServerMesh.Message;
using PlayHouse.Runtime.Proto;

namespace PlayHouse.Core.Play.Base.Command;

/// <summary>
/// CreateJoinStageReq 처리 명령 (Stage 생성 + 입장 동시 처리).
/// </summary>
internal sealed class CreateJoinStageCmd(PlayProducer producer, IPlayDispatcher dispatcher, ILogger? logger = null)
    : IBaseStageCmd
{
    private readonly IPlayDispatcher _dispatcher = dispatcher;

    public async Task Execute(BaseStage baseStage, RuntimeRoutePacket packet)
    {
        var req = CreateJoinStageReq.Parser.ParseFrom(packet.Payload.DataSpan);
        logger?.LogDebug("CreateJoinStageReq: StageType={StageType}", req.StageType);

        bool isCreated = false;

        // Stage가 아직 생성되지 않았으면 생성
        if (!baseStage.IsCreated)
        {
            var createPacket = CPacket.Of(req.CreatePayloadId, req.CreatePayload.ToByteArray());
            var (createSuccess, createReply) = await baseStage.CreateStage(req.StageType, createPacket);

            if (!createSuccess)
            {
                logger?.LogWarning("Stage.OnCreate failed in CreateJoinStage");
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
                baseStage.Reply(CPacket.Of(failRes));
                return;
            }

            await baseStage.OnPostCreate();
            isCreated = true;
        }

        // Join 처리
        var authPacket = CPacket.Of(req.JoinPayloadId, req.JoinPayload.ToByteArray());
        var (joinSuccess, errorCode, actor) = await baseStage.JoinActor(
            req.SessionNid,
            req.Sid,
            packet.From,
            authPacket,
            producer);

        if (!joinSuccess)
        {
            var failRes = new CreateJoinStageRes
            {
                Result = false,
                IsCreated = isCreated,
                CreatePayloadId = "",
                CreatePayload = ByteString.Empty,
                JoinPayloadId = "",
                JoinPayload = ByteString.Empty
            };
            baseStage.Reply(CPacket.Of(failRes));
            return;
        }

        var successRes = new CreateJoinStageRes
        {
            Result = true,
            IsCreated = isCreated,
            CreatePayloadId = "",
            CreatePayload = ByteString.Empty,
            JoinPayloadId = "",
            JoinPayload = ByteString.Empty
        };
        baseStage.Reply(CPacket.Of(successRes));

        logger?.LogInformation("CreateJoinStage success: StageId={StageId}, IsCreated={IsCreated}, AccountId={AccountId}",
            baseStage.StageId, isCreated, actor?.AccountId);
    }
}
