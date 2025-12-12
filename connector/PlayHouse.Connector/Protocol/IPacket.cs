#nullable enable

using System;

namespace PlayHouse.Connector.Protocol;

/// <summary>
/// 패킷 인터페이스 - 메시지 ID와 페이로드를 포함
/// </summary>
public interface IPacket : IDisposable
{
    /// <summary>
    /// 메시지 ID (타입명)
    /// </summary>
    string MsgId { get; }

    /// <summary>
    /// 페이로드 데이터
    /// </summary>
    IPayload Payload { get; }
}
