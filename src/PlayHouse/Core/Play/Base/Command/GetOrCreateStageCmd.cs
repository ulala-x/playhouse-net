#nullable enable

using Google.Protobuf;
using Microsoft.Extensions.Logging;
using PlayHouse.Core.Shared;
using PlayHouse.Runtime.ServerMesh.Message;
using PlayHouse.Runtime.Proto;

namespace PlayHouse.Core.Play.Base.Command;

/// <summary>
/// GetOrCreateStageReq 처리 명령 (기존 Stage 반환 또는 생성).
/// </summary>
internal sealed class GetOrCreateStageCmd : IBaseStageCmd
{
    private readonly ILogger? _logger;

    public GetOrCreateStageCmd(ILogger? logger = null)
    {
        _logger = logger;
    }

    public async Task Execute(BaseStage baseStage, RuntimeRoutePacket packet)
    {
        var req = GetOrCreateStageReq.Parser.ParseFrom(packet.Payload.DataSpan);
        _logger?.LogDebug("GetOrCreateStageReq: StageType={StageType}", req.StageType);

        bool isCreated = false;

        // Stage가 아직 생성되지 않았으면 생성
        if (!baseStage.IsCreated)
        {
            var createPacket = CPacket.Of(req.CreatePayloadId, req.CreatePayload.ToByteArray());
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
        }

        // 성공 응답
        var successRes = new GetOrCreateStageRes
        {
            Result = true,
            IsCreated = isCreated,
            PayloadId = req.JoinPayloadId,
            Payload = req.JoinPayload
        };
        baseStage.Reply(CPacket.Of(successRes));

        _logger?.LogInformation("GetOrCreateStage success: StageId={StageId}, IsCreated={IsCreated}",
            baseStage.StageId, isCreated);
    }
}
