# [상세 설계] CGDK 기반 고속 메시지 전용 메모리 풀

## 1. 목적 및 철학
- **Zero GC:** 메시지 할당/해제 과정에서 런타임 GC가 개입할 여지를 0으로 만듦.
- **Granular Bucketing:** 53단계 세분화된 버킷을 통해 메모리 낭비와 파편화 방지.
- **Locality:** 워커 스레드별 L1 캐시를 통해 스레드 간 경합 없는 초고속 할당 실현.

## 2. 버킷 설계 (CGDK 이식)
총 53개의 버킷을 다음과 같은 규칙으로 분할 관리함:
1. **Tiny (0~1KB):** 128B 단위 (8개)
2. **Small (1~8KB):** 1KB 단위 (7개)
3. **Medium (8~64KB):** 4KB 단위 (14개)
4. **Large (64~256KB):** 16KB 단위 (15개)
5. **Huge (256KB~1MB):** 64KB 단위 (9개)

## 3. 계층형 할당 아키텍처
### L1: Thread-Local Storage (Fast-Path)
- `[ThreadStatic]` 기반의 스택 사용.
- 락(Lock) 및 원자적 연산 전혀 없음.
- 최대 16개 블록 유지.

### L2: Global Concurrent Stack (Safe-Path)
- `ConcurrentStack<byte[]>` 기반.
- 모든 스레드가 공유하며 L1 고갈 시 보충원 역할.

## 4. 구현 핵심 기술
- **Fast Indexing:** `(size - 1) >> 7` 등의 비트 연산과 조건부 분기를 조합하여 O(1) 인덱싱 구현.
- **Pinned Memory:** `GC.AllocateArray(pinned: true)`를 활용하여 메모리 주소 고정 및 GC 이동 방지.
- **Wrapper-less:** 버퍼를 래핑하는 별도 객체 생성을 지양하고 `IPayload` 인터페이스에 직접 통합.

## 5. 검증 계획
1. **Unit Test:** 모든 버킷 크기에 대해 할당 및 반환이 정확한 인덱스에서 이루어지는지 검증.
2. **Integration Test:** 기존 TCP 세션에서 ArrayPool 대신 신규 풀을 사용하여 정상 통신 확인.
3. **Benchmark:** 10,000 CCU 상황에서 TPS 및 GC 지표 비교 분석.
