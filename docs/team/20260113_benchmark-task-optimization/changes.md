# Benchmark Task Optimization Changes

## 변경 내용
- **파일**: `tests/benchmark_cs/PlayHouse.Benchmark.Client/BenchmarkRunner.cs`
- **메서드**: `RunRequestAsyncMode`

### 변경 전
- 요청마다 `Task.Run()`을 호출하여 비동기 작업을 생성함
- `SemaphoreSlim`을 사용하여 인플라이트(In-flight) 요청 수를 제어함
- 벤치마크 실행 중 수만~수십만 개의 `Task` 객체가 생성되어 과도한 메모리 할당 및 GC 부하를 유발함

### 변경 후
- 고정된 `maxInFlight` 개수만큼의 **Worker Task**를 미리 생성하여 재사용함
- 각 Worker가 루프 내에서 지속적으로 요청을 처리하는 구조로 변경함
- 생성되는 `Task` 객체 수를 `maxInFlight` (예: 200개) 수준으로 고정하여 시스템 자원 점유를 최소화함
- 요청 처리 중 예외가 발생하더라도 로깅 후 다음 반복(Iteration)을 계속 진행하도록 안정성을 강화함
- 첫 번째 Worker Task가 주기적으로 `MainThreadAction`을 호출하여 커넥터의 상태 유지 및 메트릭 수집을 수행함

## 기대 효과
- **GC 압박 감소**: 수만~수십만 개에 달하던 `Task` 객체 생성을 수백 개 수준으로 억제하여 GC 오버헤드 및 메모리 사용량 최적화
- **ThreadPool 경합 감소**: 빈번한 Task 스케줄링 및 컨텍스트 스위칭을 줄여 ThreadPool의 효율성 및 시스템 응답성 향상
- **부하 안정성 유지**: 기존과 동일한 인플라이트 수준 및 서버 부하를 유지하면서도 클라이언트 측의 불필요한 리소스 낭비를 제거하여 보다 정확한 벤치마크 결과 도출 가능

## 코드 비교

### 변경 전 (개념)
```csharp
while (DateTime.UtcNow < endTime)
{
    await semaphore.WaitAsync();
    var task = Task.Run(async () =>  // 매번 새 Task 생성!
    {
        await connector.RequestAsync(packet);
        semaphore.Release();
    });
    tasks.Add(task);  // 수만~수십만 개 누적
}
```

### 변경 후
```csharp
var workers = new Task[maxInFlight];  // 고정된 개수
for (int i = 0; i < maxInFlight; i++)
{
    workers[i] = Task.Run(async () =>
    {
        while (DateTime.UtcNow < endTime)
        {
            await connector.RequestAsync(packet);  // Worker가 루프에서 처리
        }
    });
}
await Task.WhenAll(workers);  // Task 개수: maxInFlight (200개)
```
