#nullable enable

namespace PlayHouse.Infrastructure.Memory;

/// <summary>
/// MessagePool의 버킷별 용량 및 웜업 정책 설정.
/// </summary>
public sealed class MessagePoolConfig
{
    // --- [1] 최대 수용량 (이 수치를 넘으면 할당된 메모리는 GC가 수거함) ---
    public int MaxTinyCount { get; set; } = 100000;
    public int MaxSmallCount { get; set; } = 20000;
    public int MaxMediumCount { get; set; } = 5000;
    public int MaxLargeCount { get; set; } = 500;
    public int MaxHugeCount { get; set; } = 100;

    // --- [2] 웜업 수량 (서버 시작 시 미리 만들어둘 실제 개수) ---
    // 사용자님이 직접 이 숫자를 보고 튜닝하실 수 있습니다.
    public int TinyWarmUpCount { get; set; } = 20000;
    public int SmallWarmUpCount { get; set; } = 5000;
    public int MediumWarmUpCount { get; set; } = 500;
    public int LargeWarmUpCount { get; set; } = 10;

        // --- [3] 기타 설정 ---

        public int MaxL1Capacity { get; set; } = 64;

    

        // --- [4] 자동 축소(Trim) 설정 ---

        public bool EnableAutoTrim { get; set; } = true;

        public TimeSpan TrimCheckInterval { get; set; } = TimeSpan.FromSeconds(30); // 30초마다 체크

        public TimeSpan IdleThreshold { get; set; } = TimeSpan.FromSeconds(60);     // 60초간 유휴 시 축소 시작

    }

    