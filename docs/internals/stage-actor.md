# PlayHouse-NET Stage/Actor 모델

## 1. 개요

Stage/Actor 모델은 PlayHouse-NET의 핵심 프로그래밍 모델로, 게임 로직을 **격리된 독립 단위**로 실행하여 동시성 문제를 원천적으로 방지합니다.

### 1.1 핵심 개념

- **Stage (방/룸)**: 게임 로직이 실행되는 논리적 컨테이너
- **Actor (플레이어)**: Stage 내의 개별 참가자
- **Message**: Stage/Actor 간 유일한 통신 수단
- **Lock-Free**: 메시지 큐를 통한 동시성 제어

### 1.2 설계 철학

```
격리 (Isolation)
- Stage는 독립된 상태 보유
- Actor는 Stage 내에서만 존재
- 공유 상태 최소화

메시지 기반 (Message-Driven)
- 모든 상호작용은 메시지로 처리
- 직접 메서드 호출 금지
- 비동기 처리

단일 스레드 보장 (Single-Threaded)
- Stage 내부는 단일 스레드 실행
- 동시성 문제 원천 차단
- Lock-Free 구현
```

## 2. Stage (방/룸) 상세

### 2.1 Stage 인터페이스

```csharp
#nullable enable

/// <summary>
/// 게임 로직이 실행되는 논리적 컨테이너 (방/룸).
/// </summary>
/// <remarks>
/// Stage는 lock-free 방식으로 메시지를 순차 처리합니다.
/// 내부 상태는 단일 스레드에서만 접근됩니다.
/// Actor는 연결이 끊겨도 Stage에 유지되며, 재연결을 지원합니다.
/// </remarks>
public interface IStage
{
    /// <summary>Stage 제어를 위한 Sender 인터페이스</summary>
    IStageSender StageSender { get; }

    // Stage 라이프사이클
    Task<(bool result, IPacket reply)> OnCreate(IPacket packet);
    Task OnPostCreate();
    Task OnDestroy();

    // Actor 입장
    Task<bool> OnJoinStage(IActor actor);
    Task OnPostJoinStage(IActor actor);

    // Actor 연결 상태 변경
    ValueTask OnConnectionChanged(IActor actor, bool isConnected);

    // 메시지 처리
    Task OnDispatch(IActor actor, IPacket packet);  // 클라이언트 메시지
    Task OnDispatch(IPacket packet);                // 서버간 메시지
}
```

**주요 설계 원칙:**
- **논리적 입장(OnJoinStage)과 물리적 연결(OnConnectionChanged) 분리**
  - `OnJoinStage` - Actor의 입장 허용 여부 결정
  - `OnConnectionChanged` - 연결/재연결/끊김 시마다 호출
  - Actor 퇴장은 `StageSender.LeaveStage()` 호출로 처리

**주요 .NET 스타일 적용:**
- `#nullable enable` - Nullable Reference Types 활성화
- `IPacket` - 생성 응답 패킷
- `IActor` - Stage 내에서 항상 존재하므로 non-nullable
- `ValueTask` - Hot path 메서드 (OnConnectionChanged)에 allocation 최적화

### 2.2 Stage 라이프사이클

```
[Stage 생명주기]

HTTP API: GetOrCreateRoom(roomType, roomId?)
    │
    ▼
┌─────────────────────┐
│    CreateStage      │  HTTP API 요청
│                     │  - 기존 Stage 있으면 재사용
│                     │  - 없으면 새로 생성
└──────────┬──────────┘
           │
           ▼
┌─────────────────────┐
│  Stage 인스턴스     │  Stage 객체 생성
│  생성 및 할당       │  StageId 부여
└──────────┬──────────┘
           │
           ▼
┌─────────────────────┐
│    OnCreate()       │  사용자 초기화 로직
│                     │  - 초기 상태 설정
│  return (errorCode, │  - 자원 할당
│          reply)     │  - 성공/실패 반환
└──────────┬──────────┘
           │
           ▼
┌─────────────────────┐
│  OnPostCreate()     │  생성 완료 후처리
│                     │  - 타이머 등록
│  return Task        │  - 초기 이벤트 발생
└──────────┬──────────┘
           │
           ▼
┌─────────────────────┐
│   Active State      │  메시지 처리 준비 완료
│                     │
│  - OnJoinStage()     │  Actor 입장 허용
│  - OnPostJoinStage() │  입장 완료 후처리
│  - OnDispatch()     │  메시지 처리
│  - OnConnection     │  연결 상태 변경
│      Changed()      │
└──────────┬──────────┘
           │
           ▼
┌─────────────────────┐
│   CloseStage()      │  명시적 종료 또는
│                     │  조건 충족 시 자동 종료
└──────────┬──────────┘
           │
           ▼
┌─────────────────────┐
│   Cleanup           │  - 모든 Actor 제거
│                     │  - 타이머 정리
│                     │  - 자원 해제
└─────────────────────┘
```

### 2.3 Stage 생성 및 입장 시나리오

새 설계에서는 HTTP API를 통해 토큰을 발급받고, 클라이언트가 토큰으로 직접 연결합니다.

