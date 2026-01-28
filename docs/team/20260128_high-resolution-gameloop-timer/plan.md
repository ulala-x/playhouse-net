# 고해상도 게임 루프 타이머 구현 계획 (검증·보완본)

## 1. 목적 요약
- 기존 `TimerManager`(System.Threading.Timer)와는 별개로 **Stage 전용 고해상도 게임 루프 타이머**를 추가한다.
- 고정 타임스텝(Deterministic) 기반으로 `deltaTime`/`totalElapsed`를 제공하며, 콜백은 **Stage 이벤트 루프에서 순차 실행**된다.

---

## 2. 현행 코드 구조 분석 (요청 파일 기준)

### 2.1 `src/PlayHouse/Core/Play/TimerManager.cs`
- System.Threading.Timer 기반 스케줄러.
- `_dispatchCallback(stageId, timerId, callback)`로 Stage 이벤트 루프에 디스패치.
- `TimerPacket` → `PlayDispatcher.OnPost` → `TimerManager.ProcessTimer` 흐름.
- **게임 루프 타이머도 동일한 “Stage 이벤트 루프 디스패치” 패턴에 맞춰야 함.**

### 2.2 `src/PlayHouse/Core/Play/Base/BaseStage.cs`
- `ConcurrentQueue<StageMessage>` 기반 메일박스.
- `ThreadPool.QueueUserWorkItem`으로 실행 스케줄링.
- `PostTimerCallback`이 `StageMessage.TimerMessage`를 enqueue.
- `ExecuteAsync`는 메시지를 순차 실행하고 `Dispose()` 호출.
- **게임 루프 틱도 StageMessage로 enqueue하면 기존 순차성/스레드 안전성 보장됨.**

### 2.3 `src/PlayHouse/Core/Play/Base/StageMessage.cs`
- `ClientRouteMessage`, `ContinuationMessage`는 ObjectPool 사용.
- `TimerMessage`는 콜백만 실행.
- **게임 루프 틱도 ObjectPool로 GC 압박 최소화 필요.**

### 2.4 `src/PlayHouse/Abstractions/Play/IStageSender.cs`
- 공용 API로 Timer/Async/CloseStage 제공.
- **새 게임 루프 API는 public API 변경**이므로 XML 주석, 범위 검증, 예외 정책 명확화 필요.

### 2.5 `src/PlayHouse/Core/Play/XStageSender.cs`
- `_dispatcher.OnPost(...)`로 PlayDispatcher에 전달.
- `_timerIds`로 타이머 상태 추적.
- `CloseStage()`에서 타이머 취소 후 Destroy 메시지 전송.
- **게임 루프 시작/중단과 CloseStage 연동이 필요.**

### 2.6 `src/PlayHouse/Core/Play/PlayDispatcher.cs`
- Stage 라이프사이클/라우팅/타이머 관리 중심.
- `_timerManager`와 `OnTimerCallback`로 Stage 이벤트 루프에 전달.
- `ProcessDestroy`에서 타이머 취소 후 Stage 파괴.
- **게임 루프 타이머도 Dispatcher에 중앙 관리되어야 정리/수명 주기가 일관됨.**

---

## 3. 원안(고해상도 게임 루프 계획) 실행 가능성 검증

### 검증 결과 요약
- **실행 가능**: 현재 구조(Dispatcher → BaseStage 메일박스)와 정합성이 높아 추가 구현이 자연스럽다.
- **고해상도 타이머 도입**은 TimerManager와 독립적으로 추가해도 충돌 없음.
- **Stage 안전성**: 콜백을 Stage 이벤트 루프에서 실행하는 방식은 기존 설계와 동일한 안전성을 제공.

### 반드시 보완해야 하는 부분
- Dispatcher API 확장 방식 (IPlayDispatcher/PlayMessage 처리 방식)
- GameLoopTimer 스레드 종료/정리 방식 (Stop/Dispose, Cancel 시점)
- Stage 종료/서버 종료 시 정리 순서
- Backpressure/메일박스 적재 시 동작 정책
- `totalElapsed` 정의(실시간 vs 시뮬레이션 시간) 명확화

---

## 4. 보완/개선 사항 (원안 대비)

### 4.1 Dispatcher 연동 방식 명확화
- **선택지 A (권장):** `IPlayDispatcher`에 `StartGameLoop/StopGameLoop/IsGameLoopRunning` 추가.
  - `XStageSender`가 `_dispatcher.StartGameLoop(...)` 호출.
  - Dispatcher 내부에서 `_gameLoopTimers`를 중앙 관리.
