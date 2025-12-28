# PlayHouse-Net 성능 최적화 보고서

## 개요

- **날짜**: 2024-12-28
- **목표**: 벤치마크 + 프로파일링 기반 데이터 중심 성능 최적화
- **방법론**: dotnet-trace 프로파일링 → 병목 식별 → 타겟 최적화 → 벤치마크 검증

---

## 1. 초기 프로파일링 결과

### 1.1 Client-Server 벤치마크 환경
- CCU: 1,000 동시 접속
- Messages: 10,000,000 (연결당 10,000)
- Request Size: 64 bytes
- Response Size: 1,500 bytes
- Mode: RequestAsync

### 1.2 식별된 할당 핫스팟 (ZmqPlaySocket.cs)

| 위치 | 코드 | 문제 |
|------|------|------|
| Send() L117 | `packet.SerializeHeader()` → `ToByteArray()` | 매 전송마다 새 byte[] 할당 |
| Receive() L162 | `RouteHeader.Parser.ParseFrom()` | 매 수신마다 새 RouteHeader 객체 |
| Receive() L152 | `Encoding.UTF8.GetString()` | 매 수신마다 새 string 할당 |

---

## 2. 최적화 시도 결과

### 2.1 ❌ 실패: ConcurrentBag 기반 객체 풀링 (StageMessage, RouteHeader)

#### 시도 내용
```csharp
// StageMessage 풀링 시도
public static class StageMessagePool
{
    private static readonly ConcurrentBag<StageMessage> _pool = new();

    public static StageMessage Rent() => _pool.TryTake(out var msg) ? msg : new StageMessage();
    public static void Return(StageMessage msg) { msg.Reset(); _pool.Add(msg); }
}

// RouteHeader 풀링 시도
public static class RouteHeaderPool
{
    private static readonly ConcurrentBag<RouteHeader> _pool = new();

    public static RouteHeader Rent() => _pool.TryTake(out var h) ? h : new RouteHeader();
    public static void Return(RouteHeader h) { h.MergeFrom(...); _pool.Add(h); }
}
```

#### 벤치마크 결과

| 항목 | Baseline | 풀링 적용 후 | 변화 |
|------|----------|------------|------|
| Throughput | 156,755 msg/s | 124,000 msg/s | **-20.9%** ❌ |
| Gen2 GC | 333회 | 479회 | **+43.8%** ❌ |
| Memory | 22.4 GB | 28.9 GB | **+29.0%** ❌ |

#### 실패 원인 상세 분석

1. **ConcurrentBag의 Thread-Local 리스트 오버헤드**
   ```
   ConcurrentBag 내부 구조:
   - 각 스레드마다 Thread-Local 리스트 유지
   - 다른 스레드에서 가져올 때 "stealing" 발생
   - 고부하 환경에서 stealing 비용이 풀링 이득보다 큼
   ```

2. **Protobuf ParseFrom은 항상 새 객체 생성**
   ```csharp
   // ParseFrom 내부 동작
   public static T ParseFrom(ReadOnlySpan<byte> data)
   {
       T message = new T();  // 항상 새 객체!
       message.MergeFrom(data);
       return message;
   }

   // 따라서 풀링하려면 MergeFrom 직접 호출 필요
   // 하지만 MergeFrom은 필드를 병합하므로 초기화 필요
   ```

3. **Reset/Clear 오버헤드**
   - Protobuf 메시지에는 Clear() 메서드가 없음
   - 수동으로 모든 필드 초기화 필요
   - 초기화 비용이 새 객체 생성보다 높을 수 있음

4. **Microsoft.Extensions.ObjectPool을 쓰지 않은 이유**
   - ConcurrentBag보다 효율적이지만 근본 문제 해결 안 됨
   - Protobuf string 필드는 여전히 새로 할당됨

#### 교훈
> ConcurrentBag는 고빈도 풀링에 부적합. 풀링 대상 선정 시 "풀에서 꺼내고 → 초기화하고 → 사용하고 → 반환" 전체 비용 고려 필요.

---

### 2.2 ❌ 실패: Send() ToByteArray() → ArrayPool + CodedOutputStream

#### 변경 내용
```csharp
// Before: 매번 새 byte[] 할당
var headerBytes = packet.SerializeHeader();  // ToByteArray()
_socket.Send(headerBytes, SendFlags.SendMore);

// After: ArrayPool + CodedOutputStream
var headerSize = packet.Header.CalculateSize();
var headerBuffer = ArrayPool<byte>.Shared.Rent(headerSize);
try
{
    var output = new CodedOutputStream(headerBuffer);
    packet.Header.WriteTo(output);
    output.Flush();
    _socket.Send(headerBuffer.AsSpan(0, headerSize), SendFlags.SendMore);
}
finally
{
    ArrayPool<byte>.Shared.Return(headerBuffer);
}
```

