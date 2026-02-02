# PlayHouse-NET 모범 사례 (Best Practices)

> 작성일: 2026-01-29
> 버전: 1.0
> 목적: 프로덕션 수준의 게임 서버 개발을 위한 권장 패턴 및 설계 원칙

## 개요

이 문서는 PlayHouse-NET을 사용하여 확장 가능하고 유지보수 가능한 게임 서버를 구축하기 위한 모범 사례와 권장 패턴을 제시합니다. 각 항목에는 **잘못된 예**와 **올바른 예**를 비교하여 이해를 돕고, 실제 코드 예제를 포함합니다.

---

## 1. Stage 설계 원칙

### 1.1 단일 책임 원칙 (Single Responsibility Principle)

**하나의 Stage는 하나의 명확한 목적을 가져야 합니다.**

#### ❌ 잘못된 예: 여러 책임을 가진 Stage

```csharp
// 하나의 Stage가 너무 많은 일을 처리
public class MegaStage : IStage
{
    private Dictionary<string, PlayerData> _players;
    private Dictionary<string, Item> _shop;
    private List<ChatMessage> _chatHistory;
    private BattleManager _battleManager;
    private LobbyManager _lobbyManager;

    public async Task OnDispatch(IActor actor, IPacket packet)
    {
        // 50개 이상의 메시지 타입 처리...
        switch (packet.MsgId)
        {
            case "JoinLobby": /* ... */ break;
            case "StartBattle": /* ... */ break;
            case "BuyItem": /* ... */ break;
            case "SendChat": /* ... */ break;
            // ... 수십 개의 케이스
        }
    }
}
```

**문제점:**
- Stage가 비대해져 유지보수가 어려움
- 테스트가 복잡함
- 코드 변경 시 영향 범위가 큼
- 로직 간 결합도가 높아짐

#### ✅ 올바른 예: 책임을 명확히 분리한 Stage

```csharp
// 로비 전용 Stage
public class LobbyStage : IStage
{
    private Dictionary<string, IActor> _waitingPlayers;

    public async Task OnDispatch(IActor actor, IPacket packet)
    {
        if (packet.MsgId == "FindMatch")
        {
            await HandleMatchmaking(actor, packet);
        }
        else if (packet.MsgId == "CancelMatch")
        {
            await HandleCancelMatch(actor, packet);
        }
    }
}

// 배틀 전용 Stage
public class BattleStage : IStage
{
    private BattleManager _battleManager;
    private Dictionary<string, PlayerState> _playerStates;

    public async Task OnDispatch(IActor actor, IPacket packet)
    {
        if (packet.MsgId == "PlayerMove")
        {
            await HandleMove(actor, packet);
        }
        else if (packet.MsgId == "PlayerAttack")
        {
            await HandleAttack(actor, packet);
        }
    }
}

// 상점 전용 Stage (또는 API 서버)
public class ShopController : IApiController
{
    public async Task<IPacket> BuyItem(BuyItemRequest request)
    {
        // 상점 로직만 담당
    }
}
```

**이유:**
- 각 Stage는 하나의 게임 상태/모드를 담당
- 코드 변경 시 영향 범위가 명확
- 테스트와 유지보수가 용이
- 로직 재사용 가능

---

### 1.2 상태 관리 패턴

**Stage의 상태는 불변 객체나 명확한 소유권을 가진 가변 객체로 관리해야 합니다.**

#### ❌ 잘못된 예: 공유 상태로 인한 혼란

```csharp
// 전역 static 변수 사용 (금지!)
public class GameStage : IStage
{
    private static int _globalPlayerCount = 0;  // ❌ 여러 Stage가 공유

    public async Task<(ushort, IPacket?)> OnJoinStage(IActor actor, IPacket packet)
    {
        _globalPlayerCount++;  // Race condition!
        return (0, null);
    }
}
```

**문제점:**
- 여러 Stage가 같은 변수를 공유하면 동시성 문제 발생
- Stage의 독립성이 깨짐
- 테스트가 어려움

#### ✅ 올바른 예: Stage별 독립 상태

```csharp
public class GameStage : IStage
{
    // Stage 전용 상태 (다른 Stage와 공유하지 않음)
    private readonly Dictionary<string, PlayerState> _players = new();
    private GamePhase _currentPhase = GamePhase.Waiting;
    private int _maxPlayers = 4;

    public IStageLink StageLink { get; }

    public GameStage(IStageLink stageLink)
    {
        StageLink = stageLink;
    }

    public async Task<(ushort, IPacket?)> OnJoinStage(IActor actor, IPacket packet)
    {
        if (_players.Count >= _maxPlayers)
        {
            return (ErrorCode.StageFull, null);
        }

        _players[actor.ActorLink.AccountId] = new PlayerState();
        return (0, null);
    }
}
```

**이유:**
- Stage는 단일 스레드에서 실행되므로 Stage 내부 상태는 동시성 문제가 없음
- 각 Stage는 독립된 상태를 관리하여 격리 보장
- 테스트 시 Stage별로 독립적으로 검증 가능

---

### 1.3 Stage 간 통신

**Stage 간 직접 호출 대신 메시지 기반 통신을 사용합니다.**

#### ❌ 잘못된 예: 직접 호출 시도

```csharp
// ❌ Stage 간 직접 호출은 불가능
public class LobbyStage : IStage
{
    private BattleStage? _battleStage;  // ❌ 다른 Stage 참조

    public async Task HandleStartMatch(IActor actor)
    {
        _battleStage?.StartGame(actor);  // ❌ 직접 호출 불가
    }
}
```

**문제점:**
- Stage는 다른 Stage의 참조를 가질 수 없음
- 메시지 큐를 우회하여 동시성 문제 발생
- Lock-Free 원칙 위반

#### ✅ 올바른 예: 메시지 기반 통신

```csharp
public class LobbyStage : IStage
{
    public async Task HandleMatchFound(List<IActor> players)
    {
        // API 서버에 배틀 Stage 생성 요청
        var result = await StageLink.RequestToApiService(
            serviceId: 200,
            packet: CPacket.Of(new CreateBattleRequest
            {
                Players = players.Select(p => p.ActorLink.AccountId).ToList()
            })
        );

        var response = result.Parse<CreateBattleResponse>();
        long battleStageId = response.StageId;

        // 플레이어들에게 배틀 Stage 정보 전달
        foreach (var player in players)
        {
            player.ActorLink.SendToClient(CPacket.Of(new MatchFoundNotify
            {
                BattleStageId = battleStageId
            }));
        }
    }
}
```

**이유:**
- Stage 간 메시지 통신은 Lock-Free 보장
- 각 Stage의 독립성 유지
- 비동기 처리로 블로킹 없음

---

### 1.4 Stage 수명 관리

**Stage는 빈 상태가 되면 자동으로 정리하고, 리소스는 OnDestroy에서 해제합니다.**

#### ✅ 올바른 예: 자동 정리 패턴

```csharp
public class BattleStage : IStage
{
    private readonly Dictionary<string, IActor> _players = new();
    private long _inactivityTimerId;

    public async ValueTask OnLeaveRoom(IActor actor, LeaveReason reason)
    {
        _players.Remove(actor.ActorLink.AccountId);

        // 플레이어가 모두 떠나면 Stage 닫기
        if (_players.Count == 0)
        {
            // 비활성 타이머 시작 (5분 후 자동 닫기)
            _inactivityTimerId = StageLink.AddCountTimer(
                initialDelay: TimeSpan.FromMinutes(5),
                period: TimeSpan.Zero,
                count: 1,
                callback: async () =>
                {
                    if (_players.Count == 0)
                    {
                        StageLink.CloseStage();
                    }
                }
            );
        }
        else
        {
            // 플레이어가 다시 입장하면 타이머 취소
            if (StageLink.HasTimer(_inactivityTimerId))
            {
                StageLink.CancelTimer(_inactivityTimerId);
            }
        }
    }

    public Task OnDestroy()
    {
        // 모든 타이머 정리
        if (StageLink.HasTimer(_inactivityTimerId))
        {
            StageLink.CancelTimer(_inactivityTimerId);
        }

        // 게임루프 정리
        if (StageLink.IsGameLoopRunning)
        {
            StageLink.StopGameLoop();
        }

        return Task.CompletedTask;
    }
}
```

**이유:**
- 메모리 누수 방지
- 리소스 효율적 관리
- 서버 부하 감소

---

## 2. Actor 설계 원칙

### 2.1 가벼운 Actor 유지

**Actor는 최소한의 상태만 보유하고, 대부분의 게임 로직은 Stage에 위임합니다.**

#### ❌ 잘못된 예: 무거운 Actor

```csharp
public class HeavyActor : IActor
{
    private Dictionary<int, Item> _inventory = new();  // ❌ 큰 상태
    private List<Quest> _activeQuests = new();
    private Dictionary<int, Skill> _skills = new();
    private PlayerStats _stats = new();

    public IActorLink ActorLink { get; }

    public HeavyActor(IActorLink actorLink)
    {
        ActorLink = actorLink;
    }

    // Actor가 너무 많은 로직을 가짐
    public async Task UseSkill(int skillId)
    {
        var skill = _skills[skillId];
        // 복잡한 스킬 로직...
    }
}
```

