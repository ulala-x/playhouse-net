#nullable enable

using System;
using PlayHouse.Connector.Protocol;

namespace PlayHouse.Connector;

/// <summary>
/// Connector 예외
/// </summary>
public sealed class ConnectorException : Exception
{
    /// <summary>
    /// Stage ID (0이면 Stage 없음)
    /// </summary>
    public long StageId { get; }

    /// <summary>
    /// 에러 코드
    /// </summary>
    public ushort ErrorCode { get; }

    /// <summary>
    /// 요청 패킷
    /// </summary>
    public IPacket Request { get; }

    /// <summary>
    /// 메시지 시퀀스
    /// </summary>
    public ushort MsgSeq { get; }

    /// <summary>
    /// ConnectorException 생성자
    /// </summary>
    public ConnectorException(long stageId, ushort errorCode, IPacket request, ushort msgSeq)
        : base($"Connector error: {errorCode} for message {request.MsgId}")
    {
        StageId = stageId;
        ErrorCode = errorCode;
        Request = request;
        MsgSeq = msgSeq;
    }
}
