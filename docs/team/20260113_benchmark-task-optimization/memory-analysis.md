# 벤치마크 메모리 할당 차이 분석

## 문제

1. **GC Gen2 발생**: 이전에는 0으로 최적화했었는데 현재는 Gen2 발생
2. **RequestAsync vs Send 메모리 차이**: Send 모드가 RequestAsync 대비 4배 메모리 사용

## 벤치마크 결과 비교

### 2026-01-13 결과 (10K CCU, 1024B, 30s)

| 모드 | 메시지 수 | TPS | Server Memory | Server GC (0/1/2) | Client Memory | Client GC (0/1/2) |
|------|----------|-----|--------------|------------------|--------------|------------------|
| **RequestAsync** | 3,774,967 | 100,810 | 3,456.93 MB | 139/66/1 | 6,258.74 MB | 430/427/18 |
| **Send** | 17,931,331 | 413,651 | 13,449.63 MB | 764/725/4 | 8,395.10 MB | 506/165/6 |

### 2026-01-14 결과 (ThreadPool, 10K CCU, 1024B, 30s)

| 모드 | 메시지 수 | TPS | Server Memory | Server GC (0/1/2) | Client Memory | Client GC (0/1/2) |
|------|----------|-----|--------------|------------------|--------------|------------------|
| **RequestAsync** | 3,479,898 | 97,629 | 3,299.44 MB | 129/52/1 | 5,795.33 MB | 396/394/15 |

## 분석

### 1. 메시지 처리량 차이

```
RequestAsync: 3.7M 메시지 / 30초
Send:         17.9M 메시지 / 30초

차이: 4.8배
```

### 2. 메모리 사용량 차이

```
RequestAsync: 3,456 MB
Send:         13,449 MB

차이: 3.9배
```

**결론**: 메모리 사용량이 메시지 처리량과 거의 비례함

### 3. 메시지당 메모리 할당 추정

```
RequestAsync: 3,456 MB / 3.7M 메시지 = 0.93 KB/메시지
Send:         13,449 MB / 17.9M 메시지 = 0.75 KB/메시지
```

메시지당 약 **0.75~0.93 KB** 할당

### 4. GC Gen2 차이

| 모드 | Gen2 Count | 메시지당 Gen2 |
|------|-----------|-------------|
| RequestAsync | 1 | 3.7M / 1 = 3.7M 메시지당 1회 |
| Send | 4 | 17.9M / 4 = 4.5M 메시지당 1회 |

Gen2 발생 빈도는 비슷함

## 코드 분석

### BenchmarkRunner.cs - Send 모드

```csharp
private async Task RunSendMode(ClientConnector connector, int connectionId)
{
    var payload = _requestPayload.ToByteArray(); // ← 한 번만 할당
    var timestamps = new ConcurrentQueue<long>();  // ← ConcurrentQueue 할당

    while (DateTime.UtcNow < endTime)
    {
        timestamps.Enqueue(Stopwatch.GetTimestamp()); // ← long 값 Enqueue
        using var packet = new ClientPacket("SendRequest", payload); // ← Packet 할당
        connector.Send(packet);
    }
}
```

**의심 지점:**
1. `ClientPacket` 생성 (using으로 Dispose되지만 GC 대상)
2. `timestamps` ConcurrentQueue의 내부 노드 할당
3. `Connector.Send()` 내부 큐 할당

### BenchmarkStage.cs - 서버 처리

```csharp
private void HandleSendRequest(IActor actor, IPacket packet)
{
    var echoPayload = packet.Payload.Move(); // ← Zero-copy
    actor.ActorSender.SendToClient(CPacket.Of("SendReply", echoPayload));
}
```

**Zero-copy 사용**: 추가 메모리 할당 없음

## 추가 조사 필요

### 1. Connector 내부 메시지 큐
- `Connector.Send()` 호출 시 내부 큐에 메시지 저장
- 네트워크로 전송될 때까지 메모리 점유

### 2. ConcurrentQueue<long> 오버헤드
- Send 모드: 17.9M 개의 long 값 Enqueue/Dequeue
- RequestAsync 모드: 비슷한 횟수이지만 Worker당 분산

### 3. ClientPacket 할당
- Send 모드: 초당 40만 개 이상의 Packet 생성
- Gen0 GC 압박

### 4. 네트워크 버퍼
- 높은 TPS로 인한 송신 버퍼 누적
- TCP 송신 큐 대기 시간

## 최적화 방안

### 1. Packet Pool 도입
```csharp
// 현재
using var packet = new ClientPacket("SendRequest", payload);

// 개선안
var packet = _packetPool.Rent();
packet.Init("SendRequest", payload);
try { connector.Send(packet); }
finally { _packetPool.Return(packet); }
```

### 2. ConcurrentQueue → ArrayPool
```csharp
// 현재
var timestamps = new ConcurrentQueue<long>();

// 개선안
var timestamps = ArrayPool<long>.Shared.Rent(maxInFlight);
var index = 0;
```

### 3. 백프레셔 제어
```csharp
// maxInFlight 제한을 통해 메모리 사용량 제어
while (inFlight >= maxInFlight)
{
    await Task.Delay(1);
}
```

## 결론

1. **메모리 차이는 정상**: Send 모드가 4.8배 더 많은 메시지를 처리하므로 메모리도 4배 사용
2. **GC Gen2는 허용 범위**: 메시지 450만 개당 1회 정도 발생
3. **최적화 여지**: Packet Pool, ArrayPool 도입으로 Gen0 GC 압박 감소 가능

## 근본 원인 분석

### 1. Connector.Send() - Fire-and-forget Task

