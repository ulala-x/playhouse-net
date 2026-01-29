# PlayHouse-NET 타이머 시스템

## 1. 개요

PlayHouse-NET의 타이머 시스템은 Stage 내부에서 주기적인 작업을 안전하게 실행하기 위한 메커니즘을 제공합니다. 모든 타이머 콜백은 **Stage의 메시지 큐를 통해 전달**되어 동시성 문제를 원천적으로 방지합니다.

### 1.1 핵심 특징

- **Stage 컨텍스트 보장**: 콜백은 Stage 메시지 큐를 통해 실행
- **자동 정리**: Stage 종료 시 모든 타이머 자동 취소
- **두 가지 타입**: RepeatTimer, CountTimer
- **정밀도**: 밀리초 단위 제어 가능

### 1.2 설계 원칙

```
안전성 (Safety):
- 모든 콜백은 Stage 단일 스레드에서 실행
- Race Condition 불가능

편의성 (Convenience):
- 간단한 API
- 자동 정리

성능 (Performance):
- .NET System.Threading.Timer 기반
- 낮은 오버헤드
```

## 2. 타이머 타입

### 2.1 RepeatTimer (반복 타이머)

무한 반복 실행되는 타이머

```csharp
long AddRepeatTimer(
    TimeSpan initialDelay,  // 최초 실행까지 지연
    TimeSpan period,        // 반복 주기
    TimerCallback callback  // 콜백 함수
);
```

#### 사용 예시

```csharp
public class GameStage : IStage
{
    private long _gameTickTimer;

    public async Task OnPostCreate()
    {
        // 게임 틱: 즉시 시작, 100ms마다 실행
        _gameTickTimer = StageSender.AddRepeatTimer(
            initialDelay: TimeSpan.Zero,
            period: TimeSpan.FromMilliseconds(100),
            callback: OnGameTick
        );
    }

    private async Task OnGameTick()
    {
        // 게임 로직 실행
        UpdatePhysics();
        CheckCollisions();
        await BroadcastGameState();
    }
}
```

#### 타이밍 다이어그램

```
initialDelay = 1s, period = 2s

Timeline:
0s ──────1s──────3s──────5s──────7s──────9s──────11s──▶
         │       │       │       │       │       │
         ▼       ▼       ▼       ▼       ▼       ▼
       Call    Call    Call    Call    Call    Call
         1       2       3       4       5       6

실행 시점:
- t = 1s  : 첫 실행 (initialDelay)
- t = 3s  : 두 번째 실행 (1s + 2s)
- t = 5s  : 세 번째 실행 (1s + 2s + 2s)
- ...     : 무한 반복
```

### 2.2 CountTimer (카운트 타이머)

제한된 횟수만 실행되는 타이머

```csharp
long AddCountTimer(
    TimeSpan initialDelay,  // 최초 실행까지 지연
    TimeSpan period,        // 반복 주기
    int count,              // 실행 횟수
    TimerCallback callback  // 콜백 함수
);
```

#### 사용 예시

```csharp
public class BattleStage : IStage
{
    private int _countdown = 3;

    public async Task StartCountdown()
    {
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
        await Broadcast("Countdown", _countdown);

        if (_countdown == 0)
        {
            await StartGame();
        }
    }
}
```

#### 타이밍 다이어그램

```
initialDelay = 1s, count = 3, period = 1s

Timeline:
0s ──────1s──────2s──────3s──────4s──────5s──────▶
         │       │       │       X       X
         ▼       ▼       ▼
       Call    Call    Call    (자동 취소)
         1       2       3

실행 시점:
- t = 1s  : 1회 실행 (initialDelay)
- t = 2s  : 2회 실행 (1s + 1s)
- t = 3s  : 3회 실행 (1s + 1s + 1s)
- t = 3s+ : 자동 취소 (count 도달)
```

## 3. 타이머 관리

### 3.1 타이머 취소

```csharp
void CancelTimer(long timerId);
```

#### 사용 예시

```csharp
public class GameStage : IStage
{
    private long _countdownTimer;

    public async Task StartCountdown()
    {
        _countdownTimer = StageSender.AddCountTimer(
            TimeSpan.FromSeconds(3),   // initialDelay
            TimeSpan.FromSeconds(1),   // period
            10,                         // count
            OnCountdownTick
        );
    }

    public void CancelCountdown()
    {
        // 타이머 취소
        StageSender.CancelTimer(_countdownTimer);
        _countdownTimer = 0;
    }

    public async Task OnDispatch(IActor actor, IPacket packet)
    {
        if (packet.MsgId == "AllReady")
        {
            // 모든 플레이어 준비 완료 → 카운트다운 취소하고 즉시 시작
            CancelCountdown();
            await StartGameImmediately();
        }
    }
}
```

