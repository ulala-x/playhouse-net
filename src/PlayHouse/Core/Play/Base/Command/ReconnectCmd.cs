#nullable enable

using Microsoft.Extensions.Logging;
using PlayHouse.Abstractions;
using PlayHouse.Runtime.ServerMesh.Message;
using PlayHouse.Runtime.Proto;

namespace PlayHouse.Core.Play.Base.Command;

/// <summary>
/// ReconnectMsg 처리 명령 (재연결 처리).
/// </summary>
internal sealed class ReconnectCmd : IBaseStageCmd
{
    private readonly ILogger? _logger;

    public ReconnectCmd(ILogger? logger = null)
    {
        _logger = logger;
    }

    public async Task Execute(BaseStage baseStage, RoutePacket packet)
    {
        var msg = ReconnectMsg.Parser.ParseFrom(packet.Payload.DataSpan);
        _logger?.LogDebug("ReconnectMsg: AccountId={AccountId}, Sid={Sid}, SessionNid={SessionNid}",
            msg.AccountId, msg.Sid, msg.SessionNid);

        var baseActor = baseStage.GetActor(msg.AccountId.ToString());
        if (baseActor == null)
        {
            _logger?.LogWarning("Actor not found for reconnect: AccountId={AccountId}", msg.AccountId);
            return;
        }

        // 세션 정보 업데이트
        baseActor.ActorSender.Update(msg.SessionNid, msg.Sid, msg.ApiNid);

        // IStage.OnConnectionChanged(actor, true) 호출
        await baseStage.Stage.OnConnectionChanged(baseActor.Actor, true);

        _logger?.LogInformation("Actor reconnected: StageId={StageId}, AccountId={AccountId}",
            baseStage.StageId, baseActor.AccountId);
    }
}
