# E2E 테스트 마이그레이션 요약

## 현재 상황

### 기존 문제점
기존 `ChatRoomE2ETests.cs`는 **E2E 테스트가 아닌 통합 테스트**였습니다:

```csharp
// ❌ 기존: 서버 내부 API 직접 호출 (통합 테스트)
var (stageContext, errorCode, _) = await _server.StageFactory.CreateStageAsync("chat-stage", createPacket);
await stageContext.JoinActorAsync(accountId, sessionId, joinPacket);
```

### 마이그레이션 작업
진짜 E2E 테스트로 재작성했습니다:

```csharp
// ✅ 재작성: PlayHouseClient로 실제 네트워크 통신
await using var client = new PlayHouseClient(options, logger);
await client.ConnectAsync(_server.Endpoint, authToken);

// Request-Response 패턴
var response = await client.RequestAsync<CreateStageRequest, CreateStageReply>(request);

// 서버 푸시 메시지 수신
client.On<ChatMessage>(msg => { /* 핸들러 */ });
```

## 테스트 결과

### 성공한 테스트 (11/18)
✅ **연결 테스트 (2/2)**
- 클라이언트 연결 및 Connected 이벤트 수신
- 여러 클라이언트 동시 연결

✅ **Bootstrap 테스트 (4/4)**
- TestServerFixture로 서버 시작
- Stage 생성 및 Actor 입장
- 멀티플레이어 로비 설정
- Stage 전체 생명주기

### 실패한 테스트 (7/18)

#### 1. Request-Response 패턴 테스트 (전체 실패)
**문제**: 서버가 응답을 보내지 않음
```
[client-1] Debug: Sending request: Type=CreateStageRequest, MsgSeq=1, MsgId=736
// 응답이 없어서 타임아웃 발생
```

**원인**:
- PlayHouseClient는 TCP 연결은 성공하지만 **서버측 Request-Response 핸들링이 미구현**
- 서버가 `CreateStageRequest`를 받아서 `CreateStageReply`를 보내는 로직 부재

#### 2. 서버 푸시 메시지 테스트 (전체 실패)
**문제**: 채팅 메시지 전송 후 수신 실패
- `client.SendAsync(chatMessage)` 성공
- `client.On<ChatMessage>()` 핸들러가 호출되지 않음

**원인**: 서버측 메시지 라우팅 미구현

#### 3. 멀티 클라이언트 테스트 (전체 실패)
**문제**: NullReferenceException at line 434
```csharp
var createResponse = await clients[0].RequestAsync<CreateStageRequest, CreateStageReply>(...);
// createResponse.Data가 null → NullReferenceException
```

**원인**: Request-Response 타임아웃으로 인한 null 데이터

## 필요한 서버측 구현

### 1. TCP 메시지 핸들링 파이프라인
현재 서버는 TCP 연결만 수락하고 메시지 처리를 하지 않습니다. 필요한 구현:

```csharp
// 서버가 구현해야 할 메시지 핸들러
public class StageMessageHandler
{
    public async Task<CreateStageReply> HandleCreateStageRequest(CreateStageRequest request)
    {
        // StageFactory로 Stage 생성
        var (stageContext, errorCode, _) = await _stageFactory.CreateStageAsync(...);

        // 클라이언트에게 응답 전송
        return new CreateStageReply
        {
            StageId = stageContext.StageId,
            StageName = request.StageName
        };
    }

    public async Task<JoinStageReply> HandleJoinStageRequest(JoinStageRequest request, long sessionId)
    {
        // SessionManager로 세션 생성 및 매핑
        _sessionManager.CreateSession(sessionId);
        _sessionManager.MapAccountId(sessionId, request.AccountId);

        // Actor를 Stage에 입장
        var (error, _, actor) = await stageContext.JoinActorAsync(...);

        return new JoinStageReply
        {
            AccountId = request.AccountId,
            StageId = stageId,
            JoinedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };
    }
}
```

### 2. 메시지 디스패처 등록
`TcpServer` 또는 `PlayHouseServer`에서 메시지 핸들러 등록:

```csharp
public class PlayHouseServer
{
    public void ConfigureMessageHandlers()
    {
        // Protobuf 메시지 ID를 핸들러에 매핑
        _messageDispatcher.Register<CreateStageRequest>(HandleCreateStageRequest);
        _messageDispatcher.Register<JoinStageRequest>(HandleJoinStageRequest);
        _messageDispatcher.Register<ChatMessage>(HandleChatMessage);
    }
}
```

### 3. 프로토콜 계층 통합
PlayHouseClient의 `RequestAsync`와 서버의 메시지 핸들러 연결:

```
Client                        Server
------                        ------
RequestAsync(CreateStageRequest)
  → Encode with MsgSeq=1
  → TCP Send
                              ← TCP Receive
                              ← Decode packet
                              ← Dispatch to StageMessageHandler
                              ← HandleCreateStageRequest
                              ← Encode CreateStageReply with MsgSeq=1
                              → TCP Send
← TCP Receive
← CompleteRequest(MsgSeq=1, payload)
← Return CreateStageReply
```

## 권장 사항

### 즉시 조치 필요
1. **서버측 메시지 핸들링 구현** (최우선)
   - `CreateStageRequest` → `CreateStageReply`
   - `JoinStageRequest` → `JoinStageReply`
   - 메시지 ID 레지스트리 구현

2. **메시지 디스패처 연결**
   - `TcpSession`에서 받은 패킷을 메시지 핸들러로 라우팅
   - Protobuf 역직렬화 및 핸들러 호출

3. **응답 전송 구현**
   - 핸들러 결과를 클라이언트로 전송
   - MsgSeq 매칭으로 Request-Response 연결

### 중기 조치
1. **서버 푸시 메시지 구현**
   - Actor 간 메시지 브로드캐스트
   - `IStageSender`와 TCP 세션 연결

2. **세션 생명주기 자동화**
   - 클라이언트 연결 시 자동 세션 생성
   - 연결 해제 시 Actor 퇴장 및 정리

## 현재 E2E 테스트 파일 상태

### 작성 완료
- `D:\project\ulalax\playhouse-net\tests\PlayHouse.Tests.E2E\ChatRoomE2ETests.cs`
  - 6개 카테고리, 18개 테스트
  - 진짜 E2E 패턴으로 재작성 완료
  - 서버 구현 완료 시 바로 실행 가능

### 테스트 커버리지
1. **Connection**: 클라이언트 연결 및 상태 관리 (2개)
2. **RequestResponse**: Stage 생성/입장 요청-응답 (3개)
3. **PushMessages**: 서버 푸시 메시지 수신 (2개)
4. **MultiClient**: 멀티 클라이언트 시나리오 (2개)
5. **ConnectionManagement**: 연결 해제 및 재연결 (2개)
6. **ErrorHandling**: 에러 케이스 처리 (2개)
7. **Bootstrap**: 서버 설정 및 생명주기 (5개)

## 다음 단계

1. **서버측 메시지 핸들링 구현** → E2E 테스트 통과
2. **메시지 ID 레지스트리** → 타입 안전성 확보
3. **통합 테스트 재분류** → 기존 테스트를 적절한 레벨로 이동
4. **성능 테스트** → 대규모 클라이언트 시나리오 추가

## 결론

E2E 테스트는 **올바른 패턴으로 재작성 완료**되었으나, **서버측 구현 부재**로 현재는 통과하지 못합니다.

PlayHouseClient는 완성되었지만, 서버가 클라이언트의 요청을 처리하고 응답을 보내는 로직이 필요합니다. 이는 프레임워크의 핵심 기능이므로 우선순위를 높여 구현해야 합니다.