### 3.2 자동 정리

```csharp
// Stage 종료 시 모든 타이머 자동 취소
StageSender.CloseStage();

// 내부 동작:
// 1. 등록된 모든 타이머 ID 조회
// 2. 각 타이머 CancelTimer() 호출
// 3. 타이머 컬렉션 Clear
// 4. Stage 정리
```

## 4. 내부 구현 메커니즘

### 4.1 타이머 등록 흐름

```
[타이머 등록 프로세스]

StageSender.AddRepeatTimer()
    │
    ▼
timerId = GenerateTimerId()
    │
    ▼
System.Threading.Timer 생성
    │
    │  callback = (state) =>
    │  {
    │      RoutePacket packet = CreateTimerPacket(stageId, timerId, userCallback);
    │      dispatcher.OnPost(packet);  // 메시지 큐에 전달
    │  }
    │
    ▼
타이머 등록 (Dictionary에 저장)
    │
    ▼
return timerId


[타이머 실행 프로세스]

System.Threading.Timer 트리거
    │
    ▼
콜백 실행 (임의 스레드)
    │
    ▼
RoutePacket 생성 (TimerMessage)
    │
    ▼
Dispatcher.OnPost(packet)
    │
    ▼
Stage Message Queue에 추가
    │
    ▼
Stage 스레드에서 Dequeue
    │
    ▼
사용자 콜백 실행 (Stage Context 안전)
```

### 4.2 TimerManager 구조

```csharp
internal class TimerManager
{
    private readonly ConcurrentDictionary<long, Timer> _timers = new();
    private readonly IPlayDispatcher _dispatcher;

    public long RegisterRepeatTimer(
        long stageId,
        long timerId,
        long initialDelay,
        long period,
        TimerCallback timerCallback)
    {
        var timer = new Timer(timerState =>
        {
            // 별도 스레드에서 실행
            var routePacket = RoutePacket.StageTimerOf(
                stageId,
                timerId,
                timerCallback,
                timerState
            );

            // Stage 메시지 큐에 전달
            _dispatcher.OnPost(routePacket);

        }, null, initialDelay, period);

        _timers[timerId] = timer;
        return timerId;
    }

    public long RegisterCountTimer(
        long stageId,
        long timerId,
        long initialDelay,
        int count,
        long period,
        TimerCallback timerCallback)
    {
        var remainingCount = count;

        var timer = new Timer(timerState =>
        {
            if (remainingCount > 0)
            {
                var routePacket = RoutePacket.StageTimerOf(
                    stageId,
                    timerId,
                    timerCallback,
                    timerState
                );
                _dispatcher.OnPost(routePacket);

                remainingCount--;
            }
            else
            {
                // 카운트 소진 → 자동 취소
                CancelTimer(timerId);
            }

        }, null, initialDelay, period);

        _timers[timerId] = timer;
        return timerId;
    }

    public void CancelTimer(long timerId)
    {
        if (_timers.TryGetValue(timerId, out var timer))
        {
            timer.Dispose();  // System.Threading.Timer 정리
            _timers.Remove(timerId, out _);
        }
    }
}
```

### 4.3 타이머 ID 생성

```csharp
internal static class TimerIdMaker
{
    private static long _sequence = 0;

    public static long MakeId()
    {
        return Interlocked.Increment(ref _sequence);
    }
}

// 타이머 ID 특성:
// - 단조 증가 (Monotonic)
// - 서버 내 고유성 보장
// - Thread-Safe (Interlocked)
```

## 5. 실전 예제

### 5.1 게임 틱 시스템

