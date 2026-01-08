# [최종 전략] S2S(Server-to-Server) 성능 극대화 및 아키텍처 가이드

## 1. 개요 (Overview)
순수 ZMQ Baseline 측정 결과(3프레임 Pipeline 77만 TPS, Echo 38.5만 TPS 환산)를 바탕으로, PlayHouse S2S 프레임워크 오버헤드를 최소화하여 **30만 TPS(Echo 기준)** 이상을 안정적으로 달성하기 위한 최종 전략임.

## 2. 핵심 원칙 (Core Principles)

### A. Managed Copy 전략 (User's Insight)
- **방침:** 네이티브 Zero-copy(Zmq.Message)의 높은 인터옵 비용을 피하고, 관리형 영역(`byte[]`)에서의 고속 복사 및 풀링을 고수함.
- **적용:** `MessagePool`에서 대여한 `byte[]`를 사용하여 ZMQ 표준 API를 타격.

### B. Zero-Allocation & Zero-Object
- **할당 제거:** S2S 통신 경로상의 모든 핵심 객체를 풀링함.
  - `RouteHeader`: `ObjectPool` + `MergeFrom` (역직렬화 오버헤드 제거).
  - `RoutePacket`: 전송/수신용 패킷 풀링.
  - `ApiWorkItem`: 워커 풀 투입용 작업 객체 풀링.
  - `ApiSender`: `ThreadStatic`을 통한 재사용.
- **직렬화 최적화:** `ThreadLocal` 고정 버퍼를 사용하여 헤더 직렬화 시 0-ns 지연 달성.

### C. 전용 워커 풀 기반 스케줄링 (Real Thread Model)
- **구조:** `GlobalTaskPool`을 통한 전용 워커 스레드 관리.
- **설정:** 
  - **Min:** 100 워커 (PlayServer 검증 수치).
  - **Max:** 1000 워커.
- **확장 전략 (Starvation-based):** 
  - 부하 증가 시 즉시 확장하지 않고, 기존 워커들의 **기아(Starvation) 상태**가 감지될 때만 점진적으로 워커를 추가함.
  - 이는 불필요한 컨텍스트 스위칭을 억제하고 처리 밀도를 극대화함.

---

## 3. 상세 아키텍처 흐름

### 전송 (Send) 경로
1. **Worker Thread:** 로직 처리 후 `ApiSender`를 통해 전송 요청.
2. **XClientCommunicator:** `Channel<SendRequest>`에 구조체(Value Type) 투입. (Lock-free & Batching)
3. **Sender Thread:** 전 전용 스레드가 깨어나 채널의 메시지를 `socket.Send`로 일괄 방출.

### 수신 (Receive) 경로
1. **Zmq Recv Thread:** 소켓에서 직접 3프레임 수신.
2. **Header Matching:** `MergeFrom`으로 풀링된 헤더에 데이터 주입.
3. **Dispatch:** `ApiWorkItem`을 풀에서 꺼내 `GlobalTaskPool`로 전달.
4. **Execution:** 전용 워커 스레드가 핸들러 실행 (스레드 전환 최소화).

---

## 4. 기대 효과 및 목표
- **TPS:** 15만 msg/s -> **30만 msg/s 이상** (물리적 한계의 80% 도달).
- **Latency:** P99 지연 시간 20% 이상 감축.
- **안정성:** 10,000 CCU 풀로드 상황에서 **Gen 0/1/2 GC 발생 횟수 '0'** 수렴.

## 5. 결론
우리는 물리적 한계(38.5만)를 명확히 확인했습니다. 이제 남은 과제는 정교한 스케줄링과 객체 재사용을 통해 이 혈관을 손실 없이 이어주는 것입니다.
