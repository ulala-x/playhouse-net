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
    TimerCallbackTask callback  // 콜백 함수
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
    int count,              // 실행 횟수
    TimeSpan period,        // 반복 주기
    TimerCallbackTask callback  // 콜백 함수
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
            count: 3,
            period: TimeSpan.FromSeconds(1),
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
            TimeSpan.FromSeconds(3),
            10,
            TimeSpan.FromSeconds(1),
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
        TimerCallbackTask timerCallback)
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
        TimerCallbackTask timerCallback)
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
            count: _countdownSeconds,
            period: TimeSpan.FromSeconds(1),
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
            count: 1,
            period: TimeSpan.Zero,
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
            TimeSpan.FromSeconds(IdleTimeoutSeconds),
            1,
            TimeSpan.Zero,
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
            count: 1,
            period: TimeSpan.Zero,
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
        TimerCallbackTask callback)
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

## 8. 다음 단계

- `03-stage-actor-model.md`: Stage/Actor에서 타이머 사용 패턴
- `05-http-api.md`: HTTP API를 통한 타이머 제어
