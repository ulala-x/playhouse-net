# PlayHouse-NET 이벤트 루프 및 메시징 처리

## 1. 개요

PlayHouse-NET의 핵심 동시성 모델은 **Stage별 독립 이벤트 루프**입니다. 각 Stage는 자체 메시지 큐와 비동기 Task 기반 이벤트 루프를 가지며, 이를 통해 Lock-Free 방식의 순차적 메시지 처리를 보장합니다.

### 1.1 핵심 목표

| 목표 | 설명 |
|------|------|
| **순차성 보장** | Stage 내 메시지는 FIFO 순서로 처리 |
| **Lock-Free** | CAS 기반 동시성 제어, Lock/Mutex 없음 |
| **async/await 지원** | Stage 핸들러에서 비동기 코드 자연스럽게 작성 |
| **Room간 병렬성** | 서로 다른 Stage는 완전히 독립적으로 병렬 실행 |

### 1.2 설계 철학

```
핵심 원칙:
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

1. 하나의 Stage = 하나의 async Task 컨텍스트
   - await 전후에도 같은 논리적 실행 흐름 유지
   - Thread가 바뀌어도 순차성 보장

2. 메시지 큐 + CAS = Lock-Free 진입점
   - 여러 Thread에서 동시에 Post() 가능
   - 실제 처리는 단일 async Task에서만

3. Lazy Task 생성
   - 메시지가 있을 때만 Task 실행
   - 유휴 Stage는 리소스 0 소비
```

## 2. 이벤트 루프 아키텍처

### 2.1 전체 구조

```
┌─────────────────────────────────────────────────────────────┐
│                     PlayDispatcher                           │
│                                                              │
│  ┌────────────┐  ┌────────────┐  ┌────────────┐            │
│  │  Stage A   │  │  Stage B   │  │  Stage C   │   ...      │
│  │            │  │            │  │            │            │
│  │ ┌────────┐ │  │ ┌────────┐ │  │ ┌────────┐ │            │
│  │ │ Queue  │ │  │ │ Queue  │ │  │ │ Queue  │ │            │
│  │ └───┬────┘ │  │ └───┬────┘ │  │ └───┬────┘ │            │
│  │     │      │  │     │      │  │     │      │            │
│  │ ┌───▼────┐ │  │ ┌───▼────┐ │  │ ┌───▼────┐ │            │
│  │ │ Event  │ │  │ │ Event  │ │  │ │ Event  │ │            │
│  │ │ Loop   │ │  │ │ Loop   │ │  │ │ Loop   │ │            │
│  │ │(async) │ │  │ │(async) │ │  │ │(async) │ │            │
│  │ └────────┘ │  │ └────────┘ │  │ └────────┘ │            │
│  └────────────┘  └────────────┘  └────────────┘            │
│                                                              │
│       ▲               ▲               ▲                     │
│       │               │               │                     │
│       └───────────────┴───────────────┘                     │
│                       │                                      │
│              외부 Thread들 (Socket I/O, Timer, HTTP)         │
└─────────────────────────────────────────────────────────────┘
```

### 2.2 Stage 단위 격리

```
Stage A                          Stage B
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━    ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
독립된 상태 (_players, _state)    독립된 상태 (_players, _state)
독립된 메시지 큐                   독립된 메시지 큐
독립된 async Task                 독립된 async Task
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━    ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

→ Stage A와 B는 완전히 병렬 실행 가능
→ 서로의 상태에 직접 접근 불가 (메시지로만 통신)
```

## 3. 핵심 구현: CAS + async Task 패턴

### 3.1 기본 메커니즘