**문제점:**
- Actor당 메모리 사용량이 큼
- 재연결 시 상태 동기화 복잡
- Actor 생성/파괴 오버헤드 증가

#### ✅ 올바른 예: 가벼운 Actor

```csharp
public class LightActor : IActor
{
    // 최소한의 상태만 보유
    private bool _isReady = false;

    public IActorLink ActorLink { get; }

    public LightActor(IActorLink actorLink)
    {
        ActorLink = actorLink;
    }

    public Task OnCreate()
    {
        return Task.CompletedTask;
    }

    public async Task<(bool, IPacket?)> OnAuthenticate(IPacket authPacket)
    {
        var request = authPacket.Parse<AuthenticateRequest>();

        // 필수: AccountId 설정
        ActorLink.AccountId = request.UserId;

        return (true, CPacket.Of(new AuthenticateResponse
        {
            Success = true,
            AccountId = ActorLink.AccountId
        }));
    }

    public Task OnPostAuthenticate()
    {
        // 인증 완료 후 처리
        return Task.CompletedTask;
    }

    public Task OnDestroy()
    {
        return Task.CompletedTask;
    }
}
```

**이유:**
- Actor는 클라이언트 연결과 1:1 매핑
- 게임 로직은 Stage가 담당
- 메모리 효율성 향상

---

### 2.2 인증 데이터 캐싱

**인증 정보는 Actor에 캐싱하여 재연결 시 빠른 복구를 지원합니다.**

#### ✅ 올바른 예: 인증 정보 캐싱

```csharp
public class CachedActor : IActor
{
    private string _nickname = "";
    private int _level = 1;
    private bool _isInitialized = false;

    public IActorLink ActorLink { get; }

    public CachedActor(IActorLink actorLink)
    {
        ActorLink = actorLink;
    }

    public async Task OnCreate()
    {
        // 최초 생성 시 DB에서 로드
        var userData = await LoadUserDataFromDB(ActorLink.AccountId);
        _nickname = userData.Nickname;
        _level = userData.Level;
    }

    public async Task<(bool, IPacket?)> OnAuthenticate(IPacket authPacket)
    {
        var request = authPacket.Parse<AuthenticateRequest>();

        // 토큰 검증
        bool isValid = await ValidateToken(request.Token);
        if (!isValid)
        {
            return (false, CPacket.Of(new AuthenticateResponse
            {
                Success = false,
                Error = "Invalid token"
            }));
        }

        ActorLink.AccountId = request.UserId;

        return (true, CPacket.Of(new AuthenticateResponse
        {
            Success = true,
            AccountId = ActorLink.AccountId,
            Nickname = _nickname,
            Level = _level
        }));
    }

    public async Task OnPostAuthenticate()
    {
        if (!_isInitialized)
        {
            // 최초 연결 시
            _isInitialized = true;

            // 웰컴 메시지
            ActorLink.SendToClient(CPacket.Of(new WelcomeMessage
            {
                Message = $"Welcome, {_nickname}!"
            }));
        }
        else
        {
            // 재연결 시 - 현재 게임 상태 동기화
            ActorLink.SendToClient(CPacket.Of(new ReconnectedMessage
            {
                Message = "Reconnected successfully"
            }));
        }
    }

    private async Task<UserData> LoadUserDataFromDB(string accountId)
    {
        // DB 조회 로직
        return new UserData { Nickname = "Player", Level = 1 };
    }

    private async Task<bool> ValidateToken(string token)
    {
        // 토큰 검증 로직
        return !string.IsNullOrEmpty(token);
    }

    public Task OnDestroy()
    {
        return Task.CompletedTask;
    }

    private class UserData
    {
        public string Nickname { get; set; } = "";
        public int Level { get; set; }
    }
}
```

**이유:**
- 재연결 시 DB 조회 불필요
- 빠른 재연결 복구
- 네트워크 불안정 상황에서 사용자 경험 개선

---

### 2.3 재연결 처리

**재연결 시 OnAuthenticate는 호출되지만 OnJoinStage는 호출되지 않습니다.**

#### ✅ 올바른 예: 재연결 시나리오 처리

```csharp
public class ReconnectableStage : IStage
{
    private readonly Dictionary<string, IActor> _actors = new();
    private readonly Dictionary<string, long> _reconnectTimers = new();

    public IStageLink StageLink { get; }

    public ReconnectableStage(IStageLink stageLink)
    {
        StageLink = stageLink;
    }

    public async ValueTask OnConnectionChanged(IActor actor, bool isConnected, DisconnectReason? reason)
    {
        var accountId = actor.ActorLink.AccountId;

        if (isConnected)
        {
            // 재연결
            LOG.Info($"Player reconnected: {accountId}");

            // 재연결 타이머 취소
            if (_reconnectTimers.TryGetValue(accountId, out var timerId))
            {
                StageLink.CancelTimer(timerId);
                _reconnectTimers.Remove(accountId);
            }

            // 다른 플레이어에게 재연결 알림
            await BroadcastToOthers(actor, new PlayerReconnectedNotify
            {
                AccountId = accountId
            });
        }
        else
        {
            // 연결 끊김
            LOG.Info($"Player disconnected: {accountId}, reason: {reason}");

            // 30초 재연결 타이머 시작
            var timerId = StageLink.AddCountTimer(
                initialDelay: TimeSpan.FromSeconds(30),
                period: TimeSpan.Zero,
                count: 1,
                callback: async () =>
                {
                    // 재연결 타임아웃 - Actor 제거
                    if (_actors.ContainsKey(accountId))
                    {
                        await actor.ActorLink.LeaveStageAsync();
                    }
                }
            );
            _reconnectTimers[accountId] = timerId;

            // 다른 플레이어에게 연결 끊김 알림
            await BroadcastToOthers(actor, new PlayerDisconnectedNotify
            {
                AccountId = accountId
            });
        }
    }

    private async Task BroadcastToOthers(IActor link, IMessage message)
    {
        var packet = CPacket.Of(message);
        foreach (var actor in _actors.Values)
        {
            if (actor.ActorLink.AccountId != link.ActorLink.AccountId)
            {
                actor.ActorLink.SendToClient(packet);
            }
        }
    }

    // 나머지 메서드들...
    public Task<(ushort, IPacket?)> OnCreate(IPacket packet) => Task.FromResult<(ushort, IPacket?)>((0, null));
    public Task OnPostCreate() => Task.CompletedTask;
    public Task<(ushort, IPacket?)> OnJoinStage(IActor actor, IPacket packet) => Task.FromResult<(ushort, IPacket?)>((0, null));
    public Task OnPostJoinStage(IActor actor) => Task.CompletedTask;
    public ValueTask OnLeaveRoom(IActor actor, LeaveReason reason) => ValueTask.CompletedTask;
    public ValueTask OnDispatch(IActor actor, IPacket packet) => ValueTask.CompletedTask;
    public Task OnDestroy() => Task.CompletedTask;
}
```

**이유:**
- 재연결은 PlayHouse가 자동으로 처리
- 타임아웃으로 유령 세션 방지
- 다른 플레이어에게 상태 변경 알림

---

## 3. 메시지 설계 원칙

### 3.1 메시지 크기 최적화

**메시지는 가능한 작게 유지하고, 큰 데이터는 별도로 전송합니다.**

#### ❌ 잘못된 예: 거대한 메시지

```csharp
// ❌ 모든 플레이어 정보를 하나의 메시지에
message GameStateUpdate {
    repeated PlayerFullData players = 1;  // 100명 × 10KB = 1MB
}

message PlayerFullData {
    string account_id = 1;
    string nickname = 2;
    bytes inventory_data = 3;       // 큰 인벤토리 데이터
    bytes quest_data = 4;            // 큰 퀘스트 데이터
    bytes skill_data = 5;            // 큰 스킬 데이터
    // ...
}
```

**문제점:**
- 네트워크 대역폭 낭비
- 불필요한 데이터 전송
- 직렬화/역직렬화 오버헤드

#### ✅ 올바른 예: 필요한 정보만 전송

```csharp
// ✅ 게임 상태는 최소 정보만
message GameStateUpdate {
    repeated PlayerPosition players = 1;  // 100명 × 20B = 2KB
}

message PlayerPosition {
    string account_id = 1;
    float x = 2;
    float y = 3;
    float rotation = 4;
}

// ✅ 큰 데이터는 필요할 때만 별도 요청
message InventoryDataResponse {
    repeated InventoryItem items = 1;
}
```

**이유:**
- 네트워크 효율성 향상
- 레이턴시 감소
- 서버 부하 감소

---

### 3.2 버전 호환성 (Protobuf)

**Protobuf를 사용할 때는 필드 번호를 절대 재사용하지 않고, 하위 호환성을 유지합니다.**

#### ❌ 잘못된 예: 필드 번호 재사용

