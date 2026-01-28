# 고해상도 게임 루프 타이머 구현 계획

## 1. 배경 및 동기

### 현재 타이머 시스템
- `TimerManager`가 `System.Threading.Timer` 기반으로 이벤트 스케줄링 제공
- API: `AddRepeatTimer`, `AddCountTimer`, `CancelTimer`, `HasTimer`
- 해상도: 밀리초 단위, Windows 기본 ~15.6ms 해상도
- 용도: 버프 지속시간, 쿨다운, 스폰 타이밍 등 이벤트 스케줄링

### 문제점
| 항목 | 현재 시스템 | FPS 게임 서버 요구 |
|------|------------|-------------------|
| 해상도 | ~15.6ms (Windows) | 8~16ms 일관성 |
| Delta Time | 미제공 | 필수 |
| 고정 타임스텝 | 미지원 | 시뮬레이션 결정론 필수 |
| Jitter | ThreadPool 2회 경유 | 최소화 필요 |
| 패턴 | 이벤트 스케줄링 | 프레임 기반 업데이트 |

### 결론
기존 타이머를 **교체하지 않고**, 게임 루프 전용 고해상도 타이머를 **별도 기능으로 추가**

---

## 2. API 설계

### 2.1 새 델리게이트

```csharp
// IStageSender.cs에 추가
public delegate Task GameLoopCallback(TimeSpan deltaTime, TimeSpan totalElapsed);
```

- `deltaTime`: 고정 타임스텝 값 (항상 설정된 fixedTimestep과 동일)
- `totalElapsed`: 게임 루프 시작 이후 총 경과 시간

### 2.2 IStageSender 새 메서드

```csharp
#region Game Loop

void StartGameLoop(TimeSpan fixedTimestep, GameLoopCallback callback);
void StartGameLoop(GameLoopConfig config, GameLoopCallback callback);
void StopGameLoop();
bool IsGameLoopRunning { get; }

#endregion
```

- Stage당 최대 1개의 게임 루프
- `StartGameLoop` 중복 호출 시 `InvalidOperationException`
- `fixedTimestep` 유효 범위: 1ms ~ 1000ms

### 2.3 GameLoopConfig

```csharp
// 새 파일: src/PlayHouse/Abstractions/Play/GameLoopConfig.cs
public sealed class GameLoopConfig
{
    public TimeSpan FixedTimestep { get; init; } = TimeSpan.FromMilliseconds(50);
    public TimeSpan? MaxAccumulatorCap { get; init; }  // 기본값: 5 × FixedTimestep

    internal TimeSpan EffectiveMaxAccumulatorCap =>
        MaxAccumulatorCap ?? TimeSpan.FromTicks(FixedTimestep.Ticks * 5);
}
```

- `MaxAccumulatorCap`: Spiral of Death 방지. 누적 시간 초과 시 틱 버림

### 2.4 사용 예시

```csharp
// IStage.OnPostCreate()에서 게임 루프 시작
public async Task OnPostCreate()
{
    StageSender.StartGameLoop(
        TimeSpan.FromMilliseconds(16.67),  // 60Hz
        async (deltaTime, totalElapsed) =>
        {
            // Stage 이벤트 루프에서 실행 → 스레드 안전
            UpdatePhysics(deltaTime);
            UpdateGameState(deltaTime);
            BroadcastStateToClients();
        });
}
```

---

## 3. 내부 설계

### 3.1 GameLoopTimer (핵심 엔진)

**새 파일**: `src/PlayHouse/Core/Play/GameLoopTimer.cs`

```
전용 스레드 루프:
  while (running)
    now = Stopwatch.GetTimestamp()
    frameTime = (now - last) / Stopwatch.Frequency
    last = now
    accumulator += frameTime

    if (accumulator > maxCap)
      accumulator = maxCap  // Spiral of Death 방지

    while (accumulator >= fixedDt)
      dispatch(stageId, callback, fixedTimestep, totalElapsed)
      accumulator -= fixedDt

    // 하이브리드 슬립: 대부분 Sleep + 마지막 ~2ms는 SpinWait
    remainingMs = (fixedDt - accumulator) * 1000 - 2
    if (remainingMs > 1) Thread.Sleep(remainingMs)
    else Thread.SpinWait(100)
```