#### 시나리오 1: 새 Stage 생성 및 입장

```
[1. HTTP API 요청 - 토큰 발급]

Web Server: GetOrCreateRoom(roomType, userInfo)
    │
    ├─→ Room Server: CreateStage (새 Stage 생성)
    │       │
    │       ▼
    │   OnCreate(initPacket)
    │       │
    │       ▼
    │   OnPostCreate()
    │       │
    │       ▼
    │   Active (대기 중)
    │
    └─→ 응답: { roomToken, endpoint }


[2. 클라이언트 연결 - 입장]

Client: TCP/WebSocket Connect (with roomToken)
    │
    ▼
Room Server: ValidateToken → JoinRoom
    │
    ▼
OnJoinStage(actor, userInfo)    ← Stage 콜백
    │
    ▼
actor.OnCreate()               ← Actor 최초 생성
    │
    ▼
actor.OnAuthenticate(authData) ← Actor 인증 완료
    │
    ▼
OnPostJoinStage(actor)          ← Stage 콜백
    │
    ▼
OnConnectionChanged(actor, isConnected=true)
    │
    ▼
Active (플레이 중)
```

#### 시나리오 2: 기존 Stage 입장

```
[1. HTTP API 요청 - 기존 Stage 토큰 발급]

Web Server: GetOrCreateRoom(roomType, roomId?, userInfo)
    │
    ├─→ Room Server: FindStage(roomId)  ← 기존 Stage 확인
    │       │
    │       └─→ Stage 존재: 토큰 발급
    │
    └─→ 응답: { roomToken, endpoint }


[2. 클라이언트 연결 - 기존 Stage 입장]

Client: TCP/WebSocket Connect (with roomToken)
    │
    ▼
Room Server: ValidateToken → JoinRoom
    │
    ▼
OnJoinStage(actor, userInfo)    ← Stage 콜백
    │
    ▼
actor.OnCreate()
    │
    ▼
actor.OnAuthenticate(authData)
    │
    ▼
OnPostJoinStage(actor)
    │
    ▼
OnConnectionChanged(actor, isConnected=true)
    │
    ▼
Active (플레이 중)
```

#### 시나리오 3: 재연결

```
[재연결 - OnJoinStage 호출 안 함]

Client: TCP/WebSocket Reconnect (with roomToken)
    │
    ▼
Room Server: ValidateToken
    │
    ├─→ AccountId로 기존 Actor 찾기
    │
    └─→ Actor 세션 갱신 (새 SessionId)
            │
            ▼
actor.OnAuthenticate(authData) ← 재연결 시에도 호출!
            │
            ▼
OnConnectionChanged(actor, isConnected=true)
            │
            ▼
Active (다시 활성화)
```

### 2.4 주요 콜백 메서드

#### OnCreate

```csharp
/// <summary>
/// Stage 생성 시 호출
/// </summary>
/// <param name="packet">생성 요청 패킷 (초기 설정 데이터)</param>
/// <returns>성공 여부 및 응답 패킷</returns>
Task<(bool result, IPacket reply)> OnCreate(IPacket packet);

// 사용 예시
public class GameStage : IStage
{
    public async Task<(bool, IPacket)> OnCreate(IPacket packet)
    {
        // 초기 설정 파싱
        var config = packet.Parse<StageConfig>();

        // 상태 초기화
        _maxPlayers = config.MaxPlayers;
        _gameMode = config.GameMode;

        // 성공 응답
        return (true, CPacket.Of(new CreateStageReply { Success = true }));
    }
}
```

#### OnPostCreate

```csharp
/// <summary>
/// Stage 생성 완료 후 호출 (비동기 후처리)
/// </summary>
Task OnPostCreate();

// 사용 예시
public async Task OnPostCreate()
{
    // 주기 타이머 등록 (게임 틱)
    StageSender.AddRepeatTimer(
        TimeSpan.FromSeconds(1), // 1초마다
        OnGameTick               // 콜백
    );

    // 초기 이벤트 발생
    await BroadcastEvent("StageReady");
}
```

#### OnJoinStage

```csharp
/// <summary>
/// Actor가 Stage에 논리적으로 입장할 때 호출 (최초 1회)
/// </summary>
/// <param name="actor">입장하는 Actor</param>
/// <returns>true면 입장 허용, false면 거부</returns>
/// <remarks>
/// Actor가 Stage에 입장하려 할 때 호출됩니다.
/// 재연결 시에는 호출되지 않습니다.
/// </remarks>
Task<bool> OnJoinStage(IActor actor);

// 사용 예시
public async Task<bool> OnJoinStage(IActor actor)
{
    // 인원 체크
    if (_actors.Count >= _maxPlayers)
    {
        return false;  // 입장 거부
    }

    // Actor 목록에 추가
    _actors.Add(actor);

    // 다른 플레이어에게 알림
    var notify = CPacket.Of(new PlayerJoinedNotify { AccountId = actor.ActorSender.AccountId });
    BroadcastToOthers(actor, notify);

    return true;  // 입장 허용
}
```

#### OnPostJoinStage

