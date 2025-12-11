#nullable enable

using PlayHouse.Abstractions;

namespace PlayHouse.Bootstrap;

/// <summary>
/// API Server 설정 옵션.
/// </summary>
public sealed class ApiServerOption
{
    /// <summary>
    /// 서비스 타입 (기본값: ServiceType.Api).
    /// </summary>
    public ServiceType ServiceType { get; set; } = ServiceType.Api;

    /// <summary>
    /// 서비스 식별자 (ServiceType의 ushort 값).
    /// </summary>
    public ushort ServiceId => (ushort)ServiceType;

    /// <summary>
    /// 서버 인스턴스 ID.
    /// </summary>
    public ushort ServerId { get; set; } = 1;

    /// <summary>
    /// NetMQ 서버 간 통신용 바인드 주소.
    /// 예: "tcp://0.0.0.0:5100"
    /// </summary>
    public string BindEndpoint { get; set; } = "tcp://0.0.0.0:5100";

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
        if (ServiceType == 0)
            throw new InvalidOperationException("ServiceType must be set");

        if (ServerId == 0)
            throw new InvalidOperationException("ServerId must be greater than 0");

        if (string.IsNullOrEmpty(BindEndpoint))
            throw new InvalidOperationException("BindEndpoint is required");
    }
}
