# PlayHouse-NET 테스트 스펙

## 1. 개요

PlayHouse-NET은 **Integration Test 우선** 전략을 채택합니다. 프레임워크 특성상 여러 컴포넌트의 상호작용을 검증하는 것이 더 중요하며, Integration Test는 **API 사용 가이드처럼** 읽혀야 합니다.

### 1.1 테스트 철학

**Integration Test 우선**
- API 사용 가이드로서의 Integration Test
- 입력, 출력, 부작용을 명시적으로 기술
- 실제 사용 시나리오 중심

**Unit Test는 보완용**
- Integration으로 테스트하기 어려운 경우만
- 스펙 문서로서의 Unit Test - "시스템이 어떻게 동작해야 하는가"를 명세
- 엣지케이스, 경계조건, 타이밍 이슈 검증

### 1.2 테스트 레벨 정의

| 레벨 | 대상 | 특징 | 비율 |
|------|------|------|------|
| **Integration Test** | Stage/Actor 상호작용, HTTP API, 클라이언트 연결 | 실제 동작 검증, API 가이드 | 70% |
| **Unit Test** | 패킷 파싱, 타이머 정밀도, 동시성 | 엣지케이스, 스펙 문서 | 20% |
| **E2E Test** | 전체 시나리오 (Web Server + Room Server + Client) | 실제 환경 검증 | 10% |

### 1.3 테스트 프로젝트 구조

```
PlayHouse-NET.sln
├── src/
│   ├── PlayHouse.Core/          # 핵심 엔진
│   ├── PlayHouse.Http/          # HTTP API
│   ├── PlayHouse.Backend/       # Backend SDK (Web Server용)
│   └── PlayHouse.Connector/     # .NET 클라이언트 (테스트용)
└── tests/
    ├── PlayHouse.IntegrationTests/    # Integration Test (주력)
    │   ├── StageLifecycleTests.cs
    │   ├── ActorLifecycleTests.cs
    │   ├── MessageRoutingTests.cs
    │   ├── ConnectionStateTests.cs
    │   ├── TimerSystemTests.cs
    │   ├── HttpApiTests.cs
    │   └── TestHelpers/
    ├── PlayHouse.UnitTests/            # Unit Test (보완)
    │   ├── PacketParsingTests.cs
    │   ├── TimerPrecisionTests.cs
    │   └── ConcurrencyTests.cs
    └── PlayHouse.E2E.Tests/            # E2E Test
        ├── RoomCreationE2ETests.cs
        ├── ChatRoomE2ETests.cs
        └── BattleRoomE2ETests.cs
```

## 2. 테스트 작성 규칙

### 2.1 Given-When-Then 구조

모든 테스트는 전제-행동-결과의 흐름이 명확히 구분되어야 합니다.

- **Given (전제조건)**: 테스트 실행 전 필요한 상태
- **When (행동)**: 테스트 대상 동작
- **Then (결과)**: 검증할 출력과 부작용

### 2.2 네이밍 규칙

**메서드명 (영문)**
```
[테스트대상]_[상황/조건]_[기대결과]
```

**DisplayName (한글)**
- 동작: "~할 때", "~시"로 시작
- 결과: "~됨", "~함"으로 종료
- 부작용: 콜백, 상태 변경, 브로드캐스트 등 명시

**예시**
| 메서드명 | DisplayName |
|----------|-------------|
| `CreateStage_ValidRequest_StageBecomesActive` | "Stage 생성 시 Active 상태가 됨" |
| `ConnectWithToken_InvalidToken_ConnectionRejected` | "잘못된 토큰으로 연결 시 연결이 거부됨" |
| `Broadcast_WithFilter_OnlyMatchingActorsReceive` | "필터를 사용한 브로드캐스트에서 조건에 맞는 Actor만 수신함" |

### 2.3 의미있는 테스트 데이터

테스트 데이터는 의도가 드러나는 이름을 사용합니다.

| 나쁜 예 | 좋은 예 |
|---------|---------|
| `accountId = 1` | `accountId = vipPlayerAccountId` |
| `roomType = "test"` | `roomType = "BattleStage"` |
| `message = "abc"` | `message = "Hello, World!"` |
| `token = "xxx"` | `token = expiredRoomToken` |

### 2.4 실패 메시지 가이드

Assertion 실패 시 기대값, 실제값, 변수 상태를 포함한 상세 메시지를 출력해야 합니다.

