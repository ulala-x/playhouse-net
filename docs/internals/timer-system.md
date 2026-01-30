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

## 5. 타이머 정밀도

```
System.Threading.Timer의 정밀도:
- Windows: ~15ms (시스템 타이머 해상도)
- Linux: ~1ms

권장 사항:
- 최소 주기: 10ms 이상
- 고정밀 타이밍: Stopwatch + 게임 틱 활용
```

## 6. 게임루프 시스템 (GameLoop)

### 6.1 개요

게임루프는 고정 시간 간격(Fixed Timestep)으로 실행되는 고해상도 타이머로, 게임 로직 업데이트에 최적화되어 있습니다. 일반 타이머와 달리 결정론적 시뮬레이션(Deterministic Simulation)을 위해 설계되었습니다.

### 6.2 핵심 특징

- **고해상도 타이밍**: `Stopwatch.GetTimestamp()` 기반 (Windows QPC / Linux clock_gettime)
- **전용 스레드**: ThreadPool 지터(jitter) 회피
- **Fixed Timestep 누산기**: 일정한 시간 간격으로 틱 실행 보장
- **Spiral of Death 방지**: 과부하 시 틱 드롭으로 시스템 안정성 유지
- **하이브리드 슬립**: Thread.Sleep + SpinWait로 정밀도와 CPU 효율성 균형

### 6.3 API

#### 6.3.1 게임루프 시작 (간단한 방식)

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

#### 6.3.2 게임루프 시작 (설정 방식)

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

#### 6.3.3 게임루프 중지

```csharp
void StopGameLoop();
```

실행 중인 게임루프를 중지합니다. 게임루프가 실행 중이 아니면 아무 작업도 수행하지 않습니다.

#### 6.3.4 게임루프 실행 상태 확인

```csharp
bool IsGameLoopRunning { get; }
```

현재 Stage에 게임루프가 실행 중인지 확인합니다.

### 6.4 Fixed Timestep 패턴

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

### 6.5 Spiral of Death 방지

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

### 6.6 내부 구현 메커니즘

#### 6.6.1 게임루프 시작 흐름

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

#### 6.6.2 고해상도 타이밍

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

## 7. 다음 단계

- `03-stage-actor-model.md`: Stage/Actor에서 타이머 사용 패턴
- `05-http-api.md`: HTTP API를 통한 타이머 제어