```csharp
/// <summary>
/// Actor 입장 완료 후 호출
/// </summary>
/// <param name="actor">입장 완료된 Actor</param>
Task OnPostJoinStage(IActor actor);

// 사용 예시
public async Task OnPostJoinStage(IActor actor)
{
    // 게임 시작 조건 확인
    if (_players.Count == _maxPlayers)
    {
        await StartGame();
    }
}
```

#### OnConnectionChanged

```csharp
/// <summary>
/// Actor 연결 상태 변경 시 호출 (연결/끊김)
/// </summary>
/// <param name="actor">상태가 변경된 Actor</param>
/// <param name="isConnected">현재 연결 상태</param>
/// <remarks>
/// isConnected=true: 연결됨
/// isConnected=false: 연결 끊김
/// </remarks>
ValueTask OnConnectionChanged(IActor actor, bool isConnected);

// 사용 예시
public async ValueTask OnConnectionChanged(IActor actor, bool isConnected)
{
    var accountId = actor.ActorSender.AccountId;

    if (isConnected)
    {
        LOG.Info($"Player connected: {accountId}");

        // 재연결 타이머 취소
        if (_reconnectTimers.TryGetValue(accountId, out var timerId))
        {
            StageSender.CancelTimer(timerId);
            _reconnectTimers.Remove(accountId);
        }

        // 다른 플레이어에게 알림
        BroadcastToOthers(actor, CPacket.Of(new PlayerReconnectedNotify { AccountId = accountId }));
    }
    else
    {
        LOG.Info($"Player disconnected: {accountId}");

        // 재연결 타이머 시작 (30초)
        var timerId = StageSender.AddCountTimer(
            TimeSpan.FromSeconds(30),
            TimeSpan.Zero,
            1,
            async () => await HandleReconnectTimeout(actor));
        _reconnectTimers[accountId] = timerId;

        // 다른 플레이어에게 알림
        BroadcastToOthers(actor, CPacket.Of(new PlayerDisconnectedNotify { AccountId = accountId }));
    }
}
```


#### OnDispatch

```csharp
/// <summary>
/// Stage로 메시지 수신 시 호출
/// </summary>
/// <param name="actor">발신 Actor</param>
/// <param name="packet">메시지 패킷</param>
Task OnDispatch(IActor actor, IPacket packet);

// 사용 예시
public async Task OnDispatch(IActor actor, IPacket packet)
{
    switch (packet.MsgId)
    {
        case "PlayerMove":
            await HandlePlayerMove(actor, packet);
            break;

        case "PlayerAttack":
            await HandlePlayerAttack(actor, packet);
            break;

        case "ChatMessage":
            await HandleChatMessage(actor, packet);
            break;

        default:
            LOG.Warn($"Unknown message: {packet.MsgId}");
            break;
    }
}
```

## 3. Actor (플레이어) 상세

### 3.1 Actor 인터페이스

```csharp
#nullable enable

/// <summary>
/// Stage 내의 개별 참가자 (플레이어).
/// </summary>
/// <remarks>
/// Actor는 Stage 내에서만 존재하며, Stage 생명주기에 종속됩니다.
/// OnAuthenticate에서 반드시 ActorSender.AccountId를 설정해야 합니다.
/// </remarks>
public interface IActor
{
    /// <summary>Actor 메시지 전송을 위한 Sender 인터페이스</summary>
    IActorSender ActorSender { get; }

    // 라이프사이클 메서드
    Task OnCreate();                                              // Actor 생성 시
    Task OnDestroy();                                             // Actor 파괴 시

    // 인증 콜백
    Task<(bool result, IPacket? reply)> OnAuthenticate(IPacket authPacket);  // 인증 처리
    Task OnPostAuthenticate();                                    // 인증 완료 후 처리
}
```

**주요 특징:**
- `OnAuthenticate` - 인증 성공/실패와 응답 패킷 반환
- `OnPostAuthenticate` - 인증 완료 후 API 서버 호출 등 후처리
- **중요**: `OnAuthenticate`에서 반드시 `ActorSender.AccountId`를 설정해야 함

### 3.2 Actor 라이프사이클

Actor는 **논리적 입장**(OnJoinStage)과 **물리적 연결**(OnAuthenticate)이 분리되어 있습니다.
연결이 끊겨도 Actor는 Stage에 유지되어 재연결을 지원합니다.

