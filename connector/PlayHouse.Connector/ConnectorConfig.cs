#nullable enable

namespace PlayHouse.Connector;

/// <summary>
/// Connector 설정
/// </summary>
/// <remarks>
/// 서버 접속 정보(Host, Port)는 Connect() 호출 시 전달합니다.
/// Connector 인스턴스는 여러 서버에 접속/해제가 가능하도록 설계되었습니다.
/// </remarks>
public sealed class ConnectorConfig
{
    /// <summary>
    /// WebSocket 사용 여부 (false면 TCP)
    /// </summary>
    public bool UseWebsocket { get; set; }

    /// <summary>
    /// 연결 유휴 타임아웃 (밀리초, 기본값 30000)
    /// </summary>
    public int ConnectionIdleTimeoutMs { get; set; } = 30000;

    /// <summary>
    /// 하트비트 주기 (밀리초, 기본값 10000)
    /// </summary>
    public int HeartBeatIntervalMs { get; set; } = 10000;

    /// <summary>
    /// 요청 타임아웃 (밀리초, 기본값 30000)
    /// </summary>
    public int RequestTimeoutMs { get; set; } = 30000;

    /// <summary>
    /// 응답 시간 로깅 활성화
    /// </summary>
    public bool EnableLoggingResponseTime { get; set; }

    /// <summary>
    /// 추적 로깅 활성화
    /// </summary>
    public bool TurnOnTrace { get; set; }
}
