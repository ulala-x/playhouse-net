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
public interface IStage : IAsyncDisposable
{
    /// <summary>Stage 제어를 위한 Sender 인터페이스</summary>
    IStageSender StageSender { get; }

    // Stage 라이프사이클 (최초 1회)
    Task<(ushort errorCode, IPacket? reply)> OnCreate(IPacket packet);
    Task OnPostCreate();

    // Actor 논리적 입장/퇴장 (최초 1회씩)
    Task<(ushort errorCode, IPacket? reply)> OnJoinRoom(IActor actor, IPacket userInfo);
    Task OnPostJoinRoom(IActor actor);
    ValueTask OnLeaveRoom(IActor actor, LeaveReason reason);

    // Actor 연결 상태 변경 (연결/재연결/끊김 시 호출)
    ValueTask OnActorConnectionChanged(IActor actor, bool isConnected, DisconnectReason? reason);

    // 메시지 처리
    ValueTask OnDispatch(IActor actor, IPacket packet);
}
```

**주요 설계 원칙:**
- **논리적 입장(OnJoinRoom)과 물리적 연결(OnActorConnectionChanged) 분리**
  - `OnJoinRoom` - Actor의 논리적 입장 (최초 1회)
  - `OnActorConnectionChanged` - 연결/재연결/끊김 시마다 호출
  - `OnLeaveRoom` - Actor의 논리적 퇴장 (타임아웃 또는 명시적 퇴장)

**주요 .NET 스타일 적용:**
- `#nullable enable` - Nullable Reference Types 활성화
- `IAsyncDisposable` - 비동기 리소스 정리 지원
- `IPacket?` - 응답 패킷은 nullable (없을 수 있음)
- `IActor` - Room 서버에서 항상 존재하므로 non-nullable
- `ValueTask` - Hot path 메서드 (OnDispatch, OnLeaveRoom, OnActorConnectionChanged)에 allocation 최적화
- `userInfo` 파라미터 - 게임 컨텐츠에서 정의하는 패킷 (캐릭터 정보, 로비 상태 등)

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
│  - OnJoinRoom()     │  Actor 논리적 입장
│  - OnPostJoinRoom() │  입장 완료 후처리
│  - OnDispatch()     │  메시지 처리
│  - OnActorConnection│  연결 상태 변경
│      Changed()      │
│  - OnLeaveRoom()    │  Actor 논리적 퇴장
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
OnJoinRoom(actor, userInfo)    ← Stage 콜백
    │
    ▼
actor.OnCreate()               ← Actor 최초 생성
    │
    ▼
actor.OnAuthenticate(authData) ← Actor 인증 완료
    │
    ▼
OnPostJoinRoom(actor)          ← Stage 콜백
    │
    ▼
OnActorConnectionChanged(actor, isConnected=true)
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
OnJoinRoom(actor, userInfo)    ← Stage 콜백
    │
    ▼
actor.OnCreate()
    │
    ▼
actor.OnAuthenticate(authData)
    │
    ▼
OnPostJoinRoom(actor)
    │
    ▼
OnActorConnectionChanged(actor, isConnected=true)
    │
    ▼
Active (플레이 중)
```

#### 시나리오 3: 재연결

```
[재연결 - OnJoinRoom 호출 안 함]

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
OnActorConnectionChanged(actor, isConnected=true)
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
/// <returns>에러 코드 및 응답 패킷</returns>
Task<(ushort errorCode, IPacket? reply)> OnCreate(IPacket packet);

