#nullable enable

using Microsoft.Extensions.Logging;
using PlayHouse.Abstractions.Play;
using PlayHouse.Runtime.Message;

namespace PlayHouse.Core.Play.Base;

/// <summary>
/// Stage 시스템 메시지 핸들러.
/// </summary>
/// <remarks>
/// 내부 시스템 메시지를 처리합니다.
/// 실제 메시지 타입은 proto 정의에 따라 확장됩니다.
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
            // 시스템 메시지 처리 - proto 메시지에 따라 확장
            // 현재는 기본 처리만 수행
            _logger?.LogDebug("System message received: {MsgId}", msgId);

            // 알려진 시스템 메시지 처리
            if (msgId.StartsWith("PlayHouse.Proto."))
            {
                return await HandleInternalMessageAsync(msgId, packet);
            }

            return false;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error handling system message {MsgId}", msgId);
            return false;
        }
    }

    private Task<bool> HandleInternalMessageAsync(string msgId, RuntimeRoutePacket packet)
    {
        // 내부 프레임워크 메시지 처리
        // 실제 구현은 proto 메시지 정의에 따라 확장
        _logger?.LogDebug("Internal message: {MsgId}", msgId);
        return Task.FromResult(true);
    }

    /// <summary>
    /// 시스템 메시지인지 확인합니다.
    /// </summary>
    /// <param name="msgId">메시지 ID.</param>
    /// <returns>시스템 메시지 여부.</returns>
    public static bool IsSystemMessage(string msgId)
    {
        return msgId.StartsWith("PlayHouse.Proto.") ||
               msgId.StartsWith("PlayHouse.Runtime.Proto.");
    }
}