```
═══════════════════════════════════════════════════════════════
                        최초 입장
═══════════════════════════════════════════════════════════════

Client: TCP/WebSocket Connect (with roomToken)
    │
    ▼
┌─────────────────────┐
│  Socket Connected   │  물리적 연결 수립
│                     │  SessionId 할당
└──────────┬──────────┘
           │
           ▼
┌─────────────────────┐
│  ValidateToken      │  Room Server 내부
│  (토큰 검증)        │  - 토큰 유효성 확인
│                     │  - AccountId 추출
└──────────┬──────────┘
           │
           ▼
Web Server: JoinRoom(roomToken, accountId, userInfo)
    │
    ▼
┌─────────────────────┐
│  Actor 인스턴스     │  Actor 객체 생성
│  생성 및 할당       │  AccountId 매핑
└──────────┬──────────┘
           │
           ▼
┌─────────────────────┐
│   OnJoinStage()      │  Stage의 논리적 입장 처리
│   (Stage)           │  - 입장 허용 여부 결정
│                     │  - userInfo(콘텐츠 패킷) 처리
└──────────┬──────────┘
           │
           ▼
┌─────────────────────┐
│    OnCreate()       │  Actor 초기화 (최초 1회)
│    (Actor)          │  - 개인 상태 설정
│                     │  - 리소스 할당
└──────────┬──────────┘
           │
           ▼
┌─────────────────────┐
│  OnAuthenticate()   │  인증 완료 콜백 (Actor)
│  (Actor)            │  - 연결/재연결 시마다 호출
│                     │  - 클라이언트에 상태 동기화
└──────────┬──────────┘
           │
           ▼
┌─────────────────────┐
│   Active State      │  메시지 송수신 가능
│   IsConnected=true  │  - OnDispatch 처리
└─────────────────────┘

═══════════════════════════════════════════════════════════════
                        연결 끊김
═══════════════════════════════════════════════════════════════

    (네트워크 끊김 / 클라이언트 종료)
           │
           ▼
┌─────────────────────┐
│  Socket Disconnect  │  물리적 연결 끊김
└──────────┬──────────┘
           │
           ▼
┌─────────────────────┐
│OnConnectionChanged│  Stage에 알림 (옵션)
│  (Stage)            │  - isConnected = false
│                     │  - 다른 플레이어에게 알림
└──────────┬──────────┘
           │
           ▼
┌─────────────────────┐
│  Disconnected State │  Actor는 Stage에 유지!
│  IsConnected=false  │  - 재연결 대기
│                     │  - 타이머로 타임아웃 관리
└─────────────────────┘

═══════════════════════════════════════════════════════════════
                        재연결
═══════════════════════════════════════════════════════════════

Client: TCP/WebSocket Connect (with roomToken)
    │
    ▼
┌─────────────────────┐
│  Socket Connected   │  물리적 연결 수립
│                     │  새 SessionId 할당
└──────────┬──────────┘
           │
           ▼
┌─────────────────────┐
│  ValidateToken      │  토큰 재검증
│  (토큰 검증)        │  - AccountId로 기존 Actor 찾기
└──────────┬──────────┘
           │
           ▼
┌─────────────────────┐
│  Actor 세션 갱신    │  기존 Actor에 새 Session 연결
│                     │  - SessionId 업데이트
│                     │  - OnJoinStage 호출 안 함!
└──────────┬──────────┘
           │
           ▼
┌─────────────────────┐
│  OnAuthenticate()   │  인증 완료 콜백 (Actor)
│  (Actor)            │  - 재연결 시에도 호출됨!
│                     │  - 상태 동기화 전송
└──────────┬──────────┘
           │
           ▼
┌─────────────────────┐
│OnConnectionChanged│  Stage에 알림 (옵션)
│  (Stage)            │  - isConnected = true
└──────────┬──────────┘
           │
           ▼
┌─────────────────────┐
│   Active State      │  다시 활성화
│   IsConnected=true  │
└─────────────────────┘

═══════════════════════════════════════════════════════════════
                   타임아웃 또는 명시적 퇴장
═══════════════════════════════════════════════════════════════

    (LeaveStage 호출)
           │
           ▼
┌─────────────────────┐
│   OnDestroy()       │  Actor 정리
│   (Actor)           │  - 자원 해제
│                     │  - 상태 저장
└─────────────────────┘
```

### 3.2.1 콜백 호출 시점 정리

| 콜백 | 위치 | 최초 입장 | 재연결 | 설명 |
|------|------|----------|--------|------|
| `OnJoinStage` | Stage | ✅ | ❌ | 입장 허용 (1회) |
| `OnCreate` | Actor | ✅ | ❌ | Actor 초기화 (1회) |
| `OnAuthenticate` | Actor | ✅ | ✅ | 인증 완료 (매 연결) |
| `OnConnectionChanged` | Stage | ✅ | ✅ | 연결 상태 변경 |
| `OnDispatch` | Stage | ✅ | ✅ | 메시지 처리 |
| `OnDestroy` | Actor | ✅ (퇴장 시) | ✅ (타임아웃 시) | Actor 파괴 |

### 3.3 Actor 주요 메서드

#### OnCreate

```csharp
/// <summary>
/// Actor 생성 시 호출 (최초 1회)
/// </summary>
/// <remarks>
/// Stage의 OnJoinStage 후에 호출됩니다.
/// DB 로드, 초기 상태 설정 등을 수행합니다.
/// </remarks>
Task OnCreate();

// 사용 예시
public class GameActor : IActor
{
    private PlayerState _state;

    public async Task OnCreate()
    {
        // DB에서 플레이어 상태 로드
        _state = await LoadPlayerState(ActorSender.AccountId);

        // 초기 인벤토리 설정
        _inventory = new Inventory(_state.Items);

        LOG.Info($"Actor created: {ActorSender.AccountId}");
    }
}
```

#### OnAuthenticate

