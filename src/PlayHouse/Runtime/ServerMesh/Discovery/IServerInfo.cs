#nullable enable

namespace PlayHouse.Runtime.ServerMesh.Discovery;

/// <summary>
/// 서버 상태.
/// </summary>
public enum ServerState
{
    /// <summary>
    /// 서버 활성화 상태.
    /// </summary>
    Running = 0,

    /// <summary>
    /// 서버 비활성화 상태 (연결 종료 대상).
    /// </summary>
    Disabled = 1
}

/// <summary>
/// 서버 정보 인터페이스.
/// </summary>
public interface IServerInfo
{
    /// <summary>
    /// 서비스 ID (1 = Play, 2 = API).
    /// </summary>
    ushort ServiceId { get; }

    /// <summary>
    /// 서버 인스턴스 ID (고유 문자열, 예: "play-1", "api-seoul-1").
    /// </summary>
    string ServerId { get; }

    /// <summary>
    /// ZMQ 연결 주소 (예: "tcp://192.168.1.100:5000").
    /// </summary>
    string Address { get; }

    /// <summary>
    /// 서버 상태.
    /// </summary>
    ServerState State { get; }

    /// <summary>
    /// 서버 가중치 (로드밸런싱용).
    /// </summary>
    int Weight { get; }
}

/// <summary>
/// 기본 서버 정보 구현.
/// </summary>
public sealed class XServerInfo : IServerInfo
{
    /// <inheritdoc/>
    public ushort ServiceId { get; }

    /// <inheritdoc/>
    public string ServerId { get; }

    /// <inheritdoc/>
    public string Address { get; }

    /// <inheritdoc/>
    public ServerState State { get; }

    /// <inheritdoc/>
    public int Weight { get; }

    /// <summary>
    /// 새 XServerInfo 인스턴스를 생성합니다.
    /// </summary>
    public XServerInfo(
        ushort serviceId,
        string serverId,
        string address,
        ServerState state = ServerState.Running,
        int weight = 100)
    {
        ServiceId = serviceId;
        ServerId = serverId;
        Address = address;
        State = state;
        Weight = weight;
    }

    /// <summary>
    /// ServerId와 ServiceId로부터 XServerInfo를 생성합니다.
    /// </summary>
    public static XServerInfo Create(ushort serviceId, string serverId, string address, ServerState state = ServerState.Running)
    {
        return new XServerInfo(serviceId, serverId, address, state);
    }

    /// <inheritdoc/>
    public override string ToString() => $"{ServiceId}:{ServerId}@{Address}[{State}]";

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is XServerInfo other && ServerId == other.ServerId;

    /// <inheritdoc/>
    public override int GetHashCode() => ServerId.GetHashCode();
}

/// <summary>
/// 서버 선택 정책.
/// </summary>
public enum ServerSelectionPolicy
{
    /// <summary>
    /// Round-Robin 방식 (기본값).
    /// 순차적으로 서버 선택.
    /// </summary>
    RoundRobin = 0,

    /// <summary>
    /// 가중치 기반 선택 (내림차순).
    /// Weight가 가장 높은 서버가 우선 선택됨.
    /// </summary>
    Weighted = 1
}
