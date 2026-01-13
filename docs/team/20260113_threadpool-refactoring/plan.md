## 최종 구조 (추가)

### 1. StageSynchronizationContext
- GlobalTaskPool 의존성 제거
- ThreadPool.QueueUserWorkItem() 직접 사용
- async/await continuation 처리용 (가벼운 작업)

### 2. AsyncBlock 분리 → AsyncCompute + AsyncIO
기존 AsyncBlock을 두 가지로 분리:

#### AsyncCompute (CPU 바운드 작업)
- ComputeTaskPool 사용
- 스레드 수: CPU 코어 수 기반 (Environment.ProcessorCount)
- 용도: 무거운 계산 작업

#### AsyncIO (I/O 바운드 작업)  
- IoTaskPool 사용
- 스레드 수: 더 여유있게 (기본 100)
- 용도: DB, 외부 API 호출 등

### IStageSender 인터페이스 변경
```csharp
public interface IStageSender
{
    // 기존 AsyncBlock → deprecated
    [Obsolete("Use AsyncCompute or AsyncIO instead")]
    void AsyncBlock(AsyncPreCallback pre, AsyncPostCallback? post = null);
    
    /// <summary>CPU 계산 작업용</summary>
    void AsyncCompute(AsyncPreCallback pre, AsyncPostCallback? post = null);
    
    /// <summary>DB/I/O 작업용</summary>
    void AsyncIO(AsyncPreCallback pre, AsyncPostCallback? post = null);
}
```

### 새로운 TaskPool 구조
- ComputeTaskPool: Task.Run() 기반, CPU 코어 수 제한
- IoTaskPool: Task.Run() 기반, I/O 작업에 최적화