```csharp
/// <summary>
/// 클라이언트 인증 처리
/// </summary>
/// <param name="authPacket">인증 요청 패킷</param>
/// <returns>
/// (result, reply) 튜플:
/// - result: 인증 성공 시 true
/// - reply: 클라이언트에 보낼 응답 패킷 (옵션)
/// </returns>
/// <remarks>
/// 중요: 이 메서드에서 ActorSender.AccountId를 반드시 설정해야 합니다.
/// AccountId가 빈 문자열로 남으면 연결이 종료됩니다.
/// </remarks>
Task<(bool result, IPacket? reply)> OnAuthenticate(IPacket authPacket);

// 사용 예시
public async Task<(bool, IPacket?)> OnAuthenticate(IPacket authPacket)
{
    var authReq = AuthRequest.Parser.ParseFrom(authPacket.Payload.DataSpan);

    // 토큰 검증
    if (!ValidateToken(authReq.Token))
    {
        return (false, CPacket.Of(new AuthReply { Success = false, Error = "Invalid token" }));
    }

    // AccountId 설정 (필수!)
    ActorSender.AccountId = authReq.UserId;

    LOG.Info($"Actor authenticated: {ActorSender.AccountId}");

    // 성공 응답
    return (true, CPacket.Of(new AuthReply { Success = true }));
}
```

#### OnDestroy

```csharp
/// <summary>
/// Actor 파괴 시 호출 (논리적 퇴장 시)
/// </summary>
/// <remarks>
/// LeaveStage 호출 시 또는 Stage 종료 시 호출됩니다.
/// DB 저장, 리소스 정리 등을 수행합니다.
/// </remarks>
Task OnDestroy();

// 사용 예시
public async Task OnDestroy()
{
    // 상태 저장
    await SavePlayerState(_state);

    // 리소스 정리
    _inventory?.Dispose();

    LOG.Info($"Actor destroyed: {ActorSender.AccountId}");
}
```

## 4. 메시지 디스패치 흐름

### 4.1 전체 메시지 흐름

```
[클라이언트 → Stage → Actor]

Client
    │
    │ Send("PlayerMove", data)
    │
    ▼
Socket Receiver
    │
    ▼
Packet Parser
    │
    ▼
Dispatcher
    │
    │ Route by StageId
    │
    ▼
Stage Message Queue (Lock-Free)
    │
    │ FIFO
    │
    ▼
Stage.OnDispatch(actor, packet)
    │
    │ switch (packet.MsgId)
    │
    ▼
Handler Method
    │
    │ HandlePlayerMove(actor, packet)
    │
    ▼
Business Logic
    │
    │ Update Position
    │ Collision Check
    │ Broadcast to Others
    │
    ▼
ActorSender.SendToClient(replyPacket)
    │
    ▼
Client
```

### 4.2 Lock-Free 메시지 처리

```
[동시성 제어 메커니즘]

Multiple Threads                  Single Thread
(Dispatcher)                      (Stage Handler)

Thread 1 ─┐
          │
Thread 2 ─┼──▶ ConcurrentQueue ──▶ Sequential Processing
          │      (Lock-Free)         (Stage Context)
Thread 3 ─┘

특징:
- Enqueue: 여러 스레드 동시 가능 (Lock-Free)
- Dequeue: Stage 전용 스레드만 수행
- FIFO 순서 보장
- 공유 상태 없음 (Stage 내부)
```

### 4.3 메시지 처리 보장

```
순서 보장 (Ordering):
- 같은 Stage의 메시지는 FIFO 순서로 처리
- 다른 Stage는 병렬 처리 가능

격리 보장 (Isolation):
- Stage A의 상태는 Stage A만 수정
- Stage B는 메시지로만 A와 통신

동시성 보장 (Concurrency):
- Stage 내부는 단일 스레드 실행
- Lock/Mutex 불필요
- Race Condition 없음
```

## 5. IStageSender 인터페이스

### 5.1 기본 ISender 인터페이스

