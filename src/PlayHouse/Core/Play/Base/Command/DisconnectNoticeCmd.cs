#nullable enable

using Microsoft.Extensions.Logging;
using PlayHouse.Runtime.ServerMesh.Message;
using PlayHouse.Runtime.Proto;

namespace PlayHouse.Core.Play.Base.Command;

/// <summary>
/// DisconnectNoticeMsg 처리 명령 (연결 끊김 알림).
/// </summary>
internal sealed class DisconnectNoticeCmd : IBaseStageCmd
{
    private readonly ILogger? _logger;

    public DisconnectNoticeCmd(ILogger? logger = null)
    {
        _logger = logger;
    }

    public async Task Execute(BaseStage baseStage, RuntimeRoutePacket packet)
    {
        var msg = DisconnectNoticeMsg.Parser.ParseFrom(packet.Payload.DataSpan);
        _logger?.LogDebug("DisconnectNoticeMsg: AccountId={AccountId}, Sid={Sid}", msg.AccountId, msg.Sid);

        var baseActor = baseStage.GetActor(msg.AccountId.ToString());
        if (baseActor == null)
        {
            _logger?.LogWarning("Actor not found for disconnect: AccountId={AccountId}", msg.AccountId);
            return;
        }

        // IStage.OnConnectionChanged(actor, false) 호출
        await baseStage.Stage.OnConnectionChanged(baseActor.Actor, false);

        _logger?.LogInformation("Actor disconnected: StageId={StageId}, AccountId={AccountId}",
            baseStage.StageId, baseActor.AccountId);
    }
}
