# [가이드] MessagePool 자원 관리 정책 및 용량 산정

## 1. 개요
PlayHouse-NET의 `MessagePool`은 10,000 CCU 이상의 고동시성 상황에서 Zero-Allocation을 달성하기 위해 메모리를 선점 관리한다. 본 문서는 각 패킷 크기별(버킷) 관리 정책과 그에 따른 메모리 점유량을 기술한다.

## 2. 버킷별 기본 정책 및 웜업 (Default Policy & Warm-up)

| 분류 | 크기 범위 | 웜업 수량 (기본) | Max 보관 개수 | 점유 용량(Max 시) |
| :--- | :--- | :--- | :--- | :--- |
| **Tiny** | ~ 1 KB | **20,000개** | 100,000 | 약 410 MB |
| **Small** | 1 ~ 8 KB | **5,000개** | 20,000 | 약 630 MB |
| **Medium** | 8 ~ 64 KB | **500개** | 5,000 | 약 2.5 GB |
| **Large** | 64 ~ 256 KB | **10개** | 500 | 약 1.2 GB |
| **Huge** | 256 KB ~ 1 MB | **10개** | 100 | 약 0.6 GB |

- **이론상 최대 점유량:** 약 5.7 GB (L1 캐시 포함 시 약 6 GB)
- **기본 시스템 비용 포함 예상 피크:** 약 8 GB

## 3. 설정 방법 (Configuration)
`MessagePoolConfig`를 통해 각 구간별 웜업 수량과 최대 개수를 직접 숫자로 조정할 수 있다.

```csharp
options.MessagePool.TinyWarmUpCount = 30000;  // Tiny 웜업 수량 직접 지정
options.MessagePool.MaxTinyCount = 50000;     // Tiny 최대치 조정
```

## 4. 운영 가이드라인
1. **메모리가 부족한 환경:** `MaxMediumCount`와 `MaxLargeCount`를 절반으로 줄여 점유량을 4GB 내외로 억제할 것을 권장함.
2. **응답성이 최우선인 환경:** `WarmUpCount`들을 실제 동시 접속자 수의 2배 이상으로 설정하여 할당 지연을 원천 차단할 것.
3. **할당 발생 모니터링:** 서버 로그의 `NewAllocs` 지표가 0이 아니라면, 해당 버킷의 `WarmUpCount` 또는 `MaxCount`를 늘려야 함.