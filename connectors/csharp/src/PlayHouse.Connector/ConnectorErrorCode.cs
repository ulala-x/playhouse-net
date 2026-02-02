#nullable enable

namespace PlayHouse.Connector;

/// <summary>
/// Connector 에러 코드
/// </summary>
public enum ConnectorErrorCode : ushort
{
    /// <summary>
    /// 연결 끊김 상태에서 요청
    /// </summary>
    Disconnected = 60201,

    /// <summary>
    /// 요청 타임아웃
    /// </summary>
    RequestTimeout = 60202,

    /// <summary>
    /// 미인증 상태에서 요청
    /// </summary>
    Unauthenticated = 60203
}
