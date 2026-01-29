#nullable enable

using PlayHouse.Runtime.ServerMesh.Discovery;

namespace PlayHouse.Abstractions;

/// <summary>
/// 서버 정보 조회 인터페이스.
/// </summary>
/// <remarks>
/// 서버 메시에 등록된 모든 서버의 정보를 조회하고 관리합니다.
/// Round-robin 방식의 로드밸런싱을 지원합니다.
/// ASP.NET Core DI 컨테이너에 등록하여 사용할 수 있습니다.
/// </remarks>
public interface IServerInfoCenter
{
    /// <summary>
    /// 현재 등록된 서버 수를 반환합니다.
    /// </summary>
    int Count { get; }

    /// <summary>
    /// ServerId로 서버 정보를 조회합니다.
    /// </summary>
    /// <param name="serverId">Server ID (예: "play-1", "api-seoul-1").</param>
    /// <returns>서버 정보 또는 null (서버를 찾을 수 없는 경우).</returns>
    XServerInfo? GetServer(string serverId);

    /// <summary>
    /// 서비스 타입별로 서버를 조회합니다 (Round-robin).
    /// </summary>
    /// <param name="serviceId">서비스 ID (1 = Play, 2 = API).</param>
    /// <returns>다음 서버 정보 또는 null (활성 서버가 없는 경우).</returns>
    /// <remarks>
    /// Running 상태의 서버 중에서 Round-robin 방식으로 선택됩니다.
    /// </remarks>
    XServerInfo? GetServerByService(ushort serviceId);

    /// <summary>
    /// 서비스 타입별로 서버를 조회합니다.
    /// </summary>
    /// <param name="serviceId">서비스 ID (1 = Play, 2 = API).</param>
    /// <param name="policy">서버 선택 정책.</param>
    /// <returns>선택된 서버 정보 또는 null.</returns>
    XServerInfo? GetServerByService(ushort serviceId, ServerSelectionPolicy policy);

    /// <summary>
    /// 서비스 타입별 모든 서버 목록을 조회합니다.
    /// </summary>
    /// <param name="serviceId">서비스 ID (1 = Play, 2 = API).</param>
    /// <returns>해당 서비스의 모든 서버 목록 (상태 무관).</returns>
    IReadOnlyList<XServerInfo> GetServerListByService(ushort serviceId);

    /// <summary>
    /// 모든 서버 목록을 조회합니다.
    /// </summary>
    /// <returns>등록된 모든 서버 목록 (상태 무관).</returns>
    IReadOnlyList<XServerInfo> GetAllServers();

    /// <summary>
    /// 활성화된 서버 목록을 조회합니다.
    /// </summary>
    /// <returns>Running 상태의 서버 목록.</returns>
    IReadOnlyList<XServerInfo> GetActiveServers();
}