```protobuf
// ❌ 기존 버전
message PlayerData {
    string name = 1;
    int32 level = 2;
    // int32 old_field = 3;  // 삭제됨
}

// ❌ 새 버전 - 필드 번호 재사용
message PlayerData {
    string name = 1;
    int32 level = 2;
    string new_field = 3;  // ❌ 번호 3 재사용 (위험!)
}
```

**문제점:**
- 구버전 클라이언트가 새 서버와 통신 시 데이터 오해석
- 디버깅 어려움

#### ✅ 올바른 예: 필드 번호 보존

```protobuf
// ✅ 기존 버전
message PlayerData {
    string name = 1;
    int32 level = 2;
    reserved 3;  // 삭제된 필드 번호 예약
}

// ✅ 새 버전 - 새 필드 번호 사용
message PlayerData {
    string name = 1;
    int32 level = 2;
    reserved 3;  // 삭제된 필드
    string new_field = 4;  // ✅ 새 번호 사용
    optional int32 optional_field = 5;  // optional로 선택적 필드 추가
}
```

**이유:**
- 버전 간 호환성 보장
- 점진적 업데이트 가능
- 클라이언트와 서버 독립 배포

---

### 3.3 메시지 ID 명명 규칙

**메시지 ID는 Protobuf Descriptor.Name을 사용하여 일관성을 유지합니다.**

#### ✅ 올바른 예: Protobuf 메시지 이름 활용

```protobuf
// Proto 정의
message PlayerMoveRequest {
    float x = 1;
    float y = 2;
}

message PlayerMoveResponse {
    bool success = 1;
}
```

```csharp
// C# 코드 - MsgId는 자동으로 Descriptor.Name 사용
var request = new PlayerMoveRequest { X = 10.0f, Y = 20.0f };
using var packet = new SimplePacket(request);  // MsgId = "PlayerMoveRequest"

// 수신 시 MsgId로 타입 판별
if (packet.MsgId == PlayerMoveRequest.Descriptor.Name)
{
    var move = packet.Parse<PlayerMoveRequest>();
    // 처리...
}
```

**이유:**
- 일관된 네이밍
- 타입 안전성
- 오타 방지

---

## 4. 타이머와 게임루프

### 4.1 타이머 vs 게임루프 선택 기준

**작업의 특성에 따라 적절한 메커니즘을 선택합니다.**

| 작업 타입 | 사용할 메커니즘 | 이유 |
|-----------|-----------------|------|
| 주기적 저장, 이벤트 | `AddRepeatTimer` | 낮은 주기성, 일반 정확도 |
| 카운트다운, 제한 시간 | `AddCountTimer` | 횟수 제한 필요 |
| 물리 시뮬레이션 | `StartGameLoop` | 고정 타임스텝 필요 |
| AI 업데이트 | `StartGameLoop` | 일정한 주기 필요 |
| 게임 상태 브로드캐스트 | `StartGameLoop` | 높은 주기성 |

#### ✅ 올바른 예: 타이머 사용

```csharp
public class GameStage : IStage
{
    public Task OnPostCreate()
    {
        // 5분마다 자동 저장
        StageLink.AddRepeatTimer(
            initialDelay: TimeSpan.FromMinutes(5),
            period: TimeSpan.FromMinutes(5),
            callback: async () =>
            {
                await SaveGameState();
            }
        );

        // 60초 카운트다운
        StageLink.AddCountTimer(
            initialDelay: TimeSpan.FromSeconds(1),
            period: TimeSpan.FromSeconds(1),
            count: 60,
            callback: async () =>
            {
                _countdown--;
                await BroadcastCountdown(_countdown);

                if (_countdown == 0)
                {
                    await StartGame();
                }
            }
        );

        return Task.CompletedTask;
    }

    // 나머지 메서드들...
    public IStageLink StageLink { get; }
    public GameStage(IStageLink stageLink) { StageLink = stageLink; }
    public Task<(ushort, IPacket?)> OnCreate(IPacket packet) => Task.FromResult<(ushort, IPacket?)>((0, null));
    private int _countdown = 60;
    private Task SaveGameState() => Task.CompletedTask;
    private Task BroadcastCountdown(int countdown) => Task.CompletedTask;
    private Task StartGame() => Task.CompletedTask;
    public Task<(ushort, IPacket?)> OnJoinStage(IActor actor, IPacket packet) => Task.FromResult<(ushort, IPacket?)>((0, null));
    public Task OnPostJoinStage(IActor actor) => Task.CompletedTask;
    public ValueTask OnConnectionChanged(IActor actor, bool isConnected, DisconnectReason? reason) => ValueTask.CompletedTask;
    public ValueTask OnLeaveRoom(IActor actor, LeaveReason reason) => ValueTask.CompletedTask;
    public ValueTask OnDispatch(IActor actor, IPacket packet) => ValueTask.CompletedTask;
    public Task OnDestroy() => Task.CompletedTask;
}
```

#### ✅ 올바른 예: 게임루프 사용

```csharp
public class BattleStage : IStage
{
    private int _tickCount = 0;

    public IStageLink StageLink { get; }

    public BattleStage(IStageLink stageLink)
    {
        StageLink = stageLink;
    }

    public Task OnPostCreate()
    {
        // 50ms (20Hz) 고정 타임스텝
        StageLink.StartGameLoop(
            fixedTimestep: TimeSpan.FromMilliseconds(50),
            callback: async (deltaTime, totalElapsed) =>
            {
                _tickCount++;

                // 물리 업데이트 (매 틱)
                UpdatePhysics(deltaTime);

                // 200ms마다 상태 브로드캐스트 (50ms × 4)
                if (_tickCount % 4 == 0)
                {
                    await BroadcastGameState();
                }
            }
        );

        return Task.CompletedTask;
    }

    private void UpdatePhysics(TimeSpan deltaTime)
    {
        var dt = (float)deltaTime.TotalSeconds;
        // 물리 계산...
    }

    private Task BroadcastGameState()
    {
        // 상태 브로드캐스트...
        return Task.CompletedTask;
    }

    public Task OnDestroy()
    {
        if (StageLink.IsGameLoopRunning)
        {
            StageLink.StopGameLoop();
        }
        return Task.CompletedTask;
    }

    // 나머지 메서드들...
    public Task<(ushort, IPacket?)> OnCreate(IPacket packet) => Task.FromResult<(ushort, IPacket?)>((0, null));
    public Task<(ushort, IPacket?)> OnJoinStage(IActor actor, IPacket packet) => Task.FromResult<(ushort, IPacket?)>((0, null));
    public Task OnPostJoinStage(IActor actor) => Task.CompletedTask;
    public ValueTask OnConnectionChanged(IActor actor, bool isConnected, DisconnectReason? reason) => ValueTask.CompletedTask;
    public ValueTask OnLeaveRoom(IActor actor, LeaveReason reason) => ValueTask.CompletedTask;
    public ValueTask OnDispatch(IActor actor, IPacket packet) => ValueTask.CompletedTask;
}
```

**이유:**
- 게임루프는 고정 타임스텝으로 물리 시뮬레이션에 적합
- 타이머는 간헐적 작업에 적합
- 각 메커니즘의 특성에 맞게 사용

---

### 4.2 타이머 누수 방지

**생성한 모든 타이머는 반드시 OnDestroy에서 정리해야 합니다.**

#### ❌ 잘못된 예: 타이머 누수

```csharp
public class LeakyStage : IStage
{
    public Task OnPostCreate()
    {
        // 타이머 ID를 저장하지 않음 - 취소 불가!
        StageLink.AddRepeatTimer(
            TimeSpan.FromSeconds(1),
            TimeSpan.FromSeconds(1),
            async () => { /* ... */ }
        );

        return Task.CompletedTask;
    }

    public Task OnDestroy()
    {
        // 타이머를 취소할 방법이 없음!
        return Task.CompletedTask;
    }

    // 나머지 메서드들...
    public IStageLink StageLink { get; }
    public LeakyStage(IStageLink stageLink) { StageLink = stageLink; }
    public Task<(ushort, IPacket?)> OnCreate(IPacket packet) => Task.FromResult<(ushort, IPacket?)>((0, null));
    public Task<(ushort, IPacket?)> OnJoinStage(IActor actor, IPacket packet) => Task.FromResult<(ushort, IPacket?)>((0, null));
    public Task OnPostJoinStage(IActor actor) => Task.CompletedTask;
    public ValueTask OnConnectionChanged(IActor actor, bool isConnected, DisconnectReason? reason) => ValueTask.CompletedTask;
    public ValueTask OnLeaveRoom(IActor actor, LeaveReason reason) => ValueTask.CompletedTask;
    public ValueTask OnDispatch(IActor actor, IPacket packet) => ValueTask.CompletedTask;
}
```

**문제점:**
- 타이머가 계속 실행됨 (메모리/CPU 누수)
- Stage 종료 후에도 콜백 호출
- 리소스 낭비

#### ✅ 올바른 예: 타이머 추적 및 정리