**나쁜 예**
```
Assert failed: false
```

**좋은 예**
```
Actor 상태 불일치: 기대값=Connected, 실제값=Disconnected, accountId=1001, stageId=12345
```

### 2.5 Fake 우선 원칙

외부 의존성은 Mock 프레임워크보다 Fake 객체 구현을 우선 사용합니다.

**Fake 사용**: Stage, Actor, RoomServerClient 등 도메인 객체
**Mock 허용**: HTTP Client, 타이머, 비동기 함수 등 Fake 구현 비용이 과도한 경우

### 2.6 상태 검증 vs 구현 검증

**상태 검증 (권장)**
- 시스템의 상태나 반환값이 올바른지 검증
- 예: "Actor가 Stage에 존재함", "응답 코드가 Success임"

**구현 검증 (금지)**
- 메서드가 호출되었는지(Verify) 검증
- 예: "OnJoinRoom이 1번 호출됨" → 이 방식은 지양

---

## 3. Integration Test 카테고리 구조

`architecture-guide.md`의 표준 카테고리를 적용합니다.

### 3.1 카테고리 표준

```
1. 기본 동작 (Basic Operations)      - API의 핵심 기능 검증
2. 응답 데이터 검증 (Response Validation) - 반환값의 형식과 제약조건
3. 입력 파라미터 검증 (Input Validation)  - 파라미터 조합과 경계값
4. 엣지 케이스 (Edge Cases)          - 예외 상황과 오류 처리
5. 실무 활용 예제 (Usage Examples)    - 실제 사용 패턴 시연
```

### 3.2 테스트 클래스 구조 예시

```
StageLifecycleTests
├── BasicOperations (기본 동작)
│   ├── Stage 생성 시 Active 상태가 됨
│   ├── Stage 종료 시 모든 리소스가 정리됨
│   └── ...
├── ResponseValidation (응답 데이터 검증)
│   ├── Stage 생성 응답에 stageId와 endpoint가 포함됨
│   └── ...
├── InputValidation (입력 파라미터 검증)
│   ├── roomType이 빈 문자열이면 에러 반환
│   └── ...
├── EdgeCases (엣지 케이스)
│   ├── 이미 종료된 Stage에 입장 시도 시 에러 반환
│   └── ...
└── UsageExamples (실무 활용 예제)
    ├── 채팅방 생성부터 메시지 전송까지
    └── ...
```

---

## 4. Integration Test 시나리오: 연결 및 인증

### 4.1 기본 동작 (Basic Operations)

#### 4.1.1 HTTP API로 방 생성 토큰 발급 후 소켓 연결 성공

**Given (전제조건)**
- Room Server가 실행 중
- HTTP API 엔드포인트가 활성화됨

**When (행동)**
- HTTP API `POST /api/rooms/get-or-create` 요청 (roomType, accountId, userInfo)

**Then (결과)**
- **출력**: roomToken, endpoint, stageId가 포함된 성공 응답
- **부작용**: Stage가 생성되고 Active 상태가 됨

**테스트**: `GetOrCreateRoom_NewRoom_ReturnsTokenAndCreatesStage`
**DisplayName**: "HTTP API로 새 방 생성 시 토큰이 발급되고 Stage가 생성됨"

---

#### 4.1.2 발급된 토큰으로 소켓 연결 및 Stage 입장 성공

**Given (전제조건)**
- HTTP API로 roomToken 발급 완료
- Stage가 생성되어 대기 중

**When (행동)**
- TCP/WebSocket 연결 후 roomToken으로 인증 요청

**Then (결과)**
- **출력**: stageId, stageInfo가 포함된 입장 성공 응답
- **부작용**: Actor가 Stage에 추가되고 연결 상태가 됨

**테스트**: `ConnectWithToken_ValidToken_JoinsStageSuccessfully`
**DisplayName**: "유효한 토큰으로 연결 시 Stage에 입장함"

---

### 4.2 응답 데이터 검증 (Response Validation)

#### 4.2.1 입장 응답에 필수 필드가 모두 포함됨

**Given (전제조건)**
- 유효한 roomToken으로 연결

**When (행동)**
- Stage 입장 완료

**Then (결과)**
- **출력**: 응답에 다음 필드 포함
  - stageId (0보다 큰 정수)
  - stageInfo (Stage 상태 정보)
  - isReconnect (false)

