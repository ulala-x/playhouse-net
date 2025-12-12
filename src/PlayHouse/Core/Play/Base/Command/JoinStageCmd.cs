#nullable enable

using Google.Protobuf;
using Microsoft.Extensions.Logging;
using PlayHouse.Abstractions.Play;
using PlayHouse.Core.Shared;
using PlayHouse.Runtime.ServerMesh.Message;
using PlayHouse.Runtime.Proto;

namespace PlayHouse.Core.Play.Base.Command;

/// <summary>
/// JoinStageReq 처리 명령 (10단계 인증 플로우).
/// </summary>
internal sealed class JoinStageCmd : IBaseStageCmd
{
    private readonly PlayProducer _producer;
    private readonly ILogger? _logger;

    public JoinStageCmd(PlayProducer producer, ILogger? logger = null)
    {
        _producer = producer;
        _logger = logger;
    }

    public async Task Execute(BaseStage baseStage, RuntimeRoutePacket packet)
    {
        var req = JoinStageReq.Parser.ParseFrom(packet.Payload.DataSpan);
        _logger?.LogDebug("JoinStageReq: SessionNid={SessionNid}, Sid={Sid}", req.SessionNid, req.Sid);

        if (!baseStage.IsCreated)
        {
            _logger?.LogWarning("Stage not created: StageId={StageId}", baseStage.StageId);
            baseStage.Reply(CPacket.Of(new JoinStageRes { Result = false }));
            return;
        }

        var authPacket = CPacket.Of(req.PayloadId, req.Payload.ToByteArray());
        var (success, errorCode, actor) = await baseStage.JoinActor(
            req.SessionNid,
            req.Sid,
            packet.From,
            authPacket,
            _producer);

        if (!success)
        {
            _logger?.LogWarning("JoinStage failed: ErrorCode={ErrorCode}", errorCode);
            baseStage.Reply(CPacket.Of(new JoinStageRes { Result = false }));
            return;
        }

        var successRes = new JoinStageRes
        {
            Result = true,
            PayloadId = "",
            Payload = ByteString.Empty
        };
        baseStage.Reply(CPacket.Of(successRes));

        _logger?.LogInformation("Actor joined Stage: StageId={StageId}, AccountId={AccountId}",
            baseStage.StageId, actor?.AccountId);
    }
}
