# PlayHouse C# Connector Integration Tests

PlayHouse C# Connector의 통합 테스트 프로젝트입니다. 실제 테스트 서버와 통신하여 Connector의 전체 기능을 검증합니다.

## 사전 요구사항

### 테스트 서버 실행
통합 테스트를 실행하기 전에 테스트 서버가 실행 중이어야 합니다.

```bash
# playhouse-net 루트 디렉토리에서
cd connectors/test-server
docker compose up -d
```

테스트 서버는 다음 포트에서 실행됩니다:
- HTTP API: 8080
- TCP Server: 34001
- WebSocket: 8080/ws

## 테스트 실행

### 전체 테스트 실행
```bash
cd connectors/csharp/tests/PlayHouse.Connector.IntegrationTests
dotnet test
```

### 특정 테스트 클래스 실행
```bash
dotnet test --filter "FullyQualifiedName~C01_StageCreationTests"
```

### 특정 테스트 케이스 실행
```bash
dotnet test --filter "DisplayName~C-01-01"
```

### 상세 출력으로 실행
```bash
dotnet test --logger "console;verbosity=detailed"
```

## 환경 변수 설정

테스트 서버의 주소와 포트를 환경 변수로 설정할 수 있습니다:

```bash
export TEST_SERVER_HOST=localhost
export TEST_SERVER_HTTP_PORT=8080
export TEST_SERVER_TCP_PORT=34001

dotnet test
```

## 테스트 구조

### 테스트 인프라
- `TestServerFixture.cs`: 테스트 서버 연결 관리 (HTTP API를 통한 Stage 생성)
- `BaseIntegrationTest.cs`: 공통 테스트 설정 및 헬퍼 메서드

### Core 테스트 (필수)
1. **C-01: Stage 생성** (`C01_StageCreationTests.cs`)
   - HTTP API를 통한 Stage 생성
   - 여러 Stage 생성 및 고유 ID 검증

2. **C-02: TCP 연결** (`C02_TcpConnectionTests.cs`)
   - TCP 연결 성공/실패
   - 연결 상태 확인
   - 재연결 기능

3. **C-03: 인증 성공** (`C03_AuthenticationSuccessTests.cs`)
   - 유효한 토큰으로 인증
   - Async/Callback 방식 인증
   - 메타데이터와 함께 인증

4. **C-05: Echo Request-Response** (`C05_EchoRequestResponseTests.cs`)
   - 기본 Request-Response 패턴
   - 연속/병렬 요청 처리
   - 다양한 페이로드 크기

5. **C-06: Push 메시지 수신** (`C06_PushMessageTests.cs`)
   - BroadcastNotify 수신
   - OnReceive 이벤트 처리
   - Push와 Request-Response 동시 처리

6. **C-07: Heartbeat 자동 처리** (`C07_HeartbeatTests.cs`)
   - 장시간 연결 유지
   - Heartbeat 중 메시지 송수신
   - 여러 Connector 동시 Heartbeat

7. **C-08: 연결 해제** (`C08_DisconnectionTests.cs`)
   - Disconnect 호출
   - 연결 해제 후 상태
   - 재연결 기능

8. **C-09: 인증 실패** (`C09_AuthenticationFailureTests.cs`)
   - 잘못된 토큰
   - 빈 UserId/Token
   - 인증 실패 후 재시도

9. **C-10: 요청 타임아웃** (`C10_RequestTimeoutTests.cs`)
   - NoResponseRequest 타임아웃
   - 타임아웃 후 연결 유지
   - 타임아웃 예외 처리

10. **C-11: 에러 응답** (`C11_ErrorResponseTests.cs`)
    - FailRequest 에러 응답
    - 다양한 에러 코드 처리
    - 에러 후 정상 요청

## 테스트 가이드라인

### Proto 메시지 사용
테스트에서는 항상 proto-defined 메시지를 사용해야 합니다:

```csharp
// Good
var echoRequest = new EchoRequest { Content = "Hello", Sequence = 1 };
using var packet = new Packet(echoRequest);

// Bad - 사용하지 마세요
using var packet = Packet.Empty("EchoRequest");
```

### 리소스 정리
IDisposable/IAsyncDisposable 리소스는 반드시 정리해야 합니다:

```csharp
using var packet = new Packet(message); // using 사용

// 또는
try {
    var connector = new Connector();
    // ...
}
finally {
    await connector.DisposeAsync();
}
```

### xUnit 패턴
- `IClassFixture<TestServerFixture>`: 테스트 클래스 당 한 번 초기화
- `IAsyncLifetime`: 각 테스트 메서드 전/후 초기화/정리

## 트러블슈팅

### 테스트 서버가 실행되지 않음
```bash
docker compose ps
docker compose logs
```

### 연결 실패
- 테스트 서버가 실행 중인지 확인
- 포트가 사용 가능한지 확인 (8080, 34001)
- 방화벽 설정 확인

### 타임아웃 발생
- 네트워크 상태 확인
- 테스트 서버 로그 확인
- RequestTimeoutMs 설정 증가

## 참고 자료
- [Test Server Proto 정의](../../../test-server/proto/test_messages.proto)
- [Test Server Message IDs](../../../test-server/src/PlayHouse.TestServer/Shared/TestMessages.cs)
- [Connector API](../../Connector.cs)