```csharp
internal class BaseStage
{
    private readonly ConcurrentQueue<RoutePacket> _msgQueue = new();
    private readonly AtomicBoolean _isUsing = new(false);

    /// <summary>
    /// 외부에서 메시지를 Stage에 전달 (Lock-Free)
    /// </summary>
    public void Post(RoutePacket routePacket)
    {
        // 1. 메시지를 큐에 추가 (Lock-Free, 여러 Thread 동시 가능)
        _msgQueue.Enqueue(routePacket);

        // 2. CAS로 이벤트 루프 시작 여부 결정
        if (_isUsing.CompareAndSet(false, true))
        {
            // 3. 이벤트 루프 시작 (하나의 async Task)
            Task.Run(async () =>
            {
                // 4. 큐가 빌 때까지 순차 처리
                while (_msgQueue.TryDequeue(out var item))
                {
                    try
                    {
                        using (item)
                        {
                            await Dispatch(item);  // await 가능!
                        }
                    }
                    catch (Exception e)
                    {
                        HandleError(e);
                    }
                }

                // 5. 모든 메시지 처리 완료, 플래그 해제
                _isUsing.Set(false);
            });
        }
        // CAS 실패 = 이미 이벤트 루프 실행 중 = 큐에만 추가됨
    }
}
```

### 3.2 동작 원리 상세

```
시나리오: Thread A, B, C가 동시에 Stage에 메시지 전송
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

t=0  [초기 상태]
     _isUsing = false
     _msgQueue = []

t=1  [Thread A: Post(msg1)]
     _msgQueue.Enqueue(msg1)     → [msg1]
     CAS(false→true) 성공!
     Task.Run() 시작 → EventLoop Task 생성

t=2  [Thread B: Post(msg2)]  (동시에 발생)
     _msgQueue.Enqueue(msg2)     → [msg1, msg2]
     CAS(false→true) 실패 (이미 true)
     → Task.Run() 호출 안함 (리턴)

t=3  [Thread C: Post(msg3)]  (동시에 발생)
     _msgQueue.Enqueue(msg3)     → [msg1, msg2, msg3]
     CAS(false→true) 실패 (이미 true)
     → Task.Run() 호출 안함 (리턴)

t=4  [EventLoop Task]
     TryDequeue() → msg1
     await Dispatch(msg1)        ← 비동기 처리 가능

t=5  [EventLoop Task 계속]
     TryDequeue() → msg2
     await Dispatch(msg2)

t=6  [EventLoop Task 계속]
     TryDequeue() → msg3
     await Dispatch(msg3)

t=7  [EventLoop Task]
     TryDequeue() → false (큐 비어있음)
     _isUsing.Set(false)         ← 이벤트 루프 종료
     Task 완료
```

### 3.3 async/await가 동작하는 이유

```
핵심 포인트: 하나의 async Task 내에서 await
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

Task.Run(async () =>           ← 단일 async 컨텍스트 시작
{
    while (_msgQueue.TryDequeue(out var item))
    {
        await Dispatch(item);   ← await 해도 같은 Task 컨텍스트
        // await 이후에도 while 루프 계속
        // 다음 TryDequeue는 순차적으로 실행됨
    }
});

┌─────────────────────────────────────────────────────────┐
│                    async Task 컨텍스트                   │
│                                                          │
│  ┌──────────┐     ┌──────────┐     ┌──────────┐        │
│  │Dispatch  │────▶│  await   │────▶│Dispatch  │        │
│  │ (msg1)   │     │ DB호출   │     │ (msg1)   │        │
│  │          │     │          │     │ 계속...  │        │
│  └──────────┘     └──────────┘     └──────────┘        │
│       │                                   │             │
│       ▼                                   ▼             │
│  ┌──────────┐                       ┌──────────┐       │
│  │Dispatch  │                       │  while   │       │
│  │ (msg2)   │                       │  종료    │       │
│  └──────────┘                       └──────────┘       │
│                                                          │
└─────────────────────────────────────────────────────────┘

→ Thread가 바뀌어도 논리적 순서는 보장됨
→ msg1 처리 완료 후에야 msg2 처리 시작
```

### 3.4 Worker Pool과의 비교

