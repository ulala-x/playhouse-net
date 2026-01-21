#nullable enable

using Google.Protobuf;
using Microsoft.Extensions.Logging;
using PlayHouse.Abstractions;
using PlayHouse.Core.Shared;
using PlayHouse.Runtime.ServerMesh.Message;
using PlayHouse.Runtime.Proto;

namespace PlayHouse.Core.Play.Base.Command;

/// <summary>
/// GetOrCreateStageReq 처리 명령 (기존 Stage 반환 또는 생성).
/// </summary>
internal sealed class GetOrCreateStageCmd(ILogger logger) : IBaseStageCmd
{
    public async Task Execute(BaseStage baseStage, RoutePacket packet)
    {
        var req = GetOrCreateStageReq.Parser.ParseFrom(packet.Payload.DataSpan);
        logger.LogDebug("GetOrCreateStageReq: StageType={StageType}", req.StageType);

        bool isCreated = false;
        IPacket? onCreateReply = null;

        // Stage가 아직 생성되지 않았으면 생성
        if (!baseStage.IsCreated)
        {
            // Zero-copy: use ByteString.Memory directly
            var createPacket = CPacket.Of(req.CreatePayloadId, new MemoryPayload(req.CreatePayload.Memory));
            var (createSuccess, createReply) = await baseStage.CreateStage(req.StageType, createPacket);

            if (!createSuccess)
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
                baseStage.Reply(CPacket.Of(failRes));
                return;
            }

            await baseStage.OnPostCreate();
            isCreated = true;
            onCreateReply = createReply;
        }

        // 성공 응답: 새로 생성된 경우 OnCreate reply 반환, 기존 stage인 경우 빈 응답
        var successRes = new GetOrCreateStageRes
        {
            Result = true,
            IsCreated = isCreated,
            PayloadId = onCreateReply?.MsgId ?? "",
            Payload = onCreateReply != null
                ? ByteString.CopyFrom(onCreateReply.Payload.DataSpan)
                : ByteString.Empty
        };
        baseStage.Reply(CPacket.Of(successRes));

        logger.LogInformation("GetOrCreateStage success: StageId={StageId}, IsCreated={IsCreated}",
            baseStage.StageId, isCreated);
    }
}