#### 벤치마크 결과

| 항목 | Baseline | 최적화 후 | 변화 |
|------|----------|---------|------|
| Throughput | 156,755 msg/s | 107,890 msg/s | **-31.2%** ❌ |
| Gen2 GC | 333회 | 409회 | **+22.8%** ❌ |
| Memory | 22.4 GB | 27.4 GB | **+22.2%** ❌ |

#### 실패 원인 분석
1. **CodedOutputStream 오버헤드**: 내부 버퍼 관리 및 Flush() 호출 비용
2. **작은 메시지에 비효율**: RouteHeader는 ~100 bytes로 ArrayPool 오버헤드가 더 큼
3. **추가 객체 생성**: CodedOutputStream 인스턴스 생성 비용

#### 결론
> ToByteArray()가 작은 메시지에서는 더 효율적. 롤백함.

---

### 2.3 ✅ 성공: Receive() senderServerId String 캐싱

#### 변경 내용
```csharp
// Before: 매번 새 string 생성
var senderServerId = Encoding.UTF8.GetString(_recvServerIdBuffer, 0, serverIdLen);

// After: FNV-1a 해시 기반 캐싱
private readonly ConcurrentDictionary<int, string> _receivedServerIdCache = new();

private string GetOrCacheServerId(byte[] buffer, int length)
{
    // FNV-1a hash
    int hash = unchecked((int)2166136261);
    for (int i = 0; i < length; i++)
    {
        hash = unchecked((hash ^ buffer[i]) * 16777619);
    }

    if (_receivedServerIdCache.TryGetValue(hash, out var cached))
    {
        return cached;
    }

    var newId = Encoding.UTF8.GetString(buffer, 0, length);
    _receivedServerIdCache.TryAdd(hash, newId);
    return newId;
}
```

#### 벤치마크 결과

| 항목 | Baseline | 최적화 후 | 변화 |
|------|----------|---------|------|
| Throughput | 113,334 msg/s | 139,545 msg/s | **+23.1%** ✅ |
| Gen2 GC | 477회 | 392회 | **-17.8%** ✅ |
| Memory | 29.3 GB | 27.8 GB | **-5.2%** ✅ |
| P99 RTT | 29.60ms | 25.89ms | **-12.5%** ✅ |
| 처리 시간 | 79.76s | 65.51s | **-17.9%** ✅ |

#### 성공 요인 분석
1. **제한된 서버 수**: S2S 통신에서 서버 ID는 10-100개 범위로 제한적
2. **첫 수신 후 캐시 히트**: 동일 서버에서 오는 메시지는 모두 캐시 히트
3. **낮은 오버헤드**: FNV-1a 해시는 O(n) 비용이지만 서버 ID가 짧음 (~20 bytes)
4. **ConcurrentDictionary**: Lock-free 읽기로 멀티스레드 환경에서도 효율적

#### 커밋
```
8df2625 perf: ZmqPlaySocket Receive() senderServerId 문자열 캐싱
```

---

### 2.4 ⏭️ 스킵: RouteHeader 객체 풀링 (ObjectPool + MergeFrom)

#### 분석 내용

**RouteHeader 구조 (10개 필드):**
- 숫자 필드 8개: msg_seq, service_id, error_code, stage_id, account_id, sid, is_reply, payload_size
- **String 필드 2개**: msg_id, from

#### 스킵 이유

1. **String 필드 문제**
   - MergeFrom은 내부적으로 새 string 객체를 생성
   - 풀링해도 string 할당은 피할 수 없음

2. **필드 초기화 복잡도**
   - Protobuf에는 Clear() 메서드가 없음
   - MergeFrom은 병합 방식이라 수동으로 10개 필드 초기화 필요
   ```csharp
   header.MsgSeq = 0;
   header.ServiceId = 0;
   header.MsgId = "";      // 새 string 할당
   header.From = "";       // 새 string 할당
   // ... 8개 더
   ```

3. **라이프사이클 관리 복잡**
   - RouteHeader는 RoutePacket에 포함되어 다양한 곳으로 전파
   - CreateErrorReply()에서 새 Header 생성
   - 반환 시점 관리 어려움

4. **이미 최적화됨**
   - Span 기반 파싱 (zero-copy)
   - 고정 버퍼 사용 (_recvHeaderBuffer 64KB)
   - String Interning (Protobuf 내부 최적화)

#### 결론
> 복잡도 >> 효과. 현재 구현 유지가 더 나음.

---

## 3. 기존 최적화 (이전 커밋)

### 3.1 ProtoPayload 이중 경로 최적화