**테스트**: `JoinRoom_Success_ResponseContainsAllRequiredFields`
**DisplayName**: "입장 성공 응답에 stageId, stageInfo, isReconnect가 포함됨"

---

### 4.3 입력 파라미터 검증 (Input Validation)

#### 4.3.1 잘못된 토큰으로 연결 시 인증 실패

**Given (전제조건)**
- Room Server가 실행 중

**When (행동)**
- 존재하지 않는 토큰으로 연결 시도

**Then (결과)**
- **출력**: Unauthorized 에러 코드
- **부작용**: 연결이 종료됨

**테스트**: `ConnectWithToken_InvalidToken_ReturnsUnauthorized`
**DisplayName**: "잘못된 토큰으로 연결 시 Unauthorized가 반환됨"

---

#### 4.3.2 만료된 토큰으로 연결 시 인증 실패

**Given (전제조건)**
- 발급 후 TTL이 지난 roomToken

**When (행동)**
- 만료된 토큰으로 연결 시도

**Then (결과)**
- **출력**: TokenExpired 에러 코드
- **부작용**: 연결이 종료됨

**테스트**: `ConnectWithToken_ExpiredToken_ReturnsTokenExpired`
**DisplayName**: "만료된 토큰으로 연결 시 TokenExpired가 반환됨"

---

### 4.4 엣지 케이스 (Edge Cases)

#### 4.4.1 Stage가 가득 찬 상태에서 입장 시도

**Given (전제조건)**
- Stage의 maxPlayers가 4명
- 이미 4명의 Actor가 입장해 있음

**When (행동)**
- 5번째 플레이어가 입장 시도

**Then (결과)**
- **출력**: StageFull 에러 코드
- **부작용**: 토큰은 발급되지만 입장 거부됨

**테스트**: `ConnectWithToken_StageFull_ReturnsStageFull`
**DisplayName**: "Stage가 가득 찬 상태에서 입장 시도 시 StageFull이 반환됨"

---

#### 4.4.2 Stage가 종료된 후 입장 시도

**Given (전제조건)**
- Stage가 CloseStage()로 종료됨
- 종료 전에 발급된 roomToken 보유

**When (행동)**
- 종료된 Stage에 입장 시도

**Then (결과)**
- **출력**: StageNotFound 에러 코드
- **부작용**: 연결이 종료됨

**테스트**: `ConnectWithToken_StageAlreadyClosed_ReturnsStageNotFound`
**DisplayName**: "종료된 Stage에 입장 시도 시 StageNotFound가 반환됨"

---

## 5. Integration Test 시나리오: Stage 라이프사이클

### 5.1 기본 동작 (Basic Operations)

#### 5.1.1 Stage 생성 시 Active 상태가 됨

**Given (전제조건)**
- Room Server가 실행 중
- 해당 roomType의 Stage가 등록됨

**When (행동)**
- HTTP API로 Stage 생성 요청

**Then (결과)**
- **출력**: Stage 생성 성공 응답
- **부작용**: Stage가 Active 상태로 전환됨

**테스트**: `CreateStage_ValidRequest_StageBecomesActive`
**DisplayName**: "Stage 생성 시 Active 상태가 됨"

---

#### 5.1.2 CloseStage 호출 시 모든 리소스가 정리됨

**Given (전제조건)**
- Stage가 Active 상태
- 2명의 Actor가 입장해 있음
- 3개의 타이머가 등록되어 있음

**When (행동)**
- CloseStage() 호출

**Then (결과)**
- **출력**: 없음
- **부작용**:
  - 모든 Actor가 퇴장 처리됨
  - 모든 타이머가 취소됨
  - Stage가 제거됨

**테스트**: `CloseStage_WithActiveActors_RemovesAllActorsAndTimers`
**DisplayName**: "Stage 종료 시 모든 Actor가 퇴장 처리되고 타이머가 정리됨"

---

### 5.2 엣지 케이스 (Edge Cases)

#### 5.2.1 마지막 Actor 퇴장 시 Stage 자동 종료 (옵션에 따라)

**Given (전제조건)**
- Stage의 autoCloseOnEmpty 옵션이 true
- 1명의 Actor만 입장해 있음

**When (행동)**
- 마지막 Actor가 퇴장

**Then (결과)**
- **출력**: 없음
- **부작용**: Stage가 자동으로 종료됨

