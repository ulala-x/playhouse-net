# [계획서] 10,000 CCU 대응을 위한 통합 메시징 및 세션 최적화

## 1. 개요 (Overview)
PlayHouse-NET의 현재 구조는 Stage별로 독립적인 큐와 스케줄링 로직을 가짐으로써 발생하는 **물리적 파편화 오버헤드**로 인해 10,000 CCU 상황에서 TPS가 급락합니다. 본 계획은 "10,000개의 Stage를 10,000명의 Actor처럼" 가볍게 처리하기 위한 **통합 메시징 아키텍처** 도입을 목표로 합니다.

## 2. 핵심 문제 정의 (Root Cause)
1. **과도한 상주 Task:** 세션당 3개(수신, 처리, 송신)의 비동기 루프가 상주하여 3만 개의 Task가 시스템 자원을 낭비함.
2. **스케줄링의 벽:** 10,000개의 독립된 큐를 순회하며 발생하는 컨텍스트 스위칭 및 캐시 미스 비용.
3. **원자적 연산 경합:** 10,000개 큐에 대한 개별 CAS(Compare-and-swap) 연산이 CPU 버스 경합 유발.

## 3. 새로운 설계 원칙 (Core Design Principles)

### A. 통합 메일박스 (Unified Mailbox)
- **개념:** 각 Stage가 가졌던 `ConcurrentQueue`를 제거하고, `EventLoop` 스레드가 소유한 **단일 통합 큐**로 메시지를 집중함.
- **이점:** 10,000개의 큐를 확인할 필요 없이 단 하나의 큐에서 메시지를 뭉텅이로 꺼내 처리(Bulk Drain) 가능. 큐 확인 오버헤드가 CCU에 무관하게 일정해짐.

### B. 상주 Task 최소화 (Task Diet)
- **수신 루프 통합:** 데이터 수신(`Receive`)과 메시지 디스패치(`Process`)를 하나의 Task로 통합하여 세션당 상주 Task를 최소 1개로 축소.
- **기회주의적 실행:** 메시지가 없는 세션은 실제 CPU 자원을 거의 소모하지 않는 완전한 비동기 상태(Idle) 유지.

### C. 비동기 순차 보장 (Sequential Async Execution)
- **Busy Flag:** 특정 Stage의 메시지가 처리 중(혹은 `await` 중)일 때, 해당 Stage를 위한 다음 메시지는 실행 대기 상태로 유지하여 **Thread-safety** 보장.
- **Async/Await 편의성 유지:** 개발자는 기존처럼 락(Lock) 없이 순차적인 비동기 코드를 작성 가능.

## 4. 상세 구현 계획

### 1단계: 네트워크 레이어 Task 통합 (3 -> 1)
- `TcpTransportSession`의 비동기 루프 구조 변경.
- `IO.Pipelines`를 활용하여 수신 즉시 파싱 및 EventLoop 큐로 전달.

### 2단계: Stage 큐 제거 및 통합 큐 도입
- `BaseStage` 내부의 `_messageQueue`와 `_isProcessing` 로직 제거.
- `PlayDispatcher`가 메시지를 Stage 객체가 아닌 해당 `EventLoop`의 통합 큐로 직접 전달.

### 3단계: EventLoop 배치 스케줄러 고도화
- 통합 큐에서 한 번에 여러 메시지를 꺼내오는 Batch Read 구현.
- 꺼내온 메시지들을 행선지(Stage)별로 묶어서 실행하는 **Bundling** 기법 적용 (캐시 효율 극대화).

### 4단계: 비동기 Continuation 추적
- `StageSynchronizationContext`를 통해 `await` 이후 돌아오는 작업도 어느 Stage 소속인지 명확히 판별하여 순차성 보장 루프에 포함.

## 5. 기대 효과 (Expected Outcomes)
- **성능:** 10,000 CCU 상황에서 현재 대비 **5배 이상의 TPS 향상** (10만 msg/s 이상 목표).
- **확장성:** Stage의 개수가 늘어나도 물리적 오버헤드가 비례하지 않는 선형적 확장성 확보.
- **안정성:** 수만 개의 Task가 유발하던 스케줄링 Spike 현상 제거 및 일정한 Latency 보장.

## 6. 로드맵 (Roadmap)
1. `TcpTransportSession` 수신 루프 통합 및 검증.
2. `BaseStage` 큐 제거 및 `EventLoop` 통합 큐 기반 라우팅 구현.
3. 10,000 CCU 벤치마크 수행 및 결과 분석.