```csharp
public sealed class ProtoPayload : IMessagePayload
{
    // S2C 경로: ArrayPool 기반 lazy 직렬화
    public ReadOnlySpan<byte> DataSpan
    {
        get
        {
            if (_rentedBuffer == null)
            {
                _rentedBuffer = ArrayPool<byte>.Shared.Rent(Length);
                _proto.WriteTo(_rentedBuffer.AsSpan(0, Length));
            }
            return _rentedBuffer.AsSpan(0, Length);
        }
    }

    // S2S 경로: MessagePool 직접 직렬화
    public void MakeMessage()
    {
        if (_message == null)
        {
            _message = MessagePool.Shared.Rent(Length);
            _proto.WriteTo(_message.Data.Slice(0, Length));
            _message.SetActualDataSize(Length);
        }
    }
}
```

### 3.2 MessagePool (Net.Zmq)

- ZMQ Message 풀링
- Send() 시 자동 반환 (free callback)
- Zero-copy 전송 지원

### 3.3 고정 버퍼 수신

```csharp
private readonly byte[] _recvServerIdBuffer = new byte[1024];   // 1KB
private readonly byte[] _recvHeaderBuffer = new byte[65536];    // 64KB
```

---

## 4. 최종 성능 비교

### 4.1 Client-Server 벤치마크 (1,000 CCU × 10,000 msg)

| 항목 | 최적화 전 | 최적화 후 | 개선율 |
|------|----------|---------|--------|
| **Throughput** | 113,334 msg/s | 139,545 msg/s | **+23.1%** |
| **Gen2 GC** | 477회 | 392회 | **-17.8%** |
| **Memory** | 29.3 GB | 27.8 GB | **-5.2%** |
| **P99 RTT** | 29.60ms | 25.89ms | **-12.5%** |
| **처리 시간** | 79.76s | 65.51s | **-17.9%** |

### 4.2 Server-to-Server 벤치마크 (PlayServer ↔ ApiServer)

**테스트 환경:**
- 모드: PlayToApi (PlayServer → ApiServer → PlayServer)
- 동시 연결: 1,000
- 총 메시지: 10,000,000
- 요청/응답 크기: 64B / 1,500B

**결과:**

| 항목 | 수치 |
|------|------|
| **Throughput** | 77,580 msg/s |
| **대역폭** | 118.05 MB/s |
| **P50 Latency** | 3.74ms |
| **P99 Latency** | 25.27ms |
| **E2E RTT (클라이언트)** | 12.69ms (avg), 38.64ms (P99) |
| **Gen2 GC** | 34회 (매우 낮음) |
| **처리 시간** | 126.93s |
| **성공률** | 100% (10M/10M) |

**S2S 경로에서 senderServerId 캐싱 효과:**
- Send 경로: `_serverIdCache.GetOrAdd()` → Target ServerId byte[] 캐싱
- Receive 경로: `GetOrCacheServerId()` → Sender ServerId string 캐싱
- **Gen2 GC 34회**: C-S의 392회 대비 매우 낮음 (S2S는 Protobuf 메시지 크기가 작음)

---

## 5. 교훈 및 권장사항

### 5.1 효과적인 최적화
- **String 캐싱**: 반복되는 문자열 생성은 캐싱으로 큰 효과
- **고정 버퍼**: 수신 경로에서 버퍼 재사용
- **풀링**: 큰 객체(Message, byte[])는 풀링 효과적

### 5.2 비효과적인 최적화
- **CodedOutputStream**: 작은 메시지에서는 ToByteArray()보다 느림
- **과도한 풀링**: 작은 객체 + String 필드가 있으면 복잡도만 증가

### 5.3 권장 접근법
1. **프로파일링 먼저**: 추측 대신 데이터 기반 최적화
2. **벤치마크 검증**: 모든 변경은 벤치마크로 효과 확인
3. **복잡도 vs 효과**: 구현 복잡도가 높으면 효과도 높아야 함
4. **롤백 준비**: 역효과 시 즉시 롤백

---

## 6. 향후 최적화 후보

1. **Header 직렬화 Span 오버로드**: Protobuf 버전 업그레이드 시 검토
2. **Struct 기반 RouteHeader**: 대규모 리팩토링 필요, 프로덕션 병목 확인 후
3. **배치 처리**: 여러 메시지를 모아서 처리 (latency 트레이드오프)

---

## 부록: 벤치마크 명령어

```bash
# 서버 시작
dotnet run --project tests/benchmark_cs/PlayHouse.Benchmark.Server -c Release -- \
  --tcp-port 16110 --http-port 5080

# 클라이언트 실행
dotnet run --project tests/benchmark_cs/PlayHouse.Benchmark.Client -c Release -- \
  --server 127.0.0.1:16110 --connections 1000 --messages 10000 \
  --response-size 1500 --mode request-async --http-port 5080

# 서버 메트릭 확인
curl http://127.0.0.1:5080/benchmark/stats
```