**테스트**: `LeaveRoom_LastActor_StageAutoCloses`
**DisplayName**: "autoCloseOnEmpty 옵션이 켜진 상태에서 마지막 Actor 퇴장 시 Stage가 자동 종료됨"

---

## 6. Integration Test 시나리오: 메시지 송수신

### 6.1 기본 동작 (Basic Operations)

#### 6.1.1 Request-Reply 패턴으로 정상 응답 수신

**Given (전제조건)**
- 클라이언트가 Stage에 연결됨

**When (행동)**
- Request 메시지 전송 (MsgSeq 포함)

**Then (결과)**
- **출력**: 동일한 MsgSeq를 가진 Reply 메시지 수신
- **부작용**: Stage에서 해당 요청이 처리됨

**테스트**: `RequestReply_ValidRequest_ReturnsResponseWithSameMsgSeq`
**DisplayName**: "Request-Reply 패턴에서 요청과 응답의 MsgSeq가 일치함"

---

#### 6.1.2 Fire-and-Forget 메시지 전송

**Given (전제조건)**
- 클라이언트가 Stage에 연결됨

**When (행동)**
- MsgSeq=0으로 메시지 전송

**Then (결과)**
- **출력**: 응답 없음 (Fire-and-Forget)
- **부작용**: Stage에서 해당 메시지가 처리됨

**테스트**: `FireAndForget_Message_ServerReceivesWithoutResponse`
**DisplayName**: "Fire-and-Forget 메시지 전송 시 서버가 수신하고 응답하지 않음"

---

#### 6.1.3 브로드캐스트 시 모든 Actor가 메시지 수신

**Given (전제조건)**
- Stage에 3명의 Actor가 연결됨

**When (행동)**
- Stage에서 Broadcast 호출

**Then (결과)**
- **출력**: 없음
- **부작용**: 3명의 클라이언트 모두 메시지 수신

**테스트**: `Broadcast_ToAllActors_AllClientsReceiveMessage`
**DisplayName**: "브로드캐스트 시 Stage 내 모든 Actor가 메시지를 수신함"

---

### 6.2 입력 파라미터 검증 (Input Validation)

#### 6.2.1 필터를 사용한 브로드캐스트

**Given (전제조건)**
- Stage에 3명의 Actor가 연결됨 (accountId: 1001, 1002, 1003)

**When (행동)**
- 1001을 제외한 필터로 Broadcast 호출

**Then (결과)**
- **출력**: 없음
- **부작용**: 1002, 1003만 메시지 수신, 1001은 수신하지 않음

**테스트**: `Broadcast_WithFilter_OnlyMatchingActorsReceive`
**DisplayName**: "필터를 사용한 브로드캐스트에서 조건에 맞는 Actor만 메시지를 수신함"

---

### 6.3 엣지 케이스 (Edge Cases)

#### 6.3.1 타임아웃 시간 내에 응답이 없으면 에러

**Given (전제조건)**
- 클라이언트가 Stage에 연결됨
- 요청 타임아웃이 1초로 설정됨
- 서버가 응답을 지연하도록 설정됨

**When (행동)**
- Request 메시지 전송 후 1초 대기

**Then (결과)**
- **출력**: Timeout 에러
- **부작용**: 요청이 취소됨

**테스트**: `RequestReply_Timeout_ReturnsTimeoutError`
**DisplayName**: "타임아웃 시간 내에 응답이 없으면 Timeout 에러가 반환됨"

---

## 7. Integration Test 시나리오: 연결 상태 관리

### 7.1 기본 동작 (Basic Operations)

#### 7.1.1 네트워크 끊김 시 Actor는 Stage에 유지됨

**Given (전제조건)**
- Actor가 Stage에 입장해 있음

**When (행동)**
- TCP 연결 강제 종료

**Then (결과)**
- **출력**: 없음
- **부작용**:
  - Actor는 Stage에 유지됨 (IsConnected=false)
  - 재연결 타임아웃 타이머가 시작됨

**테스트**: `NetworkDisconnect_ActorRemainsInStage`
**DisplayName**: "네트워크 끊김 시 Actor는 Stage에 유지됨"

---

#### 7.1.2 재연결 성공 시 기존 세션 복구

**Given (전제조건)**
- Actor가 연결 끊김 상태 (IsConnected=false)
- 재연결 타임아웃 전

**When (행동)**
- 기존 roomToken으로 재연결 요청

