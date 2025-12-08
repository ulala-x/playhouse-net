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
    StageSender.SetCurrentPacketHeader(routePacket.RouteHeader);

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
        StageSender.Reply((ushort)BaseErrorCode.SystemError);
        _log.Error(() => $"{e}");
    }
    finally
    {
        // 컨텍스트 정리
        StageSender.ClearCurrentPacketHeader();
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
    StageSender.AddRepeatTimer(interval, callback);
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

## 7. 비동기 패턴 가이드

### 7.1 권장 패턴

```csharp
// ✅ 좋은 예: Stage 핸들러에서 async/await 사용
public async ValueTask OnDispatch(IActor actor, IPacket packet)
{
    if (packet.MsgId == "SaveGame")
    {
        // DB 저장 (await 가능)
        await _database.SaveAsync(actor.AccountId, _gameState);

        // await 이후에도 Stage 상태 안전하게 접근
        _lastSaveTime = DateTime.UtcNow;

        // 응답
        StageSender.Reply(new SaveGameRes { Success = true });
    }
}

// ✅ 좋은 예: 여러 비동기 작업 순차 실행
public async Task ProcessComplexLogic()
{
    // 1단계
    var data1 = await FetchDataAsync();

    // 2단계 (1단계 완료 후)
    var data2 = await ProcessDataAsync(data1);

    // 3단계 (2단계 완료 후) - 상태 업데이트
    _state.Update(data2);  // 안전!
}
```

### 7.2 주의 패턴

```csharp
// ⚠️ 주의: Task.Run 내에서 Stage 상태 접근
public void BadPattern()
{
    Task.Run(async () =>
    {
        var data = await FetchDataAsync();
        _state.Update(data);  // 위험! Stage 이벤트 루프 밖에서 실행
    });
}

// ✅ 해결: AsyncBlock 사용
public void GoodPattern()
{
    StageSender.AsyncBlock(
        preCallback: async () =>
        {
            // 별도 Thread에서 실행 (Stage 상태 접근 금지)
            return await FetchDataAsync();
        },
        postCallback: async (result) =>
        {
            // Stage 이벤트 루프에서 실행 (안전)
            _state.Update(result);
        }
    );
}
```

### 7.3 병렬 비동기 작업

```csharp
// ✅ 좋은 예: 병렬 I/O 후 순차 상태 업데이트
public async Task LoadAllDataAsync()
{
    // 병렬로 데이터 로드
    var task1 = _db.LoadPlayerAsync(accountId);
    var task2 = _db.LoadInventoryAsync(accountId);
    var task3 = _db.LoadQuestsAsync(accountId);

    // 모두 완료 대기
    await Task.WhenAll(task1, task2, task3);

    // 순차적으로 상태 업데이트 (Stage 컨텍스트에서 안전)
    _player = task1.Result;
    _inventory = task2.Result;
    _quests = task3.Result;
}
```

## 8. 성능 특성

### 8.1 메모리 효율성

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

### 8.2 처리량 특성

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

### 8.3 vs Worker Pool 비교

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

## 9. 실전 코드 예제

### 9.1 완전한 Stage 구현

```csharp
public class GameStage : IStage
{
    private readonly Dictionary<long, GameActor> _players = new();
    private GameState _state = GameState.Waiting;
    private int _tickCount = 0;

    public required IStageSender StageSender { get; init; }

    public async Task<(ushort, IPacket?)> OnCreate(IPacket packet)
    {
        var config = packet.Parse<CreateStageReq>();
        _state = GameState.Initializing;

        // 비동기 초기화 (안전)
        await LoadStageDataAsync(config.StageType);

        _state = GameState.Waiting;
        return (0, new SimplePacket(new CreateStageRes { Success = true }));
    }

    public async Task OnPostCreate()
    {
        // 게임 틱 타이머 등록
        StageSender.AddRepeatTimer(
            TimeSpan.FromMilliseconds(100),
            OnGameTick  // Stage 이벤트 루프에서 실행됨
        );
    }

    /// <summary>
    /// 타이머 콜백 - Stage 이벤트 루프에서 안전하게 실행
    /// </summary>
    private async Task OnGameTick()
    {
        _tickCount++;

        if (_state != GameState.Playing) return;

        // 모든 플레이어 위치 업데이트 (안전)
        foreach (var player in _players.Values)
        {
            player.UpdatePosition();
        }

        // 주기적 브로드캐스트
        if (_tickCount % 10 == 0)
        {
            await BroadcastGameState();
        }
    }

    /// <summary>
    /// 메시지 핸들러 - async/await 자유롭게 사용 가능
    /// </summary>
    public async ValueTask OnDispatch(IActor actor, IPacket packet)
    {
        var player = (GameActor)actor;

        switch (packet.MsgId)
        {
            case "PlayerMove":
                await HandlePlayerMove(player, packet);
                break;

            case "SaveGame":
                await HandleSaveGame(player, packet);
                break;
        }
    }

    private async Task HandlePlayerMove(GameActor player, IPacket packet)
    {
        var move = packet.Parse<PlayerMoveMsg>();

        // 상태 업데이트 (안전)
        player.X = move.X;
        player.Y = move.Y;

        // 다른 플레이어에게 브로드캐스트
        await StageSender.BroadcastAsync(
            new SimplePacket(new PlayerMovedNotify
            {
                AccountId = player.ActorSender.AccountId,
                X = move.X,
                Y = move.Y
            }),
            a => a.ActorSender.AccountId != player.ActorSender.AccountId
        );
    }

    private async Task HandleSaveGame(GameActor player, IPacket packet)
    {
        // DB 저장 (await 가능, 안전)
        await _database.SavePlayerAsync(player.AccountId, player.GetSaveData());

        // 저장 완료 후 상태 업데이트 (Stage 컨텍스트, 안전)
        player.LastSaveTime = DateTime.UtcNow;

        StageSender.Reply(new SimplePacket(new SaveGameRes { Success = true }));
    }

    private async Task BroadcastGameState()
    {
        var state = new GameStateNotify();

        foreach (var player in _players.Values)
        {
            state.Players.Add(new PlayerState
            {
                AccountId = player.ActorSender.AccountId,
                X = player.X,
                Y = player.Y,
                Health = player.Health
            });
        }

        await StageSender.BroadcastAsync(new SimplePacket(state));
    }

    private async Task LoadStageDataAsync(string stageType)
    {
        // 비동기 데이터 로드
        var data = await _database.LoadStageConfigAsync(stageType);
        ApplyConfig(data);
    }

    // ... 나머지 IStage 메서드 구현
}
```

### 9.2 AsyncBlock 활용

```csharp
public async ValueTask OnDispatch(IActor actor, IPacket packet)
{
    if (packet.MsgId == "HeavyComputation")
    {
        // 무거운 연산은 별도 Thread에서 실행
        StageSender.AsyncBlock(
            preCallback: async () =>
            {
                // ThreadPool Thread에서 실행
                // Stage 상태 접근 금지!
                var result = await PerformHeavyComputationAsync();
                return result;
            },
            postCallback: async (result) =>
            {
                // Stage 이벤트 루프에서 실행 (안전)
                _computationResult = (ComputationResult)result!;

                await StageSender.BroadcastAsync(
                    new SimplePacket(new ComputationCompleteNotify
                    {
                        Result = _computationResult.Value
                    })
                );
            }
        );

        // 즉시 리턴 (non-blocking)
        StageSender.Reply(new SimplePacket(new HeavyComputationRes
        {
            Status = "Processing"
        }));
    }
}
```

## 10. 트러블슈팅

### 10.1 흔한 실수와 해결

```csharp
// ❌ 실수 1: Stage 밖에서 상태 접근
public void BadExample()
{
    Task.Run(() =>
    {
        _players.Add(123, new Player());  // Race Condition!
    });
}

// ✅ 해결: Post로 메시지 전달
public void GoodExample()
{
    Post(new AddPlayerMessage(123, new Player()));
}


// ❌ 실수 2: 동기 블로킹
public void BadBlockingExample()
{
    var result = _database.LoadSync();  // 이벤트 루프 블로킹!
}

// ✅ 해결: async/await 또는 AsyncBlock 사용
public async Task GoodAsyncExample()
{
    var result = await _database.LoadAsync();  // 안전
}


// ❌ 실수 3: Fire-and-forget async void
public async void BadFireAndForget()  // async void는 예외 처리 불가
{
    await SomethingAsync();
}

// ✅ 해결: async Task 사용
public async Task GoodFireAndForget()
{
    await SomethingAsync();
}
```

### 10.2 디버깅 팁

```csharp
// 이벤트 루프 상태 확인
public void DebugEventLoop()
{
    _log.Debug(() => $"Stage {StageId}: " +
        $"QueueCount={_msgQueue.Count}, " +
        $"IsUsing={_isUsing.Get()}, " +
        $"CurrentThread={Thread.CurrentThread.ManagedThreadId}");
}

// 메시지 처리 시간 측정
private async Task Dispatch(RoutePacket packet)
{
    var sw = Stopwatch.StartNew();

    try
    {
        await _stage!.OnDispatch(...);
    }
    finally
    {
        sw.Stop();
        if (sw.ElapsedMilliseconds > 100)
        {
            _log.Warn(() => $"Slow dispatch: {packet.MsgId} took {sw.ElapsedMilliseconds}ms");
        }
    }
}
```

## 11. 요약

### 11.1 핵심 포인트

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

### 11.2 설계 결정 근거

| 결정 | 근거 |
|------|------|
| CAS vs Lock | Lock-Free로 높은 동시성, 데드락 방지 |
| async Task vs Worker Pool | async/await 지원 필수, 자연스러운 코드 작성 |
| Stage별 격리 | 공유 상태 제거, Race Condition 원천 차단 |
| 메시지 기반 통신 | 느슨한 결합, 테스트 용이, 확장성 |

## 12. 테스트 전략

### 12.1 테스트 피라미드

```
           △
          / \          Unit Tests (20%)
         /   \         ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
        /     \        - 통합 테스트로 커버 어려운 영역만
       /       \       - AtomicBoolean CAS 동작
      /         \      - 타이밍 종속적 엣지 케이스
     /           \
    /_____________\    Integration Tests (80%)
                       ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
                       - 이벤트 루프 핵심 동작 검증
                       - 실제 메시지 흐름 시뮬레이션
                       - Stage 생명주기 전체 검증
```

#### 통합 테스트 우선 이유

| 이유 | 설명 |
|------|------|
| **End-to-End 보장** | Post() → EventLoop → Dispatch() 전체 흐름이 실제로 동작하는지 검증 |
| **Thread Safety 검증** | 다중 Thread 환경에서 순차 처리가 보장되는지 확인 |
| **async/await 동작** | 실제 비동기 작업이 Stage 컨텍스트에서 안전하게 실행되는지 검증 |
| **Timer 통합** | 타이머 콜백이 이벤트 루프를 통해 안전하게 실행되는지 확인 |

### 12.2 통합 테스트 시나리오

#### 카테고리 1: 기본 동작 (Basic Operations)

| Given | When | Then |
|-------|------|------|
| Stage 생성 완료 | 단일 메시지 Post | 메시지가 Dispatch되고 핸들러 실행됨 |
| Stage 생성 완료 | 3개 메시지 순차 Post | FIFO 순서로 처리됨 (msg1 → msg2 → msg3) |
| Stage 유휴 상태 | 메시지 Post | CAS 성공, 새 EventLoop Task 시작 |
| EventLoop 실행 중 | 메시지 Post | CAS 실패, 큐에만 추가됨 |

**테스트 예제**:
```csharp
[Test]
public async Task Post_SingleMessage_ShouldDispatchToHandler()
{
    // Given
    var stage = CreateTestStage();
    var handler = new TestMessageHandler();
    var message = new RoutePacket { MsgId = "TestMsg" };

    // When
    stage.Post(message);
    await Task.Delay(100); // EventLoop 처리 대기

    // Then
    Assert.That(handler.ReceivedMessages, Has.Count.EqualTo(1));
    Assert.That(handler.ReceivedMessages[0].MsgId, Is.EqualTo("TestMsg"));
}

[Test]
public async Task Post_MultipleMessages_ShouldProcessInFIFOOrder()
{
    // Given
    var stage = CreateTestStage();
    var handler = new TestMessageHandler();

    // When
    stage.Post(new RoutePacket { MsgId = "Msg1" });
    stage.Post(new RoutePacket { MsgId = "Msg2" });
    stage.Post(new RoutePacket { MsgId = "Msg3" });
    await Task.Delay(100);

    // Then
    Assert.That(handler.ReceivedMessages.Select(m => m.MsgId),
                Is.EqualTo(new[] { "Msg1", "Msg2", "Msg3" }));
}
```

#### 카테고리 2: 응답 검증 (Response Validation)

| Given | When | Then |
|-------|------|------|
| Stage에서 async DB 호출 | await 후 상태 업데이트 | Stage 상태가 안전하게 변경됨 |
| 메시지 처리 중 예외 발생 | Dispatch 내부에서 throw | 예외가 catch되어 에러 응답 전송, EventLoop 계속됨 |
| AsyncBlock 실행 | preCallback → postCallback | postCallback이 Stage EventLoop에서 실행됨 |

**테스트 예제**:
```csharp
[Test]
public async Task Dispatch_WithAsyncOperation_ShouldMaintainStageContext()
{
    // Given
    var stage = CreateTestStage();
    var handler = new AsyncTestHandler();

    // When
    stage.Post(new RoutePacket { MsgId = "AsyncMsg" });
    await Task.Delay(200); // async 작업 완료 대기

    // Then
    Assert.That(handler.StateUpdatedAfterAwait, Is.True,
                "await 후에도 Stage 상태 업데이트가 안전해야 함");
}

[Test]
public async Task Dispatch_ExceptionInHandler_ShouldNotStopEventLoop()
{
    // Given
    var stage = CreateTestStage();
    var handler = new FaultyTestHandler(); // 첫 메시지는 예외 발생

    // When
    stage.Post(new RoutePacket { MsgId = "FaultyMsg" });
    stage.Post(new RoutePacket { MsgId = "NormalMsg" });
    await Task.Delay(100);

    // Then
    Assert.That(handler.ProcessedMessages, Contains.Item("NormalMsg"),
                "예외 발생 후에도 다음 메시지가 처리되어야 함");
}
```

#### 카테고리 3: 입력 검증 (Input Validation)

| Given | When | Then |
|-------|------|------|
| 5개 Thread에서 동시 Post | 100개 메시지 동시 전송 | 모든 메시지가 유실 없이 순차 처리됨 |
| EventLoop 실행 중 | 큐 비기 직전 메시지 도착 | Double-Check 패턴으로 메시지 처리 보장 |
| Stage 처리 중 | Timer 만료 도착 | Timer 메시지도 순차 처리됨 |

**테스트 예제**:
```csharp
[Test]
public async Task Post_ConcurrentFromMultipleThreads_ShouldProcessAllMessages()
{
    // Given
    var stage = CreateTestStage();
    var handler = new CountingTestHandler();
    var messageCount = 100;
    var threadCount = 5;

    // When
    var tasks = Enumerable.Range(0, threadCount).Select(threadId =>
        Task.Run(() =>
        {
            for (int i = 0; i < messageCount / threadCount; i++)
            {
                stage.Post(new RoutePacket { MsgId = $"Thread{threadId}_Msg{i}" });
            }
        })
    ).ToArray();

    await Task.WhenAll(tasks);
    await Task.Delay(500); // 모든 메시지 처리 대기

    // Then
    Assert.That(handler.ProcessedCount, Is.EqualTo(messageCount),
                "모든 메시지가 유실 없이 처리되어야 함");
}
```

#### 카테고리 4: 엣지 케이스 (Edge Cases)

| Given | When | Then |
|-------|------|------|
| EventLoop가 큐 비움 체크 | Set(false) 직전 메시지 도착 | 새 EventLoop이 시작되어 처리 |
| 메시지 처리 중 오래 걸림 | 100ms 이상 await | 다른 메시지는 대기, 순차성 유지 |
| Stage 종료 중 | 메시지 도착 | 종료 전 모든 메시지 처리 완료 |

**테스트 예제**:
```csharp
[Test]
public async Task Post_MessageArrivesJustBeforeEventLoopFinish_ShouldStartNewEventLoop()
{
    // Given
    var stage = CreateTestStage();
    var handler = new TimingTestHandler();

    // When
    stage.Post(new RoutePacket { MsgId = "Msg1" });
    await Task.Delay(50); // EventLoop이 거의 종료되는 시점
    stage.Post(new RoutePacket { MsgId = "Msg2" }); // 타이밍 엣지 케이스
    await Task.Delay(100);

    // Then
    Assert.That(handler.ReceivedMessages, Has.Count.EqualTo(2),
                "EventLoop 종료 직전 메시지도 처리되어야 함");
}

[Test]
public async Task Dispatch_LongRunningAsyncOperation_ShouldNotBlockOtherStages()
{
    // Given
    var stageA = CreateTestStage("StageA");
    var stageB = CreateTestStage("StageB");
    var handlerA = new SlowTestHandler(delayMs: 1000);
    var handlerB = new FastTestHandler();

    // When
    stageA.Post(new RoutePacket { MsgId = "SlowMsg" });
    await Task.Delay(50);
    stageB.Post(new RoutePacket { MsgId = "FastMsg" });
    await Task.Delay(100);

    // Then
    Assert.That(handlerB.Completed, Is.True,
                "Stage A의 느린 처리가 Stage B를 블로킹하지 않아야 함");
    Assert.That(handlerA.Completed, Is.False,
                "Stage A는 아직 처리 중이어야 함");
}
```

#### 카테고리 5: 활용 예제 (Usage Examples)

| Given | When | Then |
|-------|------|------|
| GameStage 생성 | RepeatTimer 등록 | 타이머 콜백이 주기적으로 EventLoop에서 실행됨 |
| Player 이동 메시지 | OnDispatch에서 상태 업데이트 + Broadcast | 상태 변경 후 다른 플레이어에게 알림 전송 |
| 무거운 연산 요청 | AsyncBlock 사용 | pre는 별도 Thread, post는 EventLoop에서 실행 |

**테스트 예제**:
```csharp
[Test]
public async Task GameStage_TimerCallback_ShouldExecuteInEventLoop()
{
    // Given
    var stage = CreateGameStage();
    var timerCallbackExecuted = 0;

    // When
    stage.RegisterTimer(TimeSpan.FromMilliseconds(100), () =>
    {
        timerCallbackExecuted++;
        // Stage 상태에 안전하게 접근 가능
        stage.UpdateGameState();
        return Task.CompletedTask;
    });

    await Task.Delay(350); // 3번 실행될 시간

    // Then
    Assert.That(timerCallbackExecuted, Is.EqualTo(3),
                "타이머 콜백이 주기적으로 실행되어야 함");
    Assert.That(stage.GameStateUpdateCount, Is.EqualTo(3),
                "타이머 콜백에서 Stage 상태 업데이트가 안전해야 함");
}

[Test]
public async Task GameStage_PlayerMove_ShouldUpdateStateAndBroadcast()
{
    // Given
    var stage = CreateGameStage();
    var player1 = CreateTestPlayer("player1");
    var player2 = CreateTestPlayer("player2");
    await stage.JoinPlayer(player1);
    await stage.JoinPlayer(player2);

    // When
    var moveMsg = new PlayerMoveMsg { X = 100, Y = 200 };
    stage.Post(new RoutePacket
    {
        AccountId = player1.AccountId,
        MsgId = "PlayerMove",
        Payload = Serialize(moveMsg)
    });
    await Task.Delay(100);

    // Then
    Assert.That(player1.X, Is.EqualTo(100), "플레이어 위치가 업데이트되어야 함");
    Assert.That(player2.ReceivedNotifications, Has.Count.EqualTo(1),
                "다른 플레이어에게 이동 알림이 전송되어야 함");
}
```

### 12.3 유닛 테스트 시나리오

> **유닛 테스트 범위**: 통합 테스트로 검증하기 어려운 동시성 제어 메커니즘만 대상으로 함

#### AtomicBoolean CAS 동작 검증

**통합 테스트로 커버 불가능한 이유**:
- CAS 성공/실패는 CPU 레벨의 원자적 연산이며, 타이밍에 극도로 민감함
- 통합 테스트에서는 Task 스케줄링으로 인해 정확한 동시성 시나리오 재현이 어려움
- 특정 Thread 인터리빙 순서를 강제할 수 없음

**테스트 예제**:
```csharp
[TestFixture]
public class AtomicBooleanTests
{
    [Test]
    public void CompareAndSet_InitiallyFalse_ShouldSucceed()
    {
        // Given
        var atomic = new AtomicBoolean(false);

        // When
        var result = atomic.CompareAndSet(false, true);

        // Then
        Assert.That(result, Is.True, "초기값 false일 때 CAS(false→true)는 성공해야 함");
        Assert.That(atomic.Get(), Is.True, "값이 true로 변경되어야 함");
    }

    [Test]
    public void CompareAndSet_ExpectedValueMismatch_ShouldFail()
    {
        // Given
        var atomic = new AtomicBoolean(true);

        // When
        var result = atomic.CompareAndSet(false, true);

        // Then
        Assert.That(result, Is.False, "기대값과 현재값이 다르면 CAS 실패해야 함");
        Assert.That(atomic.Get(), Is.True, "값이 변경되지 않아야 함");
    }

    [Test]
    public void CompareAndSet_ConcurrentAccess_OnlyOneThreadShouldSucceed()
    {
        // Given
        var atomic = new AtomicBoolean(false);
        var successCount = 0;
        var barrier = new Barrier(10);

        // When
        var tasks = Enumerable.Range(0, 10).Select(_ =>
            Task.Run(() =>
            {
                barrier.SignalAndWait(); // 동시 출발 보장
                if (atomic.CompareAndSet(false, true))
                {
                    Interlocked.Increment(ref successCount);
                }
            })
        ).ToArray();

        Task.WaitAll(tasks);

        // Then
        Assert.That(successCount, Is.EqualTo(1),
                    "10개 Thread 중 정확히 1개만 CAS 성공해야 함");
        Assert.That(atomic.Get(), Is.True);
    }
}
```

#### Double-Check 패턴 검증

**통합 테스트로 커버 불가능한 이유**:
- while 루프 종료 직후와 Set(false) 사이의 정확한 타이밍을 통합 테스트에서 재현 불가
- IsEmpty 체크와 CAS 사이의 Race Condition을 통제된 환경에서 검증 필요

**테스트 예제**:
```csharp
[TestFixture]
public class DoubleCheckPatternTests
{
    [Test]
    public async Task DoubleCheck_MessageArrivesAfterSetFalse_ShouldBeProcessed()
    {
        // Given
        var queue = new ConcurrentQueue<string>();
        var isUsing = new AtomicBoolean(false);
        var processedMessages = new ConcurrentBag<string>();

        // 초기 메시지
        queue.Enqueue("Msg1");

        // When: EventLoop 시작
        if (isUsing.CompareAndSet(false, true))
        {
            await Task.Run(async () =>
            {
                do
                {
                    while (queue.TryDequeue(out var msg))
                    {
                        processedMessages.Add(msg);
                        await Task.Delay(10);
                    }

                    isUsing.Set(false);

                    // 이 타이밍에 메시지 도착 시뮬레이션
                    queue.Enqueue("Msg2");

                } while (!queue.IsEmpty && isUsing.CompareAndSet(false, true));
            });
        }

        await Task.Delay(100);

        // Then
        Assert.That(processedMessages, Contains.Item("Msg1"));
        Assert.That(processedMessages, Contains.Item("Msg2"),
                    "Set(false) 후 도착한 메시지도 Double-Check로 처리되어야 함");
    }
}
```

### 12.4 테스트 구성 가이드

#### Fake 구현 예제

```csharp
/// <summary>
/// 테스트용 Stage 구현
/// 실제 Stage의 복잡한 초기화 과정 없이 EventLoop만 테스트
/// </summary>
public class FakeTestStage : BaseStage
{
    public List<RoutePacket> DispatchedMessages { get; } = new();
    public int DispatchCallCount { get; private set; }

    protected override async Task Dispatch(RoutePacket packet)
    {
        DispatchCallCount++;
        DispatchedMessages.Add(packet);

        // 테스트 시나리오에 따라 동작 변경 가능
        if (packet.MsgId == "SlowMsg")
        {
            await Task.Delay(1000);
        }

        await Task.CompletedTask;
    }
}
```

#### 테스트 헬퍼

```csharp
public static class EventLoopTestHelper
{
    /// <summary>
    /// 지정된 조건이 충족될 때까지 대기 (타임아웃 포함)
    /// </summary>
    public static async Task WaitUntilAsync(
        Func<bool> condition,
        int timeoutMs = 5000,
        int checkIntervalMs = 10)
    {
        var elapsed = 0;
        while (!condition() && elapsed < timeoutMs)
        {
            await Task.Delay(checkIntervalMs);
            elapsed += checkIntervalMs;
        }

        if (!condition())
        {
            throw new TimeoutException(
                $"조건이 {timeoutMs}ms 내에 충족되지 않음");
        }
    }

    /// <summary>
    /// EventLoop가 유휴 상태가 될 때까지 대기
    /// </summary>
    public static async Task WaitForEventLoopIdleAsync(BaseStage stage)
    {
        await WaitUntilAsync(
            () => stage.IsIdle && stage.MessageQueueCount == 0,
            timeoutMs: 3000
        );
    }
}
```

## 다음 단계

- `03-stage-actor-model.md`: Stage/Actor 상세 동작
- `04-timer-system.md`: 타이머 시스템 상세
- `10-testing-spec.md`: 테스트 전략