**핵심 설계 결정:**
- `Stopwatch.GetTimestamp()`: 나노초 정밀도 (Windows QPC / Linux clock_gettime)
- **Stage당 전용 백그라운드 스레드**: ThreadPool jitter 회피
- **하이브리드 슬립**: CPU 절약 + 서브밀리초 정밀도 확보
- **고정 타임스텝 누적기**: "Fix Your Timestep" 패턴으로 결정론적 시뮬레이션

### 3.2 GameLoopTickMessage (메일박스 메시지)

**수정 파일**: `src/PlayHouse/Core/Play/Base/StageMessage.cs`

```csharp
public sealed class GameLoopTickMessage : StageMessage
{
    private GameLoopCallback? _callback;
    private TimeSpan _deltaTime;
    private TimeSpan _totalElapsed;

    internal void Update(GameLoopCallback callback, TimeSpan deltaTime, TimeSpan totalElapsed) { ... }
    public override Task ExecuteAsync() => _callback!.Invoke(_deltaTime, _totalElapsed);
    public override void Dispose() { /* Pool로 반환 */ }
}

// ObjectPool로 관리 (20~128개/초/Stage GC 방지)
internal static readonly ObjectPool<GameLoopTickMessage> GameLoopTickMessagePool = ...;
```

### 3.3 메시지 흐름

```
StageSender.StartGameLoop(timestep, callback)
  → PlayDispatcher: GameLoopTimer 생성 및 시작
    → [전용 스레드] Stopwatch 루프
      → accumulator >= fixedDt 일 때:
        → PlayDispatcher.OnGameLoopTick(stageId, callback, dt, totalElapsed)
          → BaseStage.PostGameLoopTick()
            → 메일박스 Enqueue(GameLoopTickMessage)
              → ExecuteAsync()에서 순차 실행
                → callback(deltaTime, totalElapsed)  // Stage 이벤트 루프에서 실행
```

---

## 4. 스레드 모델: Stage당 전용 스레드

| 항목 | Per-Stage 전용 스레드 | 공유 타이머 스레드 |
|------|---------------------|------------------|
| 격리성 | Stage 간 완전 독립 | 하나가 느리면 전체 영향 |
| 복잡도 | 단순 (각자 루프) | 우선순위 큐 + 동기화 필요 |
| 스레드 수 | 게임 루프 Stage 수만큼 | 1개 |
| 적합성 | 게임 서버 (방 수 제한적) | 수천 개 타이머 시 |

**선택: Per-Stage 전용 스레드**
- 게임 루프는 FPS/액션 방만 사용 → 스레드 수 제한적 (수십~수백)
- `IsBackground = true`, `ThreadPriority.AboveNormal`
- CPU 영향: 스레드당 ~0.1% (대부분 Sleep 상태)

---

## 5. 라이프사이클 관리

### 시작
- `StageSender.StartGameLoop()` 호출 시 전용 스레드 생성 및 시작
- Stage당 최대 1개 (중복 시 예외)

### 종료
- `StageSender.StopGameLoop()`: 명시적 종료
- `StageSender.CloseStage()`: 기존 타이머 취소 + 게임 루프 자동 종료
- Stage 파괴 시: `PlayDispatcher.ProcessDestroy()`에서 자동 정리
- `PlayDispatcher.Dispose()`: 서버 종료 시 모든 게임 루프 정리

---

## 6. 수정 대상 파일

### 새 파일
| 파일 | 용도 |
|------|------|
| `src/PlayHouse/Abstractions/Play/GameLoopConfig.cs` | 게임 루프 설정 클래스 |
| `src/PlayHouse/Core/Play/GameLoopTimer.cs` | 고해상도 타이머 엔진 |

### 수정 파일
| 파일 | 변경 내용 |
|------|----------|
| `src/PlayHouse/Abstractions/Play/IStageSender.cs` | `GameLoopCallback` 델리게이트, `StartGameLoop`/`StopGameLoop`/`IsGameLoopRunning` 추가 |
| `src/PlayHouse/Core/Play/XStageSender.cs` | 새 인터페이스 메서드 구현, Dispatcher와 연동 |
| `src/PlayHouse/Core/Play/PlayDispatcher.cs` | `GameLoopTimer` 관리 (`_gameLoopTimers` 딕셔너리), 콜백 디스패치, 정리 로직 |
| `src/PlayHouse/Core/Play/Base/BaseStage.cs` | `PostGameLoopTick()` 메서드 추가 |
| `src/PlayHouse/Core/Play/Base/StageMessage.cs` | `GameLoopTickMessage` 클래스 + ObjectPool 추가 |

---

## 7. 구현 순서