```
Worker Pool 방식 (async/await 문제 있음)
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

void WorkerLoop()  // 동기 메서드
{
    while (true)
    {
        var msg = _queue.Take();      // blocking
        stage.Dispatch(msg);          // await 불가능!
    }
}

문제:
- Worker Thread가 고정됨
- await 하면 Thread 반환 → 다른 Thread에서 재개될 수 있음
- Stage 상태 보호 불가


CAS + async Task 방식 (PlayHouse 채택)
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

Task.Run(async () =>  // async 메서드
{
    while (_msgQueue.TryDequeue(out var item))
    {
        await Dispatch(item);    // await 가능!
    }
});

장점:
- async Task 컨텍스트 내에서 await
- Thread가 바뀌어도 순차성 보장
- 자연스러운 비동기 코드 작성
```

## 4. 메시지 처리 흐름

### 4.1 외부 → Stage 메시지 전달

```
[Socket I/O Thread]              [Stage Event Loop]
       │                                │
       │ 패킷 수신                        │
       ▼                                │
  ┌─────────┐                           │
  │ Parse   │                           │
  │ Packet  │                           │
  └────┬────┘                           │
       │                                │
       ▼                                │
  ┌─────────────┐                       │
  │PlayDispatcher│                      │
  │  .OnPost()  │                       │
  └──────┬──────┘                       │
         │                              │
         │ stageId로 Stage 찾기          │
         ▼                              │
  ┌─────────────┐                       │
  │ stage.Post()│─────────────────────▶ │
  │   (CAS)     │    Enqueue + 트리거    │
  └─────────────┘                       │
                                        ▼
                                  ┌─────────────┐
                                  │  Dispatch() │
                                  │   await OK  │
                                  └─────────────┘
```

### 4.2 Dispatch 메서드 구조

```csharp
private async Task Dispatch(RoutePacket routePacket)
{
    // 현재 패킷 컨텍스트 설정
    StageLink.SetCurrentPacketHeader(routePacket.RouteHeader);

    try
    {
        if (routePacket.IsBase())
        {
            // 시스템 메시지 (CreateStage, JoinStage, Timer 등)
            await _msgHandler.Dispatch(this, routePacket);
        }
        else
        {
            // 사용자 메시지 → Stage.OnDispatch() 호출
            var accountId = routePacket.AccountId;
            var baseUser = _dispatcher.FindUser(accountId);

            if (baseUser != null)
            {
                // Actor 컨텍스트와 함께 디스패치
                await _stage!.OnDispatch(
                    baseUser.Actor,
                    CPacket.Of(routePacket.MsgId, routePacket.Payload)
                );
            }
        }
    }
    catch (Exception e)
    {
        // 에러 응답
        StageLink.Reply((ushort)BaseErrorCode.SystemError);
        _log.Error(() => $"{e}");
    }
    finally
    {
        // 컨텍스트 정리
        StageLink.ClearCurrentPacketHeader();
    }
}
```

### 4.3 메시지 타입별 처리

```
┌─────────────────────────────────────────────────────────────┐
│                    메시지 분류 및 처리                        │
├─────────────────────────────────────────────────────────────┤
│                                                              │
│  IsBase() == true (시스템 메시지)                            │
│  ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━                      │
│  │                                                           │
│  ├─ CreateStageReq  → Stage 생성                            │
│  ├─ JoinStageReq    → Actor 입장                            │
│  ├─ CreateJoinStageReq → 생성 + 입장                        │
│  ├─ StageTimer      → 타이머 콜백 실행                       │
│  ├─ DisconnectNoticeMsg → 연결 끊김 알림                    │
│  └─ AsyncBlock      → 비동기 블록 결과 처리                  │
│                                                              │
│  IsBase() == false (사용자 메시지)                           │
│  ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━                      │
│  │                                                           │
│  └─ Stage.OnDispatch(actor, packet)                         │
│     → 게임 로직에서 처리                                     │
│                                                              │
└─────────────────────────────────────────────────────────────┘
```

## 5. 타이머와 이벤트 루프 통합

### 5.1 타이머 동작 원리