```csharp
public class GameStage : IStage
{
    private const int TickRate = 20; // 20 TPS (Ticks Per Second)
    private long _gameTickTimer;
    private int _tickCount = 0;

    public async Task OnPostCreate()
    {
        // 50ms마다 게임 틱 (20 TPS)
        _gameTickTimer = StageSender.AddRepeatTimer(
            initialDelay: TimeSpan.Zero,
            period: TimeSpan.FromMilliseconds(1000 / TickRate),
            callback: OnGameTick
        );
    }

    private async Task OnGameTick()
    {
        _tickCount++;

        // 물리 업데이트
        UpdatePhysics(1.0f / TickRate);

        // 충돌 감지
        CheckCollisions();

        // 게임 상태 전송 (초당 10회만)
        if (_tickCount % 2 == 0)
        {
            await BroadcastGameState();
        }

        // 1초마다 통계 로깅
        if (_tickCount % TickRate == 0)
        {
            LOG.Info($"Game tick: {_tickCount}, Players: {_players.Count}");
        }
    }

    public void PauseGame()
    {
        StageSender.CancelTimer(_gameTickTimer);
    }

    public void ResumeGame()
    {
        _gameTickTimer = StageSender.AddRepeatTimer(
            TimeSpan.Zero,
            TimeSpan.FromMilliseconds(1000 / TickRate),
            OnGameTick
        );
    }
}
```

### 5.2 카운트다운 타이머

```csharp
public class BattleStage : IStage
{
    private int _countdownSeconds = 10;
    private long _countdownTimer;

    public async Task StartMatchCountdown()
    {
        await Broadcast("CountdownStart", _countdownSeconds);

        _countdownTimer = StageSender.AddCountTimer(
            initialDelay: TimeSpan.FromSeconds(1),
            period: TimeSpan.FromSeconds(1),
            count: _countdownSeconds,
            callback: OnCountdownTick
        );
    }

    private async Task OnCountdownTick()
    {
        _countdownSeconds--;

        await Broadcast("CountdownTick", _countdownSeconds);

        if (_countdownSeconds == 0)
        {
            await StartMatch();
        }
    }

    public async Task OnDispatch(IActor actor, IPacket packet)
    {
        if (packet.MsgId == "CancelCountdown")
        {
            StageSender.CancelTimer(_countdownTimer);
            _countdownSeconds = 10;
            await Broadcast("CountdownCancelled");
        }
    }
}
```

### 5.3 세션 타임아웃

```csharp
public class LobbyStage : IStage
{
    private readonly Dictionary<long, long> _actorTimers = new();
    private const int IdleTimeoutSeconds = 300; // 5분

    public async Task<(ushort, IPacket)> OnJoinStage(IActor actor, IPacket packet)
    {
        _actors.Add(actor.AccountId, actor);

        // 5분 후 자동 퇴장 타이머
        var timerId = StageSender.AddCountTimer(
            initialDelay: TimeSpan.FromSeconds(IdleTimeoutSeconds),
            period: TimeSpan.Zero,
            count: 1,
            callback: async () => await OnActorTimeout(actor)
        );

        _actorTimers[actor.AccountId] = timerId;

        return (0, CreateReply("Joined"));
    }

    public async Task OnDispatch(IActor actor, IPacket packet)
    {
        // 활동 감지 → 타이머 리셋
        ResetIdleTimer(actor.AccountId);

        // ... 메시지 처리
    }

    private void ResetIdleTimer(long accountId)
    {
        // 기존 타이머 취소
        if (_actorTimers.TryGetValue(accountId, out var oldTimerId))
        {
            StageSender.CancelTimer(oldTimerId);
        }

        // 새 타이머 등록
        var newTimerId = StageSender.AddCountTimer(
            TimeSpan.FromSeconds(IdleTimeoutSeconds),  // initialDelay
            TimeSpan.Zero,                              // period
            1,                                          // count
            async () => await OnActorTimeout(_actors[accountId])
        );

        _actorTimers[accountId] = newTimerId;
    }

    private async Task OnActorTimeout(IActor actor)
    {
        LOG.Info($"Actor idle timeout: {actor.AccountId}");

        // 강제 퇴장
        StageSender.SessionClose(actor.SessionNid, actor.Sid);
    }

    public async Task OnDisconnect(IActor actor)
    {
        // 타이머 정리
        if (_actorTimers.TryGetValue(actor.AccountId, out var timerId))
        {
            StageSender.CancelTimer(timerId);
            _actorTimers.Remove(actor.AccountId);
        }

        _actors.Remove(actor.AccountId);
        await actor.OnDestroy();
    }
}
```

### 5.4 주기적 상태 저장