### Step 1: 추상화 레이어
1. `GameLoopConfig.cs` 생성
2. `IStageSender.cs`에 `GameLoopCallback` 델리게이트 및 메서드 추가

### Step 2: 메시지 인프라
3. `StageMessage.cs`에 `GameLoopTickMessage` + ObjectPool 추가
4. `BaseStage.cs`에 `PostGameLoopTick()` 추가

### Step 3: 핵심 엔진
5. `GameLoopTimer.cs` 생성 (전용 스레드, Stopwatch 루프, 누적기)

### Step 4: 통합
6. `PlayDispatcher.cs`에 게임 루프 관리 로직 추가
7. `XStageSender.cs`에 인터페이스 구현
8. `CloseStage()` 및 `Dispose()`에 정리 로직 추가

### Step 5: 테스트
9. 단위 테스트 작성 (`GameLoopTimerTests.cs`)
10. Proto 메시지 추가 (E2E용)
11. `TestStageImpl`에 핸들러 추가
12. E2E 검증기 작성 (`GameLoopVerifier.cs`)

---

## 8. 테스트 계획

### 8.1 단위 테스트

| 테스트 | 검증 내용 |
|--------|----------|
| `Start_CreatesBackgroundThread` | 스레드 생성 및 실행 |
| `Stop_TerminatesThread` | 정상 종료 |
| `TicksDispatchedAtCorrectRate` | 50ms 타임스텝 → ~20 tick/sec |
| `AccumulatorCap_PreventsSpiralOfDeath` | 최대 N틱만 발화 |
| `DeltaTime_AlwaysEqualsFixedTimestep` | 콜백에 정확한 fixedTimestep 전달 |
| `TotalElapsed_IncreasesMonotonically` | 총 경과 시간 단조 증가 |
| `DoubleStart_Throws` | 중복 시작 시 예외 |
| `StopWhenNotRunning_NoOp` | 미실행 상태 Stop은 무시 |
| `CloseStage_StopsGameLoop` | Stage 종료 시 자동 정지 |

### 8.2 E2E 테스트

| 테스트 | 검증 내용 |
|--------|----------|
| `GameLoop_ReceivesTicksAtConfiguredRate` | 클라이언트가 설정된 Hz에 근접한 Push 수신 |
| `GameLoop_DeltaTimeMatchesFixedTimestep` | 각 Notify의 deltaTime이 설정값과 일치 |
| `GameLoop_StopsOnCloseStage` | Stage 종료 후 Push 중단 |
| `GameLoop_CoexistsWithRepeatTimer` | 기존 타이머와 동시 동작 |

### 8.3 E2E Proto 메시지

```protobuf
message StartGameLoopRequest {
    double timestep_ms = 1;
    int32 max_ticks = 2;
}
message StartGameLoopReply { bool success = 1; }
message GameLoopTickNotify {
    int32 tick_number = 1;
    double delta_time_ms = 2;
    double total_elapsed_ms = 3;
}
message StopGameLoopRequest {}
message StopGameLoopReply { bool success = 1; int32 total_ticks = 2; }
```

---

## 9. 리스크 및 대응

| 리스크 | 대응 |
|--------|------|
| **스레드 수 증가** | 게임 루프는 FPS/액션 방만 사용. `GameLoopCount` 메트릭 추가로 모니터링 |
| **Windows Thread.Sleep 해상도** | 하이브리드 슬립 (Sleep + SpinWait) 으로 서브밀리초 정밀도 확보 |
| **콜백 처리 지연** | 누적기 cap으로 최대 catch-up 틱 수 제한. 메일박스 깊이 모니터링 |
| **메일박스 메시지 순서** | 게임 루프 틱도 FIFO로 처리되므로 다른 메시지와 자연스럽게 인터리브 |
| **GC 압박** | `GameLoopTickMessage` ObjectPool로 할당 최소화 |

---

## 10. 검증 방법

1. **빌드**: `dotnet build` 전체 솔루션 성공 확인
2. **단위 테스트**: `dotnet test` - 새 테스트 및 기존 테스트 모두 통과
3. **타이밍 검증**: 단위 테스트에서 50ms 타임스텝 설정 후 1초간 실행 → 18~22 틱 범위 확인
4. **E2E 테스트**: 클라이언트에서 게임 루프 시작 → Push 수신 → 정지 → Push 중단 흐름 검증
5. **기존 기능 회귀**: 기존 `TimerManager` 테스트 및 E2E `TimerVerifier` 통과 확인
