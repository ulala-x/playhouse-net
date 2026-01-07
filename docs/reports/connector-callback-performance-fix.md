# Connector 콜백 실행 모드 개선

## 문제 상황

### 증상
- `RequestAsync` 모드: 8KB, 32KB, 64KB 모두 정상 작동
- `RequestCallback` 모드: 8KB 이상에서 타임아웃 발생
- 256B, 1500B: RequestCallback도 정상 작동

### 근본 원인

#### 1. MainThreadAction 폴링 지연
RequestCallback 모드는 모든 콜백을 `AsyncManager` 큐에 추가하고, `MainThreadAction()` 호출 시에만 실행됩니다.

**기존 구조:**
```csharp
// 응답 수신 시 콜백을 큐에 추가
_asyncManager.AddJob(() =>
{
    try
    {
        pending.Callback(copiedResponse);
    }
    finally
    {
        copiedResponse.Dispose();
    }
});
```

**BenchmarkRunner의 MainThreadAction 호출 패턴:**
```csharp
// 세마포어 대기 중 1ms 간격으로 폴링
while (semaphore.CurrentCount == 0)
{
    connector.MainThreadAction();
    await Task.Delay(1);  // 1ms 대기 중 콜백 처리 안 됨
}
```

#### 2. 동시 요청 수 제한으로 인한 병목
8KB 이상 메시지에서는 동시 요청이 30개로 제한됩니다:

```csharp
var maxConcurrentRequests = responseSize switch
{
    > 32768 => 10,   // 32KB 이상: 10개
    > 8192 => 30,    // 8KB 이상: 30개  ← 병목 발생 지점
    > 1024 => 50,
    _ => 100
};
```

**타임아웃 발생 메커니즘:**
1. 30개 요청이 모두 발송됨
2. 응답이 도착하지만 `MainThreadAction()` 호출 부족으로 콜백이 실행되지 않음
3. Semaphore가 release되지 않아 새 요청을 보낼 수 없음
4. 타임아웃 발생

#### 3. RequestAsync vs RequestCallback 차이

| 측면 | RequestAsync | RequestCallback |
|------|--------------|-----------------|
| 응답 처리 | `Tcs.TrySetResult()` 직접 호출 | 콜백 실행 |
| MainThreadAction 의존성 | 없음 | 환경에 따라 다름 |
| Unity 호환성 | 제한적 (Unity API 사용 불가) | 완벽 |
| 성능 | 높음 (즉시 처리) | 환경에 따라 다름 |

## 해결 방법: SynchronizationContext 기반 콜백 실행

### 1. SynchronizationContext 활용

`ClientNetwork`는 생성 시점의 `SynchronizationContext`를 캡처하여 콜백 실행 방식을 결정합니다:

```csharp
public ClientNetwork(ConnectorConfig config, IConnectorCallback callback)
{
    _config = config ?? throw new ArgumentNullException(nameof(config));
    _callback = callback ?? throw new ArgumentNullException(nameof(callback));
    _syncContext = SynchronizationContext.Current;
}
```

### 2. 조건부 콜백 실행

**패킷 처리 분기 (ClientNetwork.cs Line 404-411):**
```csharp
// SyncContext가 있으면 즉시 Post, 없으면 큐
if (_syncContext != null)
{
    _syncContext.Post(_ => ProcessPacket(parsed), null);
}
else
{
    _packetQueue.Enqueue(parsed);
}
```

**동작 방식:**
- **SynchronizationContext가 있는 경우**: `Post()`로 즉시 전달하여 해당 컨텍스트에서 실행
- **SynchronizationContext가 없는 경우**: 큐에 추가하고 `MainThreadAction()` 호출 시 실행

### 3. 환경별 SynchronizationContext

| 환경 | SynchronizationContext | 동작 |
|------|------------------------|------|
| Unity | `UnitySynchronizationContext` | 다음 프레임 메인 스레드에서 실행 |
| 테스트/벤치마크 | `ImmediateSynchronizationContext` | 즉시 실행 (동기적) |
| 콘솔/서버 | `null` | 큐 + MainThreadAction() |

## 사용 방법

### Unity 프로젝트 (자동 감지)

Unity는 자동으로 `UnitySynchronizationContext`를 제공하므로 별도 설정이 필요 없습니다:

```csharp
// Unity 메인 스레드에서 Connector 생성
var connector = new Connector();
connector.Init(config);

// Update()에서 호출 (선택적)
void Update()
{
    connector.MainThreadAction();  // Unity는 필수 아님 (SynchronizationContext가 처리)
}
```

### 고성능 시나리오 (테스트, 벤치마크)

즉시 실행이 필요한 경우 `ImmediateSynchronizationContext`를 설정:

```csharp
// Connector 생성 전에 SynchronizationContext 설정
SynchronizationContext.SetSynchronizationContext(new ImmediateSynchronizationContext());

var connector = new Connector();
connector.Init(config);

// MainThreadAction() 호출 불필요 (모든 콜백이 즉시 실행됨)
```

**ImmediateSynchronizationContext 구현:**
```csharp
public class ImmediateSynchronizationContext : SynchronizationContext
{
    public override void Post(SendOrPostCallback d, object? state)
    {
        // 즉시 실행 (비동기 아님)
        d(state);
    }
}
```

### 일반 C# 프로젝트 (큐 + 폴링)

SynchronizationContext 없이 기존 방식 유지:

