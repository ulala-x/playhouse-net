#nullable enable

using PlayHouse.Abstractions;
using PlayHouse.Core.Messaging;
using PlayHouse.Runtime.ServerMesh.Communicator;
using PlayHouse.Runtime.ServerMesh.Discovery;

namespace PlayHouse.Core.Shared;

/// <summary>
/// System message handler용 ILink 구현체.
/// </summary>
/// <remarks>
/// 시스템 메시지 핸들러에서 Reply 및 다른 서버로 메시지 전송을 지원합니다.
/// </remarks>
internal sealed class XSystemLink : XLink
{
    /// <summary>
    /// 새 XSystemLink 인스턴스를 생성합니다.
    /// </summary>
    public XSystemLink(
        IClientCommunicator communicator,
        RequestCache requestCache,
        IServerInfoCenter serverInfoCenter,
        ServerType serverType,
        ushort serviceId,
        string serverId,
        int requestTimeoutMs = 30000)
        : base(communicator, requestCache, serverInfoCenter, serverType, serviceId, serverId, requestTimeoutMs)
    {
    }
}
