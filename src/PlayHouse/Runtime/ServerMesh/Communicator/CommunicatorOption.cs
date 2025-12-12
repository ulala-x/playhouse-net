#nullable enable

namespace PlayHouse.Runtime.ServerMesh.Communicator;

/// <summary>
/// Communicator 설정 옵션.
/// </summary>
public sealed class CommunicatorOption
{
    /// <summary>
    /// 서비스 ID (1 = Play, 2 = API).
    /// </summary>
    public ushort ServiceId { get; set; }

    /// <summary>
    /// 서버 인스턴스 ID.
    /// </summary>
    public ushort ServerId { get; set; }

    /// <summary>
    /// NetMQ 바인드 주소 (예: "tcp://*:5000").
    /// </summary>
    public string BindEndpoint { get; set; } = "tcp://*:5000";

    /// <summary>
    /// 요청 타임아웃 (밀리초).
    /// </summary>
    public int RequestTimeoutMs { get; set; } = 30000;

    /// <summary>
    /// 송신 큐 크기.
    /// </summary>
    public int SendQueueSize { get; set; } = 10000;

    /// <summary>
    /// 송신 High Water Mark.
    /// </summary>
    public int SendHighWatermark { get; set; } = 1000;

    /// <summary>
    /// 수신 High Water Mark.
    /// </summary>
    public int ReceiveHighWatermark { get; set; } = 1000;

    /// <summary>
    /// TCP Keepalive 활성화 여부.
    /// </summary>
    public bool TcpKeepalive { get; set; } = true;

    /// <summary>
    /// 서버 디스커버리 갱신 주기 (밀리초).
    /// </summary>
    public int DiscoveryRefreshIntervalMs { get; set; } = 3000;

    /// <summary>
    /// Node ID를 생성합니다.
    /// </summary>
    public string GetNid() => $"{ServiceId}:{ServerId}";

    /// <summary>
    /// ServerConfig를 생성합니다.
    /// </summary>
    /// <returns>ServerConfig 인스턴스.</returns>
    public ServerConfig ToServerConfig()
    {
        return new ServerConfig(
            ServiceId,
            ServerId,
            BindEndpoint,
            RequestTimeoutMs,
            SendHighWatermark,
            ReceiveHighWatermark,
            TcpKeepalive);
    }

    /// <summary>
    /// 설정을 검증합니다.
    /// </summary>
    /// <exception cref="InvalidOperationException">유효하지 않은 설정.</exception>
    public void Validate()
    {
        if (ServiceId == 0)
            throw new InvalidOperationException("ServiceId must be greater than 0.");

        if (ServerId == 0)
            throw new InvalidOperationException("ServerId must be greater than 0.");

        if (string.IsNullOrEmpty(BindEndpoint))
            throw new InvalidOperationException("BindEndpoint must be specified.");

        if (RequestTimeoutMs <= 0)
            throw new InvalidOperationException("RequestTimeoutMs must be greater than 0.");
    }
}

/// <summary>
/// Communicator 빌더.
/// </summary>
public sealed class CommunicatorBuilder
{
    private readonly CommunicatorOption _option = new();

    /// <summary>
    /// 서비스 ID를 설정합니다.
    /// </summary>
    public CommunicatorBuilder WithServiceId(ushort serviceId)
    {
        _option.ServiceId = serviceId;
        return this;
    }

    /// <summary>
    /// 서버 ID를 설정합니다.
    /// </summary>
    public CommunicatorBuilder WithServerId(ushort serverId)
    {
        _option.ServerId = serverId;
        return this;
    }

    /// <summary>
    /// 바인드 엔드포인트를 설정합니다.
    /// </summary>
    public CommunicatorBuilder WithBindEndpoint(string endpoint)
    {
        _option.BindEndpoint = endpoint;
        return this;
    }

    /// <summary>
    /// 포트로 바인드 엔드포인트를 설정합니다.
    /// </summary>
    public CommunicatorBuilder WithPort(int port)
    {
        _option.BindEndpoint = $"tcp://*:{port}";
        return this;
    }

    /// <summary>
    /// 요청 타임아웃을 설정합니다.
    /// </summary>
    public CommunicatorBuilder WithRequestTimeout(int timeoutMs)
    {
        _option.RequestTimeoutMs = timeoutMs;
        return this;
    }

    /// <summary>
    /// 송신 큐 크기를 설정합니다.
    /// </summary>
    public CommunicatorBuilder WithSendQueueSize(int size)
    {
        _option.SendQueueSize = size;
        return this;
    }

    /// <summary>
    /// High Water Mark를 설정합니다.
    /// </summary>
    public CommunicatorBuilder WithHighWatermark(int send, int receive)
    {
        _option.SendHighWatermark = send;
        _option.ReceiveHighWatermark = receive;
        return this;
    }

    /// <summary>
    /// 디스커버리 갱신 주기를 설정합니다.
    /// </summary>
    public CommunicatorBuilder WithDiscoveryRefreshInterval(int intervalMs)
    {
        _option.DiscoveryRefreshIntervalMs = intervalMs;
        return this;
    }

    /// <summary>
    /// 옵션을 직접 설정합니다.
    /// </summary>
    public CommunicatorBuilder Configure(Action<CommunicatorOption> configure)
    {
        configure(_option);
        return this;
    }

    /// <summary>
    /// PlayCommunicator를 빌드합니다.
    /// </summary>
    /// <returns>ICommunicator 인스턴스.</returns>
    public ICommunicator Build()
    {
        _option.Validate();
        var config = _option.ToServerConfig();
        return PlayCommunicator.Create(config);
    }

    /// <summary>
    /// 옵션을 반환합니다.
    /// </summary>
    public CommunicatorOption GetOption()
    {
        _option.Validate();
        return _option;
    }
}