```
[Timer 등록]
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

Stage 내부:
    StageLink.AddRepeatTimer(interval, callback);
                    │
                    ▼
            ┌─────────────┐
            │TimerManager │  System.Threading.Timer 등록
            └──────┬──────┘
                   │
                   │  interval 경과
                   ▼
            ┌─────────────┐
            │Timer Thread │  타이머 만료
            └──────┬──────┘
                   │
                   ▼
            ┌─────────────┐
            │ StageTimer  │  메시지로 래핑
            │   Packet    │
            └──────┬──────┘
                   │
                   ▼
            ┌─────────────┐
            │ stage.Post()│  Stage 메시지 큐로 전달
            └──────┬──────┘
                   │
                   ▼
            ┌─────────────────────────────────┐
            │     Stage Event Loop            │
            │                                  │
            │  while (TryDequeue)             │
            │  {                              │
            │      if (StageTimer)            │
            │          await callback();  ✓   │  Stage 컨텍스트에서 실행!
            │  }                              │
            └─────────────────────────────────┘
```

### 5.2 타이머 콜백의 안전성

```csharp
// 타이머 콜백은 Stage 이벤트 루프에서 실행됨
private async Task OnGameTick()
{
    // 이 코드는 Stage의 async Task 내에서 실행
    // → Stage 상태에 안전하게 접근 가능

    foreach (var player in _players.Values)
    {
        player.UpdatePosition();  // 안전!
    }

    // await도 안전
    await BroadcastGameState();
}
```

## 6. 동시성 보장 분석

### 6.1 Race Condition 방지

```
시나리오: msg1 처리 중 msg2 도착
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

t=0  Event Loop: await Dispatch(msg1)... (I/O 대기 중)
     _isUsing = true
     _msgQueue = []

t=1  Thread X: Post(msg2)
     _msgQueue.Enqueue(msg2)  → [msg2]
     CAS(false→true) 실패!    (이미 true)
     → 리턴 (Task.Run 호출 안함)

t=2  Event Loop: msg1 await 완료
     while 계속...
     TryDequeue() → msg2
     await Dispatch(msg2)

결과: msg1 → msg2 순차 처리 보장 ✓
      Race Condition 없음 ✓
```

### 6.2 Edge Case: 큐 비움 직후 메시지 도착

```
시나리오: while 종료 직전 메시지 도착
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

t=0  Event Loop: TryDequeue() → false (큐 비어있음)
     // while 조건 체크 완료, 루프 종료 직전

t=1  Thread X: Post(msg)
     _msgQueue.Enqueue(msg)   → [msg]
     CAS(false→true)...

t=2  Event Loop: _isUsing.Set(false)  // ← 여기가 먼저 실행

t=3  Thread X: CAS 성공! (false→true)
     Task.Run() 시작 → 새 Event Loop

결과: msg는 새 Event Loop에서 처리됨 ✓
      메시지 유실 없음 ✓


반대 시나리오: CAS가 먼저 체크되는 경우
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

t=0  Event Loop: TryDequeue() → false

t=1  Thread X: Post(msg)
     _msgQueue.Enqueue(msg)   → [msg]
     CAS(false→true) 실패!    (아직 true)

t=2  Event Loop: _isUsing.Set(false)

문제? → 아니요!
Event Loop이 종료되기 전에 while 조건을 다시 체크하지 않으므로,
다음 Post() 호출 시 새 Event Loop이 시작되어 처리됨

단, 이 구현에는 미묘한 타이밍 이슈 가능성 있음
→ 해결책: Double-Check 패턴 (아래 참조)
```

### 6.3 개선된 구현 (Double-Check)

```csharp
public void Post(RoutePacket routePacket)
{
    _msgQueue.Enqueue(routePacket);

    if (_isUsing.CompareAndSet(false, true))
    {
        Task.Run(async () =>
        {
            do
            {
                while (_msgQueue.TryDequeue(out var item))
                {
                    using (item)
                    {
                        await Dispatch(item);
                    }
                }

                _isUsing.Set(false);

                // Double-Check: Set 직후 큐에 새 메시지 있는지 확인
                // 있으면 다시 CAS 시도하여 처리 계속
            } while (!_msgQueue.IsEmpty && _isUsing.CompareAndSet(false, true));
        });
    }
}
```