```csharp
public class PersistentStage : IStage
{
    private long _autoSaveTimer;

    public async Task OnPostCreate()
    {
        // 5분마다 자동 저장
        _autoSaveTimer = StageSender.AddRepeatTimer(
            initialDelay: TimeSpan.FromMinutes(5),
            period: TimeSpan.FromMinutes(5),
            callback: OnAutoSave
        );
    }

    private async Task OnAutoSave()
    {
        LOG.Info("Auto-saving stage state...");

        // AsyncBlock으로 DB 저장 (블로킹 작업)
        StageSender.AsyncBlock(
            preCallback: async () =>
            {
                var stateData = SerializeStageState();
                await _database.SaveStageState(StageSender.StageId, stateData);
                return true;
            },
            postCallback: async (result) =>
            {
                LOG.Info("Auto-save completed");
            }
        );
    }
}
```

### 5.5 버프/디버프 시스템

```csharp
public class GameStage : IStage
{
    private readonly Dictionary<long, List<long>> _actorBuffTimers = new();

    public async Task ApplyBuff(IActor actor, BuffType buffType, int durationSeconds)
    {
        // 버프 적용
        ApplyBuffEffect(actor, buffType);

        // 버프 만료 타이머
        var timerId = StageSender.AddCountTimer(
            initialDelay: TimeSpan.FromSeconds(durationSeconds),
            period: TimeSpan.Zero,
            count: 1,
            callback: async () => await RemoveBuff(actor, buffType)
        );

        // 타이머 ID 저장
        if (!_actorBuffTimers.ContainsKey(actor.AccountId))
        {
            _actorBuffTimers[actor.AccountId] = new List<long>();
        }
        _actorBuffTimers[actor.AccountId].Add(timerId);

        // 클라이언트에 알림
        await SendToActor(actor, "BuffApplied", new { buffType, durationSeconds });
    }

    private async Task RemoveBuff(IActor actor, BuffType buffType)
    {
        // 버프 제거
        RemoveBuffEffect(actor, buffType);

        // 클라이언트에 알림
        await SendToActor(actor, "BuffRemoved", new { buffType });
    }

    public async Task OnDisconnect(IActor actor)
    {
        // Actor의 모든 버프 타이머 정리
        if (_actorBuffTimers.TryGetValue(actor.AccountId, out var timerIds))
        {
            foreach (var timerId in timerIds)
            {
                StageSender.CancelTimer(timerId);
            }
            _actorBuffTimers.Remove(actor.AccountId);
        }

        _actors.Remove(actor.AccountId);
        await actor.OnDestroy();
    }
}
```

## 6. 타이머 정밀도 및 성능

### 6.1 정밀도

```
System.Threading.Timer의 정밀도:
- Windows: ~15ms (시스템 타이머 해상도)
- Linux: ~1ms

권장 사항:
- 최소 주기: 10ms 이상
- 고정밀 타이밍: Stopwatch + 게임 틱 활용
```

### 6.2 성능 고려사항

```csharp
// ❌ 나쁜 예: 너무 많은 타이머
foreach (var actor in _actors)
{
    StageSender.AddRepeatTimer(
        TimeSpan.Zero,
        TimeSpan.FromMilliseconds(10),
        () => UpdateActor(actor)
    );
}

// ✅ 좋은 예: 단일 타이머에서 일괄 처리
StageSender.AddRepeatTimer(
    TimeSpan.Zero,
    TimeSpan.FromMilliseconds(10),
    async () =>
    {
        foreach (var actor in _actors)
        {
            UpdateActor(actor);
        }
    }
);
```

### 6.3 타이머 수 제한

```
권장 제한:
- Stage당 타이머 수: < 100개
- 전체 서버: < 10,000개

모니터링:
- 타이머 수 추적
- 메모리 사용량 확인
```

## 7. 주의사항 및 베스트 프랙티스

### 7.1 Do (권장)

```
1. 타이머 ID 저장
   - 나중에 취소할 수 있도록 timerId 보관

2. 자동 정리 활용
   - CloseStage() 시 자동 정리 신뢰

3. 단일 타이머 활용
   - 여러 작업을 하나의 타이머로 통합

4. AsyncBlock 활용
   - 블로킹 작업은 AsyncBlock으로 분리

5. 에러 처리
   - 콜백 내부에서 try-catch 사용
```

### 7.2 Don't (금지)

```
1. 타이머 콜백 내 블로킹
   - Thread.Sleep 금지
   - 동기 DB 호출 금지

2. 너무 짧은 주기
   - < 10ms 주기는 피하기
   - CPU 과다 사용 위험

3. 타이머 누수
   - 사용 후 반드시 취소
   - OnDisconnect에서 정리

4. 무한 재귀
   - 콜백 내부에서 같은 타이머 재등록 주의

5. 타이머 ID 재사용
   - 취소 후 ID 초기화 (0 또는 -1)
```