```csharp
public class ProperStage : IStage
{
    private readonly List<long> _timerIds = new();

    public IStageLink StageLink { get; }

    public ProperStage(IStageLink stageLink)
    {
        StageLink = stageLink;
    }

    public Task OnPostCreate()
    {
        // 타이머 ID 저장
        var timerId1 = StageLink.AddRepeatTimer(
            TimeSpan.FromSeconds(1),
            TimeSpan.FromSeconds(1),
            async () => { await OnTick(); }
        );
        _timerIds.Add(timerId1);

        var timerId2 = StageLink.AddCountTimer(
            TimeSpan.FromSeconds(10),
            TimeSpan.FromSeconds(1),
            count: 10,
            callback: async () => { await OnCountdown(); }
        );
        _timerIds.Add(timerId2);

        return Task.CompletedTask;
    }

    public Task OnDestroy()
    {
        // 모든 타이머 취소
        foreach (var timerId in _timerIds)
        {
            if (StageLink.HasTimer(timerId))
            {
                StageLink.CancelTimer(timerId);
            }
        }
        _timerIds.Clear();

        return Task.CompletedTask;
    }

    private Task OnTick() => Task.CompletedTask;
    private Task OnCountdown() => Task.CompletedTask;

    // 나머지 메서드들...
    public Task<(ushort, IPacket?)> OnCreate(IPacket packet) => Task.FromResult<(ushort, IPacket?)>((0, null));
    public Task<(ushort, IPacket?)> OnJoinStage(IActor actor, IPacket packet) => Task.FromResult<(ushort, IPacket?)>((0, null));
    public Task OnPostJoinStage(IActor actor) => Task.CompletedTask;
    public ValueTask OnConnectionChanged(IActor actor, bool isConnected, DisconnectReason? reason) => ValueTask.CompletedTask;
    public ValueTask OnLeaveRoom(IActor actor, LeaveReason reason) => ValueTask.CompletedTask;
    public ValueTask OnDispatch(IActor actor, IPacket packet) => ValueTask.CompletedTask;
}
```

**이유:**
- 타이머 누수 방지
- 정확한 리소스 정리
- 예측 가능한 동작

---

### 4.3 게임루프 최적화

**게임루프에서는 매 틱마다 실행할 필요가 없는 작업을 적절히 간헐화합니다.**

#### ✅ 올바른 예: 간헐화 패턴

```csharp
public class OptimizedGameLoop : IStage
{
    private int _tickCount = 0;

    public IStageLink StageLink { get; }

    public OptimizedGameLoop(IStageLink stageLink)
    {
        StageLink = stageLink;
    }

    public Task OnPostCreate()
    {
        StageLink.StartGameLoop(
            TimeSpan.FromMilliseconds(50),  // 20Hz
            async (deltaTime, totalElapsed) =>
            {
                _tickCount++;

                // 매 틱: 물리 업데이트 (50ms)
                UpdatePhysics(deltaTime);

                // 4틱마다: 상태 브로드캐스트 (200ms)
                if (_tickCount % 4 == 0)
                {
                    await BroadcastGameState();
                }

                // 20틱마다: AI 업데이트 (1초)
                if (_tickCount % 20 == 0)
                {
                    UpdateAI();
                }

                // 200틱마다: 통계 저장 (10초)
                if (_tickCount % 200 == 0)
                {
                    await SaveStatistics();
                }
            }
        );

        return Task.CompletedTask;
    }

    private void UpdatePhysics(TimeSpan deltaTime) { /* ... */ }
    private Task BroadcastGameState() => Task.CompletedTask;
    private void UpdateAI() { /* ... */ }
    private Task SaveStatistics() => Task.CompletedTask;

    // 나머지 메서드들...
    public Task<(ushort, IPacket?)> OnCreate(IPacket packet) => Task.FromResult<(ushort, IPacket?)>((0, null));
    public Task<(ushort, IPacket?)> OnJoinStage(IActor actor, IPacket packet) => Task.FromResult<(ushort, IPacket?)>((0, null));
    public Task OnPostJoinStage(IActor actor) => Task.CompletedTask;
    public ValueTask OnConnectionChanged(IActor actor, bool isConnected, DisconnectReason? reason) => ValueTask.CompletedTask;
    public ValueTask OnLeaveRoom(IActor actor, LeaveReason reason) => ValueTask.CompletedTask;
    public ValueTask OnDispatch(IActor actor, IPacket packet) => ValueTask.CompletedTask;
    public Task OnDestroy()
    {
        if (StageLink.IsGameLoopRunning)
        {
            StageLink.StopGameLoop();
        }
        return Task.CompletedTask;
    }
}
```

**이유:**
- CPU 사용률 감소
- 네트워크 대역폭 절약
- 우선순위에 따른 리소스 배분

---

## 5. 비동기 작업 (AsyncCompute/AsyncIO)

### 5.1 AsyncCompute vs AsyncIO 선택

**작업의 특성에 따라 적절한 AsyncBlock을 선택합니다.**

#### ✅ AsyncIO - I/O 바운드 작업

```csharp
public class IoStage : IStage
{
    private readonly IUserRepository _userRepository;

    public IStageLink StageLink { get; }

    public IoStage(IStageLink stageLink, IUserRepository userRepository)
    {
        StageLink = stageLink;
        _userRepository = userRepository;
    }

    public async Task HandleSaveRequest(IActor actor, IPacket packet)
    {
        var request = packet.Parse<SaveDataRequest>();

        // 즉시 수락 응답
        StageLink.Reply(CPacket.Of(new SaveDataResponse
        {
            Accepted = true
        }));

        // AsyncIO로 DB 저장
        StageLink.AsyncIO(
            preCallback: async () =>
            {
                // I/O 스레드 풀에서 실행 (블로킹 가능)
                await _userRepository.SavePlayerDataAsync(
                    actor.ActorLink.AccountId,
                    request.Data
                );
                return "Save completed";
            },
            postCallback: async (result) =>
            {
                // Stage 이벤트 루프로 복귀
                actor.ActorLink.SendToClient(CPacket.Of(new SaveCompletedNotify
                {
                    Success = true,
                    Message = result?.ToString() ?? ""
                }));
                return Task.CompletedTask;
            }
        );
    }

    // 나머지 메서드들...
    public Task<(ushort, IPacket?)> OnCreate(IPacket packet) => Task.FromResult<(ushort, IPacket?)>((0, null));
    public Task OnPostCreate() => Task.CompletedTask;
    public Task<(ushort, IPacket?)> OnJoinStage(IActor actor, IPacket packet) => Task.FromResult<(ushort, IPacket?)>((0, null));
    public Task OnPostJoinStage(IActor actor) => Task.CompletedTask;
    public ValueTask OnConnectionChanged(IActor actor, bool isConnected, DisconnectReason? reason) => ValueTask.CompletedTask;
    public ValueTask OnLeaveRoom(IActor actor, LeaveReason reason) => ValueTask.CompletedTask;
    public ValueTask OnDispatch(IActor actor, IPacket packet) { HandleSaveRequest(actor, packet); return ValueTask.CompletedTask; }
    public Task OnDestroy() => Task.CompletedTask;
}
```

#### ✅ AsyncCompute - CPU 바운드 작업

```csharp
public class ComputeStage : IStage
{
    public IStageLink StageLink { get; }

    public ComputeStage(IStageLink stageLink)
    {
        StageLink = stageLink;
    }

    public async Task HandlePathfinding(IActor actor, IPacket packet)
    {
        var request = packet.Parse<PathfindingRequest>();

        StageLink.Reply(CPacket.Of(new PathfindingResponse
        {
            Accepted = true
        }));

        // AsyncCompute로 경로 탐색 (CPU 집약적)
        StageLink.AsyncCompute(
            preCallback: async () =>
            {
                // Compute 스레드 풀에서 실행 (CPU 코어 수만큼 제한)
                var path = CalculatePath(request.Start, request.End);
                return path;
            },
            postCallback: async (result) =>
            {
                var path = (List<Vector2>)result!;

                actor.ActorLink.SendToClient(CPacket.Of(new PathFoundNotify
                {
                    Path = { path.Select(p => new Position { X = p.X, Y = p.Y }) }
                }));
                return Task.CompletedTask;
            }
        );
    }

    private List<Vector2> CalculatePath(Position start, Position end)
    {
        // A* 알고리즘 같은 CPU 집약적 계산
        return new List<Vector2>();
    }

    private class Vector2 { public float X { get; set; } public float Y { get; set; } }

    // 나머지 메서드들...
    public Task<(ushort, IPacket?)> OnCreate(IPacket packet) => Task.FromResult<(ushort, IPacket?)>((0, null));
    public Task OnPostCreate() => Task.CompletedTask;
    public Task<(ushort, IPacket?)> OnJoinStage(IActor actor, IPacket packet) => Task.FromResult<(ushort, IPacket?)>((0, null));
    public Task OnPostJoinStage(IActor actor) => Task.CompletedTask;
    public ValueTask OnConnectionChanged(IActor actor, bool isConnected, DisconnectReason? reason) => ValueTask.CompletedTask;
    public ValueTask OnLeaveRoom(IActor actor, LeaveReason reason) => ValueTask.CompletedTask;
    public ValueTask OnDispatch(IActor actor, IPacket packet) { HandlePathfinding(actor, packet); return ValueTask.CompletedTask; }
    public Task OnDestroy() => Task.CompletedTask;
}
```

