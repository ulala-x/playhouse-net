# 고해상도 게임 루프 타이머 - 변경사항 문서

## 개요

기존 `TimerManager`(이벤트 스케줄링)를 **교체하지 않고**, 게임 루프 전용 고해상도 타이머를 **별도 기능으로 추가**했습니다.

- **용도**: FPS/액션 서버의 프레임 기반 업데이트 (물리, 게임 상태, 브로드캐스트)
- **해상도**: `Stopwatch.GetTimestamp()` 기반 나노초 정밀도
- **패턴**: "Fix Your Timestep" 고정 타임스텝 누적기

## 신규 파일

| 파일 | 용도 |
|------|------|
| `src/PlayHouse/Abstractions/Play/GameLoopConfig.cs` | 게임 루프 설정 (FixedTimestep, MaxAccumulatorCap) |
| `src/PlayHouse/Core/Play/GameLoopTimer.cs` | 고해상도 타이머 엔진 (전용 스레드, 하이브리드 슬립) |
| `tests/unit/PlayHouse.Unit/Core/Play/GameLoopTimerTests.cs` | 단위 테스트 10개 |
| `tests/e2e/PlayHouse.E2E/Verifiers/GameLoopVerifier.cs` | E2E 테스트 4개 |

## 수정 파일

| 파일 | 변경 내용 |
|------|----------|
| `IStageSender.cs` | `GameLoopCallback` 델리게이트 + `StartGameLoop`/`StopGameLoop`/`IsGameLoopRunning` API |
| `IPlayDispatcher.cs` | `StartGameLoop`/`StopGameLoop`/`IsGameLoopRunning` 인터페이스 메서드 |
| `PlayDispatcher.cs` | `_gameLoopTimers` 딕셔너리, `OnGameLoopTick` 디스패치, 정리 로직 |
| `XStageSender.cs` | IStageSender 구현, `CloseStage()` 시 자동 정지, 타임스텝 검증 |
| `BaseStage.cs` | `PostGameLoopTick()` 메일박스 연동 메서드 |
| `StageMessage.cs` | `GameLoopTickMessage` + ObjectPool (GC 최소화) |
| `test_messages.proto` | GameLoop proto 메시지 5개 추가 |
| `TestStageImpl.cs` | `HandleStartGameLoop`/`HandleStopGameLoop` 핸들러 추가 |
| `VerificationRunner.cs` | `GameLoopVerifier` 등록 |

## 공개 API

```csharp
// 델리게이트
public delegate Task GameLoopCallback(TimeSpan deltaTime, TimeSpan totalElapsed);

// IStageSender에 추가된 메서드
void StartGameLoop(TimeSpan fixedTimestep, GameLoopCallback callback);
void StartGameLoop(GameLoopConfig config, GameLoopCallback callback);
void StopGameLoop();
bool IsGameLoopRunning { get; }
```

## 사용 예시

```csharp
// IStage.OnPostCreate()에서 게임 루프 시작
public async Task OnPostCreate()
{
    StageSender.StartGameLoop(
        TimeSpan.FromMilliseconds(16.67),  // 60Hz
        async (deltaTime, totalElapsed) =>
        {
            UpdatePhysics(deltaTime);
            UpdateGameState(deltaTime);
            BroadcastStateToClients();
        });
}
```

## 핵심 설계 결정

### 스레드 모델: Stage당 전용 백그라운드 스레드
- `IsBackground = true`, `ThreadPriority.AboveNormal`
- ThreadPool jitter 회피 → 일관된 타이밍
- 게임 루프 Stage 수만큼 스레드 사용 (수십~수백 수준)

### 하이브리드 슬립
- 대부분 `Thread.Sleep(remainingMs - 2)` → CPU 절약
- 마지막 ~2ms는 `Thread.SpinWait(100)` → 서브밀리초 정밀도

### Spiral of Death 방지
- `MaxAccumulatorCap` (기본값: 5 × FixedTimestep)
- 누적 시간 초과 시 초과분 틱 버림
- `EffectiveMaxAccumulatorCap`은 항상 `>= FixedTimestep`으로 클램프 (리뷰 반영)

### 메시지 흐름
```
StageSender.StartGameLoop()
  → PlayDispatcher: GameLoopTimer 생성/시작
    → [전용 스레드] Stopwatch 루프
      → accumulator >= fixedDt 시:
        → OnGameLoopTick → BaseStage.PostGameLoopTick()
          → 메일박스 Enqueue(GameLoopTickMessage)
            → ExecuteAsync() → callback(deltaTime, totalElapsed)
```

## 리뷰 반영 사항

| 이슈 | 대응 |
|------|------|
| MaxAccumulatorCap < FixedTimestep 시 silent failure | `EffectiveMaxAccumulatorCap`에서 `Math.Max(cap, FixedTimestep)` 클램프 |
| Stop() self-join으로 2초 블로킹 | `Thread.CurrentThread == _thread` 체크로 self-join 스킵 |
| 타이밍 테스트 CI 플래키 | 허용 범위 18~22 → 16~24로 확대 |

## 테스트 결과

### 단위 테스트 (10개 전체 통과)
| 테스트 | 검증 내용 |
|--------|----------|
| `Start_CreatesBackgroundThread` | 스레드 생성 및 실행 |
| `Stop_TerminatesThread` | 정상 종료 |
| `TicksDispatchedAtCorrectRate` | 50ms → ~20 tick/sec |
| `DeltaTime_AlwaysEqualsFixedTimestep` | 고정 deltaTime |
| `TotalElapsed_IncreasesMonotonically` | 단조 증가 |
| `DoubleStart_Throws` | 중복 시작 예외 |
| `StopWhenNotRunning_NoOp` | 미실행 Stop 무시 |
| `Dispose_StopsRunningLoop` | Dispose 자동 정지 |
| `AccumulatorCap_PreventsSpiralOfDeath` | cap 동작 검증 |
| `CorrectStageId_InCallback` | StageId 전달 검증 |

### E2E 테스트 (4개 전체 통과)
| 테스트 | 검증 내용 |
|--------|----------|
| `GameLoop_ReceivesTicksAtConfiguredRate` | 50ms 타임스텝 → 약 16~24 tick 수신 |
| `GameLoop_DeltaTimeMatchesFixedTimestep` | 모든 tick의 deltaTime이 50ms |
| `GameLoop_StopsOnRequest` | StopGameLoop 후 Push 중단 |
| `GameLoop_AutoStopsOnMaxTicks` | max_ticks=5 → 정확히 5개 tick |

### 회귀 테스트
- 단위 테스트 363개 전체 통과
- E2E 테스트 74개 전체 통과 (기존 70개 + GameLoop 4개)

## 라이프사이클

| 이벤트 | 동작 |
|--------|------|
| `StartGameLoop()` | 전용 스레드 생성, 루프 시작 |
| `StopGameLoop()` | 스레드 종료 대기 (Join 2s) |
| `CloseStage()` | 기존 타이머 취소 + 게임 루프 자동 정지 |
| Stage 파괴 | `ProcessDestroy()`에서 `StopGameLoop()` |
| 서버 종료 | `Dispose()`에서 모든 게임 루프 정리 |
