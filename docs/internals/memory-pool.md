# [명세서] 메시지 전용 메모리 풀 (MessagePool)

## 1. 설계 목적
본 메모리 풀은 10,000 CCU 이상의 고동시성 환경에서 .NET GC의 간섭을 최소화하고, CPU 캐시 지역성을 극대화하기 위해 설계되었다. CGDK10의 53단계 버킷 전략을 계승하며, 물리 메모리 선점(Deep Pre-warm) 기능을 통해 실행 중 할당 지연을 원천 차단한다.

## 2. 메모리 할당 및 웜업 전략
단순한 객체 생성을 넘어, OS 수준의 물리 메모리 할당(Commit)을 보장하기 위한 2단계 전략을 사용한다.

### A. Deep Pre-warm (Physical Commit)
- **방식:** `new byte[]` 할당 직후 `Span.Clear()`를 호출하여 전 영역에 데이터를 기록.
- **효과:** OS가 해당 메모리 페이지에 물리적 RAM을 즉시 할당(Commit)하게 하여, 런타임 중 Page Fault로 인한 지연을 제거한다.

### B. 계층형 스토리지
- **L1 (Thread-Local):** 락(Lock) 없이 스레드별로 최대 64개의 버퍼를 즉시 대여/반납.
- **L2 (Global Pool):** 모든 스레드가 공유하는 전역 저장소로, 버킷별 Max 수치까지 자원을 유지.

## 3. 버킷별 상세 정책 (기본값)

| 구간 | 버킷 개수 | 웜업 수량 (물리 점유) | 최대 보관 (Max) | 점유량 (Max 시) |
| :--- | :---: | :---: | :---: | :--- |
| **Tiny (~1KB)** | 8개 | **20,000개** | 100,000개 | 약 410 MB |
| **Small (~8KB)** | 7개 | **5,000개** | 20,000개 | 약 630 MB |
| **Medium (~64KB)** | 14개 | **500개** | 5,000개 | 약 2.5 GB |
| **Large (~256KB)** | 15개 | **10개** | 500개 | 약 1.2 GB |
| **Huge (~1MB)** | 9개 | **10개** | 100개 | 약 0.6 GB |

## 4. 사용자 설정 (Configuration)
`MessagePoolConfig`를 통해 시스템 사양에 맞게 수치를 직접 튜닝할 수 있다.

```csharp
var config = new MessagePoolConfig {
    // 웜업 수량 직접 제어 (예시: 10,000 CCU 대응)
    TinyWarmUpCount = 30000, 
    SmallWarmUpCount = 10000,
    
    // 최대 수용량 제어 (누수 방지 가드레일)
    MaxTinyCount = 100000,
    MaxMediumCount = 2000 
};
```

## 5. 성능 지표 확인
서버 종료 시 `MessagePool.PrintStats()`를 통해 각 버킷별 할당(NewAllocs) 발생 여부와 반납 거부(Rejected) 횟수를 모니터링할 수 있다. 벤치마크 시 `NewAllocs`가 0임을 확인하는 것이 최적화의 목표이다.
