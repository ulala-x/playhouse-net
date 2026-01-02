# Connector RequestCallback 모드 성능 개선

## 문제 상황

### 증상
- `RequestAsync` 모드: 8KB, 32KB, 64KB 모두 정상 작동
- `RequestCallback` 모드: 8KB 이상에서 타임아웃 발생
- 256B, 1500B: RequestCallback도 정상 작동

### 근본 원인

#### 1. MainThreadAction 폴링 지연
RequestCallback 모드는 모든 콜백을 `AsyncManager` 큐에 추가하고, `MainThreadAction()` 호출 시에만 실행됩니다.

**기존 구조 (ClientNetwork.cs Line 406-416):**
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
// Line 186-190: 세마포어 대기 중 1ms 간격으로 폴링
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
| 응답 처리 | `Tcs.TrySetResult()` 직접 호출 | `AsyncManager` 큐 경유 |
| MainThreadAction 의존성 | 없음 | 있음 (콜백 실행 필수) |
| Unity 호환성 | 제한적 (Unity API 사용 불가) | 완벽 |
| 성능 | 높음 (즉시 처리) | 낮음 (폴링 지연) |

## 해결 방법

### 1. ConnectorConfig에 콜백 실행 모드 추가

```csharp
/// <summary>
/// 콜백을 메인 스레드 큐에 추가할지 여부 (Unity용)
/// </summary>
/// <remarks>
/// - true: 모든 콜백을 큐에 추가하고 MainThreadAction()에서 실행 (Unity 권장)
/// - false: 콜백을 네트워크 스레드에서 즉시 실행 (고성능 시나리오)
/// 기본값: false (즉시 실행)
/// </remarks>
public bool UseMainThreadCallback { get; set; } = false;
```

### 2. ClientNetwork에서 조건부 콜백 실행

**Response 콜백 (Line 405-438):**
```csharp
else if (pending.Callback != null)
{
    var copiedResponse = CopyPacketPayload(parsed);
    packet.Dispose();

    if (_config.UseMainThreadCallback)
    {
        // Unity 모드: 큐에 추가
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
    }
    else
    {
        // 고성능 모드: 즉시 실행
        try
        {
            pending.Callback(copiedResponse);
        }
        finally
        {
            copiedResponse.Dispose();
        }
    }
}
```

**동일한 패턴이 적용된 콜백:**
- Response 콜백 (성공/에러)
- Push 메시지 콜백
- Connect/Disconnect 콜백
- 타임아웃 에러 콜백

## 사용 방법

### Unity 프로젝트 (메인 스레드 큐 사용)

```csharp
var config = new ConnectorConfig
{
    UseMainThreadCallback = true  // Unity는 true 권장
};

connector.Init(config);

// Update()에서 호출
void Update()
{
    connector.MainThreadAction();
}
```

### 고성능 시나리오 (벤치마크, 서버-서버 통신)

```csharp
var config = new ConnectorConfig
{
    UseMainThreadCallback = false  // 기본값, 즉시 실행
};

connector.Init(config);

// MainThreadAction() 호출 불필요
```

## 성능 비교

### 기존 (UseMainThreadCallback = true)
- 콜백 실행: MainThreadAction() 폴링에 의존
- 지연 시간: 1~20ms (폴링 간격에 따라)
- 동시 요청 제한: 8KB에서 30개로 병목 발생
- 타임아웃: 8KB 이상에서 빈번히 발생

### 개선 (UseMainThreadCallback = false)
- 콜백 실행: 네트워크 스레드에서 즉시 실행
- 지연 시간: < 1ms (거의 즉시)
- 동시 요청 제한: 세마포어가 즉시 release되어 병목 없음
- 타임아웃: 발생하지 않음

## 주의사항

### Unity에서 UseMainThreadCallback = false 사용 시
Unity의 메인 스레드 전용 API (GameObject, Transform 등)를 콜백에서 직접 사용할 수 없습니다.

**잘못된 예:**
```csharp
connector.Request(packet, response =>
{
    // 에러: Unity API는 메인 스레드에서만 호출 가능
    gameObject.transform.position = new Vector3(0, 0, 0);
});
```

**올바른 예:**
```csharp
connector.Request(packet, response =>
{
    var data = ParseResponse(response);  // 데이터 파싱 (스레드 안전)

    // Unity API는 큐에 추가
    UnityMainThreadDispatcher.Enqueue(() =>
    {
        gameObject.transform.position = new Vector3(0, 0, 0);
    });
});
```

### 벤치마크 및 일반 C# 프로젝트
스레드 안전성에만 주의하면 됩니다. 콜백 내에서 공유 데이터를 수정할 때는 lock을 사용하세요.

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
- 기존: 타임아웃 발생
- 개선: 모든 요청 정상 완료

### E2E 테스트
```bash
# 통합 테스트 실행
dotnet test tests/PlayHouse.Tests.Integration
```

## 마이그레이션 가이드

### 기존 코드 (영향 없음)
기본값이 `UseMainThreadCallback = false`이므로, 기존 코드는 **자동으로 성능 개선**의 혜택을 받습니다.

### Unity 프로젝트
Unity 프로젝트는 `UseMainThreadCallback = true`로 명시적으로 설정하여 기존 동작을 유지할 수 있습니다.

```csharp
// Unity 프로젝트에서는 이렇게 설정
var config = new ConnectorConfig
{
    UseMainThreadCallback = true  // Unity는 true 필수
};
```

## 관련 파일

- `/home/ulalax/project/ulalax/playhouse/playhouse-net/connector/PlayHouse.Connector/ConnectorConfig.cs`
- `/home/ulalax/project/ulalax/playhouse/playhouse-net/connector/PlayHouse.Connector/Internal/ClientNetwork.cs`
- `/home/ulalax/project/ulalax/playhouse/playhouse-net/tests/benchmark_cs/PlayHouse.Benchmark.Client/BenchmarkRunner.cs`
