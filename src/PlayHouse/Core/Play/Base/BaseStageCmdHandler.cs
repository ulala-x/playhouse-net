#nullable enable

using Microsoft.Extensions.Logging;
using PlayHouse.Runtime.ServerMesh.Message;

namespace PlayHouse.Core.Play.Base;

/// <summary>
/// Stage 시스템 메시지 디스패처.
/// </summary>
/// <remarks>
/// Command Pattern을 사용하여 각 시스템 메시지를 해당 Command 클래스로 위임합니다.
/// </remarks>
internal sealed class BaseStageCmdHandler(ILogger logger)
{
    private readonly Dictionary<string, IBaseStageCmd> _commands = new();

    /// <summary>
    /// 명령을 등록합니다.
    /// </summary>
    public void Register(string msgId, IBaseStageCmd command)
    {
        if (!_commands.TryAdd(msgId, command))
        {
            throw new InvalidOperationException($"Command already registered: {msgId}");
        }
    }

    /// <summary>
    /// 시스템 메시지를 처리합니다.
    /// </summary>
    public async Task<bool> HandleAsync(string msgId, RoutePacket packet, BaseStage baseStage)
    {
        if (_commands.TryGetValue(msgId, out var command))
        {
            try
            {
                await command.Execute(baseStage, packet);
                return true;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error executing command {MsgId}", msgId);
                if (packet.MsgSeq != 0)
                {
                    baseStage.StageSender.SetCurrentHeader(packet.Header);
                    baseStage.StageSender.Reply(500); // Internal error
                }
                return false;
            }
        }

        logger.LogWarning("Command not registered: {MsgId}", msgId);
        return false;
    }

    /// <summary>
    /// 시스템 메시지인지 확인합니다.
    /// </summary>
    public bool IsRegistered(string msgId) => _commands.ContainsKey(msgId);
}