- **선택지 B:** `PlayMessage`로 Start/Stop 메시지를 추가해 `OnPost`에서 처리.
  - 구조 일관성은 높지만 메시지 클래스가 늘어남.
- **권장**: A가 구현/테스트 간단. 단, `IPlayDispatcher` 인터페이스 수정 필요.

### 4.2 종료/정리 흐름 보완
- `XStageSender.CloseStage()`에서 **StopGameLoop** 먼저 수행 후 타이머 취소.
- `PlayDispatcher.ProcessDestroy()`에서도 Stage 제거 시 **GameLoopTimer 정리** 필수.
- `PlayDispatcher.Dispose()`에서 모든 GameLoopTimer 중단/정리.

### 4.3 Backpressure 및 메일박스 적재 대응
- 고빈도(1~5ms) 틱일수록 메일박스가 과적될 가능성 존재.
- **기본 정책**: `MaxAccumulatorCap`으로 틱 폭발 방지.
- **추가 개선(선택):**
  - `BaseStage`에 메일박스 적재량 카운터 추가 후 일정 이상이면 틱 드롭.
  - 또는 GameLoopTimer에서 `MaxTicksPerBurst`(예: 3~5) 제공.

### 4.4 시간 정의 명확화
- `deltaTime`은 **항상 FixedTimestep**으로 보장.
- `totalElapsed`는 **시뮬레이션 시간(= tickCount * fixedTimestep)**으로 정의.
  - 실제 경과 시간과 차이가 날 수 있음을 문서화.

### 4.5 GameLoopTimer 중지 응답성
- `Thread.Sleep`만 사용하면 Stop 호출 시 대기 시간이 길 수 있음.
- **Stop 시점 즉시 종료**를 위해 `ManualResetEventSlim` 또는 `CancellationToken` + `WaitHandle` 사용 권장.

### 4.6 로깅/메트릭
- `PlayDispatcher`에 `ActiveGameLoopCount` 추가.
- GameLoop 시작/중단 시 로그 (StageId, FixedTimestep, MaxCap).
- `MaxAccumulatorCap` 초과 시 warn 로그 (옵션).

---

## 5. 보완된 상세 설계

### 5.1 Public API (`IStageSender`)
```csharp
public delegate Task GameLoopCallback(TimeSpan deltaTime, TimeSpan totalElapsed);

void StartGameLoop(TimeSpan fixedTimestep, GameLoopCallback callback);
void StartGameLoop(GameLoopConfig config, GameLoopCallback callback);
void StopGameLoop();
bool IsGameLoopRunning { get; }
```
- 중복 Start 호출 시 `InvalidOperationException`.
- `fixedTimestep` 범위: **1ms ~ 1000ms** (ArgumentOutOfRangeException).

### 5.2 GameLoopConfig (Abstractions)
```csharp
public sealed class GameLoopConfig
{
    public TimeSpan FixedTimestep { get; init; } = TimeSpan.FromMilliseconds(50);
    public TimeSpan? MaxAccumulatorCap { get; init; }

    internal TimeSpan EffectiveMaxAccumulatorCap =>
        MaxAccumulatorCap ?? TimeSpan.FromTicks(FixedTimestep.Ticks * 5);
}
```
- `MaxAccumulatorCap`는 `FixedTimestep` 이상으로 보정.
- API 최소화를 위해 SpinWaitThreshold 등은 **GameLoopTimer 내부 상수**로 처리.

### 5.3 GameLoopTimer (Core)
- **전용 백그라운드 스레드** + `Stopwatch.GetTimestamp()`.
- 고정 타임스텝 누적기(“Fix Your Timestep”).

의사코드:
```
while (running)
  now = Stopwatch.GetTimestamp()
  frame = now - last
  last = now
  accumulator += frame
  if (accumulator > maxCap) accumulator = maxCap

  while (accumulator >= fixed)
    totalElapsed += fixed
    dispatch(stageId, callback, fixed, totalElapsed)
    accumulator -= fixed

  sleep/spin until next tick
```

### 5.4 Dispatcher 연동
- `PlayDispatcher`에 `_gameLoopTimers: ConcurrentDictionary<long, GameLoopTimer>` 추가.
- `StartGameLoop(stageId, config, callback)`:
  - Stage 존재 확인 → TryAdd → GameLoopTimer 시작.