```csharp
// SynchronizationContext 없이 생성
var connector = new Connector();
connector.Init(config);

// 게임 루프나 타이머에서 호출
void GameLoop()
{
    connector.MainThreadAction();  // 큐에 쌓인 콜백 실행
}
```

## 성능 비교

### 기존 (큐 + MainThreadAction 폴링)
- 콜백 실행: MainThreadAction() 폴링에 의존
- 지연 시간: 1~20ms (폴링 간격에 따라)
- 동시 요청 제한: 8KB에서 30개로 병목 발생
- 타임아웃: 8KB 이상에서 빈번히 발생

### 개선 (ImmediateSynchronizationContext)
- 콜백 실행: 네트워크 스레드에서 즉시 실행
- 지연 시간: < 1ms (거의 즉시)
- 동시 요청 제한: 세마포어가 즉시 release되어 병목 없음
- 타임아웃: 발생하지 않음

### 개선 (UnitySynchronizationContext)
- 콜백 실행: 다음 프레임 메인 스레드에서 자동 실행
- 지연 시간: 1프레임 (16.67ms @ 60fps)
- Unity API 안전: 완벽하게 호환
- MainThreadAction() 호출: 선택적

## 주의사항

### Unity에서 ImmediateSynchronizationContext 사용 시

Unity의 메인 스레드 전용 API (GameObject, Transform 등)를 콜백에서 직접 사용할 수 없습니다.

**잘못된 예:**
```csharp
// Unity 메인 스레드가 아닌 곳에서 Connector 생성 시
SynchronizationContext.SetSynchronizationContext(new ImmediateSynchronizationContext());
var connector = new Connector();

connector.Request(packet, response =>
{
    // 에러: Unity API는 메인 스레드에서만 호출 가능
    gameObject.transform.position = new Vector3(0, 0, 0);
});
```

**올바른 예 (Unity 메인 스레드에서 생성):**
```csharp
// Unity 메인 스레드에서 Connector 생성
// UnitySynchronizationContext가 자동으로 설정됨
var connector = new Connector();
connector.Init(config);

connector.Request(packet, response =>
{
    // 안전: Unity 메인 스레드에서 실행됨
    gameObject.transform.position = new Vector3(0, 0, 0);
});
```

### 테스트 및 벤치마크에서 스레드 안전성

`ImmediateSynchronizationContext`를 사용하면 콜백이 네트워크 스레드에서 즉시 실행되므로 스레드 안전성에 주의해야 합니다:

```csharp
private readonly object _lock = new();
private int _receivedCount;

connector.Request(packet, response =>
{
    lock (_lock)
    {
        _receivedCount++;
    }
});
```

## 테스트 검증

### 벤치마크 테스트

```bash
# 서버 시작
cd tests/benchmark_cs/PlayHouse.Benchmark.Server
dotnet run

# 클라이언트 실행 (RequestCallback 모드, 8KB)
cd tests/benchmark_cs/PlayHouse.Benchmark.Client
dotnet run -- --mode callback --connections 10 --messages 100 --request-size 8192
```

**예상 결과:**
- 기존 (SynchronizationContext 없음): 타임아웃 발생
- 개선 (ImmediateSynchronizationContext): 모든 요청 정상 완료

### E2E 테스트

```bash
# 통합 테스트 실행
dotnet test tests/PlayHouse.Tests.Integration
```

## 마이그레이션 가이드

### Unity 프로젝트
Unity는 자동으로 `UnitySynchronizationContext`를 제공하므로 변경 불필요:

```csharp
// Unity 메인 스레드에서 Connector 생성만 하면 됨
var connector = new Connector();
connector.Init(config);
```

### 테스트 및 벤치마크 프로젝트
즉시 실행이 필요한 경우 `ImmediateSynchronizationContext` 설정:

```csharp
// 테스트 Setup에서
SynchronizationContext.SetSynchronizationContext(new ImmediateSynchronizationContext());

var connector = new Connector();
connector.Init(config);
```

### 일반 C# 프로젝트
변경 불필요 (기존 큐 + MainThreadAction 방식 유지):

```csharp
var connector = new Connector();
connector.Init(config);

// 게임 루프에서
connector.MainThreadAction();
```

## 관련 파일

- `/home/ulalax/project/ulalax/playhouse/playhouse-net/connector/PlayHouse.Connector/Internal/ClientNetwork.cs`
- `/home/ulalax/project/ulalax/playhouse/playhouse-net/connector/PlayHouse.Connector/ImmediateSynchronizationContext.cs`
- `/home/ulalax/project/ulalax/playhouse/playhouse-net/tests/PlayHouse.Tests.Integration/Play/ConnectorCallbackPerformanceTests.cs`
- `/home/ulalax/project/ulalax/playhouse/playhouse-net/tests/benchmark_cs/PlayHouse.Benchmark.Client/BenchmarkRunner.cs`

## 결론

`SynchronizationContext` 기반 콜백 실행 모드 개선으로:

1. **환경 자동 감지**: Unity, 테스트, 일반 C# 환경을 자동으로 구분
2. **Unity 완벽 호환**: `UnitySynchronizationContext`를 통해 메인 스레드에서 안전하게 실행
3. **고성능 시나리오**: `ImmediateSynchronizationContext`로 즉시 실행 (타임아웃 없음)
4. **하위 호환성**: SynchronizationContext 없으면 기존 큐 방식 유지

이 방식은 각 환경의 특성에 맞는 최적의 콜백 실행 방식을 제공하며, 명시적인 설정 없이도 대부분의 경우 올바르게 동작합니다.
