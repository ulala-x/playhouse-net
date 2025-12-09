#nullable enable

namespace PlayHouse.Core.Bootstrap;

/// <summary>
/// PlayHouse 서버 부트스트랩 진입점.
/// Core 레이어에서 제공하는 시스템 초기화 API입니다.
/// </summary>
public static class PlayHouseBootstrap
{
    /// <summary>
    /// 새 부트스트랩 빌더를 생성합니다.
    /// </summary>
    public static PlayHouseBootstrapBuilder Create() => new();
}