### 7.3 디버깅 팁

```csharp
// 타이머 상태 로깅
public class GameStage : IStage
{
    private readonly Dictionary<string, long> _namedTimers = new();

    public long AddNamedTimer(string name, TimeSpan delay, TimeSpan period,
        TimerCallback callback)
    {
        var timerId = StageSender.AddRepeatTimer(delay, period, async () =>
        {
            LOG.Debug($"Timer '{name}' fired");
            await callback();
        });

        _namedTimers[name] = timerId;
        LOG.Info($"Timer '{name}' registered: {timerId}");

        return timerId;
    }

    public void CancelNamedTimer(string name)
    {
        if (_namedTimers.TryGetValue(name, out var timerId))
        {
            StageSender.CancelTimer(timerId);
            _namedTimers.Remove(name);
            LOG.Info($"Timer '{name}' cancelled: {timerId}");
        }
    }
}
```

## 8. 게임루프 시스템 (GameLoop)

### 8.1 개요

게임루프는 고정 시간 간격(Fixed Timestep)으로 실행되는 고해상도 타이머로, 게임 로직 업데이트에 최적화되어 있습니다. 일반 타이머와 달리 결정론적 시뮬레이션(Deterministic Simulation)을 위해 설계되었습니다.

### 8.2 핵심 특징

- **고해상도 타이밍**: `Stopwatch.GetTimestamp()` 기반 (Windows QPC / Linux clock_gettime)
- **전용 스레드**: ThreadPool 지터(jitter) 회피
- **Fixed Timestep 누산기**: 일정한 시간 간격으로 틱 실행 보장
- **Spiral of Death 방지**: 과부하 시 틱 드롭으로 시스템 안정성 유지
- **하이브리드 슬립**: Thread.Sleep + SpinWait로 정밀도와 CPU 효율성 균형

### 8.3 API

#### 8.3.1 게임루프 시작 (간단한 방식)

```csharp
void StartGameLoop(TimeSpan fixedTimestep, GameLoopCallback callback);
```

**파라미터**
- `fixedTimestep`: 고정 시간 간격 (유효 범위: 1ms ~ 1000ms)
- `callback`: 각 틱마다 호출될 콜백 함수

**GameLoopCallback 델리게이트**
```csharp
public delegate Task GameLoopCallback(TimeSpan deltaTime, TimeSpan totalElapsed);
```

- `deltaTime`: 고정 시간 간격 (항상 `fixedTimestep`과 동일)
- `totalElapsed`: 게임루프 시작 이후 총 경과 시뮬레이션 시간

#### 8.3.2 게임루프 시작 (설정 방식)

```csharp
void StartGameLoop(GameLoopConfig config, GameLoopCallback callback);
```

**GameLoopConfig**
```csharp
public sealed class GameLoopConfig
{
    // 고정 시간 간격 (기본값: 50ms = 20Hz)
    public TimeSpan FixedTimestep { get; init; } = TimeSpan.FromMilliseconds(50);

    // 최대 누산기 제한 (기본값: FixedTimestep × 5)
    public TimeSpan? MaxAccumulatorCap { get; init; }
}
```

**설정 옵션**
- `FixedTimestep`: 각 틱 간격 (1ms ~ 1000ms)
- `MaxAccumulatorCap`: Spiral of Death 방지를 위한 최대 누산기 값
  - 기본값: `FixedTimestep × 5`
  - 누산기가 이 값을 초과하면 초과 틱은 폐기됨
  - 최소값: `FixedTimestep` (자동으로 클램핑됨)

#### 8.3.3 게임루프 중지

```csharp
void StopGameLoop();
```

실행 중인 게임루프를 중지합니다. 게임루프가 실행 중이 아니면 아무 작업도 수행하지 않습니다.

#### 8.3.4 게임루프 실행 상태 확인

```csharp
bool IsGameLoopRunning { get; }
```

현재 Stage에 게임루프가 실행 중인지 확인합니다.

### 8.4 Fixed Timestep 패턴

게임루프는 "Fix Your Timestep" 패턴을 구현합니다.

```
[Fixed Timestep 누산기 동작]

실제 시간 경과: 67ms
Fixed Timestep: 50ms

Timeline:
0ms ────── 67ms ───────▶
           │
           ▼
    accumulator = 67ms

    while (accumulator >= 50ms):
        OnGameLoopTick(50ms)
        accumulator -= 50ms

    실행 결과:
    - Tick 1 실행 (deltaTime = 50ms)
    - accumulator = 17ms (다음 프레임으로 이월)
```

