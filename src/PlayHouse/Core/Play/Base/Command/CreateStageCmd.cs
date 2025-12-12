#nullable enable

using Google.Protobuf;
using Microsoft.Extensions.Logging;
using PlayHouse.Abstractions;
using PlayHouse.Core.Shared;
using PlayHouse.Runtime.ServerMesh.Message;
using PlayHouse.Runtime.Proto;

namespace PlayHouse.Core.Play.Base.Command;

/// <summary>
/// CreateStageReq 처리 명령.
/// </summary>
internal sealed class CreateStageCmd : IBaseStageCmd
{
    private readonly IPlayDispatcher _dispatcher;
    private readonly ILogger? _logger;

    public CreateStageCmd(IPlayDispatcher dispatcher, ILogger? logger = null)
    {
        _dispatcher = dispatcher;
        _logger = logger;
    }

    public async Task Execute(BaseStage baseStage, RuntimeRoutePacket packet)
    {
        var req = CreateStageReq.Parser.ParseFrom(packet.Payload.DataSpan);
        _logger?.LogDebug("CreateStageReq: StageType={StageType}, PayloadId={PayloadId}",
            req.StageType, req.PayloadId);

        var contentPacket = CPacket.Of(req.PayloadId, req.Payload.ToByteArray());

        var (success, replyPacket) = await baseStage.CreateStage(req.StageType, contentPacket);

        if (!success)
        {
            _logger?.LogWarning("Stage.OnCreate failed for StageId={StageId}", baseStage.StageId);
            var failRes = new CreateStageRes
            {
                Result = false,
                PayloadId = replyPacket?.MsgId ?? "",
                Payload = replyPacket != null
                    ? ByteString.CopyFrom(replyPacket.Payload.DataSpan)
                    : ByteString.Empty
            };
            baseStage.Reply(CPacket.Of(failRes));
            return;
        }

        await baseStage.OnPostCreate();

        var successRes = new CreateStageRes
        {
            Result = true,
            PayloadId = replyPacket?.MsgId ?? "",
            Payload = replyPacket != null
                ? ByteString.CopyFrom(replyPacket.Payload.DataSpan)
                : ByteString.Empty
        };
        baseStage.Reply(CPacket.Of(successRes));

        _logger?.LogInformation("Stage created: StageId={StageId}, StageType={StageType}",
            baseStage.StageId, req.StageType);
    }
}
