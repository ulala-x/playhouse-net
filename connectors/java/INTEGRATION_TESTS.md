# Java Connector Integration Tests

## Overview

이 문서는 Java Connector의 통합 테스트 구조와 실행 방법을 설명합니다.

## Test Structure

### Test Infrastructure (`src/integrationTest/java/com/playhouse/connector/support/`)

#### BaseIntegrationTest.java
- 모든 테스트의 베이스 클래스
- Connector 초기화 및 정리
- 헬퍼 메서드 제공:
  - `createStageAndConnect()`: Stage 생성 및 연결
  - `authenticate()`: 인증 헬퍼
  - `echo()`: Echo 요청 헬퍼
  - `waitForCondition()`: MainThreadAction과 함께 조건 대기
  - `waitWithMainThreadAction()`: MainThreadAction과 함께 Future 대기

#### TestServerClient.java
- 테스트 서버 HTTP API 클라이언트
- Stage 생성 API 호출
- OkHttp 사용

#### CreateStageResponse.java
- Stage 생성 응답 데이터 클래스

#### TestMessages.java
- 간단한 Protobuf 메시지 헬퍼
- AuthenticateRequest/Reply
- EchoRequest/Reply
- BroadcastRequest/Notify
- FailRequest
- NoResponseRequest

### Core Tests (`src/integrationTest/java/com/playhouse/connector/core/`)

#### C01_StageCreationTests (3 tests)
1. C-01-01: TestStage 타입으로 Stage를 생성할 수 있다
2. C-01-02: 커스텀 페이로드로 Stage를 생성할 수 있다
3. C-01-03: 여러 개의 Stage를 생성할 수 있다

#### C02_TcpConnectionTests (6 tests)
1. C-02-01: Stage 생성 후 TCP 연결이 성공한다
2. C-02-02: 연결 후 IsConnected는 true를 반환한다
3. C-02-03: 연결 전 IsAuthenticated는 false를 반환한다
4. C-02-04: OnConnect 이벤트가 성공 결과로 발생한다
5. C-02-05: 잘못된 Stage ID로 연결해도 TCP 연결은 성공한다
6. C-02-06: 동일한 Connector로 재연결할 수 있다

#### C03_AuthenticationSuccessTests (6 tests)
1. C-03-01: 유효한 토큰으로 인증이 성공한다
2. C-03-02: AuthenticateAsync로 인증할 수 있다
3. C-03-03: Authenticate 콜백 방식으로 인증할 수 있다
4. C-03-04: 메타데이터와 함께 인증할 수 있다
5. C-03-05: 인증 성공 후 AccountId가 할당된다
6. C-03-06: 여러 유저가 동시에 인증할 수 있다

#### C05_EchoRequestResponseTests (9 tests)
1. C-05-01: Echo Request-Response가 정상 동작한다
2. C-05-02: RequestAsync로 Echo 요청할 수 있다
3. C-05-03: Request 콜백 방식으로 Echo 요청할 수 있다
4. C-05-04: 연속된 Echo 요청을 처리할 수 있다
5. C-05-05: 병렬 Echo 요청을 처리할 수 있다
6. C-05-06: 빈 문자열도 에코할 수 있다
7. C-05-07: 긴 문자열도 에코할 수 있다
8. C-05-08: 유니코드 문자열도 에코할 수 있다
9. C-05-09: 응답 메시지 타입이 올바르다

#### C06_PushMessageTests (6 tests)
1. C-06-01: Push 메시지를 수신할 수 있다
2. C-06-02: OnReceive 이벤트가 올바른 파라미터로 호출된다
3. C-06-03: 여러 개의 Push 메시지를 순차적으로 수신할 수 있다
4. C-06-04: Push 메시지와 Request-Response를 동시에 처리할 수 있다
5. C-06-05: BroadcastNotify에 데이터가 포함된다
6. C-06-06: OnReceive 핸들러가 등록되지 않아도 예외가 발생하지 않는다

#### TODO: C07_HeartbeatTests (6 tests)
- 하트비트 전송 및 수신 테스트

#### TODO: C08_DisconnectionTests (8 tests)
- 연결 해제 및 재연결 테스트

#### TODO: C09_AuthenticationFailureTests (9 tests)
- 인증 실패 시나리오 테스트

#### TODO: C10_RequestTimeoutTests (7 tests)
- 요청 타임아웃 테스트

#### TODO: C11_ErrorResponseTests (8 tests)
- 에러 응답 처리 테스트

