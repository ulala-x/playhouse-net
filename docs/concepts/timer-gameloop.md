# 타이머 및 게임루프 가이드

> Stage에서 사용하는 타이머와 게임루프 기능을 다룹니다.
> 전체 구조는 [개요](./overview.md)를, Stage/Actor 모델은 [Stage/Actor 개념](./stage-actor.md)을 참고하세요.

## 개요

PlayHouse는 Stage에서 사용할 수 있는 두 가지 시간 기반 기능을 제공합니다:

- **타이머 (Timer)**: 일정 시간 후 또는 주기적으로 콜백 실행
- **게임루프 (GameLoop)**: 고정 타임스텝으로 게임 로직 실행

이러한 기능들은 Stage의 이벤트 루프에서 안전하게 실행되며, Stage 상태에 직접 접근할 수 있습니다.

## 타이머 (Timer)

타이머는 `IStageSender`를 통해 관리되며, 두 가지 타입이 있습니다:

- **RepeatTimer**: 무한 반복 타이머
- **CountTimer**: 지정한 횟수만큼 실행되는 타이머

### RepeatTimer - 반복 타이머

무한히 반복되는 타이머로, 명시적으로 취소하기 전까지 계속 실행됩니다.

```csharp
public class GameRoomStage : IStage
{
    private long _autoSaveTimerId;

    public IStageSender StageSender { get; }

    public GameRoomStage(IStageSender stageSender)
    {
        StageSender = stageSender;
    }

    public Task OnPostCreate()
    {
        // 5분마다 자동 저장 타이머 시작
        _autoSaveTimerId = StageSender.AddRepeatTimer(
            initialDelay: TimeSpan.FromMinutes(5),   // 첫 실행: 5분 후
            period: TimeSpan.FromMinutes(5),         // 이후: 5분마다
            callback: async () =>
            {
                await SaveGameState();
            }
        );

        return Task.CompletedTask;
    }

    private async Task SaveGameState()
    {
        // 게임 상태 저장 로직
        await Task.CompletedTask;
    }

    public Task OnDestroy()
    {
        // Stage 종료 시 타이머 취소
        StageSender.CancelTimer(_autoSaveTimerId);
        return Task.CompletedTask;
    }

    // 나머지 메서드들...
}
```

**AddRepeatTimer 파라미터:**
- `initialDelay`: 첫 번째 콜백 실행까지의 대기 시간
- `period`: 이후 콜백 간격
- `callback`: 실행할 콜백 함수 (`TimerCallback` 델리게이트)

**반환값:**
- 타이머 ID (long): 타이머를 취소할 때 사용

### CountTimer - 카운트 타이머

지정한 횟수만큼만 실행되는 타이머로, 카운트가 완료되면 자동으로 종료됩니다.

```csharp
public Task OnPostCreate()
{
    // 10초 후 게임 시작 카운트다운 (10, 9, 8, ... 1)
    StageSender.AddCountTimer(
        initialDelay: TimeSpan.FromSeconds(1),  // 1초 후 시작
        period: TimeSpan.FromSeconds(1),        // 1초마다
        count: 10,                               // 10번 실행
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
```

**AddCountTimer 파라미터:**
- `initialDelay`: 첫 번째 콜백 실행까지의 대기 시간
- `period`: 이후 콜백 간격
- `count`: 총 실행 횟수
- `callback`: 실행할 콜백 함수

### 타이머 관리

```csharp
public class GameRoomStage : IStage
{
    private long _timerId;

    // 타이머 시작
    private void StartTimer()
    {
        _timerId = StageSender.AddRepeatTimer(
            TimeSpan.FromSeconds(1),
            TimeSpan.FromSeconds(1),
            async () => { await OnTimerTick(); }
        );
    }

    // 타이머 취소
    private void StopTimer()
    {
        if (StageSender.HasTimer(_timerId))
        {
            StageSender.CancelTimer(_timerId);
        }
    }

    // 타이머 활성 여부 확인
    private bool IsTimerActive()
    {
        return StageSender.HasTimer(_timerId);
    }

    private Task OnTimerTick()
    {
        // 타이머 콜백 로직
        return Task.CompletedTask;
    }
}
```

**타이머 관리 메서드:**
- `CancelTimer(timerId)`: 타이머 취소
- `HasTimer(timerId)`: 타이머 활성 여부 확인

## 게임루프 (GameLoop)

게임루프는 고정 타임스텝(Fixed Timestep)으로 게임 로직을 실행하는 고성능 타이머입니다. 물리 시뮬레이션, 게임 상태 업데이트 등에 적합합니다.