```csharp
#nullable enable

/// <summary>
/// Provides base functionality for sending packets and replies.
/// </summary>
/// <remarks>
/// ISender is the base interface for all sender types in the framework.
/// It provides methods for sending messages to API servers and Play stages,
/// as well as replying to incoming requests.
/// </remarks>
public interface ISender
{
    /// <summary>Gets the server type of this sender.</summary>
    ServerType ServerType { get; }

    /// <summary>Gets the service ID of this sender.</summary>
    ushort ServiceId { get; }

    #region API Server Communication

    /// <summary>Sends a one-way packet to an API server.</summary>
    void SendToApi(string apiServerId, IPacket packet);

    /// <summary>Sends a request to an API server with a callback for the reply.</summary>
    void RequestToApi(string apiServerId, IPacket packet, ReplyCallback replyCallback);

    /// <summary>Sends a request to an API server and awaits the reply.</summary>
    Task<IPacket> RequestToApi(string apiServerId, IPacket packet);

    #endregion

    #region Stage Communication

    /// <summary>Sends a one-way packet to a stage on a Play server.</summary>
    void SendToStage(string playServerId, long stageId, IPacket packet);

    /// <summary>Sends a request to a stage with a callback for the reply.</summary>
    void RequestToStage(string playServerId, long stageId, IPacket packet, ReplyCallback replyCallback);

    /// <summary>Sends a request to a stage and awaits the reply.</summary>
    Task<IPacket> RequestToStage(string playServerId, long stageId, IPacket packet);

    #endregion

    #region API Service Communication

    /// <summary>
    /// Sends a packet to an API server in the specified service using RoundRobin selection.
    /// </summary>
    void SendToApiService(ushort serviceId, IPacket packet);

    /// <summary>
    /// Sends a packet to an API server in the specified service using the specified selection policy.
    /// </summary>
    void SendToApiService(ushort serviceId, IPacket packet, ServerSelectionPolicy policy);

    /// <summary>
    /// Sends a request to an API server in the specified service with a callback (RoundRobin).
    /// </summary>
    void RequestToApiService(ushort serviceId, IPacket packet, ReplyCallback replyCallback);

    /// <summary>
    /// Sends a request to an API server in the specified service with a callback and policy.
    /// </summary>
    void RequestToApiService(ushort serviceId, IPacket packet, ReplyCallback replyCallback, ServerSelectionPolicy policy);

    /// <summary>
    /// Sends a request to an API server in the specified service and awaits the reply (RoundRobin).
    /// </summary>
    Task<IPacket> RequestToApiService(ushort serviceId, IPacket packet);

    /// <summary>
    /// Sends a request to an API server in the specified service and awaits the reply with policy.
    /// </summary>
    Task<IPacket> RequestToApiService(ushort serviceId, IPacket packet, ServerSelectionPolicy policy);

    #endregion

    #region System Communication

    /// <summary>
    /// Sends a one-way system message to a server.
    /// </summary>
    /// <remarks>
    /// System messages are handled by ISystemController.Handles() registered handlers.
    /// This method does not wait for a response.
    /// </remarks>
    void SendToSystem(string serverId, IPacket packet);

    /// <summary>
    /// Sends a system request with a callback for the reply.
    /// </summary>
    /// <remarks>
    /// Note: The receiving server's system handler must explicitly send a reply
    /// for the callback to be invoked.
    /// </remarks>
    void RequestToSystem(string serverId, IPacket packet, ReplyCallback replyCallback);

    /// <summary>
    /// Sends a system request and awaits the reply.
    /// </summary>
    /// <remarks>
    /// Note: The receiving server's system handler must explicitly send a reply
    /// for this task to complete.
    /// </remarks>
    Task<IPacket> RequestToSystem(string serverId, IPacket packet);

    #endregion

    #region Reply

    /// <summary>Sends an error-only reply to the current request.</summary>
    void Reply(ushort errorCode);

    /// <summary>Sends a reply packet to the current request.</summary>
    void Reply(IPacket reply);

    #endregion
}
```

**주요 추가 기능:**
- **ServerType, ServiceId 속성**: 현재 서버의 타입과 서비스 ID 조회
- **SendToApiService/RequestToApiService**: 서비스 ID로 API 서버에 메시지 전송 (RoundRobin 또는 지정된 정책)
- **SendToSystem/RequestToSystem**: 서버 간 시스템 메시지 전송 (ISystemController 핸들러로 처리)
- **ServerSelectionPolicy**: 서버 선택 정책 (RoundRobin, Random 등)

### 5.2 IStageSender 전체 인터페이스

```csharp
/// <summary>
/// Provides Stage-specific communication and management capabilities.
/// </summary>
/// <remarks>
/// IStageSender extends ISender with:
/// - Timer management (repeat, count, cancel)
/// - Stage lifecycle management (close)
/// - AsyncCompute/AsyncIO for safe external operations
/// - Client messaging with StageId context
/// - Game loop support with high-resolution timing
/// </remarks>
public interface IStageSender : ISender
{
    /// <summary>Gets the unique identifier for this Stage.</summary>
    long StageId { get; }

    /// <summary>Gets the type identifier for this Stage.</summary>
    string StageType { get; }

    #region Timer Management

    /// <summary>Adds a repeating timer that fires indefinitely.</summary>
    long AddRepeatTimer(TimeSpan initialDelay, TimeSpan period, TimerCallback callback);

    /// <summary>Adds a timer that fires a specified number of times.</summary>
    long AddCountTimer(TimeSpan initialDelay, TimeSpan period, int count, TimerCallback callback);

    /// <summary>Cancels an active timer.</summary>
    void CancelTimer(long timerId);

    /// <summary>Checks if a timer is still active.</summary>
    bool HasTimer(long timerId);

    #endregion

    #region Stage Management

    /// <summary>Closes this Stage, canceling all timers and triggering cleanup.</summary>
    void CloseStage();

    #endregion

    #region Async Operations

    /// <summary>
    /// Executes a CPU-bound operation on a dedicated compute thread pool,
    /// then optionally processes the result back on the event loop.
    /// </summary>
    /// <remarks>
    /// ComputeTaskPool is optimized for CPU-bound work:
    /// - Limited concurrency (CPU core count)
    /// - Prevents CPU starvation
    /// </remarks>
    void AsyncCompute(AsyncPreCallback preCallback, AsyncPostCallback? postCallback = null);

    /// <summary>
    /// Executes an I/O-bound operation on a dedicated I/O thread pool,
    /// then optionally processes the result back on the event loop.
    /// </summary>
    /// <remarks>
    /// IoTaskPool is optimized for I/O-bound work:
    /// - Higher concurrency (default 100)
    /// - Handles I/O wait efficiently
    /// </remarks>
    void AsyncIO(AsyncPreCallback preCallback, AsyncPostCallback? postCallback = null);

    #endregion

    #region Client Communication

    /// <summary>Sends a message to a specific client.</summary>
    void SendToClient(string sessionServerId, long sid, IPacket packet);

    #endregion

    #region Game Loop

    /// <summary>Starts a high-resolution game loop with the specified fixed timestep.</summary>
    void StartGameLoop(TimeSpan fixedTimestep, GameLoopCallback callback);

    /// <summary>Starts a high-resolution game loop with the specified configuration.</summary>
    void StartGameLoop(GameLoopConfig config, GameLoopCallback callback);

    /// <summary>Stops the running game loop.</summary>
    void StopGameLoop();

    /// <summary>Gets whether a game loop is currently running for this Stage.</summary>
    bool IsGameLoopRunning { get; }

    #endregion
}
```

