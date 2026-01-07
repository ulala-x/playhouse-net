# PlayHouse-NET 스펙 문서

PlayHouse-NET Realtime Game Server Framework의 상세 스펙 문서입니다.

## 문서 목록

### [00. 개요 (Overview)](./00-overview.md)
PlayHouse-NET 프레임워크의 전반적인 목표, 핵심 변경사항, 아키텍처 개요를 설명합니다.

**주요 내용:**
- 프레임워크 목표 및 핵심 철학
- 기존 PlayHouse 대비 주요 변경사항
- 지원 플랫폼 및 프로토콜
- 사용 시나리오 및 성능 특성
- 개발 로드맵

**대상 독자:** 프레임워크 도입을 고려하는 개발자, 기술 의사결정자

---

### [01. 아키텍처 (Architecture)](./01-architecture.md)
전체 시스템 아키텍처와 각 계층의 역할을 상세히 다룹니다.

**주요 내용:**
- 단일 Room 서버 구조
- 계층별 컴포넌트 (Abstractions, Core, Infrastructure)
- Core Engine 구성요소 (Dispatcher, Stage Pool, Timer Manager)
- HTTP API + Socket Server 통합
- 동시성 및 스레딩 모델
- 확장성 전략

**대상 독자:** 시스템 아키텍트, 백엔드 개발자

---

### [02. 패킷 구조 (Packet Structure)](./02-packet-structure.md)
클라이언트-서버 간 통신에 사용되는 패킷의 바이너리 구조를 정의합니다.

**주요 내용:**
- 헤더 필드 정의 (MsgId, MsgSeq, StageId, ErrorCode)
- Body 구조 및 압축 정책 (LZ4)
- 패킷 타입별 구조 (Request, Response, Push)
- 직렬화/역직렬화 메커니즘
- 성능 최적화 (메모리 풀링, Zero-Copy)

**대상 독자:** 백엔드 개발자, 클라이언트 개발자

---

### [03. Stage/Actor 모델 (Stage/Actor Model)](./03-stage-actor-model.md)
게임 로직의 핵심인 Stage/Actor 모델의 동작 방식을 설명합니다.

**주요 내용:**
- Stage (방/룸) 개념 및 라이프사이클
- Actor (플레이어) 개념 및 라이프사이클
- 메시지 디스패치 흐름
- Lock-Free 메시지 처리
- IStageSender, IActorSender 인터페이스
- AsyncBlock을 통한 비동기 작업 처리
- 실전 예제 (채팅방, 배틀 Stage)

**대상 독자:** 게임 로직 개발자, 백엔드 개발자

---

### [04. 타이머 시스템 (Timer System)](./04-timer-system.md)
Stage 내부에서 주기적인 작업을 안전하게 실행하는 타이머 시스템을 다룹니다.

**주요 내용:**
- RepeatTimer (무한 반복)
- CountTimer (제한된 횟수)
- 타이머 관리 및 취소
- 내부 구현 메커니즘
- 실전 예제 (게임 틱, 카운트다운, 버프 시스템)
- 타이머 정밀도 및 성능 고려사항

**대상 독자:** 게임 로직 개발자

---

### [05. HTTP API](./05-http-api.md)
서버 관리 및 모니터링을 위한 RESTful HTTP API를 정의합니다.

**주요 내용:**
- 엔드포인트 목록 및 스펙
- Stage 관리 API (생성, 조회, 삭제)
- 서버 모니터링 API (통계, 헬스체크)
- 인증 (JWT)
- Swagger 통합
- ASP.NET Core 구현 예시

**대상 독자:** 백엔드 개발자, 운영 엔지니어

---

### [06. 소켓 전송 (Socket Transport)](./06-socket-transport.md)
.NET 네이티브 라이브러리를 사용한 소켓 통신 계층을 설명합니다.

**주요 내용:**
- TCP 소켓 구현
- WebSocket 구현
- TLS/SSL 지원
- 세션 관리
- Heartbeat 처리
- 패킷 파서
- 성능 최적화 (SocketAsyncEventArgs, 버퍼 풀링)

**대상 독자:** 백엔드 개발자, 네트워크 개발자

---

### [07. 클라이언트 프로토콜 (Client Protocol)](./07-client-protocol.md)
클라이언트가 서버와 통신하는 전체 프로토콜을 정의합니다.

