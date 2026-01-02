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
    /// SSL/TLS 사용 여부 (TCP의 경우 TLS, WebSocket의 경우 wss://)
    /// </summary>
    public bool UseSsl { get; set; }

    /// <summary>
    /// 연결 유휴 타임아웃 (밀리초, 기본값 30000)
    /// </summary>
    public int ConnectionIdleTimeoutMs { get; set; } = 30000;

    /// <summary>
    /// 하트비트 주기 (밀리초, 기본값 10000)
    /// </summary>
    public int HeartBeatIntervalMs { get; set; } = 10000;

    /// <summary>
    /// 하트비트 타임아웃 (밀리초, 기본값 30000)
    /// 마지막 메시지 수신 후 이 시간이 지나면 연결 끊김으로 판단하여 OnDisconnect 발생
    /// 0이면 비활성화
    /// </summary>
    public int HeartbeatTimeoutMs { get; set; } = 30000;

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

    /// <summary>
    /// 콜백을 메인 스레드 큐에 추가할지 여부 (Unity용)
    /// </summary>
    /// <remarks>
    /// - true: 모든 콜백을 큐에 추가하고 MainThreadAction()에서 실행 (Unity 권장)
    /// - false: 콜백을 네트워크 스레드에서 즉시 실행 (고성능 시나리오)
    /// 기본값: false (즉시 실행)
    /// </remarks>
    public bool UseMainThreadCallback { get; set; } = false;
}