**주요 특징:**
- **StageId 타입**: `long` (64비트 ID)
- **AsyncCompute/AsyncIO**: CPU-bound와 I/O-bound 작업 구분
- **GameLoop**: 고해상도 게임 루프 지원 (StartGameLoop, StopGameLoop, IsGameLoopRunning)
- **Timer**: `initialDelay` 파라미터로 최초 실행 지연 시간 설정
- **SendToClient**: 특정 클라이언트에게 메시지 전송

### 5.3 사용 예시

```csharp
public class GameStage : IStage
{
    private readonly List<IActor> _actors = new();

    public Task HandlePlayerMove(IActor actor, IPacket packet)
    {
        var moveData = packet.Payload.Parse<MoveData>();

        // 위치 업데이트
        UpdatePosition(actor, moveData);

        // 다른 플레이어에게 브로드캐스트 (헬퍼 메서드 사용)
        var notification = CPacket.Of(new PlayerMovedNotify { MoveData = moveData });
        BroadcastToOthers(actor, notification);

        // 요청자에게 응답
        actor.ActorSender.Reply(CPacket.Empty("MoveReply"));
        return Task.CompletedTask;
    }

    // 브로드캐스트 헬퍼 메서드 (직접 구현 필요)
    private void BroadcastToOthers(IActor sender, IPacket packet)
    {
        foreach (var actor in _actors)
        {
            if (actor.ActorSender.AccountId != sender.ActorSender.AccountId)
            {
                StageSender.SendToClient(
                    actor.ActorSender.SessionServerId,
                    actor.ActorSender.SessionId,
                    packet);
            }
        }
    }

    // Stage간 메시지 전송 예시 (fire-and-forget)
    public void NotifyLobby(string playServerId, long lobbyStageId, IPacket packet)
    {
        // SendToStage는 void (fire-and-forget)
        StageSender.SendToStage(playServerId, lobbyStageId, packet);
    }
}
```

**참고:**
- `SendToClient`로 개별 클라이언트에게 전송
- 브로드캐스트는 Stage에서 헬퍼 메서드로 직접 구현
- `SendToStage`는 void 메서드 (응답이 필요하면 `RequestToStage` 사용)

## 6. IActorSender 인터페이스

### 6.1 인터페이스 정의

```csharp
#nullable enable

/// <summary>
/// Provides Actor-specific communication capabilities.
/// </summary>
/// <remarks>
/// IActorSender extends ISender with:
/// - AccountId property for user identification (must be set in OnAuthenticate)
/// - LeaveStageAsync() to exit from current Stage
/// - SendToClient() for direct client messaging
/// </remarks>
public interface IActorSender : ISender
{
    /// <summary>
    /// Gets or sets the account identifier for this Actor.
    /// </summary>
    /// <remarks>
    /// MUST be set in IActor.OnAuthenticate() upon successful authentication.
    /// If empty ("") after OnAuthenticate completes, connection will be terminated.
    /// </remarks>
    string AccountId { get; set; }

    /// <summary>
    /// Removes this Actor from the current Stage.
    /// </summary>
    /// <remarks>
    /// This method:
    /// 1. Removes the Actor from BaseStage._actors
    /// 2. Calls IActor.OnDestroy()
    /// 3. Does NOT close the client connection (actor can join another stage)
    /// </remarks>
    Task LeaveStageAsync();

    /// <summary>
    /// Sends a message directly to the connected client.
    /// </summary>
    void SendToClient(IPacket packet);
}
```

**주요 변경사항:**
- **AccountId 타입**: `long` → `string` (다양한 ID 체계 지원)
- **LeaveStage**: `LeaveStage()` → `LeaveStageAsync()` (비동기 처리)
- **SendToClient**: 클라이언트에게 직접 메시지 전송 (sessionServerId, sid 파라미터 불필요)
- **ISender 상속**: Reply, SendToApi, RequestToApi 등 모든 ISender 메서드 사용 가능