## 7. 성능 특성

### 7.1 메모리 효율성

```
Stage 상태별 리소스 사용:
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

[Idle Stage] (메시지 없음)
  - ConcurrentQueue: ~72 bytes (빈 큐)
  - AtomicBoolean: 4 bytes
  - Task: 없음 (null)
  - 총: ~100 bytes 미만

[Active Stage] (메시지 처리 중)
  - ConcurrentQueue: 72 + (메시지 수 × 참조 크기)
  - AtomicBoolean: 4 bytes
  - Task: ~100 bytes (async state machine 포함)
  - 총: 가변 (메시지 수에 비례)

→ 1000개 Stage, 100개만 활성 = 약 10KB 오버헤드
```

### 7.2 처리량 특성

```
단일 Stage 처리량:
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

[CPU-bound 메시지] (await 없음)
  - 1개 메시지 처리: ~1-10μs
  - 초당 처리량: 100K-1M msg/sec

[I/O-bound 메시지] (await 있음)
  - await 중 다른 Stage 처리 가능
  - Thread 반환으로 높은 동시성
  - 처리량은 I/O 속도에 의존

다중 Stage 병렬 처리:
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

[Stage 100개, 모두 활성]
  - ThreadPool이 자동으로 Thread 할당
  - CPU 코어 수만큼 병렬 실행
  - 8코어 시스템: 8개 Stage 동시 처리
```

### 7.3 vs Worker Pool 비교

```
┌─────────────────────┬────────────────────┬────────────────────┐
│        측면         │  CAS + async Task  │    Worker Pool     │
├─────────────────────┼────────────────────┼────────────────────┤
│ 메모리 (idle)       │ 낮음 (Task 없음)   │ 높음 (Worker 대기) │
│ 메모리 (active)     │ 가변              │ 고정               │
│ async/await         │ ✅ 지원           │ ❌ 제한적          │
│ 구현 복잡도         │ 낮음              │ 중간               │
│ Context Switch      │ 높을 수 있음      │ 낮음               │
│ 확장성              │ 자동 (ThreadPool) │ 수동 튜닝 필요     │
│ 게임 서버 적합성    │ ✅ 높음           │ ⚠️ 상황에 따라    │
└─────────────────────┴────────────────────┴────────────────────┘
```

## 8. 요약

### 8.1 핵심 포인트

```
PlayHouse-NET 이벤트 루프 모델
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

1. CAS + async Task = Lock-Free + async/await 지원
   - ConcurrentQueue로 Lock-Free 메시지 큐
   - AtomicBoolean (CAS)로 이벤트 루프 진입 제어
   - 단일 async Task 컨텍스트에서 순차 처리

2. Stage별 독립 이벤트 루프
   - 각 Stage는 자체 큐와 Task
   - Stage간 완전 병렬 실행
   - 공유 상태 없음

3. Lazy Task 생성
   - 메시지 없으면 Task 없음
   - 메모리/CPU 효율적
   - 자동 스케일링

4. 타이머도 메시지로 통합
   - 타이머 콜백은 Stage 이벤트 루프에서 실행
   - 상태 접근 안전
```

### 8.2 설계 결정 근거

| 결정 | 근거 |
|------|------|
| CAS vs Lock | Lock-Free로 높은 동시성, 데드락 방지 |
| async Task vs Worker Pool | async/await 지원 필수, 자연스러운 코드 작성 |
| Stage별 격리 | 공유 상태 제거, Race Condition 원천 차단 |
| 메시지 기반 통신 | 느슨한 결합, 테스트 용이, 확장성 |

## 9. 다음 단계

- `03-stage-actor-model.md`: Stage/Actor 상세 동작
- `04-timer-system.md`: 타이머 시스템 상세
- `10-testing-spec.md`: 테스트 전략
