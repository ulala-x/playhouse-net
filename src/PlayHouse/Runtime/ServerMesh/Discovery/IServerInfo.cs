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
    /// 서버 인스턴스 ID.
    /// </summary>
    ushort ServerId { get; }

    /// <summary>
    /// Node ID (형식: "{ServiceId}:{ServerId}").
    /// </summary>
    string Nid { get; }

    /// <summary>
    /// NetMQ 연결 주소 (예: "tcp://192.168.1.100:5000").
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
    public ushort ServerId { get; }

    /// <inheritdoc/>
    public string Nid { get; }

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
        ushort serverId,
        string address,
        ServerState state = ServerState.Running,
        int weight = 100)
    {
        ServiceId = serviceId;
        ServerId = serverId;
        Nid = $"{serviceId}:{serverId}";
        Address = address;
        State = state;
        Weight = weight;
    }

    /// <summary>
    /// NID로부터 XServerInfo를 생성합니다.
    /// </summary>
    public static XServerInfo Create(string nid, string address, ServerState state = ServerState.Running)
    {
        var parts = nid.Split(':');
        if (parts.Length != 2)
            throw new ArgumentException($"Invalid NID format: {nid}. Expected 'ServiceId:ServerId'.");

        return new XServerInfo(
            ushort.Parse(parts[0]),
            ushort.Parse(parts[1]),
            address,
            state);
    }

    /// <inheritdoc/>
    public override string ToString() => $"{Nid}@{Address}[{State}]";

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is XServerInfo other && Nid == other.Nid;

    /// <inheritdoc/>
    public override int GetHashCode() => Nid.GetHashCode();
}