### 6.2 사용 예시

```csharp
public class GameActor : IActor
{
    public required IActorSender ActorSender { get; init; }

    public async Task SendInventory()
    {
        var inventoryData = _inventory.Serialize();
        var packet = CreatePacket("InventoryUpdate", inventoryData);

        // 자신의 클라이언트에게 전송 (ISender.SendAsync)
        await ActorSender.SendAsync(packet);
    }

    public async ValueTask DisposeAsync()
    {
        // 비동기 리소스 정리
        await SaveStateAsync();
    }
}
```

**주요 변경사항:**
- `required init` - 생성 시 필수 초기화
- `SendToClient` → `SendAsync` (ISender에서 상속)

## 7. 비동기 작업 (AsyncCompute/AsyncIO)

### 8.1 개념

```
문제:
Stage 내부는 단일 스레드 → 블로킹 작업(DB, HTTP, 파일 I/O) 불가

해결:
AsyncCompute/AsyncIO로 블로킹 작업을 별도 스레드 풀에서 실행
- AsyncCompute: CPU-bound 작업 (계산, 암호화 등)
- AsyncIO: I/O-bound 작업 (DB, HTTP, 파일 등)
결과는 다시 Stage 메시지 큐로 전달
```

### 8.2 AsyncIO 사용 예시 (DB 작업)

```csharp
public async Task HandleSaveRequest(IActor actor, IPacket packet)
{
    var saveData = packet.Parse<SaveData>();

    // I/O-bound 작업을 AsyncIO로 처리
    StageSender.AsyncIO(
        preCallback: async () =>
        {
            // IoTaskPool에서 실행 (블로킹 가능)
            await _database.SavePlayerData(actor.ActorSender.AccountId, saveData);
            return "Save completed";
        },
        postCallback: async (result) =>
        {
            // Stage 이벤트 루프로 복귀 (Stage Context 안전)
            _log.Info($"Save result: {result}");

            // 클라이언트에 알림
            actor.ActorSender.SendToClient(new SimplePacket(new SaveCompleteNotify
            {
                Success = true,
                Message = result?.ToString() ?? string.Empty
            }));
        }
    );

    // 즉시 리턴 (블로킹 안됨)
    StageSender.Reply(ErrorCode.Success);
}
```

### 8.3 AsyncCompute 사용 예시 (CPU-bound 작업)

```csharp
public async Task HandlePathfinding(IActor actor, IPacket packet)
{
    var pathRequest = packet.Parse<PathfindingRequest>();

    // CPU-bound 작업을 AsyncCompute로 처리
    StageSender.AsyncCompute(
        preCallback: async () =>
        {
            // ComputeTaskPool에서 실행 (CPU 집약적 계산)
            var path = CalculatePath(pathRequest.Start, pathRequest.End, _mapData);
            return path;
        },
        postCallback: async (result) =>
        {
            // Stage 이벤트 루프로 복귀
            var path = (List<Vector2>)result!;

            // 경로를 클라이언트에 전송
            actor.ActorSender.SendToClient(new SimplePacket(new PathfindingResponse
            {
                Path = { path.Select(p => new Position { X = p.X, Y = p.Y }) }
            }));
        }
    );

    StageSender.Reply(ErrorCode.Success);
}

// CPU 집약적 계산 (A* 알고리즘 등)
private List<Vector2> CalculatePath(Vector2 start, Vector2 end, MapData mapData)
{
    // 복잡한 계산...
    return path;
}
```

### 8.4 주의사항

```
PreCallback (별도 스레드):
- Stage 상태 접근 금지 (동시성 문제)
- 블로킹 작업 가능 (DB, HTTP, File I/O, CPU 집약적 계산)
- 반환값은 object? (결과 전달, nullable)
- AsyncCompute: CPU-bound 작업용 (제한된 동시성)
- AsyncIO: I/O-bound 작업용 (높은 동시성)

PostCallback (Stage 이벤트 루프):
- Stage 메시지 큐를 통해 전달
- Stage 상태 안전하게 접근 가능
- 단일 스레드 보장
- ISender 메서드 안전하게 사용 가능
```

### 8.5 AsyncCompute vs AsyncIO 선택 가이드

| 작업 타입 | 사용할 메서드 | 예시 |
|-----------|--------------|------|
| DB 쿼리 | AsyncIO | SELECT, INSERT, UPDATE |
| HTTP 요청 | AsyncIO | REST API 호출, 웹훅 |
| 파일 I/O | AsyncIO | 파일 읽기/쓰기 |
| 경로 탐색 | AsyncCompute | A* 알고리즘 |
| 암호화/복호화 | AsyncCompute | AES, RSA |
| 이미지 처리 | AsyncCompute | 리사이징, 필터링 |
| 시뮬레이션 | AsyncCompute | 물리 계산, AI 연산 |


## 8. 다음 단계

- `04-timer-system.md`: 타이머 시스템 상세 설명
- `07-client-protocol.md`: 클라이언트와의 상호작용 프로토콜