### 기본 사용법

```csharp
public class GameRoomStage : IStage
{
    private int _tickCount = 0;

    public Task OnPostCreate()
    {
        // 50ms(20Hz) 고정 타임스텝으로 게임루프 시작
        StageSender.StartGameLoop(
            fixedTimestep: TimeSpan.FromMilliseconds(50),
            callback: async (deltaTime, totalElapsed) =>
            {
                _tickCount++;

                // 게임 로직 업데이트
                await UpdateGameLogic(deltaTime, totalElapsed);
            }
        );

        return Task.CompletedTask;
    }

    private Task UpdateGameLogic(TimeSpan deltaTime, TimeSpan totalElapsed)
    {
        // deltaTime: 고정 타임스텝 (항상 50ms)
        // totalElapsed: 게임루프 시작 후 총 경과 시간

        // 물리 시뮬레이션
        UpdatePhysics(deltaTime);

        // AI 업데이트
        UpdateAI(deltaTime);

        // 게임 상태 브로드캐스트 (일정 간격마다)
        if (_tickCount % 4 == 0) // 200ms마다 (50ms × 4)
        {
            BroadcastGameState();
        }

        return Task.CompletedTask;
    }

    public Task OnDestroy()
    {
        // Stage 종료 시 게임루프 중지
        if (StageSender.IsGameLoopRunning)
        {
            StageSender.StopGameLoop();
        }

        return Task.CompletedTask;
    }

    // 나머지 메서드들...
}
```

### 게임루프 콜백 파라미터

```csharp
StageSender.StartGameLoop(
    TimeSpan.FromMilliseconds(16), // ~60fps
    async (deltaTime, totalElapsed) =>
    {
        // deltaTime: 고정 타임스텝 (항상 16ms)
        // totalElapsed: 게임루프 시작 후 총 경과 시간

        Console.WriteLine($"DeltaTime: {deltaTime.TotalMilliseconds}ms");
        Console.WriteLine($"TotalElapsed: {totalElapsed.TotalSeconds}s");

        await UpdateGame(deltaTime);
    }
);
```

**콜백 파라미터:**
- `deltaTime` (TimeSpan): 고정 타임스텝 값 (항상 설정한 `fixedTimestep`과 동일)
- `totalElapsed` (TimeSpan): 게임루프 시작 후 총 경과 시간

### 고급 설정 (GameLoopConfig)

더 세밀한 제어가 필요한 경우 `GameLoopConfig`를 사용할 수 있습니다.

```csharp
public Task OnPostCreate()
{
    var config = new GameLoopConfig
    {
        FixedTimestep = TimeSpan.FromMilliseconds(50),     // 50ms (20Hz)
        MaxAccumulatorCap = TimeSpan.FromMilliseconds(200) // 최대 4 tick 누적
    };

    StageSender.StartGameLoop(config, async (deltaTime, totalElapsed) =>
    {
        await UpdateGameLogic(deltaTime, totalElapsed);
    });

    return Task.CompletedTask;
}
```

**GameLoopConfig 속성:**
- `FixedTimestep`: 고정 타임스텝 (기본값: 50ms, 유효 범위: 1ms ~ 1000ms)
- `MaxAccumulatorCap`: 최대 누적 시간 (기본값: `FixedTimestep × 5`)
  - 서버가 과부하 상태에서 "Spiral of Death" 방지
  - 누적 시간이 이 값을 초과하면 일부 틱을 건너뜀

### 게임루프 제어

```csharp
public class GameRoomStage : IStage
{
    // 게임루프 시작
    private void StartGameLoop()
    {
        if (!StageSender.IsGameLoopRunning)
        {
            StageSender.StartGameLoop(
                TimeSpan.FromMilliseconds(50),
                async (dt, elapsed) => { await OnGameTick(dt, elapsed); }
            );
        }
    }

    // 게임루프 중지
    private void StopGameLoop()
    {
        if (StageSender.IsGameLoopRunning)
        {
            StageSender.StopGameLoop();
        }
    }

    // 게임루프 실행 여부 확인
    private bool IsGameLoopActive()
    {
        return StageSender.IsGameLoopRunning;
    }

    private Task OnGameTick(TimeSpan deltaTime, TimeSpan totalElapsed)
    {
        // 게임 틱 로직
        return Task.CompletedTask;
    }
}
```

**게임루프 관리 메서드:**
- `StartGameLoop()`: 게임루프 시작
- `StopGameLoop()`: 게임루프 중지
- `IsGameLoopRunning` (속성): 게임루프 실행 여부