**선택 가이드:**

| 작업 타입 | AsyncBlock | 예시 |
|-----------|------------|------|
| 데이터베이스 쿼리 | AsyncIO | SELECT, INSERT, UPDATE |
| HTTP API 호출 | AsyncIO | REST API, 웹훅 |
| 파일 I/O | AsyncIO | 로그 저장, 설정 읽기 |
| 경로 탐색 | AsyncCompute | A* 알고리즘 |
| 암호화/복호화 | AsyncCompute | AES, RSA |
| 이미지 처리 | AsyncCompute | 리사이징, 필터 |

---

### 5.2 데이터베이스 호출 패턴

**DB 호출은 AsyncIO에서 수행하고, 결과는 PostCallback에서 Stage 상태로 반영합니다.**

#### ❌ 잘못된 예: Stage에서 직접 DB 호출

```csharp
public async Task HandleLoadUser(IActor actor, IPacket packet)
{
    // ❌ Stage 이벤트 루프를 블로킹!
    var userData = await _database.LoadUserAsync(actor.ActorLink.AccountId);

    // Stage가 블로킹되어 다른 메시지 처리 불가
}
```

**문제점:**
- Stage 이벤트 루프 블로킹
- 다른 메시지 처리 지연
- 성능 저하

#### ✅ 올바른 예: AsyncIO 사용

```csharp
public class DatabaseStage : IStage
{
    private readonly IUserDatabase _database;
    private readonly Dictionary<string, PlayerData> _playerCache = new();

    public IStageLink StageLink { get; }

    public DatabaseStage(IStageLink stageLink, IUserDatabase database)
    {
        StageLink = stageLink;
        _database = database;
    }

    public async Task HandleLoadUser(IActor actor, IPacket packet)
    {
        var accountId = actor.ActorLink.AccountId;

        // 캐시 확인
        if (_playerCache.ContainsKey(accountId))
        {
            StageLink.Reply(CPacket.Of(new UserDataResponse
            {
                Data = _playerCache[accountId]
            }));
            return;
        }

        // 즉시 수락 응답
        StageLink.Reply(CPacket.Of(new UserDataResponse
        {
            Loading = true
        }));

        // AsyncIO로 DB 로드
        StageLink.AsyncIO(
            preCallback: async () =>
            {
                // I/O 스레드에서 DB 조회
                var userData = await _database.LoadUserAsync(accountId);
                return userData;
            },
            postCallback: async (result) =>
            {
                // Stage 이벤트 루프에서 실행 (안전하게 Stage 상태 접근)
                var userData = (PlayerData)result!;

                // 캐시에 저장
                _playerCache[accountId] = userData;

                // 클라이언트에 알림
                actor.ActorLink.SendToClient(CPacket.Of(new UserDataLoadedNotify
                {
                    Data = userData
                }));

                return Task.CompletedTask;
            }
        );
    }

    private class PlayerData { /* ... */ }

    // 나머지 메서드들...
    public Task<(ushort, IPacket?)> OnCreate(IPacket packet) => Task.FromResult<(ushort, IPacket?)>((0, null));
    public Task OnPostCreate() => Task.CompletedTask;
    public Task<(ushort, IPacket?)> OnJoinStage(IActor actor, IPacket packet) => Task.FromResult<(ushort, IPacket?)>((0, null));
    public Task OnPostJoinStage(IActor actor) => Task.CompletedTask;
    public ValueTask OnConnectionChanged(IActor actor, bool isConnected, DisconnectReason? reason) => ValueTask.CompletedTask;
    public ValueTask OnLeaveRoom(IActor actor, LeaveReason reason) => ValueTask.CompletedTask;
    public ValueTask OnDispatch(IActor actor, IPacket packet) { HandleLoadUser(actor, packet); return ValueTask.CompletedTask; }
    public Task OnDestroy() => Task.CompletedTask;
}
```

**이유:**
- Stage 이벤트 루프 블로킹 방지
- 다른 메시지 처리 지연 없음
- 응답성 향상

---

### 5.3 외부 API 호출 패턴

**외부 API 호출도 AsyncIO를 사용하고, 타임아웃을 반드시 설정합니다.**

#### ✅ 올바른 예: 타임아웃을 가진 API 호출

```csharp
public class ExternalApiStage : IStage
{
    private readonly HttpClient _httpClient;

    public IStageLink StageLink { get; }

    public ExternalApiStage(IStageLink stageLink, HttpClient httpClient)
    {
        StageLink = stageLink;
        _httpClient = httpClient;
        _httpClient.Timeout = TimeSpan.FromSeconds(5);  // 타임아웃 설정
    }

    public async Task HandleVerifyPurchase(IActor actor, IPacket packet)
    {
        var request = packet.Parse<VerifyPurchaseRequest>();

        StageLink.Reply(CPacket.Of(new VerifyPurchaseResponse
        {
            Accepted = true
        }));

        StageLink.AsyncIO(
            preCallback: async () =>
            {
                try
                {
                    // 외부 결제 API 호출 (타임아웃 5초)
                    var response = await _httpClient.PostAsJsonAsync(
                        "https://payment-api.com/verify",
                        new { receipt = request.Receipt }
                    );

                    if (response.IsSuccessStatusCode)
                    {
                        var result = await response.Content.ReadFromJsonAsync<PaymentResult>();
                        return new { Success = true, Result = result };
                    }
                    else
                    {
                        return new { Success = false, Result = (PaymentResult?)null };
                    }
                }
                catch (Exception ex)
                {
                    LOG.Error(ex, "Payment verification failed");
                    return new { Success = false, Result = (PaymentResult?)null };
                }
            },
            postCallback: async (result) =>
            {
                var response = (dynamic)result!;

                if (response.Success && response.Result != null)
                {
                    // 결제 성공 - 아이템 지급
                    GiveItemToPlayer(actor, response.Result.ItemId);

                    actor.ActorLink.SendToClient(CPacket.Of(new PurchaseVerifiedNotify
                    {
                        Success = true,
                        ItemId = response.Result.ItemId
                    }));
                }
                else
                {
                    // 결제 실패
                    actor.ActorLink.SendToClient(CPacket.Of(new PurchaseVerifiedNotify
                    {
                        Success = false,
                        Error = "Verification failed"
                    }));
                }

                return Task.CompletedTask;
            }
        );
    }

    private void GiveItemToPlayer(IActor actor, int itemId) { /* ... */ }
    private class PaymentResult { public int ItemId { get; set; } }

    // 나머지 메서드들...
    public Task<(ushort, IPacket?)> OnCreate(IPacket packet) => Task.FromResult<(ushort, IPacket?)>((0, null));
    public Task OnPostCreate() => Task.CompletedTask;
    public Task<(ushort, IPacket?)> OnJoinStage(IActor actor, IPacket packet) => Task.FromResult<(ushort, IPacket?)>((0, null));
    public Task OnPostJoinStage(IActor actor) => Task.CompletedTask;
    public ValueTask OnConnectionChanged(IActor actor, bool isConnected, DisconnectReason? reason) => ValueTask.CompletedTask;
    public ValueTask OnLeaveRoom(IActor actor, LeaveReason reason) => ValueTask.CompletedTask;
    public ValueTask OnDispatch(IActor actor, IPacket packet) { HandleVerifyPurchase(actor, packet); return ValueTask.CompletedTask; }
    public Task OnDestroy() => Task.CompletedTask;
}
```

**이유:**
- 타임아웃으로 무한 대기 방지
- 예외 처리로 안정성 확보
- 실패 시나리오 고려

---

## 6. 에러 처리

### 6.1 예외 처리 전략

**예외는 적절한 레벨에서 처리하고, 사용자에게 의미 있는 피드백을 제공합니다.**

#### ❌ 잘못된 예: 예외 무시

```csharp
public async Task HandleAction(IActor actor, IPacket packet)
{
    try
    {
        var result = await PerformAction();
        // 성공 처리만 하고 예외는 무시
    }
    catch
    {
        // ❌ 예외를 삼킴 (Silent Failure)
    }
}
```

**문제점:**
- 오류 상황이 숨겨짐
- 디버깅 어려움
- 사용자에게 피드백 없음

#### ✅ 올바른 예: 적절한 예외 처리