// 사용 예시
public class GameStage : IStage
{
    public async Task<(ushort, IPacket?)> OnCreate(IPacket packet)
    {
        // 초기 설정 파싱
        var config = packet.Parse<StageConfig>();

        // 상태 초기화
        _maxPlayers = config.MaxPlayers;
        _gameMode = config.GameMode;

        // 성공 응답
        return (ErrorCode.Success, CreateReply("Stage created"));
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

#### OnJoinRoom

```csharp
/// <summary>
/// Actor가 Stage에 논리적으로 입장할 때 호출 (최초 1회)
/// </summary>
/// <param name="actor">입장하는 Actor</param>
/// <param name="userInfo">게임 컨텐츠에서 정의하는 입장 데이터 (캐릭터 정보 등)</param>
/// <returns>에러 코드 및 응답 패킷</returns>
/// <remarks>
/// 재연결 시에는 호출되지 않습니다.
/// userInfo는 HTTP API의 JoinRoom 요청 시 전달된 데이터입니다.
/// </remarks>
Task<(ushort errorCode, IPacket? reply)> OnJoinRoom(IActor actor, IPacket userInfo);

// 사용 예시
public async Task<(ushort, IPacket?)> OnJoinRoom(IActor actor, IPacket userInfo)
{
    // 인원 체크
    if (_actors.Count >= _maxPlayers)
    {
        return (ErrorCode.StageFull, ErrorReply("Stage is full"));
    }

    // userInfo 파싱 (게임 컨텐츠 패킷)
    var playerInfo = userInfo.Parse<PlayerJoinInfo>();

    // Actor 추가
    var player = new PlayerState
    {
        AccountId = actor.ActorSender.AccountId,
        Nickname = playerInfo.Nickname,
        CharacterId = playerInfo.CharacterId
    };
    _players.Add(actor.ActorSender.AccountId, player);

    // 다른 플레이어에게 알림
    await StageSender.BroadcastAsync(
        new SimplePacket(new PlayerJoinedNotify { Player = player }),
        a => a.ActorSender.AccountId != actor.ActorSender.AccountId);

    // 성공 응답 (현재 Stage 상태 포함)
    return (ErrorCode.Success, CreateStageSnapshot());
}
```

#### OnPostJoinRoom

```csharp
/// <summary>
/// Actor 입장 완료 후 호출
/// </summary>
/// <param name="actor">입장 완료된 Actor</param>
Task OnPostJoinRoom(IActor actor);

// 사용 예시
public async Task OnPostJoinRoom(IActor actor)
{
    // 게임 시작 조건 확인
    if (_players.Count == _maxPlayers)
    {
        await StartGame();
    }
}
```

#### OnActorConnectionChanged

```csharp
/// <summary>
/// Actor 연결 상태 변경 시 호출 (연결/재연결/끊김)
/// </summary>
/// <param name="actor">상태가 변경된 Actor</param>
/// <param name="isConnected">현재 연결 상태</param>
/// <param name="reason">연결 끊김 이유 (끊김 시에만)</param>
/// <remarks>
/// 최초 연결, 재연결, 연결 끊김 시 모두 호출됩니다.
/// isConnected=true: 연결됨 (최초 또는 재연결)
/// isConnected=false: 연결 끊김
/// </remarks>
ValueTask OnActorConnectionChanged(IActor actor, bool isConnected, DisconnectReason? reason);

// 사용 예시
public async ValueTask OnActorConnectionChanged(IActor actor, bool isConnected, DisconnectReason? reason)
{
    var accountId = actor.ActorSender.AccountId;

    if (isConnected)
    {
        // 연결됨 (최초 또는 재연결)
        LOG.Info($"Player connected: {accountId}");

        // 재연결 타이머 취소
        if (_reconnectTimers.TryGetValue(accountId, out var timerId))
        {
            StageSender.CancelTimer(timerId);
            _reconnectTimers.Remove(accountId);
        }

        // 다른 플레이어에게 알림
        await StageSender.BroadcastAsync(
            new SimplePacket(new PlayerReconnectedNotify { AccountId = accountId }),
            a => a.ActorSender.AccountId != accountId);
    }
    else
    {
        // 연결 끊김
        LOG.Info($"Player disconnected: {accountId}, reason: {reason}");

        // 재연결 타이머 시작 (30초)
        var timerId = StageSender.AddCountTimer(
            initialDelay: TimeSpan.FromSeconds(30),
            period: TimeSpan.Zero,
            count: 1,
            callback: async () => await HandleReconnectTimeout(actor));
        _reconnectTimers[accountId] = timerId;

        // 다른 플레이어에게 알림
        await StageSender.BroadcastAsync(
            new SimplePacket(new PlayerDisconnectedNotify { AccountId = accountId }),
            a => a.ActorSender.AccountId != accountId);
    }
}
```

#### OnLeaveRoom

```csharp
/// <summary>
/// Actor가 Stage에서 논리적으로 퇴장할 때 호출
/// </summary>
/// <param name="actor">퇴장하는 Actor</param>
/// <param name="reason">퇴장 이유</param>
/// <remarks>
/// 재연결 타임아웃, 명시적 퇴장 요청, 강제 킥 등에서 호출됩니다.
/// 이 콜백 이후 actor.OnDestroy()가 호출됩니다.
/// </remarks>
ValueTask OnLeaveRoom(IActor actor, LeaveReason reason);

// 사용 예시
public async ValueTask OnLeaveRoom(IActor actor, LeaveReason reason)
{
    var accountId = actor.ActorSender.AccountId;

    // 재연결 타이머가 있으면 취소
    if (_reconnectTimers.TryGetValue(accountId, out var timerId))
    {
        StageSender.CancelTimer(timerId);
        _reconnectTimers.Remove(accountId);
    }

    // 플레이어 상태 제거
    _players.Remove(accountId);

    // 다른 플레이어에게 알림
    await StageSender.BroadcastAsync(
        new SimplePacket(new PlayerLeftNotify
        {
            AccountId = accountId,
            Reason = reason.ToString()
        }),
        a => a.ActorSender.AccountId != accountId);

    // Stage 종료 조건 확인
    if (_players.Count == 0)
    {
        StageSender.CloseStage(); // 빈 Stage 닫기
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
/// <remarks>
/// actor.IsConnected가 true일 때만 호출됩니다.
/// </remarks>
ValueTask OnDispatch(IActor actor, IPacket packet);

// 사용 예시
public async ValueTask OnDispatch(IActor actor, IPacket packet)
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
/// 연결/재연결 시마다 OnAuthenticate가 호출되며, Actor는 연결이 끊겨도
/// Stage에 유지되어 재연결을 지원합니다.
/// </remarks>
public interface IActor : IAsyncDisposable
{
    /// <summary>Actor 메시지 전송을 위한 Sender 인터페이스</summary>
    IActorSender ActorSender { get; }

    /// <summary>현재 연결 상태</summary>
    bool IsConnected { get; }

    // 라이프사이클 메서드 (최초 1회)
    Task OnCreate();                              // Actor 생성 시
    Task OnDestroy();                             // Actor 파괴 시

    // 인증 콜백 (연결/재연결 시마다 호출)
    Task OnAuthenticate(IPacket? authData);       // 토큰 검증 성공 후
}

/// <summary>
/// 연결 끊김 이유
/// </summary>
public enum DisconnectReason
{
    Normal,           // 정상 종료 (클라이언트 요청)
    NetworkError,     // 네트워크 오류
    Timeout,          // 하트비트 타임아웃
    Kicked,           // 강제 킥
    ServerShutdown,   // 서버 종료
    DuplicateLogin    // 중복 로그인
}

/// <summary>
/// 퇴장 이유
/// </summary>
public enum LeaveReason
{
    Normal,           // 정상 퇴장 (클라이언트 요청)
    Timeout,          // 재연결 타임아웃
    Kicked,           // 강제 킥
    ServerShutdown    // 서버 종료
}
```

**주요 .NET 스타일 적용:**
- `IAsyncDisposable` - 비동기 리소스 정리 (DB 저장 등)
- `IsConnected` - 연결 상태 추적
- `OnAuthenticate` - 연결/재연결 시마다 호출되는 인증 콜백

### 3.2 Actor 라이프사이클

Actor는 **논리적 입장**(OnJoinRoom)과 **물리적 연결**(OnAuthenticate)이 분리되어 있습니다.
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
│   OnJoinRoom()      │  Stage의 논리적 입장 처리
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
│OnActorConnectionChanged│  Stage에 알림 (옵션)
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
│                     │  - OnJoinRoom 호출 안 함!
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
│OnActorConnectionChanged│  Stage에 알림 (옵션)
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

    (재연결 타임아웃 / LeaveRoom 호출)
           │
           ▼
┌─────────────────────┐
│   OnLeaveRoom()     │  논리적 퇴장 처리
│   (Stage)           │  - 다른 플레이어에게 알림
└──────────┬──────────┘
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
| `OnJoinRoom` | Stage | ✅ | ❌ | 논리적 입장 (1회) |
| `OnCreate` | Actor | ✅ | ❌ | Actor 초기화 (1회) |
| `OnAuthenticate` | Actor | ✅ | ✅ | 인증 완료 (매 연결) |
| `OnActorConnectionChanged` | Stage | ✅ | ✅ | 연결 상태 변경 |
| `OnDispatch` | Stage | ✅ | ✅ | 메시지 처리 |
| `OnLeaveRoom` | Stage | ✅ (퇴장 시) | ✅ (타임아웃 시) | 논리적 퇴장 |
| `OnDestroy` | Actor | ✅ (퇴장 시) | ✅ (타임아웃 시) | Actor 파괴 |

### 3.3 Actor 주요 메서드

#### OnCreate

```csharp
/// <summary>
/// Actor 생성 시 호출 (최초 1회)
/// </summary>
/// <remarks>
/// Stage의 OnJoinRoom 후에 호출됩니다.
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
/// 연결/재연결 시마다 호출되는 인증 완료 콜백
/// </summary>
/// <param name="authData">인증 관련 추가 데이터 (옵션)</param>
/// <remarks>
/// 토큰 검증 성공 후 호출됩니다.
/// 최초 연결과 재연결 모두에서 호출됩니다.
/// 클라이언트에 현재 상태를 동기화하는 용도로 사용합니다.
/// </remarks>
Task OnAuthenticate(IPacket? authData);

// 사용 예시
public async Task OnAuthenticate(IPacket? authData)
{
    LOG.Info($"Actor authenticated: {ActorSender.AccountId}, IsReconnect: {_isInitialized}");

    // 클라이언트에 현재 상태 동기화
    if (_isInitialized)
    {
        // 재연결 - 현재 게임 상태 전송
        await ActorSender.SendAsync(new SimplePacket(new SyncStateMsg
        {
            GameState = SerializeCurrentState(),
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        }));
    }
    else
    {
        // 최초 연결 - 초기화 완료 표시
        _isInitialized = true;

        // 웰컴 메시지
        await ActorSender.SendAsync(new SimplePacket(new WelcomeMsg
        {
            AccountId = ActorSender.AccountId,
            ServerTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        }));
    }
}
```

#### OnDestroy

```csharp
/// <summary>
/// Actor 파괴 시 호출 (논리적 퇴장 시)
/// </summary>
/// <remarks>
/// Stage의 OnLeaveRoom 후에 호출됩니다.
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

**주요 변경사항:**
- **StageId 타입**: `int` → `long` (64비트 ID 지원)
- **AsyncCompute/AsyncIO**: `AsyncBlock` 분리 → CPU-bound와 I/O-bound 작업 구분
- **GameLoop 추가**: 고해상도 게임 루프 지원 (StartGameLoop, StopGameLoop, IsGameLoopRunning)
- **Timer 시그니처**: `initialDelay` 파라미터 추가 (최초 실행 지연 시간)
- **SendToClient**: 클라이언트에게 직접 메시지 전송 (sessionServerId, sid 파라미터)

### 5.3 사용 예시

```csharp
public class GameStage : IStage
{
    public async ValueTask HandlePlayerMove(IActor actor, IPacket packet)
    {
        var moveData = packet.Payload.Parse<MoveData>();

        // 위치 업데이트
        UpdatePosition(actor, moveData);

        // 다른 플레이어에게 브로드캐스트 (새 API 사용)
        var notification = CreatePacket("PlayerMoved", moveData);
        await StageSender.BroadcastAsync(notification,
            other => other.ActorSender.AccountId != actor.ActorSender.AccountId);

        // 요청자에게 응답
        StageSender.Reply(ErrorCode.Success);
    }

    // Stage간 메시지 전송 예시 (fire-and-forget)
    public async ValueTask NotifyLobby(int lobbyStageId, IPacket packet)
    {
        // blocking 응답 대기 없이 전송
        await StageSender.SendToStageAsync(lobbyStageId, packet);
    }
}
```

**주요 변경사항:**
- `BroadcastAsync` - 필터와 함께 브로드캐스트 (반복문 제거)
- `SendToStageAsync` - Stage간 fire-and-forget 통신
- `ValueTask` - Hot path 최적화

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

## 7. 타이머 시스템 통합

기존 playhouse-sample-net의 타이머 시스템을 기반으로 합니다.

### 7.1 RepeatTimer (무한 반복)

```csharp
// 주기적 실행 (무한 반복)
public async Task OnPostCreate()
{
    // 하트비트 타이머 - 1초 후 시작, 2초마다 실행
    _heartbeatTimerId = StageSender.AddRepeatTimer(
        initialDelay: TimeSpan.FromSeconds(1),
        period: TimeSpan.FromSeconds(2),
        timerCallback: OnHeartbeatTimer
    );

    _log.Information("Heartbeat timer started: {TimerId}", _heartbeatTimerId);
}

private async Task OnHeartbeatTimer()
{
    _heartbeatCount++;
    _log.Debug("Heartbeat #{Count}", _heartbeatCount);

    // 모든 플레이어에게 하트비트 전송
    await StageSender.BroadcastAsync(new SimplePacket(new TimerTickNotify
    {
        TimerId = _heartbeatTimerId.ToString(),
        TickCount = _heartbeatCount,
        Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
    }));
}
```

### 7.2 CountTimer (제한된 횟수)

```csharp
// 지정된 횟수만 실행
public async Task StartCountdown()
{
    // 3초 카운트다운 - 0초 후 시작, 1초 간격, 3회 실행
    _countdownTimerId = StageSender.AddCountTimer(
        initialDelay: TimeSpan.Zero,
        period: TimeSpan.FromSeconds(1),
        count: 3,
        timerCallback: OnCountdownTick
    );

    _log.Information("Countdown started: {TimerId}", _countdownTimerId);
}

private async Task OnCountdownTick()
{
    _countdown--;
    _log.Information("Countdown: {Count}", _countdown);

    await StageSender.BroadcastAsync(new SimplePacket(new CountdownNotify
    {
        Count = _countdown
    }));

    if (_countdown == 0)
    {
        await StartGame();
    }
}
```

### 7.3 클라이언트 요청 타이머 (TimerHandler 패턴)

기존 코드의 TimerHandler 패턴을 따릅니다:

```csharp
// TimerHandler.cs - 클라이언트 요청으로 타이머 시작
internal static class TimerHandler
{
    private static readonly ILogger _log = Log.Logger;

    public static async Task HandleStartTimer(
        GameRoom room,
        Player player,
        IPacket packet,
        Dictionary<string, long> activeTimers)
    {
        var request = packet.Parse<StartTimerReq>();

        // 고유 타이머 ID 생성
        var timerName = $"{player.AccountId}_{request.TimerName}_{DateTime.UtcNow.Ticks}";
        int tickCount = 0;

        // 타이머 생성 (repeatCount=0이면 무한 반복)
        long timerId;
        if (request.RepeatCount == 0)
        {
            // RepeatTimer (무한 반복)
            timerId = room.StageSender.AddRepeatTimer(
                initialDelay: TimeSpan.FromMilliseconds(request.IntervalMs),
                period: TimeSpan.FromMilliseconds(request.IntervalMs),
                timerCallback: async () =>
                {
                    tickCount++;
                    await OnTimerTick(room, player, timerName, tickCount);
                }
            );
        }
        else
        {
            // CountTimer (제한된 횟수)
            timerId = room.StageSender.AddCountTimer(
                initialDelay: TimeSpan.FromMilliseconds(request.IntervalMs),
                period: TimeSpan.FromMilliseconds(request.IntervalMs),
                count: request.RepeatCount,
                timerCallback: async () =>
                {
                    tickCount++;
                    await OnTimerTick(room, player, timerName, tickCount);
                }
            );
        }

        activeTimers[timerName] = timerId;

        // 응답
        room.StageSender.Reply(new SimplePacket(new StartTimerRes
        {
            Success = true,
            TimerId = timerName,
            Message = $"Timer started: {request.IntervalMs}ms interval"
        }));
    }

    public static async Task HandleCancelTimer(
        GameRoom room,
        Player player,
        IPacket packet,
        Dictionary<string, long> activeTimers)
    {
        var request = packet.Parse<CancelTimerReq>();

        if (activeTimers.TryGetValue(request.TimerId, out var timerId))
        {
            room.StageSender.CancelTimer(timerId);
            activeTimers.Remove(request.TimerId);

            room.StageSender.Reply(new SimplePacket(new CancelTimerRes
            {
                Success = true,
                Message = "Timer cancelled"
            }));
        }
        else
        {
            room.StageSender.Reply(new SimplePacket(new CancelTimerRes
            {
                Success = false,
                Message = "Timer not found"
            }));
        }
    }

    private static async Task OnTimerTick(
        GameRoom room,
        Player player,
        string timerName,
        int tickCount)
    {
        // 타이머 틱 알림 전송
        player.ActorSender.SendToClient(new SimplePacket(new TimerTickNotify
        {
            TimerId = timerName,
            TickCount = tickCount,
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        }));
    }
}
```

### 7.4 타이머 정리

```csharp
// Stage 종료 시 - 모든 타이머 자동 정리
public void CloseStage()
{
    StageSender.CloseStage();
}

// 개별 타이머 취소
public void CancelHeartbeat()
{
    if (StageSender.HasTimer(_heartbeatTimerId))
    {
        StageSender.CancelTimer(_heartbeatTimerId);
        _log.Information("Heartbeat timer cancelled");
    }
}

// 연결 끊김 시 타이머 관리
public async ValueTask OnActorConnectionChanged(IActor actor, bool isConnected, DisconnectReason? reason)
{
    if (!isConnected)
    {
        // 재연결 타임아웃 타이머 시작
        var timerId = StageSender.AddCountTimer(
            initialDelay: TimeSpan.FromSeconds(30),
            period: TimeSpan.Zero,
            count: 1,
            callback: async () => await HandleReconnectTimeout(actor));

        _reconnectTimers[actor.ActorSender.AccountId] = timerId;
    }
    else
    {
        // 재연결 시 타이머 취소
        if (_reconnectTimers.TryGetValue(actor.ActorSender.AccountId, out var timerId))
        {
            StageSender.CancelTimer(timerId);
            _reconnectTimers.Remove(actor.ActorSender.AccountId);
        }
    }
}
```

### 7.5 타이머 메서드 시그니처

```csharp
public interface IStageSender : ISender
{
    /// <summary>
    /// 무한 반복 타이머 추가
    /// </summary>
    /// <param name="initialDelay">최초 실행까지 대기 시간</param>
    /// <param name="period">실행 간격</param>
    /// <param name="timerCallback">콜백 함수</param>
    /// <returns>타이머 ID (취소용)</returns>
    long AddRepeatTimer(TimeSpan initialDelay, TimeSpan period, Func<Task> timerCallback);

    /// <summary>
    /// 제한된 횟수 반복 타이머 추가
    /// </summary>
    /// <param name="initialDelay">최초 실행까지 대기 시간</param>
    /// <param name="period">실행 간격</param>
    /// <param name="count">실행 횟수</param>
    /// <param name="timerCallback">콜백 함수</param>
    /// <returns>타이머 ID (취소용)</returns>
    long AddCountTimer(TimeSpan initialDelay, TimeSpan period, int count, Func<Task> timerCallback);

    /// <summary>타이머 취소</summary>
    void CancelTimer(long timerId);

    /// <summary>타이머 존재 여부 확인</summary>
    bool HasTimer(long timerId);
}
```

## 8. 비동기 작업 (AsyncCompute/AsyncIO)

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

## 9. 실전 예제

### 9.0 패킷 직렬화 패턴

PlayHouse-NET은 **Protobuf**를 기본 직렬화로 사용합니다. 컨텐츠 코드에서는 Protobuf 메시지를 생성하고 `SimplePacket`으로 감싸서 전송합니다.

```csharp
// Proto 메시지 정의 (simple.proto)
message ChatMsg {
    int64 sender_id = 1;
    string sender_name = 2;
    string message = 3;
    int64 timestamp = 4;
}

message PlayerJoinedNotify {
    PlayerInfo player = 1;
    int32 total_players = 2;
}
```

```csharp
// SimplePacket 구현 (IMessage를 IPacket으로 래핑)
using Google.Protobuf;

public class SimplePacket : IPacket
{
    private IMessage? _parsedMessage;

    // Protobuf 메시지로 패킷 생성
    public SimplePacket(IMessage message)
    {
        MsgId = message.Descriptor.Name;  // "ChatMsg", "PlayerJoinedNotify" 등
        Payload = new SimpleProtoPayload(message);
        _parsedMessage = message;
    }

    // 수신 시 패킷 재구성 (역직렬화용)
    public SimplePacket(string msgId, IPayload payload, ushort msgSeq)
    {
        MsgId = msgId;
        Payload = new CopyPayload(payload);
        MsgSeq = msgSeq;
    }

    public string MsgId { get; }
    public IPayload Payload { get; }
    public ushort MsgSeq { get; }

    // 타입 안전 파싱
    public T Parse<T>() where T : IMessage, new()
    {
        if (_parsedMessage == null)
        {
            var message = new T();
            _parsedMessage = message.Descriptor.Parser.ParseFrom(Payload.DataSpan);
        }
        return (T)_parsedMessage;
    }
}

// 확장 메서드로 편리한 파싱 제공
public static class SimplePacketExtension
{
    public static T Parse<T>(this IPacket packet) where T : IMessage, new()
    {
        return ((SimplePacket)packet).Parse<T>();
    }
}
```

### 9.1 채팅 방

```csharp
#nullable enable

using Google.Protobuf;
using Simple;  // Proto 생성 네임스페이스

public class ChatStage : IStage
{
    private readonly Dictionary<long, IActor> _actors = new();
    public required IStageSender StageSender { get; init; }

    public async Task<(ushort, IPacket?)> OnCreate(IPacket packet)
    {
        var response = new CreateRoomAnswer
        {
            Success = true,
            StageId = StageSender.StageId,
            ErrorMessage = string.Empty
        };
        return (0, new SimplePacket(response));
    }

    public async Task OnPostCreate()
    {
        // 30분 후 자동 닫기
        StageSender.AddCountTimer(
            initialDelay: TimeSpan.FromMinutes(30),
            period: TimeSpan.Zero,
            count: 1,
            callback: async () => StageSender.CloseStage()
        );
    }

    public async Task<(ushort, IPacket?)> OnJoinStage(IActor actor, IPacket packet)
    {
        _actors.Add(actor.ActorSender.AccountId, actor);

        // Protobuf 메시지로 입장 알림 생성
        var notification = new PlayerJoinedNotify
        {
            Player = new PlayerInfo
            {
                AccountId = actor.ActorSender.AccountId,
                PlayerName = $"Player_{actor.ActorSender.AccountId}",
                PlayerState = 1
            },
            TotalPlayers = _actors.Count
        };

        // SimplePacket으로 래핑하여 브로드캐스트
        await StageSender.BroadcastAsync(
            new SimplePacket(notification),
            a => a.ActorSender.AccountId != actor.ActorSender.AccountId);

        var response = new JoinRoomAnswer
        {
            Success = true,
            RoomState = BuildRoomState(),
            ErrorMessage = string.Empty
        };
        return (0, new SimplePacket(response));
    }

    public async Task OnPostJoinStage(IActor actor)
    {
        await actor.OnCreate();
    }

    public async ValueTask OnDispatch(IActor actor, IPacket packet)
    {
        // MsgId로 메시지 타입 판별 (Protobuf Descriptor.Name)
        if (packet.MsgId == ChatMsg.Descriptor.Name)
        {
            // 타입 안전 파싱
            var chatMsg = packet.Parse<ChatMsg>();

            // 발신자 정보 보강하여 새 메시지 생성
            var enrichedMsg = new ChatMsg
            {
                SenderId = actor.ActorSender.AccountId,
                SenderName = chatMsg.SenderName ?? $"Player_{actor.ActorSender.AccountId}",
                Message = chatMsg.Message,
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };

            // 전체 브로드캐스트
            await StageSender.BroadcastAsync(new SimplePacket(enrichedMsg));
        }
    }

    public async ValueTask OnDisconnect(IActor actor)
    {
        _actors.Remove(actor.ActorSender.AccountId);
        await actor.OnDestroy();

        var notification = new PlayerLeftNotify
        {
            AccountId = actor.ActorSender.AccountId,
            PlayerName = $"Player_{actor.ActorSender.AccountId}",
            TotalPlayers = _actors.Count,
            Reason = "Disconnected"
        };

        await StageSender.BroadcastAsync(
            new SimplePacket(notification),
            a => a.ActorSender.AccountId != actor.ActorSender.AccountId);
    }

    public async ValueTask DisposeAsync()
    {
        _actors.Clear();
    }

    private string BuildRoomState()
    {
        return System.Text.Json.JsonSerializer.Serialize(new
        {
            stageId = StageSender.StageId,
            playerCount = _actors.Count
        });
    }
}
```

**새 인터페이스 적용 요약:**
- `required init` 프로퍼티
- `IPacket?` nullable 응답
- `ValueTask` Hot path 메서드
- `BroadcastAsync` 필터 지원
- `IAsyncDisposable` 구현
- **Protobuf 메시지 + SimplePacket 래핑** 패턴

### 9.2 게임 배틀 Stage

```csharp
#nullable enable

using Google.Protobuf;
using Simple;  // Proto 생성 네임스페이스

// Proto 메시지 정의 (battle.proto)
// message PlayerMoveMsg { int64 account_id = 1; float x = 2; float y = 3; float vx = 4; float vy = 5; }
// message PlayerAttackMsg { int64 attacker_id = 1; int64 target_id = 2; int32 damage = 3; }
// message PlayerHitNotify { int64 attacker_id = 1; int64 target_id = 2; int32 damage = 3; int32 health = 4; }
// message PlayerDeathNotify { int64 account_id = 1; }
// message GameStartNotify { int64 timestamp = 1; }
// message GameEndNotify { int64 winner_id = 1; bool has_winner = 2; }
// message CountdownNotify { int32 count = 1; }
// message GameStateNotify { repeated PlayerState players = 1; }
// message PlayerState { int64 account_id = 1; float x = 2; float y = 3; int32 health = 4; }

public class BattleStage : IStage
{
    private readonly Dictionary<long, GameActor> _players = new();
    private BattleState _state = BattleState.Waiting;
    private int _countdown = 3;

    public required IStageSender StageSender { get; init; }

    public async Task<(ushort, IPacket?)> OnCreate(IPacket packet)
    {
        var request = packet.Parse<CreateRoomAsk>();
        // _maxPlayers = request.MaxPlayers;

        var response = new CreateRoomAnswer
        {
            Success = true,
            StageId = StageSender.StageId,
            ErrorMessage = string.Empty
        };
        return (0, new SimplePacket(response));
    }

    public async Task OnPostCreate()
    {
        // 게임 틱 타이머 (100ms마다)
        StageSender.AddRepeatTimer(
            TimeSpan.FromMilliseconds(100),
            OnGameTick
        );
    }

    public async Task<(ushort, IPacket?)> OnJoinStage(IActor actor, IPacket packet)
    {
        var accountId = actor.ActorSender.AccountId;

        if (_players.Count >= 4)
        {
            var errorResponse = new JoinRoomAnswer
            {
                Success = false,
                RoomState = string.Empty,
                ErrorMessage = "Room is full"
            };
            return (1, new SimplePacket(errorResponse));
        }

        _players.Add(accountId, (GameActor)actor);

        // Protobuf 메시지로 입장 알림
        var notification = new PlayerJoinedNotify
        {
            Player = new PlayerInfo
            {
                AccountId = accountId,
                PlayerName = $"Player_{accountId}",
                PlayerState = 1
            },
            TotalPlayers = _players.Count
        };

        await StageSender.BroadcastAsync(
            new SimplePacket(notification),
            a => a.ActorSender.AccountId != accountId);

        var response = new JoinRoomAnswer
        {
            Success = true,
            RoomState = BuildRoomState(),
            ErrorMessage = string.Empty
        };
        return (0, new SimplePacket(response));
    }

    public async Task OnPostJoinStage(IActor actor)
    {
        await actor.OnCreate();

        // 2명 이상이면 카운트다운 시작
        if (_players.Count >= 2 && _state == BattleState.Waiting)
        {
            await StartCountdown();
        }
    }

    public async ValueTask OnDispatch(IActor actor, IPacket packet)
    {
        // Protobuf Descriptor.Name으로 메시지 타입 판별
        if (packet.MsgId == PlayerMoveMsg.Descriptor.Name)
        {
            await HandleMove(actor, packet);
        }
        else if (packet.MsgId == PlayerAttackMsg.Descriptor.Name)
        {
            await HandleAttack(actor, packet);
        }
    }

    public async ValueTask OnDisconnect(IActor actor)
    {
        var accountId = actor.ActorSender.AccountId;
        _players.Remove(accountId);
        await actor.OnDestroy();

        var notification = new PlayerLeftNotify
        {
            AccountId = accountId,
            PlayerName = $"Player_{accountId}",
            TotalPlayers = _players.Count,
            Reason = "Disconnected"
        };

        await StageSender.BroadcastAsync(
            new SimplePacket(notification),
            a => a.ActorSender.AccountId != accountId);

        // 1명 이하면 게임 종료
        if (_players.Count < 2 && _state == BattleState.Playing)
        {
            await EndGame();
        }
    }

    private async Task StartCountdown()
    {
        _state = BattleState.Countdown;
        StageSender.AddCountTimer(
            initialDelay: TimeSpan.FromSeconds(1),
            period: TimeSpan.FromSeconds(1),
            count: 3,
            callback: OnCountdownTick
        );
    }

    private async Task OnCountdownTick()
    {
        _countdown--;

        var countdownMsg = new CountdownNotify { Count = _countdown };
        await StageSender.BroadcastAsync(new SimplePacket(countdownMsg));

        if (_countdown == 0)
        {
            _state = BattleState.Playing;
            var startMsg = new GameStartNotify
            {
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };
            await StageSender.BroadcastAsync(new SimplePacket(startMsg));
        }
    }

    private async Task OnGameTick()
    {
        if (_state != BattleState.Playing) return;

        // 게임 로직 실행
        UpdatePhysics();
        CheckCollisions();
        await BroadcastGameState();
    }

    private async Task HandleMove(IActor actor, IPacket packet)
    {
        var accountId = actor.ActorSender.AccountId;
        var moveMsg = packet.Parse<PlayerMoveMsg>();
        var player = _players[accountId];

        player.X = moveMsg.X;
        player.Y = moveMsg.Y;
        player.Vx = moveMsg.Vx;
        player.Vy = moveMsg.Vy;

        // 다른 플레이어에게 이동 정보 전송
        var notification = new PlayerMoveMsg
        {
            AccountId = accountId,
            X = player.X,
            Y = player.Y,
            Vx = player.Vx,
            Vy = player.Vy
        };

        await StageSender.BroadcastAsync(
            new SimplePacket(notification),
            a => a.ActorSender.AccountId != accountId);
    }

    private async Task HandleAttack(IActor actor, IPacket packet)
    {
        var attackerAccountId = actor.ActorSender.AccountId;
        var attackMsg = packet.Parse<PlayerAttackMsg>();
        var attacker = _players[attackerAccountId];

        // 충돌 감지
        foreach (var target in _players.Values)
        {
            if (target.ActorSender.AccountId == attackerAccountId) continue;

            if (CheckHit(attacker, target, attackMsg))
            {
                target.Health -= attackMsg.Damage;

                var hitNotify = new PlayerHitNotify
                {
                    AttackerId = attackerAccountId,
                    TargetId = target.ActorSender.AccountId,
                    Damage = attackMsg.Damage,
                    Health = target.Health
                };
                await StageSender.BroadcastAsync(new SimplePacket(hitNotify));

                if (target.Health <= 0)
                {
                    await HandlePlayerDeath(target);
                }
            }
        }
    }

    private async Task HandlePlayerDeath(GameActor player)
    {
        var deathNotify = new PlayerDeathNotify
        {
            AccountId = player.ActorSender.AccountId
        };
        await StageSender.BroadcastAsync(new SimplePacket(deathNotify));

        // 생존자 1명이면 게임 종료
        var alivePlayers = _players.Values.Where(p => p.Health > 0).ToList();
        if (alivePlayers.Count == 1)
        {
            await EndGame(alivePlayers[0]);
        }
    }

    private async Task EndGame(GameActor? winner = null)
    {
        _state = BattleState.Ended;

        var endNotify = new GameEndNotify
        {
            WinnerId = winner?.ActorSender.AccountId ?? 0,
            HasWinner = winner != null
        };
        await StageSender.BroadcastAsync(new SimplePacket(endNotify));

        // 10초 후 Stage 닫기
        StageSender.AddCountTimer(
            initialDelay: TimeSpan.FromSeconds(10),
            period: TimeSpan.Zero,
            count: 1,
            callback: async () => StageSender.CloseStage()
        );
    }

    private async Task BroadcastGameState()
    {
        var stateNotify = new GameStateNotify();
        foreach (var player in _players.Values)
        {
            stateNotify.Players.Add(new PlayerState
            {
                AccountId = player.ActorSender.AccountId,
                X = player.X,
                Y = player.Y,
                Health = player.Health
            });
        }

        await StageSender.BroadcastAsync(new SimplePacket(stateNotify));
    }

    private string BuildRoomState()
    {
        return System.Text.Json.JsonSerializer.Serialize(new
        {
            stageId = StageSender.StageId,
            playerCount = _players.Count,
            state = _state.ToString()
        });
    }

    public async ValueTask DisposeAsync()
    {
        _players.Clear();
    }
}
```

## 10. 메시지 디스패처 패턴 (권장)

### 10.1 Handler 분리 패턴

복잡한 Stage에서는 메시지 처리 로직을 별도 Handler 클래스로 분리하는 것을 권장합니다. 기존 playhouse-sample-net의 검증된 패턴입니다.

```csharp
// 메시지 핸들러 분리 (RoomMessageHandler.cs)
internal static class RoomMessageHandler
{
    private static readonly ILogger _log = Log.Logger;

    /// <summary>
    /// 채팅 메시지 처리 - 전체 브로드캐스트
    /// </summary>
    public static async Task HandleChatMessage(GameRoom room, Player player, IPacket packet)
    {
        var chatMsg = packet.Parse<ChatMsg>();

        // 발신자 정보 보강
        var enrichedMsg = new ChatMsg
        {
            SenderId = player.AccountId,
            SenderName = chatMsg.SenderName ?? player.GetPlayerName(),
            Message = chatMsg.Message,
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };

        // 전체 브로드캐스트
        room.BroadcastToAllPlayers(new SimplePacket(enrichedMsg));
    }

    /// <summary>
    /// 방 나가기 처리
    /// </summary>
    public static async Task HandleLeaveRoom(GameRoom room, Player player, IPacket packet)
    {
        var request = packet.Parse<LeaveRoomReq>();

        // 1. API 서버에 알림 (fire-and-forget)
        player.ActorSender.SendToApi(new SimplePacket(new LeaveRoomNotify
        {
            AccountId = player.AccountId,
            SessionNid = player.ActorSender.SessionNid(),
            Sid = player.ActorSender.Sid(),
            Reason = request.Reason
        }));

        // 2. 플레이어 제거
        room.RemovePlayer(player);

        // 3. 클라이언트에 응답 (Reply)
        room.StageSender.Reply(new SimplePacket(new LeaveRoomRes
        {
            Success = true,
            Message = "Left room successfully"
        }));

        // 4. Actor 퇴장 처리
        player.ActorSender.LeaveStage();
    }

    /// <summary>
    /// 방 정보 조회 처리
    /// </summary>
    public static async Task HandleGetRoomInfo(GameRoom room, Player player, IPacket packet)
    {
        var roomInfo = room.GetRoomInfo();
        room.StageSender.Reply(new SimplePacket(roomInfo));
    }
}
```

### 10.2 OnDispatch에서 Handler 라우팅

```csharp
public class GameRoom : IStage
{
    private readonly Dictionary<long, Player> _players = new();

    public async ValueTask OnDispatch(IActor actor, IPacket packet)
    {
        var player = (Player)actor;

        // MsgId(Protobuf Descriptor.Name)로 핸들러 라우팅
        if (packet.MsgId == ChatMsg.Descriptor.Name)
        {
            await RoomMessageHandler.HandleChatMessage(this, player, packet);
        }
        else if (packet.MsgId == LeaveRoomReq.Descriptor.Name)
        {
            await RoomMessageHandler.HandleLeaveRoom(this, player, packet);
        }
        else if (packet.MsgId == GetRoomInfoReq.Descriptor.Name)
        {
            await RoomMessageHandler.HandleGetRoomInfo(this, player, packet);
        }
        else if (packet.MsgId == StartTimerReq.Descriptor.Name)
        {
            await TimerHandler.HandleStartTimer(this, player, packet, _activeTimers);
        }
        else if (packet.MsgId == CancelTimerReq.Descriptor.Name)
        {
            await TimerHandler.HandleCancelTimer(this, player, packet, _activeTimers);
        }
        else
        {
            _log.Warning("Unknown message: {MsgId}", packet.MsgId);
        }
    }
}
```

### 10.3 Handler 분리 장점

```
1. 코드 가독성
   - 메시지 타입별 로직 분리
   - Stage 클래스 크기 축소
   - 테스트 용이

2. 재사용성
   - 여러 Stage에서 공통 Handler 사용 가능
   - 도메인별 Handler 모음

3. 유지보수
   - 메시지 추가/수정 시 영향 범위 최소화
   - 관심사 분리
```

## 11. 통신 패턴 (Communication Patterns)

### 11.1 Request-Reply 패턴

클라이언트 요청에 대한 동기식 응답. `packet.IsRequest` (MsgSeq > 0)로 판별.

```csharp
// 클라이언트: GetRoomInfoReq 전송 (MsgSeq=100)
// 서버: GetRoomInfoRes 응답 (MsgSeq=100)

public async Task HandleGetRoomInfo(GameRoom room, Player player, IPacket packet)
{
    var request = packet.Parse<GetRoomInfoReq>();
    var roomInfo = room.GetRoomInfo();

    // Reply는 동일한 MsgSeq로 응답
    room.StageSender.Reply(new SimplePacket(roomInfo));
}
```

### 11.2 Fire-and-Forget 패턴

응답 없이 단방향 메시지 전송. 비동기 알림에 사용.

```csharp
// 서버 → 클라이언트 (Push)
player.ActorSender.SendToClient(new SimplePacket(notification));

// Stage → API 서버
player.ActorSender.SendToApi(new SimplePacket(leaveNotify));

// Stage → 다른 Stage (non-blocking)
await StageSender.SendToStageAsync(lobbyStageId, packet);
```

### 11.3 Broadcast 패턴

Stage 내 모든 Actor에게 동시 전송.

```csharp
// 전체 브로드캐스트
room.BroadcastToAllPlayers(new SimplePacket(chatMsg));

// 필터 적용 브로드캐스트 (발신자 제외)
await StageSender.BroadcastAsync(
    new SimplePacket(notification),
    actor => actor.ActorSender.AccountId != senderAccountId);
```

### 11.4 패턴 선택 가이드

| 상황 | 패턴 | 메서드 |
|------|------|--------|
| 클라이언트 요청 처리 | Request-Reply | `StageSender.Reply()` |
| 상태 변경 알림 | Fire-and-Forget | `SendToClient()`, `SendToApi()` |
| 전체 공지 | Broadcast | `BroadcastAsync()` |
| 특정 조건 알림 | Filtered Broadcast | `BroadcastAsync(filter)` |
| Stage간 통신 | Fire-and-Forget | `SendToStageAsync()` |

## 12. 베스트 프랙티스

### 12.1 Do (권장)

```
1. 메시지 기반 통신 (Lock-free 원칙)
   - Actor 간 직접 호출 금지
   - SendAsync, BroadcastAsync 사용 (fire-and-forget)
   - SendToStageAsync로 다른 Stage 통신

2. 불변 메시지
   - readonly record struct 사용
   - 메시지 데이터는 불변 객체로
   - 복사본 전달

3. 타이머 활용
   - AddRepeatTimer(interval, callback) 사용
   - AddCountTimer(interval, count, callback) 사용
   - while 루프 금지

4. AsyncBlock 사용
   - DB, HTTP 등 블로킹 작업
   - 파일 I/O
   - Func<Task<object?>> 형태

5. 리소스 정리
   - IAsyncDisposable.DisposeAsync() 구현
   - 타이머는 CloseStage에서 자동 정리

6. 현대 .NET 패턴
   - ValueTask 사용 (hot path)
   - required init 프로퍼티
   - #nullable enable
```

### 12.2 Don't (금지)

```
1. 공유 상태
   - static 변수 사용 금지
   - 다른 Stage 상태 직접 접근 금지

2. 블로킹 작업
   - Stage 내부에서 Thread.Sleep 금지
   - Task.Wait(), .Result 금지
   - DB 호출 직접 금지 (AsyncBlock 사용)

3. Blocking 응답 대기
   - RequestToStage 제거됨 (lock-free 원칙 위반)
   - Stage간 응답이 필요하면 콜백 패킷으로 처리

4. Lock 사용
   - lock 키워드 불필요
   - Mutex, Semaphore 불필요
   - ConcurrentQueue가 lock-free로 처리

5. 무한 루프
   - while(true) 금지
   - 타이머 사용

6. 예외 무시
   - try-catch로 적절히 처리
   - ILogger로 구조화된 로깅
```

## 13. 다음 단계

- `04-timer-system.md`: 타이머 시스템 상세 설명
- `07-client-protocol.md`: 클라이언트와의 상호작용 프로토콜