## 타이머 vs 게임루프 비교

| 특성 | RepeatTimer | CountTimer | GameLoop |
|------|-------------|------------|----------|
| **반복 횟수** | 무한 | 지정한 횟수 | 무한 (수동 중지 필요) |
| **정확도** | 일반적 (~ms 단위) | 일반적 (~ms 단위) | 높음 (고정 타임스텝) |
| **사용 사례** | 주기적 저장, 이벤트 | 카운트다운, 제한 시간 | 물리 시뮬레이션, 게임 로직 |
| **성능 오버헤드** | 낮음 | 낮음 | 중간 (높은 주기성) |
| **Stage당 개수** | 여러 개 가능 | 여러 개 가능 | 1개만 가능 |

## 실전 예제

### 예제 1: 자동 시작 카운트다운

```csharp
public class LobbyStage : IStage
{
    private int _countdown = 10;
    private readonly Dictionary<string, IActor> _players = new();

    public IStageSender StageSender { get; }

    public LobbyStage(IStageSender stageSender)
    {
        StageSender = stageSender;
    }

    public Task OnPostJoinStage(IActor actor)
    {
        _players[actor.ActorSender.AccountId] = actor;

        // 4명이 모이면 10초 카운트다운 시작
        if (_players.Count == 4)
        {
            StartCountdown();
        }

        return Task.CompletedTask;
    }

    private void StartCountdown()
    {
        _countdown = 10;

        StageSender.AddCountTimer(
            initialDelay: TimeSpan.FromSeconds(1),
            period: TimeSpan.FromSeconds(1),
            count: 10,
            callback: async () =>
            {
                _countdown--;

                // 모든 플레이어에게 카운트다운 알림
                var notify = new CountdownNotify { RemainingSeconds = _countdown };
                BroadcastToAll(notify);

                if (_countdown == 0)
                {
                    await StartMatch();
                }
            }
        );
    }

    private async Task StartMatch()
    {
        // 매치 시작 로직
        var notify = new MatchStartedNotify();
        BroadcastToAll(notify);

        await Task.CompletedTask;
    }

    private void BroadcastToAll(IMessage message)
    {
        var packet = CPacket.Of(message);
        foreach (var player in _players.Values)
        {
            player.ActorSender.SendToClient(packet);
        }
    }

    // 나머지 메서드들...
}
```

### 예제 2: 실시간 게임 (물리 시뮬레이션)

```csharp
public class BattleStage : IStage
{
    private readonly Dictionary<string, PlayerState> _playerStates = new();
    private int _tickCount = 0;

    public IStageSender StageSender { get; }

    public BattleStage(IStageSender stageSender)
    {
        StageSender = stageSender;
    }

    public Task OnPostCreate()
    {
        // 50ms (20Hz) 고정 타임스텝으로 게임루프 시작
        StageSender.StartGameLoop(
            TimeSpan.FromMilliseconds(50),
            async (deltaTime, totalElapsed) =>
            {
                _tickCount++;

                // 물리 업데이트
                UpdatePhysics(deltaTime);

                // AI 업데이트
                UpdateAI(deltaTime);

                // 충돌 검사
                CheckCollisions();

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

        foreach (var state in _playerStates.Values)
        {
            // 속도 기반 위치 업데이트
            state.Position.X += state.Velocity.X * dt;
            state.Position.Y += state.Velocity.Y * dt;

            // 중력 적용
            state.Velocity.Y += 9.8f * dt;
        }
    }

    private void UpdateAI(TimeSpan deltaTime)
    {
        // AI 로직
    }

    private void CheckCollisions()
    {
        // 충돌 검사 로직
    }

    private Task BroadcastGameState()
    {
        var notify = new GameStateNotify();

        foreach (var (accountId, state) in _playerStates)
        {
            notify.Players.Add(new PlayerStateData
            {
                AccountId = accountId,
                Position = new Vector2Data
                {
                    X = state.Position.X,
                    Y = state.Position.Y
                },
                Velocity = new Vector2Data
                {
                    X = state.Velocity.X,
                    Y = state.Velocity.Y
                }
            });
        }

        // 모든 플레이어에게 브로드캐스트
        // (실제 구현에서는 _players 컬렉션 사용)
        return Task.CompletedTask;
    }

    public Task OnDestroy()
    {
        if (StageSender.IsGameLoopRunning)
        {
            StageSender.StopGameLoop();
        }

        return Task.CompletedTask;
    }

    // 나머지 메서드들...

    private class PlayerState
    {
        public Vector2 Position { get; set; }
        public Vector2 Velocity { get; set; }
    }

    private class Vector2
    {
        public float X { get; set; }
        public float Y { get; set; }
    }
}
```