**Then (결과)**
- **출력**: isReconnect=true가 포함된 입장 응답
- **부작용**:
  - Actor의 IsConnected가 true로 변경됨
  - 재연결 타이머가 취소됨

**테스트**: `Reconnect_WithinTimeout_RestoresSession`
**DisplayName**: "타임아웃 전 재연결 시 기존 세션이 복구됨"

---

### 7.2 엣지 케이스 (Edge Cases)

#### 7.2.1 재연결 타임아웃 시 Actor 제거

**Given (전제조건)**
- Actor가 연결 끊김 상태
- 재연결 타임아웃이 30초로 설정됨

**When (행동)**
- 30초 경과

**Then (결과)**
- **출력**: 없음
- **부작용**: Actor가 Stage에서 제거됨

**테스트**: `ReconnectTimeout_RemovesActor`
**DisplayName**: "재연결 타임아웃 시 Actor가 Stage에서 제거됨"

---

#### 7.2.2 명시적 퇴장 요청 시 Actor 제거

**Given (전제조건)**
- Actor가 Stage에 입장해 있음

**When (행동)**
- LeaveRoom 요청 전송

**Then (결과)**
- **출력**: 퇴장 성공 응답
- **부작용**:
  - Actor가 Stage에서 제거됨
  - 연결이 종료됨

**테스트**: `LeaveRoom_UserRequest_RemovesActorAndClosesConnection`
**DisplayName**: "LeaveRoom 요청 시 Actor가 제거되고 연결이 종료됨"

---

## 8. Integration Test 시나리오: 타이머

### 8.1 기본 동작 (Basic Operations)

#### 8.1.1 RepeatTimer가 주기적으로 실행됨

**Given (전제조건)**
- Stage가 생성됨

**When (행동)**
- 100ms 간격의 RepeatTimer 등록 후 500ms 대기

**Then (결과)**
- **출력**: timerId 반환
- **부작용**: 약 5회의 타이머 콜백이 실행됨 (타이밍 여유 고려)

**테스트**: `AddRepeatTimer_PeriodicExecution_CallbackInvokedMultipleTimes`
**DisplayName**: "RepeatTimer 등록 시 주기적으로 콜백이 여러 번 실행됨"

---

#### 8.1.2 CountTimer가 지정된 횟수만큼 실행되고 자동 중지됨

**Given (전제조건)**
- Stage가 생성됨

**When (행동)**
- 100ms 간격, 3회 실행의 CountTimer 등록 후 500ms 대기

**Then (결과)**
- **출력**: timerId 반환
- **부작용**: 정확히 3회의 콜백이 실행되고 자동 중지됨

**테스트**: `AddCountTimer_LimitedExecution_StopsAfterCount`
**DisplayName**: "CountTimer는 지정된 횟수만큼만 실행되고 자동 중지됨"

---

### 8.2 엣지 케이스 (Edge Cases)

#### 8.2.1 타이머 취소 시 콜백이 더 이상 실행되지 않음

**Given (전제조건)**
- RepeatTimer가 실행 중

**When (행동)**
- CancelTimer 호출 후 추가 500ms 대기

**Then (결과)**
- **출력**: 없음
- **부작용**: 취소 이후 콜백이 더 이상 실행되지 않음

**테스트**: `CancelTimer_StopsExecution_NoMoreCallbacks`
**DisplayName**: "타이머 취소 시 콜백이 더 이상 실행되지 않음"

---

#### 8.2.2 Stage 종료 시 모든 타이머가 자동 정리됨

**Given (전제조건)**
- Stage에 여러 타이머가 등록됨

**When (행동)**
- CloseStage() 호출

**Then (결과)**
- **출력**: 없음
- **부작용**: 모든 타이머가 취소되고 콜백이 더 이상 실행되지 않음

**테스트**: `CloseStage_CancelsAllTimers_NoMoreCallbacks`
**DisplayName**: "Stage 종료 시 등록된 모든 타이머가 자동으로 정리됨"

---

## 9. Integration Test 시나리오: HTTP API

### 9.1 기본 동작 (Basic Operations)

#### 9.1.1 GetOrCreateRoom - 새 방 생성

**Given (전제조건)**
- Room Server가 실행 중

**When (행동)**
- `POST /api/rooms/get-or-create` 요청 (roomType, roomId=null)