```csharp
public class SafeStage : IStage
{
    private readonly ILogger<SafeStage> _logger;

    public IStageLink StageLink { get; }

    public SafeStage(IStageLink stageLink, ILogger<SafeStage> logger)
    {
        StageLink = stageLink;
        _logger = logger;
    }

    public async Task HandleAction(IActor actor, IPacket packet)
    {
        try
        {
            var request = packet.Parse<ActionRequest>();

            // 입력 검증
            if (string.IsNullOrEmpty(request.ActionId))
            {
                StageLink.Reply(CPacket.Of(new ActionResponse
                {
                    Success = false,
                    ErrorCode = ErrorCode.InvalidParameter,
                    ErrorMessage = "ActionId is required"
                }));
                return;
            }

            // 비즈니스 로직
            var result = await PerformAction(request);

            // 성공 응답
            StageLink.Reply(CPacket.Of(new ActionResponse
            {
                Success = true,
                Result = result
            }));
        }
        catch (InvalidOperationException ex)
        {
            // 비즈니스 로직 예외
            _logger.LogWarning(ex, "Invalid action: {AccountId}", actor.ActorLink.AccountId);

            StageLink.Reply(CPacket.Of(new ActionResponse
            {
                Success = false,
                ErrorCode = ErrorCode.InvalidOperation,
                ErrorMessage = ex.Message
            }));
        }
        catch (Exception ex)
        {
            // 예상치 못한 예외
            _logger.LogError(ex, "Action failed: {AccountId}", actor.ActorLink.AccountId);

            StageLink.Reply(CPacket.Of(new ActionResponse
            {
                Success = false,
                ErrorCode = ErrorCode.InternalError,
                ErrorMessage = "An error occurred. Please try again."
            }));
        }
    }

    private Task<string> PerformAction(ActionRequest request)
    {
        return Task.FromResult("OK");
    }

    // 나머지 메서드들...
    public Task<(ushort, IPacket?)> OnCreate(IPacket packet) => Task.FromResult<(ushort, IPacket?)>((0, null));
    public Task OnPostCreate() => Task.CompletedTask;
    public Task<(ushort, IPacket?)> OnJoinStage(IActor actor, IPacket packet) => Task.FromResult<(ushort, IPacket?)>((0, null));
    public Task OnPostJoinStage(IActor actor) => Task.CompletedTask;
    public ValueTask OnConnectionChanged(IActor actor, bool isConnected, DisconnectReason? reason) => ValueTask.CompletedTask;
    public ValueTask OnLeaveRoom(IActor actor, LeaveReason reason) => ValueTask.CompletedTask;
    public ValueTask OnDispatch(IActor actor, IPacket packet) { HandleAction(actor, packet); return ValueTask.CompletedTask; }
    public Task OnDestroy() => Task.CompletedTask;
}
```

**이유:**
- 명확한 에러 피드백
- 구조화된 로깅
- 디버깅 용이

---

### 6.2 클라이언트에 에러 전달

**에러 코드와 사용자 친화적인 메시지를 함께 전달합니다.**

#### ✅ 올바른 예: 에러 코드 체계

```protobuf
// Proto 정의
message ActionResponse {
    bool success = 1;
    string result = 2;
    ErrorCode error_code = 3;
    string error_message = 4;
}

enum ErrorCode {
    SUCCESS = 0;
    INVALID_PARAMETER = 1001;
    INVALID_OPERATION = 1002;
    NOT_ENOUGH_RESOURCE = 1003;
    INTERNAL_ERROR = 9999;
}
```

```csharp
// C# 에러 처리
StageLink.Reply(CPacket.Of(new ActionResponse
{
    Success = false,
    ErrorCode = ErrorCode.NotEnoughResource,
    ErrorMessage = "Not enough gold. Required: 100, Current: 50"
}));
```

**이유:**
- 클라이언트에서 에러 타입별 처리 가능
- 사용자에게 명확한 피드백
- 디버깅과 모니터링 용이

---

### 6.3 로깅 전략

**구조화된 로깅으로 디버깅과 모니터링을 지원합니다.**

#### ✅ 올바른 예: 구조화된 로깅

```csharp
public class LoggingStage : IStage
{
    private readonly ILogger<LoggingStage> _logger;

    public IStageLink StageLink { get; }

    public LoggingStage(IStageLink stageLink, ILogger<LoggingStage> logger)
    {
        StageLink = stageLink;
        _logger = logger;
    }

    public async Task<(ushort, IPacket?)> OnJoinStage(IActor actor, IPacket packet)
    {
        // 구조화된 로깅 (Serilog 스타일)
        _logger.LogInformation(
            "Player joined stage - StageId: {StageId}, AccountId: {AccountId}, Timestamp: {Timestamp}",
            StageLink.StageId,
            actor.ActorLink.AccountId,
            DateTimeOffset.UtcNow
        );

        return (0, null);
    }

    public async Task HandleTransaction(IActor actor, IPacket packet)
    {
        var request = packet.Parse<TransactionRequest>();

        using (_logger.BeginScope(new Dictionary<string, object>
        {
            ["AccountId"] = actor.ActorLink.AccountId,
            ["TransactionId"] = request.TransactionId,
            ["StageId"] = StageLink.StageId
        }))
        {
            _logger.LogInformation("Transaction started");

            try
            {
                await ProcessTransaction(request);
                _logger.LogInformation("Transaction completed successfully");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Transaction failed");
                throw;
            }
        }
    }

    private Task ProcessTransaction(TransactionRequest request) => Task.CompletedTask;

    // 나머지 메서드들...
    public Task<(ushort, IPacket?)> OnCreate(IPacket packet) => Task.FromResult<(ushort, IPacket?)>((0, null));
    public Task OnPostCreate() => Task.CompletedTask;
    public Task OnPostJoinStage(IActor actor) => Task.CompletedTask;
    public ValueTask OnConnectionChanged(IActor actor, bool isConnected, DisconnectReason? reason) => ValueTask.CompletedTask;
    public ValueTask OnLeaveRoom(IActor actor, LeaveReason reason) => ValueTask.CompletedTask;
    public ValueTask OnDispatch(IActor actor, IPacket packet) { HandleTransaction(actor, packet); return ValueTask.CompletedTask; }
    public Task OnDestroy() => Task.CompletedTask;
}
```

**이유:**
- 로그 검색 및 필터링 용이
- APM 도구와 통합 가능
- 문제 추적 효율적

---

## 7. 성능 최적화

### 7.1 메모리 풀링 (Packet 재사용)

**Packet은 using 문을 사용하여 자동으로 풀에 반환합니다.**

#### ✅ 올바른 예: Packet 풀링

```csharp
public async Task BroadcastMessage(string message)
{
    // using으로 자동 반환
    using var packet = CPacket.Of(new ChatMessage
    {
        Content = message,
        Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
    });

    foreach (var actor in _actors.Values)
    {
        actor.ActorLink.SendToClient(packet);
    }

    // using 블록 종료 시 packet 자동 반환
}
```

**이유:**
- GC 압력 감소
- 메모리 할당 최소화
- 성능 향상

---

### 7.2 GC 친화적 코드

**불필요한 객체 할당을 피하고, struct를 활용합니다.**

#### ❌ 잘못된 예: 과도한 할당

```csharp
public void UpdatePositions()
{
    foreach (var player in _players.Values)
    {
        // ❌ 매 프레임 새 Vector2 할당
        var position = new Vector2 { X = player.X, Y = player.Y };
        var velocity = new Vector2 { X = player.VX, Y = player.VY };

        // 계산...
    }
}
```

#### ✅ 올바른 예: struct 사용 및 할당 최소화

```csharp
// struct로 정의 (스택 할당)
public readonly struct Vector2
{
    public float X { get; init; }
    public float Y { get; init; }

    public Vector2(float x, float y)
    {
        X = x;
        Y = y;
    }
}

public class OptimizedStage : IStage
{
    // 재사용 가능한 리스트 (한 번만 할당)
    private readonly List<Vector2> _tempPositions = new(100);

    public void UpdatePositions()
    {
        _tempPositions.Clear();  // 재사용

        foreach (var player in _players.Values)
        {
            // struct는 스택에 할당 (GC 영향 없음)
            var position = new Vector2(player.X, player.Y);
            _tempPositions.Add(position);
        }
    }

    // 나머지 필드 및 메서드들...
    private readonly Dictionary<string, PlayerState> _players = new();
    public IStageLink StageLink { get; }
    public OptimizedStage(IStageLink stageLink) { StageLink = stageLink; }
    public Task<(ushort, IPacket?)> OnCreate(IPacket packet) => Task.FromResult<(ushort, IPacket?)>((0, null));
    public Task OnPostCreate() => Task.CompletedTask;
    public Task<(ushort, IPacket?)> OnJoinStage(IActor actor, IPacket packet) => Task.FromResult<(ushort, IPacket?)>((0, null));
    public Task OnPostJoinStage(IActor actor) => Task.CompletedTask;
    public ValueTask OnConnectionChanged(IActor actor, bool isConnected, DisconnectReason? reason) => ValueTask.CompletedTask;
    public ValueTask OnLeaveRoom(IActor actor, LeaveReason reason) => ValueTask.CompletedTask;
    public ValueTask OnDispatch(IActor actor, IPacket packet) => ValueTask.CompletedTask;
    public Task OnDestroy() => Task.CompletedTask;

    private class PlayerState { public float X, Y; }
}
```

