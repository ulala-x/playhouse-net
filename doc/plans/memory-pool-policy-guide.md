# [가이드] MessagePool 자원 관리 정책 및 용량 산정

## 1. 개요
PlayHouse-NET의 `MessagePool`은 10,000 CCU 이상의 고동시성 상황에서 Zero-Allocation을 달성하기 위해 메모리를 선점 관리한다. 본 문서는 각 패킷 크기별(버킷) 관리 정책과 그에 따른 메모리 점유량을 기술한다.

## 2. 버킷별 기본 정책 (Default Policy)

| 분류 | 크기 범위 | Max 개수 | 점유 용량(Max 시) | 용도 및 특징 |
| :--- | :--- | :--- | :--- | :--- |
| **Tiny** | ~ 1 KB | 100,000 | 약 410 MB | 이동, 동기화 등 빈도가 극도로 높은 패킷 |
| **Small** | 1 ~ 8 KB | 20,000 | 약 630 MB | 스킬, 인벤토리 등 일반적인 게임 데이터 |
| **Medium** | 8 ~ 64 KB | 5,000 | 약 2.5 GB | 맵 데이터, 대량의 리스트 조회 등 |
| **Large** | 64 ~ 256 KB | 500 | 약 1.2 GB | 초기 로딩 정보, 압축된 대용량 데이터 |
| **Huge** | 256 KB ~ 1 MB | 100 | 약 0.6 GB | 매우 드문 대규모 시스템 데이터 |

- **이론상 최대 점유량:** 약 5.7 GB (L1 캐시 포함 시 약 6 GB)
- **기본 시스템 비용 포함 예상 피크:** 약 8 GB

## 3. 설정 방법 (Configuration)
`PlayServerOption`을 통해 각 구간별 최대 개수를 조정할 수 있다.

```csharp
options.MessagePool.MaxTinyCount = 50000;  // Tiny 버킷 최대치 하향
options.MessagePool.WarmUpFactor = 1.5f;   // 웜업 수량 1.5배 증폭
```

## 4. 운영 가이드라인
1. **메모리가 부족한 환경 (8GB 이하 서버):** `MaxMediumCount`와 `MaxLargeCount`를 절반으로 줄여 점유량을 4GB 내외로 억제할 것을 권장함.
2. **응답성이 최우선인 환경:** `WarmUpFactor`를 2.0 이상으로 설정하여 모든 버퍼를 시작 시점에 물리 RAM에 고정(Commit)시킬 것.
3. **할당 발생 모니터링:** 서버 로그의 `NewAllocs` 지표가 0이 아니라면, 해당 버킷의 `MaxCount`를 늘려야 함.