- `StopGameLoop(stageId)`:
  - TryRemove → Stop/Dispose.
- `OnGameLoopTick(...)`:
  - 해당 Stage가 존재하면 `BaseStage.PostGameLoopTick(...)`
  - 없으면 해당 Timer 중단 (Stage 삭제 레이스 대비).

### 5.5 Stage 메시지
- `StageMessage.GameLoopTickMessage` 추가 + ObjectPool.
- `BaseStage.PostGameLoopTick(...)`에서 메시지 생성/queue.

### 5.6 XStageSender
- `_dispatcher.StartGameLoop/StopGameLoop/IsGameLoopRunning` 래핑.
- `CloseStage()`에서 GameLoop 중단 후 타이머 취소.

---

## 6. 구현 순서 (보완본)

1. **Abstractions**
   - `GameLoopConfig` 추가 (`src/PlayHouse/Abstractions/Play/GameLoopConfig.cs`).
   - `IStageSender`에 `GameLoopCallback`, Start/Stop/IsRunning 추가.

2. **Stage 메시지**
   - `StageMessage.GameLoopTickMessage` + ObjectPool 추가.
   - `BaseStage.PostGameLoopTick(...)` 추가.

3. **Core 엔진**
   - `GameLoopTimer` 구현 (`src/PlayHouse/Core/Play/GameLoopTimer.cs`).

4. **Dispatcher 연동**
   - `PlayDispatcher`에 `_gameLoopTimers` + Start/Stop/OnGameLoopTick + Dispose 정리.
   - 필요 시 `IPlayDispatcher` 인터페이스 확장.

5. **StageSender 연동**
   - `XStageSender`에 Start/Stop/IsRunning 구현.
   - `CloseStage()`에서 StopGameLoop 호출.

6. **테스트/검증 추가**
   - 단위 테스트 + E2E 시나리오 추가.

---

## 7. 테스트/검증 계획 (현행 테스트 구조 반영)

### 7.1 단위 테스트 (`tests/unit/PlayHouse.Unit/Core/Play`)
- `GameLoopTimerTests.cs`
  - `Start_CreatesBackgroundThread`
  - `Stop_TerminatesThread`
  - `TicksDispatchedAtExpectedRate` (허용 오차 적용)
  - `AccumulatorCap_PreventsSpiralOfDeath`
  - `DeltaTime_EqualsFixedTimestep`
  - `TotalElapsed_IncreasesByFixedTimestep`
  - `DoubleStart_Throws`
  - `StopWhenNotRunning_NoOp`
- DisplayName은 한국어 사용.

### 7.2 Dispatcher/Stage 통합 단위 테스트
- `PlayDispatcherTests`에 GameLoopCount 및 Start/Stop 동작 검증 추가.

### 7.3 E2E 테스트 (`tests/e2e`)
- `tests/e2e/PlayHouse.E2E.Shared/Proto/test_messages.proto`
  - Start/Stop GameLoop 메시지 추가.
- `TestStageImpl`에 Start/Stop GameLoop 핸들러 추가.
- `PlayHouse.E2E/Verifiers`에 `GameLoopVerifier` 추가.

---

## 8. 리스크 및 대응 (보완본)

| 리스크 | 영향 | 대응 |
|---|---|---|
| Stage 수 증가로 스레드 증가 | CPU/메모리 | GameLoop 사용 Stage를 제한, ActiveGameLoopCount 모니터링 |
| Mailbox 적재 | 지연/메모리 | MaxAccumulatorCap + (선택) 백로그 감지 후 틱 드롭 |
| Thread.Sleep 해상도 한계 | 틱 지터 | Sleep + SpinWait 하이브리드 |
| Stop 지연 | 종료 시 느림 | WaitHandle 기반 즉시 종료 |
| 테스트 플래키 | CI 불안정 | 시간 허용 오차 확대, 실행 시간 최소화 |

---

## 9. 완료 기준 (Definition of Done)
- IStageSender에서 GameLoop API 사용 가능.
- Stage 이벤트 루프에서 deltaTime/totalElapsed 콜백 실행.
- Stage 종료/서버 종료 시 GameLoop 스레드가 모두 정리됨.
- 단위/E2E 테스트 추가 및 통과.
- 기존 Timer 기능 회귀 없음.

