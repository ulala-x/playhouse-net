#nullable enable

namespace PlayHouse.Connector;

/// <summary>
/// Connector 설정
/// </summary>
public sealed class ConnectorConfig
{
    /// <summary>
    /// 서버 호스트 주소
    /// </summary>
    public string Host { get; set; } = string.Empty;

    /// <summary>
    /// 서버 포트
    /// </summary>
    public int Port { get; set; }

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
