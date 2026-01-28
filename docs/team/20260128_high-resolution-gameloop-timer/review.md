# 고해상도 게임 루프 타이머 리뷰

## 주요 이슈 (심각도 순)

1) **MaxAccumulatorCap 검증 누락으로 틱이 영구적으로 멈출 수 있음**
   - 위치: `src/PlayHouse/Abstractions/Play/GameLoopConfig.cs:14-27`, `src/PlayHouse/Core/Play/XStageSender.cs:253-273`, `src/PlayHouse/Core/Play/GameLoopTimer.cs:99-133`
   - `MaxAccumulatorCap`이 `FixedTimestep`보다 작거나 음수인 경우, `accumulatorTicks`가 `fixedDtTicks`에 도달하지 못해 `while (accumulatorTicks >= fixedDtTicks)`가 영구히 실행되지 않습니다. 결과적으로 게임 루프가 시작돼도 콜백이 **한 번도 호출되지 않는 silent failure**가 발생합니다.
   - **제안**: `StartGameLoop(GameLoopConfig)`에서 `MaxAccumulatorCap >= FixedTimestep` 및 `> 0` 검증 후 예외 처리하거나, `EffectiveMaxAccumulatorCap`에서 `Math.Max(MaxAccumulatorCap.Value.Ticks, FixedTimestep.Ticks)`로 보정하세요.

2) **게임 루프 스레드에서 StopGameLoop 호출 시 self-join으로 지연/교착 위험**
   - 위치: `src/PlayHouse/Core/Play/PlayDispatcher.cs:432-442`, `src/PlayHouse/Core/Play/GameLoopTimer.cs:84-90`
   - Stage가 이미 제거된 상태에서 `OnGameLoopTick`이 `StopGameLoop`를 호출하면, **게임 루프 스레드가 자기 자신을 `Join(2s)`** 하게 됩니다. 현재는 타임아웃으로 2초 블로킹이지만, Stop 경로가 불필요하게 지연되고 향후 Join 정책이 바뀌면 교착 위험이 생깁니다.
   - **제안**: `Stop()`에서 `Thread.CurrentThread == _thread`일 경우 Join을 생략하거나, `CancellationToken/ManualResetEvent`로 슬립을 깨워 즉시 종료하도록 개선하세요.

3) **Stop 이후에도 큐에 쌓인 틱이 실행됨 (의도 여부 확인 필요)**
   - 위치: `src/PlayHouse/Core/Play/Base/BaseStage.cs:184-194`, `src/PlayHouse/Core/Play/Base/StageMessage.cs:134-147`
   - `StopGameLoop()`는 타이머 스레드만 중단하며 이미 메일박스에 enqueue된 `GameLoopTickMessage`는 계속 처리됩니다. 문서 상 “Stop = 중단” 기대와 다를 수 있고, Stage 종료 직후에도 틱이 실행될 수 있습니다.
   - **제안**: BaseStage에 루프 세대/플래그를 두어 중단 이후의 틱을 무시하거나, Stop 시점에 tick 메시지를 drop하는 정책을 명확히 하세요.

## 개선 제안 (선택)

- **타이밍 기반 테스트의 플래키 가능성**
  - 위치: `tests/unit/PlayHouse.Unit/Core/Play/GameLoopTimerTests.cs:85-105`
  - CPU 부하/CI 환경에서는 1초 동안 18~22틱 보장이 흔들릴 수 있습니다. 느린 환경에서 16~17틱까지 떨어질 가능성도 있습니다.
  - **제안**: 허용 범위를 넓히거나, fake clock/controlled scheduler를 도입해 시간 의존도를 낮추세요.

- **StageSender 타이머 ID 누수 가능성**
  - 위치: `src/PlayHouse/Core/Play/XStageSender.cs:140-197`
  - Count 타이머가 만료돼도 `_timerIds`에서 제거되지 않아 `HasTimer`가 부정확해지고, 장기 실행 시 HashSet이 누적될 수 있습니다.
  - **제안**: TimerManager에서 만료 시 `OnTimerRemoved`를 호출하거나, TimerMessage 처리 시 StageSender에 정리 신호를 추가하세요.

## 확인 질문

- `MaxAccumulatorCap`의 의도된 규칙은 **FixedTimestep 이상**이 맞나요? (클램프 vs 예외 중 어떤 정책이 적합한지 확인 필요)
- `StopGameLoop()` 이후에도 이미 큐에 쌓인 틱이 실행되는 것이 **허용된 동작**인가요? (문서/계약 명확화 필요)

## 간단 요약

- 고해상도 게임 루프의 구조(전용 스레드 → Stage 메일박스)는 설계와 잘 맞습니다.
- **MaxAccumulatorCap 검증 누락**과 **self-join Stop**은 실제 동작에 영향을 줄 수 있어 우선 보완이 필요합니다.
- 테스트/타이머 정리 부분은 안정성 개선 관점에서 보강을 권장합니다.