### 8.5 Spiral of Death 방지

서버가 과부하 상태일 때 틱 처리가 지연되면 누산기가 계속 증가하여 시스템이 더욱 느려지는 악순환이 발생할 수 있습니다. 이를 "Spiral of Death"라고 합니다.

```
[Spiral of Death 시나리오]

FixedTimestep = 50ms
실제 틱 처리 시간 = 60ms

Frame 1: accumulator = 60ms → Tick 1 실행, accumulator = 10ms
Frame 2: accumulator = 10 + 60 = 70ms → Tick 1 실행, accumulator = 20ms
Frame 3: accumulator = 20 + 60 = 80ms → Tick 1 실행, accumulator = 30ms
...
Frame N: accumulator = X + 60 > 100ms → Tick 2 실행! (더 느려짐)

[MaxAccumulatorCap 적용]

MaxAccumulatorCap = 250ms (50ms × 5)

Frame X: accumulator = 300ms
         → 250ms로 클램핑
         → 최대 5틱만 실행
         → 50ms 분량 폐기 (시뮬레이션 점프)
         → 시스템 안정성 유지
```

### 8.6 사용 예시

#### 8.6.1 기본 게임루프

```csharp
public class GameStage : IStage
{
    private const int TickRate = 20; // 20 TPS (50ms per tick)

    public async Task OnPostCreate()
    {
        // 20Hz 게임루프 시작
        StageSender.StartGameLoop(
            fixedTimestep: TimeSpan.FromMilliseconds(50),
            callback: OnGameLoopTick
        );
    }

    private async Task OnGameLoopTick(TimeSpan deltaTime, TimeSpan totalElapsed)
    {
        // deltaTime = 50ms (항상 일정)
        // totalElapsed = 게임 시작 후 시뮬레이션 시간

        // 물리 업데이트 (일정한 시간 간격)
        UpdatePhysics(deltaTime);

        // 충돌 감지
        CheckCollisions();

        // 게임 상태 브로드캐스트
        await BroadcastGameState();

        // 1초마다 로깅 (totalElapsed 활용)
        if (totalElapsed.TotalSeconds % 1.0 < 0.05)
        {
            LOG.Info($"Game time: {totalElapsed.TotalSeconds:F1}s");
        }
    }

    private void UpdatePhysics(TimeSpan deltaTime)
    {
        var dt = (float)deltaTime.TotalSeconds;

        foreach (var entity in _entities)
        {
            // 일정한 deltaTime으로 물리 업데이트
            entity.Position += entity.Velocity * dt;
            entity.Velocity += entity.Acceleration * dt;
        }
    }

    public async Task OnDestroy()
    {
        // Stage 종료 시 게임루프 자동 중지
        // (CloseStage()가 StopGameLoop() 호출)
    }
}
```

#### 8.6.2 고급 설정 사용

```csharp
public class HighFrequencyGameStage : IStage
{
    public async Task OnPostCreate()
    {
        // 60Hz 게임루프 + 커스텀 MaxAccumulatorCap
        var config = new GameLoopConfig
        {
            FixedTimestep = TimeSpan.FromMilliseconds(16.67), // ~60 FPS
            MaxAccumulatorCap = TimeSpan.FromMilliseconds(100) // 최대 6틱까지 허용
        };

        StageSender.StartGameLoop(config, OnGameLoopTick);
    }

    private async Task OnGameLoopTick(TimeSpan deltaTime, TimeSpan totalElapsed)
    {
        // 고주파 게임 로직
        UpdateGameState(deltaTime);
    }
}
```

#### 8.6.3 동적 게임루프 제어

```csharp
public class BattleStage : IStage
{
    public async Task StartBattle()
    {
        if (!StageSender.IsGameLoopRunning)
        {
            StageSender.StartGameLoop(
                TimeSpan.FromMilliseconds(50),
                OnBattleTick
            );
            LOG.Info("Battle started");
        }
    }

    public async Task PauseBattle()
    {
        if (StageSender.IsGameLoopRunning)
        {
            StageSender.StopGameLoop();
            LOG.Info("Battle paused");
        }
    }

    public async Task OnDispatch(IActor actor, IPacket packet)
    {
        if (packet.MsgId == "PauseRequest")
        {
            await PauseBattle();
        }
        else if (packet.MsgId == "ResumeRequest")
        {
            await StartBattle();
        }
    }

    private async Task OnBattleTick(TimeSpan deltaTime, TimeSpan totalElapsed)
    {
        // 전투 로직 업데이트
        UpdateBattle(deltaTime);

        // 전투 종료 조건 체크
        if (IsBattleOver())
        {
            StageSender.StopGameLoop();
            await EndBattle();
        }
    }
}
```