**이유:**
- GC 빈도 감소
- 메모리 할당 감소
- 캐시 친화적

---

### 7.3 브로드캐스트 최적화

**브로드캐스트 시 패킷을 한 번만 생성하고 재사용합니다.**

#### ❌ 잘못된 예: 패킷 중복 생성

```csharp
public async Task BroadcastGameState()
{
    foreach (var actor in _actors.Values)
    {
        // ❌ 각 플레이어마다 패킷 생성
        using var packet = CPacket.Of(new GameStateUpdate
        {
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        });

        actor.ActorLink.SendToClient(packet);
    }
}
```

**문제점:**
- 불필요한 직렬화 반복
- 메모리 할당 증가
- CPU 낭비

#### ✅ 올바른 예: 패킷 재사용

```csharp
public async Task BroadcastGameState()
{
    // 패킷 한 번만 생성
    using var packet = CPacket.Of(new GameStateUpdate
    {
        Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
    });

    // 모든 플레이어에게 동일한 패킷 전송
    foreach (var actor in _actors.Values)
    {
        actor.ActorLink.SendToClient(packet);
    }
}
```

**이유:**
- 직렬화 1회만 수행
- 메모리 효율적
- 성능 향상

---

## 8. 확장성

### 8.1 수평 확장 설계

**Stage는 상태를 가지지만, API Server는 상태를 가지지 않도록 설계합니다.**

#### ✅ Play Server (Stateful)

```csharp
// Play Server: 게임 상태를 메모리에 보유
public class GameStage : IStage
{
    // Stage는 상태를 메모리에 보유
    private readonly Dictionary<string, PlayerState> _players = new();
    private GamePhase _currentPhase = GamePhase.Waiting;

    // Stage는 특정 Play Server에 바인딩됨
    public IStageLink StageLink { get; }

    public GameStage(IStageLink stageLink)
    {
        StageLink = stageLink;
    }

    // 상태 기반 로직
    public async Task OnDispatch(IActor actor, IPacket packet)
    {
        if (_currentPhase == GamePhase.Playing)
        {
            // 게임 중 로직...
        }
    }

    // 나머지 메서드들...
    public Task<(ushort, IPacket?)> OnCreate(IPacket packet) => Task.FromResult<(ushort, IPacket?)>((0, null));
    public Task OnPostCreate() => Task.CompletedTask;
    public Task<(ushort, IPacket?)> OnJoinStage(IActor actor, IPacket packet) => Task.FromResult<(ushort, IPacket?)>((0, null));
    public Task OnPostJoinStage(IActor actor) => Task.CompletedTask;
    public ValueTask OnConnectionChanged(IActor actor, bool isConnected, DisconnectReason? reason) => ValueTask.CompletedTask;
    public ValueTask OnLeaveRoom(IActor actor, LeaveReason reason) => ValueTask.CompletedTask;
    public Task OnDestroy() => Task.CompletedTask;

    private enum GamePhase { Waiting, Playing, Ended }
    private class PlayerState { }
}
```

#### ✅ API Server (Stateless)

```csharp
// API Server: 상태를 데이터베이스에 저장
public class ShopController : IApiController
{
    private readonly IItemDatabase _itemDatabase;
    private readonly IUserDatabase _userDatabase;

    public ShopController(IItemDatabase itemDatabase, IUserDatabase userDatabase)
    {
        _itemDatabase = itemDatabase;
        _userDatabase = userDatabase;
    }

    public async Task<IPacket> BuyItem(IPacket packet, IApiLink link)
    {
        var request = packet.Parse<BuyItemRequest>();

        // 상태는 DB에서 조회 (메모리에 보유하지 않음)
        var user = await _userDatabase.GetUserAsync(request.UserId);
        var item = await _itemDatabase.GetItemAsync(request.ItemId);

        // 검증
        if (user.Gold < item.Price)
        {
            return CPacket.Of(new BuyItemResponse
            {
                Success = false,
                Error = "Not enough gold"
            });
        }

        // DB 업데이트
        user.Gold -= item.Price;
        user.Inventory.Add(item);
        await _userDatabase.UpdateUserAsync(user);

        return CPacket.Of(new BuyItemResponse
        {
            Success = true,
            RemainingGold = user.Gold
        });
    }
}
```

**이유:**
- Play Server: Stage는 특정 서버에 바인딩되므로 상태 보유 가능
- API Server: 무상태 설계로 수평 확장 용이

---

### 8.2 서버 간 부하 분산

**ServiceId를 활용하여 서버 간 부하를 분산합니다.**

#### ✅ 올바른 예: ServiceId 기반 라우팅

```csharp
// 여러 API 서버에 부하 분산
public async Task RequestToApiServer()
{
    // ServiceId로 요청 (RoundRobin 방식)
    var response = await StageLink.RequestToApiService(
        serviceId: 200,  // Shop 서비스
        packet: CPacket.Of(new GetItemListRequest())
    );

    // PlayHouse가 자동으로 서버 선택 및 부하 분산
}
```

**이유:**
- 자동 부하 분산
- 서버 추가/제거 동적 대응
- 높은 가용성

---

## 9. 테스트

### 9.1 단위 테스트 패턴

**Stage 로직은 Mock IStageLink를 사용하여 단위 테스트합니다.**

#### ✅ 올바른 예: Stage 단위 테스트

```csharp
public class GameStageTests
{
    [Fact]
    public async Task OnJoinStage_WhenFull_ReturnsError()
    {
        // Given: Stage 생성 (Mock IStageLink)
        var mockStageLink = new Mock<IStageLink>();
        mockStageLink.Setup(x => x.StageId).Returns(1001);

        var stage = new GameStage(mockStageLink.Object);
        await stage.OnCreate(CPacket.Empty(""));

        // 최대 인원 채우기
        for (int i = 0; i < 4; i++)
        {
            var actor = CreateMockActor($"player{i}");
            await stage.OnJoinStage(actor, CPacket.Empty(""));
        }

        // When: 5번째 플레이어 입장 시도
        var fifthPlayer = CreateMockActor("player5");
        var (errorCode, reply) = await stage.OnJoinStage(fifthPlayer, CPacket.Empty(""));

        // Then: 에러 반환
        Assert.Equal(ErrorCode.StageFull, errorCode);
    }

    private IActor CreateMockActor(string accountId)
    {
        var mockActorLink = new Mock<IActorLink>();
        mockActorLink.Setup(x => x.AccountId).Returns(accountId);

        var mockActor = new Mock<IActor>();
        mockActor.Setup(x => x.ActorLink).Returns(mockActorLink.Object);

        return mockActor.Object;
    }
}
```

**이유:**
- Stage 로직을 독립적으로 테스트
- 빠른 피드백
- 회귀 방지

---

### 9.2 통합 테스트 패턴

**E2E 테스트는 실제 서버를 구동하여 전체 흐름을 검증합니다.**

#### ✅ 올바른 예: E2E 테스트

```csharp
public class PlayHouseE2ETests : IAsyncLifetime
{
    private PlayServer _playServer = null!;
    private Connector _connector = null!;

    public async Task InitializeAsync()
    {
        // Play Server 시작
        _playServer = new PlayServerBootstrap()
            .Configure(options =>
            {
                options.ServiceId = 1;
                options.ServerId = 1;
                options.BindEndpoint = "tcp://127.0.0.1:15000";
                options.ClientEndpoint = "tcp://127.0.0.1:16000";
            })
            .UseStage<GameStage>("Game")
            .UseActor<GameActor>()
            .Build();

        await _playServer.StartAsync();

        // Connector 초기화
        _connector = new Connector();
        _connector.Init(new ConnectorConfig
        {
            Host = "127.0.0.1",
            Port = 16000,
            ConnectionType = ConnectionType.Tcp
        });
    }

    [Fact]
    public async Task Client_Should_ConnectAndAuthenticate()
    {
        // Given: Connector 준비

        // When: 연결 및 인증
        await _connector.ConnectAsync();
        var authResponse = await _connector.AuthenticateAsync(
            CPacket.Of(new AuthenticateRequest
            {
                UserId = "player1",
                Token = "valid-token"
            })
        );

        // Then: 연결 및 인증 성공
        Assert.True(_connector.IsConnected());
        Assert.True(_connector.IsAuthenticated());
    }

    public async Task DisposeAsync()
    {
        _connector?.Disconnect();
        await _playServer.StopAsync();
    }
}
```

**이유:**
- 실제 동작 검증
- 통합 문제 조기 발견
- API 사용 가이드 역할

---

## 10. 보안

### 10.1 입력 검증

**모든 클라이언트 입력은 검증하고, 신뢰하지 않습니다.**

#### ✅ 올바른 예: 입력 검증

