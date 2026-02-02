#nullable enable

using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using PlayHouse.Runtime.ServerMesh.Message;

namespace PlayHouse.Abstractions.System;

/// <summary>
/// 시스템 메시지 디스패처.
/// </summary>
/// <remarks>
/// 시스템 레벨 메시지(서버 디스커버리, 헬스체크 등)를 처리합니다.
/// </remarks>
public sealed class SystemDispatcher : ISystemHandlerRegister
{
    private readonly ConcurrentDictionary<string, SystemHandler> _handlers = new();
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
    public void Add(string msgId, SystemHandler handler)
    {
        _handlers[msgId] = handler;
        _logger?.LogDebug("System handler registered: {MsgId}", msgId);
    }

    /// <summary>
    /// 시스템 메시지를 디스패치합니다.
    /// </summary>
    /// <param name="packet">라우트 패킷.</param>
    /// <param name="link">응답 및 메시지 전송용 sender.</param>
    /// <returns>처리 성공 여부.</returns>
    public async Task<bool> DispatchAsync(RoutePacket packet, ILink link)
    {
        var msgId = packet.MsgId;

        if (_handlers.TryGetValue(msgId, out var handler))
        {
            try
            {
                var routePacket = new SystemPacket(packet);
                await handler(routePacket, link);
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

    /// <summary>
    /// 시스템 메시지용 IPacket 래퍼.
    /// </summary>
    private sealed class SystemPacket : IPacket
    {
        private readonly RoutePacket _routePacket;

        public SystemPacket(RoutePacket routePacket)
        {
            _routePacket = routePacket;
        }

        public string MsgId => _routePacket.MsgId;
        public IPayload Payload => _routePacket.Payload;

        // RoutePacket의 소유권은 호출자에게 있으므로 여기서 Dispose하지 않음
        public void Dispose() { }
    }
}