### 8.7 일반 타이머 vs 게임루프

| 특징 | 일반 타이머 (AddRepeatTimer) | 게임루프 (StartGameLoop) |
|------|----------------------------|-------------------------|
| **정밀도** | ~1-15ms (시스템 의존) | 나노초 단위 (Stopwatch) |
| **시간 간격** | 가변적 (지터 존재) | 고정적 (Fixed Timestep) |
| **스레드** | System.Threading.Timer | 전용 스레드 (우선순위 높음) |
| **CPU 사용** | 낮음 | 중간 (SpinWait 사용) |
| **용도** | 주기적 작업, 타임아웃 | 게임 로직, 물리 시뮬레이션 |
| **다중 실행** | Stage당 여러 개 가능 | Stage당 1개만 허용 |
| **과부하 처리** | 지연 누적 | 틱 드롭 (Spiral of Death 방지) |

### 8.8 내부 구현 메커니즘

#### 8.8.1 게임루프 시작 흐름

```
[게임루프 시작 프로세스]

StageSender.StartGameLoop(config, callback)
    │
    ▼
GameLoopTimer 생성
    │
    ▼
전용 스레드 시작
    │  Name: "GameLoop-Stage-{StageId}"
    │  Priority: AboveNormal
    │  IsBackground: true
    │
    ▼
RunLoop() 시작
    │
    ▼
[무한 루프]
    │
    ├─ Stopwatch로 경과 시간 측정
    ├─ accumulator += elapsedTime
    ├─ Spiral of Death 방지 (cap 적용)
    │
    ├─ while (accumulator >= fixedTimestep):
    │      │
    │      ├─ _dispatchCallback(stageId, callback, deltaTime, totalElapsed)
    │      │   │
    │      │   └─ Dispatcher.OnPost(GameLoopMessage) → Stage 메시지 큐
    │      │
    │      └─ accumulator -= fixedTimestep
    │
    └─ 하이브리드 슬립 (Thread.Sleep + SpinWait)
```

#### 8.8.2 고해상도 타이밍

```csharp
// GameLoopTimer.RunLoop() 핵심 코드

var lastTimestamp = Stopwatch.GetTimestamp();

while (_running)
{
    // 고해상도 시간 측정
    var now = Stopwatch.GetTimestamp();
    var elapsedTicks = ((now - lastTimestamp) * TimeSpan.TicksPerSecond) / Stopwatch.Frequency;
    lastTimestamp = now;

    accumulatorTicks += elapsedTicks;

    // Spiral of Death 방지
    if (accumulatorTicks > maxCapTicks)
    {
        accumulatorTicks = maxCapTicks;
    }

    // Fixed timestep 틱 실행
    while (accumulatorTicks >= fixedDtTicks)
    {
        totalElapsedTicks += fixedDtTicks;
        _dispatchCallback(_stageId, _callback, deltaTime, totalElapsed);
        accumulatorTicks -= fixedDtTicks;
    }

    // 하이브리드 슬립 (정밀도 + CPU 효율)
    var remainingTicks = fixedDtTicks - accumulatorTicks;
    var remainingMs = (int)(remainingTicks / TimeSpan.TicksPerMillisecond) - 2;

    if (remainingMs > 1)
    {
        Thread.Sleep(remainingMs); // 대부분의 시간
    }
    else
    {
        Thread.SpinWait(100); // 마지막 정밀도
    }
}
```

### 8.9 성능 특성

#### 8.9.1 타이밍 정밀도

```
Stopwatch.GetTimestamp() 해상도:
- Windows: ~100ns (QPC - Query Performance Counter)
- Linux: ~1ns (clock_gettime with CLOCK_MONOTONIC)

실제 측정 결과 (50ms 간격):
- 평균 오차: < 0.1ms
- 최대 지터: < 1ms (99.9th percentile)
```

#### 8.9.2 CPU 사용량