```csharp
public class SecureStage : IStage
{
    public async Task HandleMove(IActor actor, IPacket packet)
    {
        var request = packet.Parse<MoveRequest>();

        // 입력 검증
        if (request.X < 0 || request.X > 1000 ||
            request.Y < 0 || request.Y > 1000)
        {
            StageLink.Reply(CPacket.Of(new MoveResponse
            {
                Success = false,
                Error = "Invalid position"
            }));
            return;
        }

        // 속도 검증 (치트 방지)
        var lastPosition = GetPlayerPosition(actor.ActorLink.AccountId);
        var distance = CalculateDistance(lastPosition, new Vector2(request.X, request.Y));
        var maxDistance = GetMaxMoveDistance();

        if (distance > maxDistance)
        {
            // 치트 의심 - 로깅 및 거부
            _logger.LogWarning("Suspicious move detected: {AccountId}", actor.ActorLink.AccountId);

            StageLink.Reply(CPacket.Of(new MoveResponse
            {
                Success = false,
                Error = "Invalid move distance"
            }));
            return;
        }

        // 정상 처리
        UpdatePlayerPosition(actor.ActorLink.AccountId, request.X, request.Y);
        StageLink.Reply(CPacket.Of(new MoveResponse
        {
            Success = true
        }));
    }

    private Vector2 GetPlayerPosition(string accountId) => new Vector2(0, 0);
    private float CalculateDistance(Vector2 a, Vector2 b) => 0f;
    private float GetMaxMoveDistance() => 10f;
    private void UpdatePlayerPosition(string accountId, float x, float y) { }

    private readonly ILogger<SecureStage> _logger;
    public IStageLink StageLink { get; }
    public SecureStage(IStageLink stageLink, ILogger<SecureStage> logger) { StageLink = stageLink; _logger = logger; }
    public Task<(ushort, IPacket?)> OnCreate(IPacket packet) => Task.FromResult<(ushort, IPacket?)>((0, null));
    public Task OnPostCreate() => Task.CompletedTask;
    public Task<(ushort, IPacket?)> OnJoinStage(IActor actor, IPacket packet) => Task.FromResult<(ushort, IPacket?)>((0, null));
    public Task OnPostJoinStage(IActor actor) => Task.CompletedTask;
    public ValueTask OnConnectionChanged(IActor actor, bool isConnected, DisconnectReason? reason) => ValueTask.CompletedTask;
    public ValueTask OnLeaveRoom(IActor actor, LeaveReason reason) => ValueTask.CompletedTask;
    public ValueTask OnDispatch(IActor actor, IPacket packet) { HandleMove(actor, packet); return ValueTask.CompletedTask; }
    public Task OnDestroy() => Task.CompletedTask;

    private struct Vector2 { public float X, Y; public Vector2(float x, float y) { X = x; Y = y; } }
}
```

**이유:**
- 치트 방지
- 서버 안정성 보장
- 악의적인 입력 차단

---

### 10.2 치트 방지 기본

**중요한 게임 로직은 서버에서만 실행하고, 클라이언트는 결과만 받습니다.**

#### ❌ 잘못된 예: 클라이언트 신뢰

```csharp
// ❌ 클라이언트가 보낸 데미지 값을 그대로 사용
public async Task HandleAttack(IActor actor, IPacket packet)
{
    var request = packet.Parse<AttackRequest>();

    // ❌ 클라이언트가 보낸 데미지를 그대로 적용
    var target = _players[request.TargetId];
    target.Health -= request.Damage;  // 치트 가능!
}
```

#### ✅ 올바른 예: 서버 검증

```csharp
public async Task HandleAttack(IActor actor, IPacket packet)
{
    var request = packet.Parse<AttackRequest>();

    var attacker = _players[actor.ActorLink.AccountId];
    var target = _players[request.TargetId];

    // ✅ 서버에서 데미지 계산
    var damage = CalculateDamage(attacker, target);

    // ✅ 거리 검증
    if (!IsInAttackRange(attacker, target))
    {
        StageLink.Reply(CPacket.Of(new AttackResponse
        {
            Success = false,
            Error = "Out of range"
        }));
        return;
    }

    // ✅ 쿨다운 검증
    if (!attacker.CanAttack())
    {
        StageLink.Reply(CPacket.Of(new AttackResponse
        {
            Success = false,
            Error = "Skill on cooldown"
        }));
        return;
    }

    // 데미지 적용
    target.Health -= damage;
    attacker.SetAttackCooldown();

    // 결과 브로드캐스트
    BroadcastToAll(new AttackNotify
    {
        AttackerId = actor.ActorLink.AccountId,
        TargetId = request.TargetId,
        Damage = damage,
        TargetHealth = target.Health
    });
}

private int CalculateDamage(PlayerState attacker, PlayerState target)
{
    // 서버에서 계산
    return attacker.Attack - target.Defense;
}

private bool IsInAttackRange(PlayerState attacker, PlayerState target)
{
    var distance = CalculateDistance(attacker.Position, target.Position);
    return distance <= attacker.AttackRange;
}

private float CalculateDistance(Vector2 a, Vector2 b) => 0f;
private void BroadcastToAll(IMessage message) { }

private class PlayerState
{
    public Vector2 Position { get; set; }
    public int Health { get; set; }
    public int Attack { get; set; }
    public int Defense { get; set; }
    public float AttackRange { get; set; }
    public bool CanAttack() => true;
    public void SetAttackCooldown() { }
}

private readonly Dictionary<string, PlayerState> _players = new();

private struct Vector2 { public float X, Y; }
```

**이유:**
- 치트 방지
- 공정한 게임 플레이
- 서버 권위 확보

---

### 10.3 민감 정보 보호

**민감한 정보는 로그에 기록하지 않고, 암호화하여 전송합니다.**

#### ✅ 올바른 예: 민감 정보 보호

```csharp
public async Task<(bool, IPacket?)> OnAuthenticate(IPacket authPacket)
{
    var request = authPacket.Parse<AuthenticateRequest>();

    // ✅ 토큰은 로그에 기록하지 않음
    _logger.LogInformation("Authentication attempt: {UserId}", request.UserId);

    // 토큰 검증 (절대 로그에 기록하지 않음)
    bool isValid = await ValidateToken(request.Token);

    if (!isValid)
    {
        // ✅ 실패 이유를 상세히 로깅하지 않음 (보안 정보 노출 방지)
        _logger.LogWarning("Authentication failed: {UserId}", request.UserId);
        return (false, null);
    }

    ActorLink.AccountId = request.UserId;
    return (true, null);
}

private async Task<bool> ValidateToken(string token)
{
    // JWT 검증 또는 외부 인증 서버 호출
    return !string.IsNullOrEmpty(token);
}
```

**이유:**
- 보안 정보 노출 방지
- 규정 준수 (GDPR, 개인정보보호법 등)
- 로그 유출 시 피해 최소화

---

## 체크리스트

프로덕션 배포 전 다음 항목들을 확인하세요:

### Stage 설계
- [ ] Stage는 단일 책임 원칙을 따르는가?
- [ ] Stage 상태는 독립적으로 관리되는가?
- [ ] Stage 간 통신은 메시지 기반인가?
- [ ] 빈 Stage는 자동으로 정리되는가?

### Actor 설계
- [ ] Actor는 가볍게 유지되는가?
- [ ] 인증 데이터는 캐싱되는가?
- [ ] 재연결 시나리오가 구현되었는가?

### 메시지 설계
- [ ] 메시지 크기가 최적화되었는가?
- [ ] Protobuf 필드 번호는 보존되는가?
- [ ] 메시지 ID는 일관되게 명명되었는가?

### 타이머와 게임루프
- [ ] 적절한 메커니즘이 선택되었는가?
- [ ] 모든 타이머가 OnDestroy에서 정리되는가?
- [ ] 게임루프는 적절히 간헐화되었는가?

### 비동기 작업
- [ ] AsyncIO/AsyncCompute가 올바르게 선택되었는가?
- [ ] DB 호출이 AsyncIO로 처리되는가?
- [ ] 외부 API 호출에 타임아웃이 설정되었는가?

### 에러 처리
- [ ] 예외가 적절히 처리되는가?
- [ ] 클라이언트에 의미 있는 에러 메시지가 전달되는가?
- [ ] 구조화된 로깅이 구현되었는가?

### 성능 최적화
- [ ] Packet이 using으로 관리되는가?
- [ ] 불필요한 객체 할당이 최소화되었는가?
- [ ] 브로드캐스트가 최적화되었는가?

### 확장성
- [ ] API Server는 무상태로 설계되었는가?
- [ ] ServiceId 기반 부하 분산이 구현되었는가?

### 테스트
- [ ] 단위 테스트가 작성되었는가?
- [ ] E2E 테스트가 작성되었는가?

### 보안
- [ ] 모든 입력이 검증되는가?
- [ ] 중요 로직이 서버에서 실행되는가?
- [ ] 민감 정보가 보호되는가?

---

## 다음 단계

- [Stage 구현 가이드](04-stage-implementation.md) - Stage 구현 방법
- [Actor 구현 가이드](05-actor-implementation.md) - Actor 구현 방법
- [타이머 및 게임루프](06-timer-gameloop.md) - 타이머와 게임루프 사용법
- [비동기 작업](09-async-operations.md) - AsyncCompute/AsyncIO 상세 가이드