**Then (결과)**
- **출력**: isNewRoom=true, roomToken, endpoint, stageId
- **부작용**: Stage가 생성됨

**테스트**: `HttpApi_GetOrCreateRoom_NewRoom_CreatesStageAndReturnsToken`
**DisplayName**: "HTTP API - 새 방 생성 시 Stage가 생성되고 토큰이 발급됨"

---

#### 9.1.2 GetOrCreateRoom - 기존 방 입장

**Given (전제조건)**
- stageId=12345인 Stage가 존재함

**When (행동)**
- `POST /api/rooms/get-or-create` 요청 (roomType, roomId=12345)

**Then (결과)**
- **출력**: isNewRoom=false, roomToken, endpoint, stageId=12345
- **부작용**: 기존 Stage 재사용, 새 Stage 생성되지 않음

**테스트**: `HttpApi_GetOrCreateRoom_ExistingRoom_ReusesStageAndReturnsToken`
**DisplayName**: "HTTP API - 기존 방 입장 시 Stage를 재사용하고 토큰을 발급함"

---

### 9.2 엣지 케이스 (Edge Cases)

#### 9.2.1 존재하지 않는 방 입장 시도

**Given (전제조건)**
- stageId=99999인 Stage가 없음

**When (행동)**
- `POST /api/rooms/join` 요청 (stageId=99999)

**Then (결과)**
- **출력**: RoomNotFound 에러
- **부작용**: 없음

**테스트**: `HttpApi_JoinRoom_NonExistentStage_ReturnsNotFound`
**DisplayName**: "HTTP API - 존재하지 않는 방 입장 시 NotFound 에러 반환"

---

## 10. Integration Test 시나리오: E2E

### 10.1 실무 활용 예제 (Usage Examples)

#### 10.1.1 채팅방: 2명의 클라이언트가 메시지를 주고받음

**Given (전제조건)**
- Room Server가 실행 중

**When (행동)**
1. Client1이 HTTP API로 방 생성 토큰 발급
2. Client1이 소켓 연결 및 입장
3. Client2가 HTTP API로 같은 방 입장 토큰 발급
4. Client2가 소켓 연결 및 입장
5. Client1이 채팅 메시지 전송

**Then (결과)**
- **출력**: Client2가 Client1의 메시지를 수신
- **부작용**: Stage에서 브로드캐스트가 수행됨

**테스트**: `E2E_ChatRoom_TwoClientsExchangeMessages`
**DisplayName**: "E2E - 2명의 클라이언트가 채팅방에서 메시지를 주고받음"

---

#### 10.1.2 배틀룸: 4명 입장 시 게임 자동 시작

**Given (전제조건)**
- Room Server가 실행 중
- BattleStage는 4명이 모이면 자동 시작하도록 구현됨

**When (행동)**
1. 4명의 클라이언트가 순차적으로 입장
2. 4번째 클라이언트 입장 완료

**Then (결과)**
- **출력**: 4명 모두 게임 시작 알림 수신
- **부작용**: Stage에서 게임 시작 로직이 실행됨

**테스트**: `E2E_BattleRoom_FourPlayersAutoStartGame`
**DisplayName**: "E2E - 4명의 플레이어가 입장하면 배틀이 자동 시작됨"

---

#### 10.1.3 재연결: 연결 끊김 후 게임 상태 복구

**Given (전제조건)**
- 게임이 진행 중인 Stage에 Actor가 입장해 있음

**When (행동)**
1. 클라이언트 네트워크 연결 끊김
2. 재연결 타임아웃 전에 재연결

**Then (결과)**
- **출력**: isReconnect=true와 현재 게임 상태 수신
- **부작용**: Actor의 연결 상태가 복구됨

**테스트**: `E2E_Reconnect_RestoresGameState`
**DisplayName**: "E2E - 재연결 시 게임 상태가 복구됨"

---

## 11. Unit Test 시나리오 (보완용)

Unit Test는 Integration Test로 검증하기 어려운 **엣지케이스, 경계조건, 타이밍 이슈**를 검증합니다.
**스펙 문서처럼 읽혀야 합니다** - "시스템이 어떻게 동작해야 하는가"를 명세합니다.

### 11.1 패킷 파싱 스펙

#### 11.1.1 패킷 크기 제한

**시스템 동작 명세**
- 패킷 파서는 MaxPacketSize(기본 2MB)를 초과하는 패킷을 거부해야 한다
- 패킷 크기 초과 시 PacketException이 발생해야 한다
- 예외 발생 시 연결이 종료되어야 한다