```
설정별 CPU 사용량 (단일 Stage 기준):

FixedTimestep = 50ms (20 TPS):
- Thread.Sleep 주도: ~0.1% CPU
- SpinWait 비중: 낮음

FixedTimestep = 16ms (60 TPS):
- Thread.Sleep 주도: ~0.3% CPU
- SpinWait 비중: 중간

FixedTimestep = 1ms (1000 TPS):
- SpinWait 주도: ~5% CPU
- Thread.Sleep 거의 없음
```

### 8.10 주의사항 및 베스트 프랙티스

#### 8.10.1 Do (권장)

```
1. Fixed Timestep 사용
   - 물리 시뮬레이션에는 반드시 Fixed Timestep 사용
   - deltaTime을 직접 사용하여 일정한 시뮬레이션 보장

2. 적절한 FixedTimestep 선택
   - 20-60 TPS 권장 (50ms ~ 16ms)
   - 게임 장르에 따라 조정
     * 전략 게임: 10-20 TPS
     * 액션 게임: 30-60 TPS
     * 물리 시뮬레이션: 50-120 TPS

3. totalElapsed 활용
   - 게임 시간 기반 로직에 totalElapsed 사용
   - 일시정지/재개 시에도 정확한 시뮬레이션 시간 유지

4. 과부하 모니터링
   - MaxAccumulatorCap 도달 시 경고 로그
   - 틱 처리 시간이 FixedTimestep을 초과하면 최적화 필요

5. Stage별 독립 게임루프
   - 각 Stage는 독립적인 게임루프 실행 가능
   - 서로 다른 FixedTimestep 설정 가능
```

#### 8.10.2 Don't (금지)

```
1. 게임루프 콜백 내 블로킹
   - Thread.Sleep 금지
   - 동기 I/O 금지
   - AsyncCompute/AsyncIO 사용

2. 너무 짧은 FixedTimestep
   - < 10ms는 피하기 (CPU 과다 사용)
   - 고주파가 필요하면 로직 최적화 먼저

3. 중복 게임루프 시작
   - Stage당 1개만 허용
   - IsGameLoopRunning으로 체크

4. deltaTime 무시
   - deltaTime을 사용하여 일정한 시뮬레이션 유지
   - 실제 프레임 시간(wall time)과 혼동 금지

5. 게임루프와 타이머 혼용
   - 게임 로직은 게임루프 사용
   - 비게임 작업(타임아웃 등)은 일반 타이머 사용
```

### 8.11 디버깅 및 모니터링

```csharp
public class MonitoredGameStage : IStage
{
    private int _tickCount;
    private TimeSpan _lastLogTime;
    private TimeSpan _minTickDuration = TimeSpan.MaxValue;
    private TimeSpan _maxTickDuration = TimeSpan.Zero;

    public async Task OnPostCreate()
    {
        StageSender.StartGameLoop(
            TimeSpan.FromMilliseconds(50),
            OnMonitoredGameLoopTick
        );
    }

    private async Task OnMonitoredGameLoopTick(TimeSpan deltaTime, TimeSpan totalElapsed)
    {
        var sw = Stopwatch.StartNew();

        // 게임 로직 실행
        await UpdateGameLogic(deltaTime);

        sw.Stop();

        // 틱 처리 시간 추적
        _tickCount++;
        if (sw.Elapsed < _minTickDuration) _minTickDuration = sw.Elapsed;
        if (sw.Elapsed > _maxTickDuration) _maxTickDuration = sw.Elapsed;

        // 1초마다 통계 로깅
        if (totalElapsed - _lastLogTime >= TimeSpan.FromSeconds(1))
        {
            LOG.Info($"GameLoop Stats: " +
                     $"TPS={_tickCount}, " +
                     $"TickDuration(min={_minTickDuration.TotalMilliseconds:F2}ms, " +
                     $"max={_maxTickDuration.TotalMilliseconds:F2}ms)");

            _tickCount = 0;
            _minTickDuration = TimeSpan.MaxValue;
            _maxTickDuration = TimeSpan.Zero;
            _lastLogTime = totalElapsed;
        }

        // 과부하 경고
        if (sw.Elapsed > deltaTime)
        {
            LOG.Warn($"Tick processing exceeded fixed timestep: " +
                     $"{sw.Elapsed.TotalMilliseconds:F2}ms > {deltaTime.TotalMilliseconds}ms");
        }
    }
}
```

## 9. 다음 단계

- `03-stage-actor-model.md`: Stage/Actor에서 타이머 사용 패턴
- `05-http-api.md`: HTTP API를 통한 타이머 제어