### Advanced Tests (`src/integrationTest/java/com/playhouse/connector/advanced/`)

#### TODO: A01_WebSocketConnectionTests (7 tests)
- WebSocket 연결 테스트 (Java connector에 WebSocket 지원 추가 후)

#### TODO: A02_LargePayloadTests (5 tests)
- 대용량 페이로드 테스트

#### TODO: A03_SendMethodTests (7 tests)
- Fire-and-forget Send 메서드 테스트

#### TODO: A04_OnErrorEventTests (8 tests)
- 에러 이벤트 핸들러 테스트

#### TODO: A05_MultipleConnectorTests (7 tests)
- 다중 Connector 동시 사용 테스트

#### TODO: A06_EdgeCaseTests (13 tests)
- 엣지 케이스 테스트

## Test Configuration

### Environment Variables
- `TEST_SERVER_HOST`: 테스트 서버 호스트 (default: localhost)
- `TEST_SERVER_HTTP_PORT`: HTTP 포트 (default: 28080)
- `TEST_SERVER_TCP_PORT`: TCP 포트 (default: 28001)

### build.gradle.kts
- `integrationTest` source set 정의
- JUnit 5 + AssertJ 사용
- OkHttp, Gson 의존성 추가

## Running Tests

### Run Integration Tests
```bash
./run-tests.sh
```

### Run with Gradle
```bash
./gradlew integrationTest
```

### Run Specific Test Class
```bash
./gradlew integrationTest --tests "C01_StageCreationTests"
```

### Run with Environment Variables
```bash
TEST_SERVER_HOST=192.168.1.100 \
TEST_SERVER_HTTP_PORT=28080 \
TEST_SERVER_TCP_PORT=28001 \
./gradlew integrationTest
```

## Docker Test Environment

### Start Test Server
```bash
docker-compose -f docker-compose.test.yml up -d
```

### Stop Test Server
```bash
docker-compose -f docker-compose.test.yml down -v
```

## Test Naming Convention

### Core Tests (C-XX)
- C-01: Stage Creation
- C-02: TCP Connection
- C-03: Authentication Success
- C-05: Echo Request-Response
- C-06: Push Message
- C-07: Heartbeat
- C-08: Disconnection
- C-09: Authentication Failure
- C-10: Request Timeout
- C-11: Error Response

### Advanced Tests (A-XX)
- A-01: WebSocket Connection
- A-02: Large Payload
- A-03: Send Method
- A-04: OnError Event
- A-05: Multiple Connector
- A-06: Edge Case

## Current Status

### Completed (30 tests)
- ✅ C01_StageCreationTests (3 tests)
- ✅ C02_TcpConnectionTests (6 tests)
- ✅ C03_AuthenticationSuccessTests (6 tests)
- ✅ C05_EchoRequestResponseTests (9 tests)
- ✅ C06_PushMessageTests (6 tests)

### TODO (90 tests)
- ⏳ C07_HeartbeatTests (6 tests)
- ⏳ C08_DisconnectionTests (8 tests)
- ⏳ C09_AuthenticationFailureTests (9 tests)
- ⏳ C10_RequestTimeoutTests (7 tests)
- ⏳ C11_ErrorResponseTests (8 tests)
- ⏳ A01_WebSocketConnectionTests (7 tests)
- ⏳ A02_LargePayloadTests (5 tests)
- ⏳ A03_SendMethodTests (7 tests)
- ⏳ A04_OnErrorEventTests (8 tests)
- ⏳ A05_MultipleConnectorTests (7 tests)
- ⏳ A06_EdgeCaseTests (13 tests)

## Notes

### Protobuf Messages
현재 `TestMessages.java`는 간단한 protobuf 직렬화를 구현한 헬퍼 클래스입니다.
실제 프로젝트에서는 `protoc` 컴파일러로 생성된 클래스를 사용해야 합니다.

### MainThreadAction
Unity, Godot 등의 게임 엔진 통합 테스트에서는 `mainThreadAction()`을 주기적으로 호출해야 합니다.
일반 서버 애플리케이션에서는 필요하지 않습니다.

### Async Pattern
- `requestAsync()`: CompletableFuture 반환
- `request(..., callback)`: 콜백 방식
- `send()`: Fire-and-forget

### Test Server
C# 기반 테스트 서버가 Docker 컨테이너로 실행됩니다.
- HTTP API: Stage 생성 (`POST /api/stages`)
- TCP: PlayHouse 프로토콜
