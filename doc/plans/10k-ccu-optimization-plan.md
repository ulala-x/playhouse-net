# [계획서] 고정 워커 Task 풀 아키텍처를 이용한 10,000 CCU 성능 최적화

## 1. 개요 (Overview)
PlayHouse-NET의 성능 병목인 '세션별 상주 Task'와 'Stage별 스케줄링 파편화'를 해결하기 위해, 시스템 전체의 실행 자원을 고정된 수치로 관리하는 **워커 Task 풀(Worker Task Pool)** 모델을 도입함. 10,000명의 접속자가 들어와도 서버의 물리적 부하(Context Switching)를 일정하게 유지하는 것이 핵심 목표임.

## 2. 핵심 아키텍처 (Core Architecture)

### A. 물리 스레드 계층 (Physical Threads)
- **설정:** `Environment.ProcessorCount` (예: 16개)
- **역할:** .NET ThreadPool 위에서 실제로 CPU 연산을 수행하는 물리적 단위.

### B. 실행 Task 풀 계층 (Worker Task Pool)
- **설정:** 고정된 개수 (예: 200개, 설정 가능하도록 구현)
- **동작 방식:**
  - 시스템 시작 시 고정된 개수의 비동기 루프(`Task`)를 생성하여 상주 시킴.
  - 전역 작업 큐를 공유하며, 처리 대기 중인 Stage를 꺼내어 로직을 실행.
  - **비동기 대기 최적화:** 로직 중 `await` 발생 시 해당 Task는 즉시 스레드를 반환하여 다른 Task가 실행될 수 있도록 양보함.
- **크기 결정 로직 (Little's Law):**
  - 공식: `Task 풀 크기 = 코어 수 * (1 + 대기시간 / 처리시간)`
  - 목표: CPU 점유율 80~90%를 유지하면서 Latency(P99)가 튀지 않는 최소한의 크기 유지.

### C. 논리 Stage 계층 (Thin Stage Model)
- **상태:** 10,000개의 Stage는 개별 무한 루프나 상주 Task 없이 **큐(Mailbox)**와 **데이터**만 보유함.
- **순차성 보장:** 특정 워커 Task가 Stage를 처리 중일 때 다른 워커가 중복 접근하지 못하도록 **Busy Flag** 기반의 Lock-free 스케줄링 수행.

## 3. 네트워크 레이어 다이어트 (Loop-less Network)
- `TcpTransportSession` 내부의 `ReceiveLoop`, `SendLoop` 등 모든 상주형 `Task.Run` 제거.
- .NET의 `Socket.ReceiveAsync` 및 이벤트 콜백 방식을 활용하여, **데이터가 수신된 시점에만** 잠시 워커 자원을 소모하도록 변경.

## 4. 세부 구현 로드맵

### [1단계] 네트워크 상주 Task 제거
- `TcpTransportSession`의 `while(true)` 루프 제거 및 이벤트 기반 수신/송신 구조로 개편.

### [2단계] 전역 워커 Task 풀 엔진 구현
- 고정된 개수의 Task가 작업을 수행하는 스케줄러(`TaskPoolDispatcher`) 구현.
- 풀 크기를 런타임/설정에서 조정 가능하도록 처리.

### [3단계] Stage 스케줄링 연동
- `BaseStage`와 `PlayDispatcher`가 전역 풀과 연동되도록 수정.
- 비동기 중단/재개(`Continuation`) 시에도 동일한 워커 풀 내에서 순차 실행되도록 `SynchronizationContext` 연동.

## 5. 기대 효과
- **관리 비용 고정:** 10,000 CCU 상황에서도 관리 대상 Task가 30,000개에서 200개로 **99% 감소**.
- **확장성:** Stage나 Actor의 개수에 시스템 성능이 민감하게 반응하지 않는 견고한 아키텍처 확보.
- **편의성:** 개발자는 여전히 락(Lock) 없이 순차적인 비동기 비즈니스 로직 작성 가능.