**주요 내용:**
- 연결 플로우 (연결 → 인증 → Stage 입장)
- 메시지 타입 (시스템, Stage, 게임)
- Request-Reply 패턴 (MsgSeq 매칭)
- 재연결 처리
- 에러 코드 및 처리
- 클라이언트 구현 예시 (Unity C#, TypeScript)

**대상 독자:** 클라이언트 개발자, 게임 개발자

---

### [08. 메트릭 및 관측성 (Metrics & Observability)](./08-metrics-observability.md)
시스템 모니터링 및 성능 추적을 위한 관측성 시스템을 설명합니다.

**주요 내용:**
- OpenTelemetry 통합
- 메트릭 수집 및 내보내기
- 분산 추적
- 헬스 체크
- 로깅 전략

**대상 독자:** 백엔드 개발자, 운영 엔지니어

---

### [09. 커넥터 (Connector)](./09-connector.md)
테스트 및 클라이언트 개발을 위한 PlayHouse.Connector 라이브러리를 설명합니다.

**주요 내용:**
- 커넥터 아키텍처
- 연결 관리
- 패킷 인코딩/디코딩
- Request-Reply 패턴
- 사용 예시

**대상 독자:** 클라이언트 개발자, 테스트 엔지니어

---

### [10. 테스트 전략 (Testing Strategy)](./10-testing-spec.md)
프레임워크의 테스트 전략 및 테스트 작성 가이드를 제공합니다.

**주요 내용:**
- 테스트 피라미드
- 유닛 테스트
- 통합 테스트
- E2E 테스트
- 테스트 인프라 (TestServerFixture)

**대상 독자:** 개발자, QA 엔지니어

---

### [11. 이벤트 루프 및 메시징 (Event Loop & Messaging)](./11-event-loop-messaging.md)
메시지 기반 이벤트 루프 및 디스패칭 메커니즘을 설명합니다.

**주요 내용:**
- 이벤트 루프 아키텍처
- 메시지 디스패칭
- Lock-Free 큐
- 백프레셔 처리
- 성능 최적화

**대상 독자:** 백엔드 개발자, 성능 엔지니어

---

### [12. Bootstrap 시스템 (Bootstrap)](./12-bootstrap.md)
프레임워크 초기화 및 구성을 담당하는 Bootstrap 시스템을 설명합니다.

**주요 내용:**
- PlayHouseBootstrap 진입점
- Fluent API 빌더
- StageTypeRegistry (타입 레지스트리)
- 운영 환경 Bootstrap
- 테스트 환경 Bootstrap (TestServerFixture)
- DI 통합
- 마이그레이션 가이드

**대상 독자:** 백엔드 개발자, 테스트 엔지니어

---

## 문서 읽기 순서

### 초심자 (프레임워크 처음 접하는 경우)
1. `00-overview.md` - 전체적인 그림 이해
2. `12-bootstrap.md` - 서버 시작 방법
3. `03-stage-actor-model.md` - 핵심 프로그래밍 모델 학습
4. `07-client-protocol.md` - 클라이언트 연동 방법
5. `04-timer-system.md` - 주기적 작업 처리 방법
6. 나머지 문서 - 필요에 따라 참조

### 백엔드 개발자
1. `00-overview.md` - 개요
2. `01-architecture.md` - 시스템 아키텍처
3. `12-bootstrap.md` - Bootstrap 시스템
4. `02-packet-structure.md` - 패킷 구조
5. `03-stage-actor-model.md` - Stage/Actor 모델
6. `06-socket-transport.md` - 소켓 구현
7. `05-http-api.md` - HTTP API
8. `10-testing-spec.md` - 테스트 전략

### 클라이언트 개발자
1. `00-overview.md` - 개요
2. `07-client-protocol.md` - 클라이언트 프로토콜
3. `09-connector.md` - PlayHouse.Connector
4. `02-packet-structure.md` - 패킷 구조
5. `03-stage-actor-model.md` - Stage/Actor 이해

### 테스트 엔지니어
1. `10-testing-spec.md` - 테스트 전략
2. `12-bootstrap.md` - TestServerFixture 사용법
3. `09-connector.md` - 테스트 클라이언트

### 시스템 아키텍트
1. `00-overview.md` - 개요
2. `01-architecture.md` - 아키텍처
3. `12-bootstrap.md` - 시스템 초기화
4. 모든 문서 - 전체 시스템 이해

---

## 문서 변경 이력

### 2025-12-09
- Bootstrap 시스템 문서 추가
  - 12-bootstrap.md - 시스템 초기화 및 구성
  - 01-architecture.md 업데이트 - Bootstrap 레이어 추가

### 2024-12-09
- 초기 스펙 문서 작성
- 12개 문서 완성
  - 00-overview.md
  - 01-architecture.md
  - 02-packet-structure.md
  - 03-stage-actor-model.md
  - 04-timer-system.md
  - 05-http-api.md
  - 06-socket-transport.md
  - 07-client-protocol.md
  - 08-metrics-observability.md
  - 09-connector.md
  - 10-testing-spec.md
  - 11-event-loop-messaging.md

---

## 참고 자료

### 기존 PlayHouse (Java)
- GitHub: [playhouse](https://github.com/ulala-x/playhouse)
- 문서: [playhouse-doc](https://github.com/ulala-x/playhouse-doc)

### .NET 관련
- [.NET 8.0 Documentation](https://learn.microsoft.com/en-us/dotnet/)
- [ASP.NET Core](https://learn.microsoft.com/en-us/aspnet/core/)
- [System.Net.Sockets](https://learn.microsoft.com/en-us/dotnet/api/system.net.sockets)
- [System.Net.WebSockets](https://learn.microsoft.com/en-us/dotnet/api/system.net.websockets)

---

## 기여 및 피드백

스펙 문서에 대한 피드백이나 개선 제안은 GitHub Issues를 통해 제출해주세요.

---

## 라이센스

이 문서는 PlayHouse-NET 프로젝트의 일부이며, 프로젝트와 동일한 라이센스가 적용됩니다.
