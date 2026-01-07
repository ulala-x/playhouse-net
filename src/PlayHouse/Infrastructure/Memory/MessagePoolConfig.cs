#nullable enable

namespace PlayHouse.Infrastructure.Memory;

/// <summary>
/// MessagePool의 버킷별 용량 및 웜업 정책 설정.
/// </summary>
public sealed class MessagePoolConfig
{
    // 각 구간별 최대 보관 개수
    public int MaxTinyCount { get; set; } = 100000;
    public int MaxSmallCount { get; set; } = 20000;
    public int MaxMediumCount { get; set; } = 5000;
    public int MaxLargeCount { get; set; } = 500;
    public int MaxHugeCount { get; set; } = 100;

    // 웜업 계수 (기본 정책 대비 배수)
    public float WarmUpFactor { get; set; } = 1.0f;
    
    // 스레드별 L1 캐시 크기
    public int MaxL1Capacity { get; set; } = 64;
}
