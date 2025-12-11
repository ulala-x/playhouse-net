#nullable enable

using System.Collections.Concurrent;
using Google.Protobuf;
using Microsoft.Extensions.Logging;
using PlayHouse.Runtime.Message;

namespace PlayHouse.Abstractions.System;

/// <summary>
/// 시스템 메시지 디스패처.
/// </summary>
/// <remarks>
/// 시스템 레벨 메시지(서버 디스커버리, 헬스체크 등)를 처리합니다.
/// </remarks>
public sealed class SystemDispatcher : ISystemHandlerRegister
{
    private readonly ConcurrentDictionary<string, Func<byte[], Task>> _handlers = new();
    private readonly ILogger? _logger;

    /// <summary>
    /// 새 SystemDispatcher 인스턴스를 생성합니다.
    /// </summary>
    /// <param name="logger">로거.</param>
    public SystemDispatcher(ILogger? logger = null)
    {
        _logger = logger;
    }

    /// <summary>
    /// 시스템 컨트롤러를 등록합니다.
    /// </summary>
    /// <param name="controller">시스템 컨트롤러.</param>
    public void Register(ISystemController controller)
    {
        controller.Handles(this);
        _logger?.LogDebug("SystemController registered: {Type}", controller.GetType().Name);
    }

    /// <inheritdoc/>
    public void Add<TMessage>(Func<TMessage, Task> handler) where TMessage : IMessage, new()
    {
        var msgId = typeof(TMessage).FullName ?? typeof(TMessage).Name;

        _handlers[msgId] = async (payload) =>
        {
            var message = new TMessage();
            message.MergeFrom(payload);
            await handler(message);
        };

        _logger?.LogDebug("System handler registered: {MsgId}", msgId);
    }

    /// <summary>
    /// 시스템 메시지를 디스패치합니다.
    /// </summary>
    /// <param name="packet">라우트 패킷.</param>
    /// <returns>처리 성공 여부.</returns>
    public async Task<bool> DispatchAsync(RuntimeRoutePacket packet)
    {
        var msgId = packet.MsgId;

        if (_handlers.TryGetValue(msgId, out var handler))
        {
            try
            {
                await handler(packet.Payload.DataSpan.ToArray());
                return true;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error handling system message: {MsgId}", msgId);
                return false;
            }
        }

        _logger?.LogWarning("No handler for system message: {MsgId}", msgId);
        return false;
    }

    /// <summary>
    /// 시스템 메시지인지 확인합니다.
    /// </summary>
    /// <param name="msgId">메시지 ID.</param>
    /// <returns>시스템 메시지 여부.</returns>
    public bool IsSystemMessage(string msgId)
    {
        return _handlers.ContainsKey(msgId) ||
               msgId.StartsWith("PlayHouse.Runtime.Proto.System");
    }

    /// <summary>
    /// 등록된 핸들러 수.
    /// </summary>
    public int HandlerCount => _handlers.Count;
}