**검증 시나리오**

| 입력 크기 | 기대 동작 |
|-----------|-----------|
| 1MB | 정상 파싱 |
| 2MB (경계값) | 정상 파싱 |
| 2MB + 1byte | PacketException 발생 |
| 3MB | PacketException 발생 |

**테스트**: `PacketParsing_ExceedsMaxSize_ThrowsPacketException`
**DisplayName**: "패킷 크기가 MaxPacketSize를 초과하면 PacketException이 발생함"

---

#### 11.1.2 분할된 패킷 처리

**시스템 동작 명세**
- 패킷 파서는 ReadOnlySequence가 여러 세그먼트로 나뉜 경우에도 정상 파싱해야 한다
- 네트워크 버퍼 분할과 무관하게 패킷 무결성이 보장되어야 한다

**검증 시나리오**

| 세그먼트 수 | 기대 동작 |
|-------------|-----------|
| 1개 (연속) | 정상 파싱 |
| 2개 (헤더/바디 분리) | 정상 파싱 |
| 3개 이상 | 정상 파싱 |
| 헤더가 2개 세그먼트에 걸침 | 정상 파싱 |

**테스트**: `PacketParsing_MultipleSegments_ParsesCorrectly`
**DisplayName**: "패킷이 여러 세그먼트로 나뉘어도 정상 파싱됨"

---

#### 11.1.3 불완전 패킷 대기

**시스템 동작 명세**
- 패킷 파서는 헤더만 도착하고 바디가 없는 경우 추가 데이터를 대기해야 한다
- 불완전 패킷에 대해 예외를 발생시키지 않아야 한다
- 나머지 데이터가 도착하면 완전한 패킷으로 파싱해야 한다

**검증 시나리오**

| 도착 데이터 | 기대 동작 |
|-------------|-----------|
| 헤더 일부 | 대기 (파싱 안함) |
| 헤더 전체, 바디 없음 | 대기 (파싱 안함) |
| 헤더 + 바디 일부 | 대기 (파싱 안함) |
| 헤더 + 바디 전체 | 완전한 패킷 반환 |

**테스트**: `PacketParsing_IncompletePacket_WaitsForMoreData`
**DisplayName**: "불완전 패킷 수신 시 추가 데이터를 대기함"

---

### 11.2 타이머 정밀도 스펙

#### 11.2.1 RepeatTimer 실행 간격 정밀도

**시스템 동작 명세**
- RepeatTimer의 실제 실행 간격은 설정값의 ±10% 오차 내에 있어야 한다
- 시스템 부하 상황에서도 극단적인 지연이 발생하지 않아야 한다

**검증 시나리오**

| 설정 간격 | 허용 오차 | 기대 평균 간격 |
|-----------|-----------|----------------|
| 100ms | ±10% | 90~110ms |
| 500ms | ±10% | 450~550ms |
| 1000ms | ±10% | 900~1100ms |

**테스트**: `TimerPrecision_RepeatTimer_WithinToleranceRange`
**DisplayName**: "RepeatTimer 실행 간격이 설정값의 ±10% 오차 내"

---

#### 11.2.2 짧은 간격 타이머 제한

**시스템 동작 명세**
- 최소 타이머 간격(기본 10ms) 미만의 간격은 허용되지 않아야 한다
- 최소 간격 미만 설정 시 ArgumentException이 발생해야 한다

**검증 시나리오**

| 설정 간격 | 기대 동작 |
|-----------|-----------|
| 10ms (최소값) | 정상 등록 |
| 9ms | ArgumentException 발생 |
| 1ms | ArgumentException 발생 |
| 0ms | ArgumentException 발생 |

**테스트**: `AddTimer_IntervalBelowMinimum_ThrowsArgumentException`
**DisplayName**: "최소 간격 미만의 타이머 등록 시 ArgumentException이 발생함"

---

### 11.3 동시성 스펙

#### 11.3.1 동시 메시지 처리 순서 보장

**시스템 동작 명세**
- Stage는 여러 Actor로부터 동시에 메시지를 수신해도 순차적으로 처리해야 한다
- 각 Actor별로 메시지 순서가 보장되어야 한다
- 메시지 유실 없이 모든 메시지가 처리되어야 한다

**검증 시나리오**

