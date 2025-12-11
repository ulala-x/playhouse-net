#nullable enable

namespace PlayHouse.Bootstrap;

/// <summary>
/// Play Server 설정 옵션.
/// </summary>
public sealed class PlayServerOption
{
    /// <summary>
    /// 서비스 식별자 (기본값: 1).
    /// </summary>
    public ushort ServiceId { get; set; } = 1;

    /// <summary>
    /// 서버 인스턴스 ID.
    /// </summary>
    public ushort ServerId { get; set; } = 1;

    /// <summary>
    /// NetMQ 서버 간 통신용 바인드 주소.
    /// 예: "tcp://0.0.0.0:5000"
    /// </summary>
    public string BindEndpoint { get; set; } = "tcp://0.0.0.0:5000";

    /// <summary>
    /// 클라이언트 TCP 연결용 바인드 주소.
    /// 예: "tcp://0.0.0.0:6000"
    /// </summary>
    public string ClientEndpoint { get; set; } = "tcp://0.0.0.0:6000";

    /// <summary>
    /// 요청 타임아웃 (밀리초, 기본값: 30000).
    /// </summary>
    public int RequestTimeoutMs { get; set; } = 30000;

    /// <summary>
    /// 서버 NID (자동 계산).
    /// </summary>
    public string Nid => $"{ServiceId}:{ServerId}";

    /// <summary>
    /// 설정 유효성 검증.
    /// </summary>
    public void Validate()
    {
        if (ServiceId == 0)
            throw new InvalidOperationException("ServiceId must be greater than 0");

        if (ServerId == 0)
            throw new InvalidOperationException("ServerId must be greater than 0");

        if (string.IsNullOrEmpty(BindEndpoint))
            throw new InvalidOperationException("BindEndpoint is required");

        if (string.IsNullOrEmpty(ClientEndpoint))
            throw new InvalidOperationException("ClientEndpoint is required");
    }
}
