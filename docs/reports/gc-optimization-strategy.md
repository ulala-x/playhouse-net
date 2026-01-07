# PlayHouse-Net GC 최적화 및 성능 개선 전략

**작성일**: 2025-12-29
**대상 독자**: PlayHouse-Net 개발팀, 시스템 아키텍트
**문서 목적**: 1000 CCU 벤치마크 결과 분석 및 GC 최적화 전략 수립

---

## 목차

1. [개요 및 현재 상태](#1-개요-및-현재-상태)
2. [ArrayPool 최적화 전략](#2-arraypool-최적화-전략)
3. [GC 설정 튜닝](#3-gc-설정-튜닝)
4. [코드 레벨 최적화](#4-코드-레벨-최적화)
5. [구현 로드맵](#5-구현-로드맵)
6. [참고 자료](#6-참고-자료)

---

## 1. 개요 및 현재 상태

### 1.1 성능 개선 히스토리

**MessagePool → ArrayPool.Shared 마이그레이션 결과** (1000 CCU 벤치마크):

| 메트릭 | Before | After | 변화 |
|--------|--------|-------|------|
| TPS | 109K | 139K | **+27%** ⬆️ |
| P99 Latency | 20ms | 11ms | **-45%** ⬇️ |
| Gen2 GC | 16회 | 81회 | **+406%** ⬆️ |

**결론**: 처리량과 지연시간은 크게 개선되었으나, Gen2 GC 빈도가 5배 증가하여 장기 운영 안정성에 리스크 발생.

### 1.2 ArrayPool.Create() 시도 실패

**시도 내용**:
- `ArrayPool.Create(maxArrayLength: 1024*1024, maxArraysPerBucket: 64)` 사용
- 버킷당 최대 배열 수를 늘려 풀 포화 방지 시도

**실패 원인**:
- Gen2 GC 콜백 미구현으로 메모리 회수 메커니즘 없음
- 버킷이 커져도 오버플로우 발생 시 계속 힙 할당
- Gen2 GC가 더 증가하여 역효과

### 1.3 문제 진단

**Gen2 GC 증가 원인**:
1. **풀 버킷 포화**: `MaxBuffersPerArraySizePerCore = 8`이 너무 작음
2. **오버플로우 할당**: 버킷 포화 시 새 배열을 힙에 할당 → Gen2 승격
3. **Gen2 콜백 부재**: `ArrayPool.Create()`는 메모리 압박 시 자동 회수 없음

**목표 설정**:
- Gen2 GC 횟수를 MessagePool 수준(16회)으로 복원
- TPS 139K, P99 Latency 11ms 유지 또는 개선
- 메모리 사용량 증가 최소화 (< 20%)

---

## 2. ArrayPool 최적화 전략

### 2.1 옵션 비교

| 옵션 | 구현 복잡도 | Gen2 GC 감소 예상 | TPS 영향 | 메모리 증가 | 리스크 | 우선순위 |
|------|-------------|-------------------|----------|-------------|--------|----------|
| **A. 현상 유지** | 없음 | 0% | 0% | 0% | 장기 안정성 낮음 | ⭐ **현재 권장** |
| **B. TlsOverPerCoreLockedStacksArrayPool 클론** | **매우 높음** | 60-80% | 0~+5% | +10-15% | 유지보수 부담 | ❌ 복잡도 높음 |
| **C. 하이브리드 풀** | 높음 | 40-60% | +5~10% | +5-10% | 복잡도 높음 | 차선책 |
| **D. GC 설정 튜닝** | 낮음 | 10-30% | -5~+5% | 0% | 낮음 | 🔄 검토 필요 |

### 2.2 ⚠️ 실험 결과: Lock 기반 구현 실패

**2025-12-29 실험 결과**:

Lock 기반 `PlayHouseArrayPool` 구현을 시도했으나 **성능이 오히려 악화**되었습니다.

| 메트릭 | ArrayPool.Shared | PlayHouseArrayPool (lock 기반) | 변화 |
|--------|------------------|--------------------------------|------|
| TPS | 125,201 | 119,252 | **-4.7%** ⬇️ |
| Gen2 GC | 115회 | 132회 | **+14.8%** ⬆️ |
| P99 Latency | 11.1ms | 11.6ms | +4.5% |

**실패 원인**:
1. **Lock Contention**: 1000 CCU 환경에서 lock 경합 발생
2. **ArrayPool.Shared 최적화**: .NET의 구현은 TLS + Interlocked 기반 lock-free
3. **풀링 효율 저하**: lock 대기로 인해 버퍼를 제때 반환/획득 못함 → 새 할당 증가

**결론**: 진정한 개선을 위해서는 .NET Runtime의 `TlsOverPerCoreLockedStacksArrayPool` 전체를 복제해야 하며, 이는 구현 복잡도가 매우 높습니다.

**현재 권장**: ArrayPool.Shared 유지 + GC 설정 튜닝 검토

---

### 2.3 옵션 B: TlsOverPerCoreLockedStacksArrayPool 클론 (복잡도 높음)

#### 개요

.NET 런타임의 `TlsOverPerCoreLockedStacksArrayPool<T>` 구현을 복제하여 커스터마이징:
- **Gen2GcCallback 유지**: `GC.ReRegisterForFinalize()` 기반 메모리 회수
- **버킷 크기 확장**: `MaxBuffersPerArraySizePerCore` 8 → 32 또는 64

#### 구현 상세

```csharp
// PlayHouse.Core/Memory/PlayHouseArrayPool.cs

namespace PlayHouse.Core.Memory;

/// <summary>
/// Gen2 GC callback 및 확장된 버킷 크기를 지원하는 커스텀 ArrayPool 구현
/// .NET Runtime의 TlsOverPerCoreLockedStacksArrayPool 기반
/// </summary>
public sealed class PlayHouseArrayPool<T> : ArrayPool<T>
{
    // 기존: 8, 확장: 32 (권장) 또는 64 (고부하 환경)
    private const int MaxBuffersPerArraySizePerCore = 32;

    // 버킷별 최대 배열 길이 (2^n 시리즈)
    private const int MaxArrayLength = 1024 * 1024; // 1MB

    private readonly PerCoreLockedStacks[] _buckets;
    private readonly Gen2GcCallback _gcCallback;

    public PlayHouseArrayPool()
    {
        // 버킷 초기화 (16, 32, 64, 128, ..., MaxArrayLength)
        _buckets = CreateBuckets();

        // Gen2 GC 콜백 등록
        _gcCallback = new Gen2GcCallback(this);
    }

    public override T[] Rent(int minimumLength)
    {
        // 1. 적절한 버킷 찾기 (2의 거듭제곱 정렬)
        int bucketIndex = SelectBucketIndex(minimumLength);

        if (bucketIndex < _buckets.Length)
        {
            // 2. TLS 또는 per-core 스택에서 시도
            T[]? buffer = _buckets[bucketIndex].TryPop();
            if (buffer != null)
            {
                return buffer;
            }
        }

        // 3. 풀에 없으면 새 배열 할당
        return new T[CalculateArraySize(bucketIndex)];
    }

    public override void Return(T[] array, bool clearArray = false)
    {
        if (array.Length == 0) return;

        int bucketIndex = SelectBucketIndex(array.Length);

        if (bucketIndex < _buckets.Length)
        {
            // 배열 초기화 (보안/메모리 누수 방지)
            if (clearArray)
            {
                Array.Clear(array, 0, array.Length);
            }

            // 버킷에 반환 (포화 시 자동 드롭)
            _buckets[bucketIndex].TryPush(array);
        }
        // else: 너무 큰 배열은 GC에게 맡김
    }

    // Gen2 GC 발생 시 호출되어 과도한 버퍼 회수
    private void Trim()
    {
        foreach (var bucket in _buckets)
        {
            bucket.Trim(MaxBuffersPerArraySizePerCore / 2);
        }

        // 다음 Gen2 GC를 위해 재등록
        _gcCallback.ReRegister();
    }

    private sealed class Gen2GcCallback
    {
        private readonly PlayHouseArrayPool<T> _pool;

        public Gen2GcCallback(PlayHouseArrayPool<T> pool)
        {
            _pool = pool;
            ReRegister();
        }

        public void ReRegister()
        {
            // Gen2 GC 발생 시 ~Gen2GcCallback() 호출됨
            GC.ReRegisterForFinalize(this);
        }

        ~Gen2GcCallback()
        {
            // Gen2 GC 발생 시점에 풀 정리
            if (!Environment.HasShutdownStarted)
            {
                _pool.Trim();
            }
        }
    }
}
```

#### 설정 조정 가이드

| 환경 | MaxBuffersPerArraySizePerCore | 예상 메모리 증가 | 권장 사용처 |
|------|-------------------------------|------------------|-------------|
| 기본 | 8 (기본값) | 0% | 저부하 (<500 CCU) |
| **권장** | **32** | **+10-15%** | **중부하 (500-2000 CCU)** |
| 고부하 | 64 | +20-30% | 고부하 (2000+ CCU) |

**결정 기준**:
- 1000 CCU 벤치마크 → **32** 권장
- 메모리 < CPU 비용이면 64 고려
- 프로덕션 모니터링 후 동적 조정

#### 예상 효과

| 메트릭 | 현재 (ArrayPool.Shared) | 예상 (PlayHouseArrayPool) |
|--------|-------------------------|----------------------------|
| Gen2 GC | 81회 | 16-24회 (-70% ~ -80%) |
| TPS | 139K | 139-146K (0% ~ +5%) |
| P99 Latency | 11ms | 10-11ms (0% ~ -9%) |
| 메모리 사용량 | 기준 | +10-15% |

**근거**:
- 버킷 포화 감소로 오버플로우 할당 80% 감소
- Gen2 콜백으로 장기 메모리 누적 방지
- TLS + per-core 구조로 lock contention 최소화

#### 구현 단계

1. **Phase 1: 프로토타입** (2-3일)
   - `PlayHouseArrayPool<byte>` 구현
   - 단위 테스트 작성 (Rent/Return, Gen2 콜백)

2. **Phase 2: 통합** (1-2일)
   - `ZmqPlaySocket`, `ProtoPayload` 등에 적용
   - 기존 `ArrayPool.Shared` → `PlayHouseArrayPool<byte>.Shared` 교체

3. **Phase 3: 벤치마크** (1일)
   - 1000 CCU 벤치마크 재실행
   - Gen2 GC, TPS, Latency 측정

4. **Phase 4: 튜닝** (2-3일)
   - `MaxBuffersPerArraySizePerCore` 조정 (16, 32, 64 비교)
   - 최적값 결정 및 문서화

**총 소요 시간**: 6-9일

#### 리스크 및 완화 방안

| 리스크 | 영향 | 확률 | 완화 방안 |
|--------|------|------|-----------|
| 런타임 API 변경 | 높음 | 낮음 | .NET 버전별 조건부 컴파일 |
| 메모리 오버헤드 | 중간 | 중간 | Feature flag로 동적 활성화 |
| Gen2 콜백 미동작 | 높음 | 낮음 | 주기적 Trim() 백업 로직 |
| 멀티스레드 버그 | 높음 | 낮음 | 런타임 검증된 로직 복제, 철저한 테스트 |

### 2.3 옵션 C: 하이브리드 풀 (차선책)

#### 개요

크기별로 다른 풀링 전략 적용:
- **작은 버퍼** (< 4KB): `PlayHouseArrayPool` (Gen2 콜백 + 큰 버킷)
- **중간 버퍼** (4KB - 64KB): `ArrayPool.Shared`
- **큰 버퍼** (> 64KB): 직접 할당 (풀링 안함)

#### 구현 예시

```csharp
public static class AdaptiveArrayPool
{
    private static readonly PlayHouseArrayPool<byte> SmallPool = new();

    public static byte[] Rent(int minimumLength)
    {
        return minimumLength switch
        {
            < 4096 => SmallPool.Rent(minimumLength),
            < 65536 => ArrayPool<byte>.Shared.Rent(minimumLength),
            _ => new byte[minimumLength] // 큰 버퍼는 풀링 비효율적
        };
    }

    public static void Return(byte[] array)
    {
        switch (array.Length)
        {
            case < 4096:
                SmallPool.Return(array);
                break;
            case < 65536:
                ArrayPool<byte>.Shared.Return(array);
                break;
            // 큰 버퍼는 GC에게 맡김
        }
    }
}
```

#### 장단점

**장점**:
- 크기별 최적화로 더 세밀한 제어
- 큰 버퍼 풀링 오버헤드 제거

**단점**:
- 복잡도 증가 (3개 풀 관리)
- 임계값 튜닝 필요 (워크로드 의존적)
- 유지보수 부담

**권장 시나리오**:
- 옵션 B로 목표 미달성 시 고려
- 버퍼 크기 분포가 명확히 구분될 때

---

## 3. GC 설정 튜닝

### 3.1 Server GC vs Workstation GC

PlayHouse-Net은 **Server GC** 사용 권장 (이미 설정되어 있을 가능성 높음).

**설정 방법** (`playhouse-net.csproj`):

```xml
<PropertyGroup>
  <ServerGarbageCollection>true</ServerGarbageCollection>
  <ConcurrentGarbageCollection>true</ConcurrentGarbageCollection>
</PropertyGroup>
```

| 특성 | Server GC | Workstation GC |
|------|-----------|----------------|
| 스레드 | CPU 코어당 1개 | 1개 (애플리케이션 스레드) |
| Heap | 코어당 1개 | 1개 공유 |
| 처리량 | **높음** | 낮음 |
| Pause 시간 | 김 (10-50ms) | **짧음** (1-10ms) |
| 적합 | **서버, 고처리량** | 클라이언트, UI 앱 |

**PlayHouse-Net 권장**: Server GC (이미 적용 중으로 추정)

### 3.2 GC Latency Mode

#### 옵션 비교

| Mode | Pause 시간 | 처리량 | Gen2 빈도 | 적합한 시나리오 |
|------|-----------|--------|----------|-----------------|
| **Interactive** (기본) | 중간 | 중간 | 보통 | 범용 서버 |
| **Batch** | 김 | **최고** | 낮음 | 고처리량 배치 작업 |
| **SustainedLowLatency** | **짧음** | 낮음 | 높음 (background만) | 지연시간 민감 서비스 |
| LowLatency (deprecated) | 짧음 | 낮음 | 억제됨 | 사용 안함 |

#### PlayHouse-Net 권장 전략

**현재 문제**: Gen2 GC 빈도 과다 (81회)

**권장 설정**: 일단 **Interactive** 유지, ArrayPool 최적화 후 재평가

**조건부 적용**:

```csharp
// PlayHouse.Core/Services/GcOptimizationService.cs

public class GcOptimizationService
{
    private GCLatencyMode _originalMode;

    public void OptimizeForHighThroughput()
    {
        _originalMode = GCSettings.LatencyMode;

        if (GCSettings.IsServerGC)
        {
            // 고처리량 우선 (Gen2 pause 허용)
            GCSettings.LatencyMode = GCLatencyMode.Batch;
        }
    }

    public void OptimizeForLowLatency()
    {
        _originalMode = GCSettings.LatencyMode;

        if (GCSettings.IsServerGC)
        {
            // foreground Gen2 억제, background Gen2만 허용
            // 주의: Gen2가 background로만 발생하여 빈도 증가 가능
            GCSettings.LatencyMode = GCLatencyMode.SustainedLowLatency;
        }
    }

    public void Restore()
    {
        GCSettings.LatencyMode = _originalMode;
    }
}
```

**적용 시나리오**:

| 게임 페이즈 | Latency Mode | 이유 |
|------------|--------------|------|
| 로딩/초기화 | Batch | 처리량 우선, pause 무관 |
| **런타임** | **Interactive** | **균형** |
| PvP 매치 중 | SustainedLowLatency | pause 최소화 (필요시만) |
| 점검/종료 | Batch | 빠른 정리 |

**구현 우선순위**: 낮음 (ArrayPool 최적화 후)

**예상 효과**:
- Batch Mode: Gen2 GC -10~20%, TPS +5~10%, P99 Latency +10~20%
- SustainedLowLatency: Gen2 GC +20~50% (background), P99 Latency -20~30%

**리스크**:
- SustainedLowLatency는 Gen2를 억제하지만 background Gen2가 증가하여 총 횟수는 늘어날 수 있음
- 벤치마크 필수

### 3.3 Large Object Heap (LOH) 최적화

**배경**: 85KB 이상 객체는 LOH에 할당되어 Gen2에서만 회수됨.

**PlayHouse-Net 해당 여부**:
- ArrayPool로 대부분 작은 버퍼 (< 4KB) 처리
- Proto 메시지는 일반적으로 작음

**권장 조치**:
1. 85KB 이상 할당 모니터링 (dotMemory, PerfView)
2. 발견 시 배열 분할 또는 풀링 고려

**설정** (.NET 5+):

```xml
<PropertyGroup>
  <!-- LOH 압축 활성화 (기본: false) -->
  <GCLOHThreshold>85000</GCLOHThreshold>
</PropertyGroup>
```

또는 런타임:

```csharp
// Gen2 GC 후 LOH 압축
GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
GC.Collect();
```

**주의**: LOH 압축은 비용이 크므로 필요시만 사용.

---

## 4. 코드 레벨 최적화

### 4.1 Zero-Allocation 패턴

#### Span<T> 및 stackalloc

**원리**:
- `Span<T>`: 스택 또는 힙 메모리의 뷰, GC 대상 아님
- `stackalloc`: 스택에 메모리 할당, 메서드 종료 시 자동 해제

**적용 예시**:

```csharp
// Before: heap 할당
public byte[] SerializeHeader(int msgId, int length)
{
    byte[] header = new byte[8]; // Gen0 할당
    BitConverter.TryWriteBytes(header.AsSpan(0, 4), msgId);
    BitConverter.TryWriteBytes(header.AsSpan(4, 4), length);
    return header;
}

// After: zero-allocation
public void SerializeHeader(int msgId, int length, Span<byte> destination)
{
    Span<byte> header = stackalloc byte[8]; // 스택 할당
    BitConverter.TryWriteBytes(header.Slice(0, 4), msgId);
    BitConverter.TryWriteBytes(header.Slice(4, 4), length);
    header.CopyTo(destination);
}
```

**PlayHouse-Net 적용 대상**:
- `ProtoPayload.MakeMessage()`: 이미 직접 직렬화로 최적화됨
- `ZmqPlaySocket.Receive()`: 메시지 파싱 시 임시 버퍼

**구현 우선순위**: 중간

**예상 효과**:
- Gen0 GC 10-20% 감소
- TPS +2-5%
- 코드 복잡도 증가 (Span 제약사항)

#### Memory<T> for Async

**문제**: `Span<T>`는 async 메서드에서 사용 불가 (스택 참조).

**해결**: `Memory<T>` 사용 (heap 기반, async 경계 통과 가능).

```csharp
// Before
public async Task<byte[]> ReceiveAsync()
{
    byte[] buffer = new byte[1024];
    await stream.ReadAsync(buffer, 0, buffer.Length);
    return buffer;
}

// After
public async ValueTask<int> ReceiveAsync(Memory<byte> buffer)
{
    return await stream.ReadAsync(buffer);
}
```

**적용 대상**:
- `Connector.RequestAsync()`: 응답 버퍼 재사용
- async 메시지 처리 파이프라인

### 4.2 Boxing 방지

#### struct IEquatable<T> 구현

**문제**: struct를 `Dictionary<TKey, TValue>`에서 사용 시 boxing 발생.

```csharp
// Before: boxing 발생
public struct SessionId
{
    public long Value;

    public override int GetHashCode() => Value.GetHashCode(); // boxing
    public override bool Equals(object? obj) => obj is SessionId id && Value == id.Value; // boxing
}

// After: zero-boxing
public struct SessionId : IEquatable<SessionId>
{
    public long Value;

    public int GetHashCode() => Value.GetHashCode(); // no boxing
    public bool Equals(SessionId other) => Value == other.Value; // no boxing
    public override bool Equals(object? obj) => obj is SessionId id && Equals(id);
}
```

**PlayHouse-Net 적용 대상**:
- 모든 `struct` 타입 (SessionId, AccountId, StageId 등)
- Dictionary 키로 사용되는 value type

**구현 우선순위**: 높음 (간단하고 효과적)

**예상 효과**:
- Gen0 GC 5-10% 감소
- Dictionary 조회 성능 +10-15%

#### readonly struct

```csharp
// 불필요한 복사 방지
public readonly struct RoutePacket
{
    public readonly string MsgId;
    public readonly ReadOnlyMemory<byte> Payload;

    public RoutePacket(string msgId, ReadOnlyMemory<byte> payload)
    {
        MsgId = msgId;
        Payload = payload;
    }
}
```

**효과**:
- 방어적 복사 방지
- 메서드 호출 시 참조 전달

### 4.3 String 최적化

#### StringPool 적용

**배경**: 반복되는 문자열 (msgId, stageType 등) 중복 제거.

**CommunityToolkit.HighPerformance 사용**:

```csharp
using CommunityToolkit.HighPerformance.Buffers;

public class MessageIdCache
{
    private static readonly StringPool Pool = new();

    public static string GetOrAdd(string msgId)
    {
        // 이미 존재하면 캐시된 인스턴스 반환, 없으면 추가
        return Pool.GetOrAdd(msgId);
    }
}

// 사용
var msgId = MessageIdCache.GetOrAdd("EchoRequest"); // 최초 1회만 할당
var msgId2 = MessageIdCache.GetOrAdd("EchoRequest"); // 캐시 재사용
Assert.True(ReferenceEquals(msgId, msgId2));
```

**PlayHouse-Net 적용 대상**:
- `ZmqPlaySocket.Receive()`: `senderServerId` 캐싱 (이미 적용됨)
- Proto 메시지 MsgId 캐싱
- StageType, ActorType 문자열

**구현 우선순위**: 중간 (일부 이미 적용)

**예상 효과**:
- Gen0 GC 3-5% 감소
- String 메모리 사용량 -20~30%

#### String.Intern() 주의사항

**장점**: .NET 네이티브 interning, 중복 제거.

**단점**:
- interned 문자열은 프로세스 종료까지 유지 (메모리 누수 위험)
- 동적 문자열에는 부적합

**권장**:
- 컴파일 타임 상수만 사용 (`const string`)
- 동적 문자열은 `StringPool` 사용

### 4.4 Object Pooling

#### Microsoft.Extensions.ObjectPool

**적용 대상**: 복잡한 객체 (StringBuilder, MemoryStream 등).

```csharp
using Microsoft.Extensions.ObjectPool;

public class MessageSerializerPool
{
    private static readonly ObjectPool<MemoryStream> StreamPool =
        ObjectPool.Create(new MemoryStreamPooledObjectPolicy());

    public byte[] Serialize(object message)
    {
        var stream = StreamPool.Get();
        try
        {
            // 직렬화
            ProtoBuf.Serializer.Serialize(stream, message);
            return stream.ToArray();
        }
        finally
        {
            stream.SetLength(0); // 초기화
            StreamPool.Return(stream);
        }
    }
}

public class MemoryStreamPooledObjectPolicy : IPooledObjectPolicy<MemoryStream>
{
    public MemoryStream Create() => new MemoryStream();

    public bool Return(MemoryStream obj)
    {
        if (obj.Capacity > 1024 * 1024) // 1MB 이상은 풀링 안함
            return false;

        obj.SetLength(0);
        obj.Position = 0;
        return true;
    }
}
```

**PlayHouse-Net 적용 대상**:
- `MemoryStream` (직렬화 버퍼)
- `StringBuilder` (로깅, 문자열 조합)

**구현 우선순위**: 낮음 (프로파일링 후)

**예상 효과**:
- Gen0 GC 5-10% 감소
- 복잡한 객체 생성 비용 절감

---

## 5. 구현 로드맵

### 5.1 우선순위별 작업 계획

#### Phase 1: Quick Wins (1-2주)

**목표**: 낮은 리스크로 빠른 개선

| 작업 | 복잡도 | 예상 효과 | 소요 시간 |
|------|--------|-----------|-----------|
| 1. struct IEquatable 구현 | 낮음 | Gen0 GC -5-10% | 2일 |
| 2. readonly struct 적용 | 낮음 | 복사 오버헤드 감소 | 1일 |
| 3. StringPool 확대 적용 | 낮음 | Gen0 GC -3-5% | 2일 |
| 4. GC 모니터링 추가 | 낮음 | 가시성 확보 | 1일 |

**총 소요**: 6일

**예상 효과**:
- Gen0 GC: -10-15%
- Gen2 GC: -5-10%
- TPS: +2-5%

#### Phase 2: ArrayPool 최적화 (2-3주)

**목표**: Gen2 GC 근본 해결

| 작업 | 복잡도 | 예상 효과 | 소요 시간 |
|------|--------|-----------|-----------|
| 1. PlayHouseArrayPool 구현 | 중간 | Gen2 GC -60-80% | 3일 |
| 2. 단위/통합 테스트 | 중간 | 안정성 확보 | 2일 |
| 3. 전체 시스템 적용 | 낮음 | 적용 범위 확대 | 1일 |
| 4. 벤치마크 및 튜닝 | 중간 | 최적값 도출 | 3일 |
| 5. 문서화 및 리뷰 | 낮음 | 유지보수성 | 1일 |

**총 소요**: 10일

**예상 효과**:
- Gen2 GC: 81회 → 16-24회 (-70% ~ -80%)
- TPS: 139K → 139-146K (유지 또는 +5%)
- 메모리: +10-15%

#### Phase 3: Advanced Optimization (3-4주)

**목표**: 극한 최적화 (필요시만)

| 작업 | 복잡도 | 예상 효과 | 소요 시간 |
|------|--------|-----------|-----------|
| 1. Span/stackalloc 적용 | 중간 | Gen0 GC -10-20% | 5일 |
| 2. Memory<T> async 파이프라인 | 높음 | 할당 감소 | 5일 |
| 3. Object Pooling (선택적) | 중간 | Gen0 GC -5-10% | 3일 |
| 4. GC Latency Mode 튜닝 | 낮음 | 지연시간 최적화 | 2일 |
| 5. LOH 모니터링 및 최적화 | 중간 | 큰 객체 처리 | 3일 |

**총 소요**: 18일

**예상 효과**:
- Gen0 GC: -20-30%
- Gen2 GC: -5-10% (추가)
- P99 Latency: -10-20%

### 5.2 성과 측정 기준

**Baseline** (ArrayPool.Shared):
- TPS: 139K
- P99 Latency: 11ms
- Gen0 GC: (미측정)
- Gen2 GC: 81회

**Phase 1 목표** (Quick Wins):
- TPS: 142K (+2%)
- P99 Latency: 10.5ms (-5%)
- Gen2 GC: 73회 (-10%)

**Phase 2 목표** (ArrayPool 최적화):
- TPS: 139-146K (유지 또는 +5%)
- P99 Latency: 10-11ms (유지)
- Gen2 GC: **16-24회** (-70% ~ -80%)

**Phase 3 목표** (Advanced):
- TPS: 150K+ (+8%)
- P99 Latency: 9ms (-18%)
- Gen0 GC: -30%
- Gen2 GC: 12-20회 (-75% ~ -85%)

### 5.3 Go/No-Go 기준

**Phase 1 → Phase 2 진행 조건**:
- Gen2 GC가 10% 이상 감소
- 회귀 없음 (TPS -5% 이내, Latency +10% 이내)

**Phase 2 → Phase 3 진행 조건**:
- Gen2 GC가 60% 이상 감소
- Phase 2 목표 달성 시 Phase 3은 선택적

**중단 조건**:
- 메모리 사용량 +30% 초과
- P99 Latency +20% 이상 증가
- 프로덕션 안정성 이슈 발생

---

## 6. 참고 자료

### 6.1 기술 문서

1. **ArrayPool 최적화**
   - [Adam Sitnik - Array Pool](https://adamsitnik.com/Array-Pool/)
   - [.NET Runtime PR #56316](https://github.com/dotnet/runtime/pull/56316) - Gen2GcCallback 구현

2. **GC 튜닝**
   - [MS Docs - GC Latency Modes](https://learn.microsoft.com/en-us/dotnet/standard/garbage-collection/latency)
   - [MS Docs - Server vs Workstation GC](https://learn.microsoft.com/en-us/dotnet/standard/garbage-collection/workstation-server-gc)

3. **Zero-Allocation 패턴**
   - [Dan.Net - Span and stackalloc](https://dev.to/danqzq/c-performance-optimization-using-span-and-stackalloc-to-eliminate-allocations-ikc)
   - [MS Docs - Memory<T> and Span<T>](https://learn.microsoft.com/en-us/dotnet/standard/memory-and-spans/)

4. **String 최적화**
   - [Code Maze - StringPool](https://code-maze.com/csharp-use-stringpool-to-reduce-string-allocations/)
   - [CommunityToolkit.HighPerformance](https://github.com/CommunityToolkit/dotnet)

5. **GC Pressure 회피**
   - [Michael's Coding Spot - Avoid GC Pressure](https://michaelscodingspot.com/avoid-gc-pressure/)

### 6.2 실전 사례

1. **NDC 2011 - 마비노기 영웅전** (데브캣 스튜디오)
   - [NDC 발표 자료](https://www.slideshare.net/ssusere2065c/ndc-public)
   - C# 기반 최초 MO/MMORPG 서버
   - 주요 교훈:
     - GC는 초기 이후 큰 이슈 아니었음 (적절한 설계 전제)
     - GC 친화적 코드 작성 (stack value object 등)
     - 단일 스레드 로직 + 멀티스레딩 분리 아키텍처

2. **.NET Runtime 팀 최적화 사례**
   - ASP.NET Core: TechEmpower 벤치마크 1위 달성
   - kestrel: zero-copy pipeline, Span<T> 적극 활용
   - Orleans: actor 시스템에서 GC 최적화

### 6.3 도구

1. **프로파일링**
   - dotMemory (JetBrains)
   - PerfView (Microsoft)
   - BenchmarkDotNet

2. **모니터링**
   - Prometheus + Grafana
   - Application Insights
   - EventCounters

---

## 7. 결론 및 권장사항

### 7.1 핵심 권장사항

1. **PlayHouseArrayPool 구현** (우선순위 1)
   - Gen2 GC 81회 → 16-24회 목표
   - `MaxBuffersPerArraySizePerCore = 32` 권장
   - 구현 복잡도 중간, 효과 매우 큼

2. **struct IEquatable 구현** (우선순위 2)
   - 간단하고 즉각적인 효과
   - boxing 제거로 Gen0 GC 감소

3. **StringPool 확대 적용** (우선순위 3)
   - 이미 일부 적용, 확대 적용
   - 문자열 중복 제거

4. **GC 모니터링 강화** (우선순위 4)
   - Gen0/Gen1/Gen2 분리 측정
   - 프로덕션 대시보드 구축

### 7.2 예상 최종 성과

**Phase 2 완료 시**:

| 메트릭 | Before (MessagePool) | Current (ArrayPool.Shared) | After (PlayHouseArrayPool) | 총 개선 |
|--------|----------------------|----------------------------|----------------------------|---------|
| TPS | 109K | 139K | 142-146K | **+30-34%** |
| P99 Latency | 20ms | 11ms | 10-11ms | **-45-50%** |
| Gen2 GC | 16회 | 81회 | 16-24회 | **0-50%** |
| 메모리 | 기준 | ? | +10-15% | +10-15% |

**결론**: ArrayPool.Shared의 성능 이득을 유지하면서 Gen2 GC를 원래 수준으로 복원 가능.

### 7.3 장기 전략

1. **지속적인 모니터링**
   - 프로덕션 GC 메트릭 수집
   - 워크로드 변화에 따른 튜닝

2. **.NET 버전 업그레이드**
   - .NET 9/10의 GC 개선사항 활용
   - Dynamic PGO, On-Stack Replacement 등

3. **아키텍처 진화**
   - Actor 모델 성능 프로파일링
   - 핫패스 zero-allocation 전환

4. **지식 공유**
   - 팀 내부 GC 최적화 가이드 작성
   - 코드 리뷰 체크리스트 업데이트

---

**문서 버전**: 1.0
**최종 검토**: 2025-12-29
**작성자**: PlayHouse-Net Architecture Team
**다음 리뷰**: Phase 2 완료 후 (예정: 2025-02)