| 동시 전송 수 | 기대 동작 |
|--------------|-----------|
| 10개 스레드 × 100개 메시지 | 1000개 모두 처리됨 |
| 각 스레드 내 메시지 순서 | 순서 보장됨 |

**테스트**: `Concurrency_MultipleThreadsSendMessages_AllProcessedInOrder`
**DisplayName**: "여러 스레드에서 동시 메시지 전송 시 모두 순서대로 처리됨"

---

#### 11.3.2 동시 연결/해제 안정성

**시스템 동작 명세**
- 여러 클라이언트가 동시에 연결/해제해도 시스템이 안정적으로 동작해야 한다
- 데드락이나 레이스 컨디션이 발생하지 않아야 한다
- 모든 연결/해제가 정상적으로 완료되어야 한다

**검증 시나리오**

| 동시 작업 | 기대 동작 |
|-----------|-----------|
| 10개 동시 연결 | 모두 성공 |
| 5개 연결 + 5개 해제 동시 | 모두 성공, 데드락 없음 |
| 연결 중 해제 요청 | 정상 처리 |

**테스트**: `Concurrency_SimultaneousConnectDisconnect_NoDeadlock`
**DisplayName**: "동시 연결/해제 시 데드락이 발생하지 않음"

---

## 12. 테스트 환경 구성 가이드

### 12.1 테스트 서버 설정

테스트용 Room Server는 다음 설정으로 시작합니다:
- 랜덤 포트 사용 (테스트 간 충돌 방지)
- 최대 연결 수 100으로 제한
- 재연결 타임아웃 5초 (테스트 속도를 위해 단축)

### 12.2 테스트 격리

각 테스트는 독립적으로 실행 가능해야 합니다:
- TestInitialize에서 서버 시작
- TestCleanup에서 서버 종료 및 리소스 정리
- 테스트 간 상태 공유 금지

### 12.3 Fake 객체 구현 가이드

Fake 객체는 실제 객체와 동일한 인터페이스를 구현하되, 테스트 검증을 위한 추가 기능을 제공합니다:
- 상태 추적 (현재 상태, 변경 이력)
- 동작 제어 (특정 시나리오 시뮬레이션)
- 검증 지원 (상태 조회 메서드)

---

## 13. 테스트 실행

### 13.1 테스트 실행 명령

**전체 테스트 실행**
```bash
dotnet test
```

**Integration Test만 실행**
```bash
dotnet test --filter "FullyQualifiedName~IntegrationTests"
```

**특정 카테고리 실행**
```bash
dotnet test --filter "TestCategory=StageLifecycle"
```

### 13.2 목표 커버리지

| 영역 | 목표 |
|------|------|
| 전체 | 80% 이상 |
| 핵심 엔진 (Stage/Actor) | 90% 이상 |
| HTTP API | 85% 이상 |
| 타이머 시스템 | 90% 이상 |

---

## 14. 베스트 프랙티스 요약

### Do (권장)

1. **Integration Test 우선** - API 사용 시나리오 중심으로 작성
2. **Given-When-Then 구조** - 모든 테스트에 명확한 구조 적용
3. **명확한 DisplayName** - 한글로 테스트 의도를 명확히 표현
4. **Fake 객체 활용** - Mock 프레임워크보다 Fake 우선
5. **상태 검증** - 시스템 상태나 반환값 검증
6. **의미있는 테스트 데이터** - 의도가 드러나는 이름 사용
7. **상세한 실패 메시지** - 기대값, 실제값, 변수 상태 포함

### Don't (금지)

1. **Unit Test 과다 사용** - Integration으로 가능한 것은 Unit으로 하지 말 것
2. **구현 검증** - 메서드 호출 횟수 검증 지양
3. **Sleep 남용** - Task.Delay 최소화, 이벤트 기반 대기 우선
4. **하드코딩된 타이밍** - 절대값 대신 범위로 검증
5. **테스트 간 의존성** - 실행 순서에 의존하지 말 것
6. **테스트 내 조건문** - if문 사용 금지, 분기가 필요하면 별도 테스트로 분리

---

## 15. 참조 문서

- [architecture-guide.md](architecture-guide.md): 테스트 설계 원칙
- [03-stage-actor-model.md](03-stage-actor-model.md): Stage/Actor 테스트 대상
- [09-connector.md](09-connector.md): 테스트용 클라이언트 구현