### 예제 3: 주기적 상태 저장

```csharp
public class PersistentRoomStage : IStage
{
    private long _autoSaveTimerId;
    private long _cleanupTimerId;

    public IStageSender StageSender { get; }

    public PersistentRoomStage(IStageSender stageSender)
    {
        StageSender = stageSender;
    }

    public Task OnPostCreate()
    {
        // 5분마다 자동 저장
        _autoSaveTimerId = StageSender.AddRepeatTimer(
            initialDelay: TimeSpan.FromMinutes(5),
            period: TimeSpan.FromMinutes(5),
            callback: async () =>
            {
                await SaveRoomState();
            }
        );

        // 1시간마다 비활성 플레이어 정리
        _cleanupTimerId = StageSender.AddRepeatTimer(
            initialDelay: TimeSpan.FromHours(1),
            period: TimeSpan.FromHours(1),
            callback: async () =>
            {
                await CleanupInactivePlayers();
            }
        );

        return Task.CompletedTask;
    }

    private async Task SaveRoomState()
    {
        // 외부 API에 방 상태 저장
        await Task.CompletedTask;
    }

    private async Task CleanupInactivePlayers()
    {
        // 비활성 플레이어 제거 로직
        await Task.CompletedTask;
    }

    public Task OnDestroy()
    {
        // 타이머 모두 취소
        if (StageSender.HasTimer(_autoSaveTimerId))
        {
            StageSender.CancelTimer(_autoSaveTimerId);
        }

        if (StageSender.HasTimer(_cleanupTimerId))
        {
            StageSender.CancelTimer(_cleanupTimerId);
        }

        return Task.CompletedTask;
    }

    // 나머지 메서드들...
}
```

## 주의사항 및 팁

### 1. Stage당 게임루프는 1개만

```csharp
// ❌ 잘못된 예 - 게임루프를 여러 번 시작
public Task OnPostCreate()
{
    StageSender.StartGameLoop(TimeSpan.FromMilliseconds(50), OnTick1);
    StageSender.StartGameLoop(TimeSpan.FromMilliseconds(100), OnTick2); // 예외 발생!

    return Task.CompletedTask;
}

// ✅ 올바른 예 - 하나의 게임루프에서 모든 로직 처리
public Task OnPostCreate()
{
    StageSender.StartGameLoop(
        TimeSpan.FromMilliseconds(50),
        async (deltaTime, totalElapsed) =>
        {
            await OnTick1(deltaTime);
            await OnTick2(deltaTime);
        }
    );

    return Task.CompletedTask;
}
```

### 2. OnDestroy에서 타이머/게임루프 정리

```csharp
public Task OnDestroy()
{
    // 모든 타이머 취소
    foreach (var timerId in _activeTimerIds)
    {
        if (StageSender.HasTimer(timerId))
        {
            StageSender.CancelTimer(timerId);
        }
    }

    // 게임루프 중지
    if (StageSender.IsGameLoopRunning)
    {
        StageSender.StopGameLoop();
    }

    return Task.CompletedTask;
}
```

### 3. 타이머 콜백에서 Stage 상태 접근

타이머와 게임루프 콜백은 Stage의 이벤트 루프에서 실행되므로 Stage 상태에 안전하게 접근할 수 있습니다.

```csharp
private int _gameTime = 0;
private readonly Dictionary<string, IActor> _players = new();

public Task OnPostCreate()
{
    StageSender.AddRepeatTimer(
        TimeSpan.FromSeconds(1),
        TimeSpan.FromSeconds(1),
        async () =>
        {
            _gameTime++; // 안전하게 접근 가능

            // Stage의 다른 상태에도 접근 가능
            if (_gameTime >= 300 && _players.Count == 0)
            {
                StageSender.CloseStage();
            }
        }
    );

    return Task.CompletedTask;
}
```

### 4. 고정 타임스텝 선택 가이드

| 타임스텝 | 주파수 | 사용 사례 |
|---------|--------|----------|
| 16ms | ~60 Hz | 빠른 액션 게임, 부드러운 물리 |
| 33ms | ~30 Hz | 일반 게임, 중간 정확도 |
| 50ms | 20 Hz | 턴제 게임, 낮은 부하 |
| 100ms | 10 Hz | 전략 게임, 느린 업데이트 |

## 다음 단계

- [Stage 구현 가이드](04-stage-implementation.md) - Stage 구현 방법
- [Actor 구현 가이드](05-actor-implementation.md) - Actor 구현 방법