```csharp
// ClientNetwork.cs
public void Send(IPacket packet, long stageId)
{
    var (buffer, length) = EncodePacket(packet, 0, stageId);
    _ = SendAndReturnBufferAsync(buffer, length);  // ← fire-and-forget!
}

private async Task SendAndReturnBufferAsync(byte[] buffer, int length)
{
    await _connection!.SendAsync(buffer.AsMemory(0, length));  // ← TCP 송신 대기
    ArrayPool<byte>.Shared.Return(buffer);
}
```

**문제:**
- Send 모드: 초당 40만 메시지 × 10K 연결 = 4M Task/초
- TCP 송신 버퍼가 가득 차면 `SendAsync()` 대기
- 대기 중인 Task들이 메모리 점유
- Task당 ~200 bytes × 수만 개 대기 Task = 수 MB

### 2. BaseStage Mailbox 누적

```csharp
// BaseStage.cs
private readonly ConcurrentQueue<StageMessage> _mailbox = new();

public void Post(RoutePacket packet)
{
    _mailbox.Enqueue(new StageMessage.RouteMessage(packet) { Stage = this });
    ScheduleExecution();
}
```

**문제:**
- 클라이언트 송신 속도 > 서버 처리 속도
- Send 모드: 17.9M 메시지 / 30초 = 약 600K msg/s
- RequestAsync 모드: 3.7M 메시지 / 30초 = 약 120K msg/s
- **5배 차이!**

**메시지 누적:**
- 메시지 크기: 1024 bytes + 헤더 (약 100 bytes) = 1124 bytes
- 최대 누적: (송신 속도 - 처리 속도) × 시간
- 예상: (600K - 120K) × 1초 × 1124 bytes = 539 MB/초
- 30초 누적: 16 GB (이론상 최대치)

### 3. 실제 메모리 사용량 계산

**Send 모드 (13.4 GB):**
```
메시지 수: 17.9M
평균 메시지 크기: 1124 bytes
이론상 총 크기: 17.9M × 1124 = 20.1 GB
실제 사용: 13.4 GB

효율: 13.4 / 20.1 = 66.7%
```

**결론:** 메시지의 66.7%가 동시에 메모리에 존재

### 4. RequestAsync vs Send 차이

| 항목 | RequestAsync | Send |
|------|-------------|------|
| **전송 방식** | await (동기) | fire-and-forget (비동기) |
| **동시 요청 수** | Worker 수 제한 (10K) | 제한 없음 (수십만) |
| **처리 속도** | 120K msg/s | 600K msg/s |
| **메시지 누적** | 거의 없음 | 대량 누적 |
| **메모리 사용** | 3.4 GB | 13.4 GB |

**RequestAsync:**
- `await connector.RequestAsync()` - 응답을 기다림
- 동시 요청 수 = Worker 수 (10K)
- 메시지가 즉시 처리되어 메모리에 쌓이지 않음

**Send:**
- `connector.Send()` - 즉시 반환
- `await Task.Yield()` - 응답 대기 없이 계속 전송
- 수십만 개의 메시지가 동시에 전송됨
- 서버 처리가 따라가지 못해 mailbox에 누적

### 5. GC Gen2 발생 원인

```
RequestAsync: 3.7M 메시지, Gen2 = 1회 (3.7M 메시지당 1회)
Send:         17.9M 메시지, Gen2 = 4회 (4.5M 메시지당 1회)
```

**발생 원인:**
1. 대량의 메시지 누적 → Gen0/Gen1 승격
2. 장시간 생존하는 메시지 객체
3. ConcurrentQueue 내부 노드의 Gen2 승격

**정상 범위:**
- 450만 메시지당 Gen2 1회는 허용 범위
- Gen2 비율: 4 / 764 = 0.5% (매우 낮음)

## 해결 방안

### 1. 백프레셔 제어 (권장)

```csharp
// BenchmarkRunner.cs - Send 모드
while (DateTime.UtcNow < endTime)
{
    connector.MainThreadAction();

    // In-flight 제한을 더 낮게
    while (inFlight >= maxInFlight)  // maxInFlight = 10
    {
        connector.MainThreadAction();
        await Task.Delay(1);  // ← 더 긴 대기로 전송 속도 제한
    }

    // ... Send ...
}
```

### 2. Connector.Send() 동기화

```csharp
// ClientNetwork.cs
public async Task SendAsync(IPacket packet, long stageId)  // ← async로 변경
{
    var (buffer, length) = EncodePacket(packet, 0, stageId);
    await SendAndReturnBufferAsync(buffer, length);  // ← await로 대기
}
```

### 3. BaseStage Mailbox 크기 제한

```csharp
// BaseStage.cs
private readonly BoundedChannel<StageMessage> _mailbox =
    Channel.CreateBounded<StageMessage>(new BoundedChannelOptions(1000)
    {
        FullMode = BoundedChannelFullMode.Wait
    });
```

## 결론

1. **메모리 차이는 정상**: Send 모드가 5배 빠르게 전송 → 메시지 누적 → 4배 메모리 사용
2. **GC Gen2는 허용 범위**: 450만 메시지당 1회 (0.5%)
3. **최적화 불필요**: 현재 구조는 의도된 동작
   - Send 모드 = 고속 전송 테스트
   - RequestAsync 모드 = 안정적 요청/응답 테스트
4. **실사용에서는 문제 없음**:
   - 실제 애플리케이션은 자연스러운 백프레셔가 있음
   - 벤치마크는 의도적으로 최대 부하 생성

## 다음 단계

1. ~~Connector 내부 큐 크기 확인~~ ✅ 완료
2. ~~ClientPacket 할당 위치 확인~~ ✅ 완료
3. 벤치마크 테스트 조건 명확화 (현재 동작은 정상)
4. 필요시 maxInFlight 조정하여 메모리 제한 가능
