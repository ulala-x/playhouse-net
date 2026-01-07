# PlayHouse 기존 구현 참고 문서

## 1. 개요

이 문서는 기존 PlayHouse 코드베이스(`D:\project\kairos\playhouse\playhouse-net\PlayHouse`)에서 PlayHouse-NET 구현 시 참고할 만한 패턴과 코드를 정리합니다.

### 1.1 재사용 우선순위

| 우선순위 | 컴포넌트 | 재사용 방식 | 비고 |
|---------|---------|------------|------|
| **높음** | AtomicBoolean, AtomicEnum | 그대로 복사 | 검증된 Lock-Free 패턴 |
| **높음** | TimerManager | 패턴 참고 | Stage 이벤트 루프 통합 |
| **높음** | RequestCache | 패턴 참고 | Request-Reply 상관관계 |
| **중간** | RoutePacket | 구조 참고 후 단순화 | 패킷 라우팅 |
| **중간** | Lz4Holder | 그대로 복사 | 압축 싱글톤 |
| **낮음** | XSender | 단순화 필요 | 멀티서버 코드 제거 |
| ~~제거~~ | ~~RingBuffer, PooledByteBuffer~~ | ~~불필요~~ | ~~Pipelines가 대체~~ |
| ~~제거~~ | ~~PacketParser~~ | ~~재작성~~ | ~~Pipelines 기반으로~~ |

---

## 2. 핵심 재사용 컴포넌트

### 2.1 AtomicBoolean (그대로 복사)

**위치**: `Infrastructure/Common/Utils/AtomicBoolean.cs`

```csharp
namespace PlayHouse.Infrastructure.Common.Utils;

public class AtomicBoolean
{
    private int _value;

    public AtomicBoolean(bool initialValue)
    {
        _value = initialValue ? 1 : 0;
    }

    public bool CompareAndSet(bool expected, bool update)
    {
        var expectedValue = expected ? 1 : 0;
        var newValue = update ? 1 : 0;
        return Interlocked.CompareExchange(ref _value, newValue, expectedValue) == expectedValue;
    }

    public bool Get()
    {
        return Interlocked.CompareExchange(ref _value, 0, 0) != 0;
    }

    public void Set(bool newValue)
    {
        Interlocked.Exchange(ref _value, newValue ? 1 : 0);
    }
}
```

**사용처**:
- `BaseStage.Post()` - 이벤트 루프 진입 제어
- Lock-Free 플래그 관리

**재사용 가치**: ⭐⭐⭐⭐⭐
- 검증된 CAS 패턴
- 이벤트 루프 구현의 핵심

---

### 2.2 TimerManager (패턴 참고)

**위치**: `Core/Shared/TimerManager.cs`

```csharp
internal class TimerManager(IPlayDispatcher dispatcher)
{
    private readonly ConcurrentDictionary<long, Timer> _timers = new();

    public long RegisterRepeatTimer(long stageId, long timerId, long initialDelay, long period,
        TimerCallbackTask timerCallback)
    {
        var timer = new Timer(timerState =>
        {
            // 타이머 콜백을 메시지로 변환하여 Stage에 전달
            var routePacket = RoutePacket.StageTimerOf(stageId, timerId, timerCallback, timerState);
            dispatcher.OnPost(routePacket);
        }, null, initialDelay, period);

        _timers[timerId] = timer;
        return timerId;
    }

    public long RegisterCountTimer(long stageId, long timerId, long initialDelay, int count, long period,
        TimerCallbackTask timerCallback)
    {
        var remainingCount = count;

        var timer = new Timer(timerState =>
        {
            if (remainingCount > 0)
            {
                var routePacket = RoutePacket.StageTimerOf(stageId, timerId, timerCallback, timerState);
                dispatcher.OnPost(routePacket);
                remainingCount--;
            }
            else
            {
                CancelTimer(timerId);
            }
        }, null, initialDelay, period);

        _timers[timerId] = timer;
        return timerId;
    }

    public void CancelTimer(long timerId)
    {
        if (_timers.TryGetValue(timerId, out var timer))
        {
            timer.Dispose();
            _timers.Remove(timerId, out _);
        }
    }
}
```

**핵심 패턴**:
1. **타이머 콜백 → 메시지 변환**: System.Threading.Timer 콜백에서 직접 Stage 상태 접근 안함
2. **Dispatcher를 통한 전달**: 타이머 이벤트를 Stage 메시지 큐로 전달
3. **Stage 컨텍스트 실행**: 실제 콜백은 Stage 이벤트 루프에서 실행

**재사용 가치**: ⭐⭐⭐⭐⭐
- 타이머-Stage 통합 패턴의 정석
- CountTimer의 remainingCount 관리 방식

---

### 2.3 RequestCache (패턴 참고)

**위치**: `Runtime/RequestCache.cs`

```csharp
internal class ReplyObject(
    ReplyCallback? callback = null,
    TaskCompletionSource<RoutePacket>? taskCompletionSource = null)
{
    private readonly DateTime _requestTime = DateTime.UtcNow;

    public void OnReceive(RoutePacket routePacket)
    {
        if (callback != null)
        {
            using (routePacket)
            {
                callback?.Invoke(routePacket.ErrorCode, CPacket.Of(routePacket));
            }
        }

        if (routePacket.ErrorCode == 0)
        {
            taskCompletionSource?.TrySetResult(routePacket);
        }
        else
        {
            Throw(routePacket.ErrorCode);
        }
    }

    public void Throw(ushort errorCode)
    {
        taskCompletionSource?.TrySetException(
            new PlayHouseException($"request has exception - errorCode:{errorCode}", errorCode));
    }

    public bool IsExpired(int timeoutMs)
    {
        var difference = DateTime.UtcNow - _requestTime;
        return difference.TotalMilliseconds > timeoutMs;
    }
}

internal class RequestCache(int timeout)
{
    private readonly ConcurrentDictionary<int, ReplyObject> _cache = new();
    private readonly AtomicShort _sequence = new();

    public ushort GetSequence()
    {
        return _sequence.IncrementAndGet();
    }

    public void Put(int seq, ReplyObject replyObject)
    {
        _cache[seq] = replyObject;
    }

    public void OnReply(RoutePacket routePacket)
    {
        int msgSeq = routePacket.Header.MsgSeq;
        var replyObject = _cache.GetValueOrDefault(msgSeq);

        if (replyObject != null)
        {
            replyObject.OnReceive(routePacket);
            _cache.TryRemove(msgSeq, out _);
        }
    }
}
```

**핵심 패턴**:
1. **MsgSeq 기반 상관관계**: Request와 Reply를 sequence number로 매칭
2. **TaskCompletionSource**: async/await 패턴 지원
3. **타임아웃 처리**: 만료된 요청 자동 정리

**재사용 가치**: ⭐⭐⭐⭐
- Request-Reply 패턴의 표준 구현
- 타임아웃 로직 참고

---

### 2.4 Lz4Holder (그대로 복사)

**위치**: `Infrastructure/Common/Compression/Lz4Holder.cs`

```csharp
public class Lz4Holder
{
    private readonly Lz4? _lz4;
    private static readonly Lazy<Lz4Holder> _instance = new(() => new Lz4Holder());

    public static Lz4Holder Instance => _instance.Value;

    private Lz4Holder()
    {
        _lz4 = new Lz4();
    }

    public ReadOnlySpan<byte> Compress(ReadOnlySpan<byte> input)
    {
        return _lz4!.Compress(input);
    }

    public ReadOnlySpan<byte> Decompress(ReadOnlySpan<byte> compressed, int originalSize)
    {
        return _lz4!.Decompress(compressed, originalSize);
    }
}
```

**재사용 가치**: ⭐⭐⭐⭐⭐
- Thread-safe 싱글톤
- 간단한 API

---

## 3. 참고할 패턴

### 3.1 이벤트 루프 패턴 (BaseStage.Post)

**위치**: `Core/Play/Base/BaseStage.cs:93-119`

```csharp
public void Post(RoutePacket routePacket)
{
    _msgQueue.Enqueue(routePacket);

    if (_isUsing.CompareAndSet(false, true))
    {
        Task.Run(async () =>
        {
            while (_msgQueue.TryDequeue(out var item))
            {
                try
                {
                    using (item)
                    {
                        await Dispatch(item);
                    }
                }
                catch (Exception e)
                {
                    StageSender.Reply((ushort)BaseErrorCode.UncheckedContentsError);
                    _log.Error(() => $"{e}");
                }
            }
            _isUsing.Set(false);
        });
    }
}
```

**핵심 패턴**:
1. **ConcurrentQueue + AtomicBoolean**: Lock-Free 진입
2. **단일 async Task**: await 지원
3. **while + TryDequeue**: 배치 처리
4. **using (item)**: 리소스 정리

**11-event-loop-messaging.md에 상세 문서화됨**

---

### 3.2 패킷 구조 패턴

**위치**: `Runtime/Message/RoutePacket.cs`

```
패킷 레이아웃 (클라이언트 → 서버):
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

┌──────────┬───────────┬──────────┬─────────┬─────────┬─────────┬─────────┐
│BodySize  │ ServiceId │ MsgIdLen │ MsgId   │ MsgSeq  │ StageId │ Payload │
│ (4byte)  │ (2byte)   │ (1byte)  │ (N byte)│ (2byte) │ (8byte) │ (var)   │
└──────────┴───────────┴──────────┴─────────┴─────────┴─────────┴─────────┘

패킷 레이아웃 (서버 → 클라이언트):
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

┌──────────┬───────────┬──────────┬─────────┬─────────┬─────────┬───────────┬────────────┬─────────┐
│BodySize  │ ServiceId │ MsgIdLen │ MsgId   │ MsgSeq  │ StageId │ ErrorCode │ OriginalSz │ Payload │
│ (4byte)  │ (2byte)   │ (1byte)  │ (N byte)│ (2byte) │ (8byte) │ (2byte)   │ (4byte)*   │ (var)   │
└──────────┴───────────┴──────────┴─────────┴─────────┴─────────┴───────────┴────────────┴─────────┘

* OriginalSize는 압축된 경우에만 포함 (BodySize >= 256)
```

**핵심 패턴**:
1. **가변 길이 MsgId**: 1byte 길이 + UTF-8 문자열
2. **MsgSeq > 0**: Request 패킷 (Reply 기대)
3. **압축 조건**: Body >= 256 bytes → LZ4 압축

**참고 코드**:
```csharp
// 압축 여부 결정
if (bodySize < PacketConst.MinCompressionSize)  // 256
{
    // 비압축 전송
    buffer.WriteInt32(bodySize);
    // ...
    buffer.WriteInt32(0);  // originalSize = 0 (비압축)
    buffer.Write(payload);
}
else
{
    // 압축 전송
    var originalSize = bodySize;
    var compressed = Lz4Holder.Instance.Compress(body.Span);
    bodySize = compressed.Length;

    buffer.WriteInt32(bodySize);
    // ...
    buffer.WriteInt32(originalSize);  // 원본 크기 포함
    buffer.Write(compressed);
}
```

---

### 3.3 패킷 파싱 패턴 (Pipelines 기반 재작성)

기존 PlayHouse는 `RingBuffer`를 사용했지만, **PlayHouse-NET은 `System.IO.Pipelines`를 사용**합니다.

**기존 패턴의 핵심 로직** (참고용):
1. **Peek 후 Read**: 완전한 패킷 확인 후에만 소비
2. **불완전 패킷 대기**: TCP 스트림 특성 처리
3. **다중 패킷 파싱**: 한번에 여러 패킷 처리

**Pipelines 기반 새 구현**:
```csharp
public async ValueTask<List<ClientPacket>> ParseAsync(PipeReader reader)
{
    var packets = new List<ClientPacket>();

    while (true)
    {
        ReadResult result = await reader.ReadAsync();
        ReadOnlySequence<byte> buffer = result.Buffer;

        // 패킷 파싱 (SequenceReader 사용)
        while (TryParsePacket(ref buffer, out var packet))
        {
            packets.Add(packet);
        }

        // 소비한 위치 알림 (남은 불완전 패킷은 자동 유지)
        reader.AdvanceTo(buffer.Start, buffer.End);

        if (result.IsCompleted) break;
    }

    return packets;
}

private bool TryParsePacket(ref ReadOnlySequence<byte> buffer, out ClientPacket packet)
{
    var reader = new SequenceReader<byte>(buffer);

    // 최소 헤더 크기 확인
    if (buffer.Length < PacketConst.MinPacketSize)
    {
        packet = default;
        return false;
    }

    // Body 크기 peek
    if (!reader.TryReadBigEndian(out int bodySize))
    {
        packet = default;
        return false;
    }

    // 완전한 패킷인지 확인
    // ... (기존 로직과 동일하지만 SequenceReader 사용)

    // 성공 시 buffer 위치 이동
    buffer = buffer.Slice(reader.Position);
    return true;
}
```

**Pipelines 장점**:
- `RingBuffer`, `PooledByteBuffer` 불필요
- 자동 버퍼 풀링 (ArrayPool 내장)
- Zero-copy 파싱 (`ReadOnlySequence<byte>`)
- Backpressure 자동 처리

---

### 3.5 세션 디스패처 패턴

**위치**: `Core/Session/SessionDispatcher.cs`

```csharp
internal class SessionDispatcher : ISessionDispatcher
{
    private readonly ConcurrentDictionary<long, SessionActor> _sessionActors = new();
    private readonly BlockingCollection<KeyValuePair<ISession, ClientPacket>> _sendQueueToClient = new();
    private readonly Thread _sendThread;
    private readonly Timer _timer;

    // 클라이언트 연결
    public void OnConnect(long sid, ISession session, string remoteIp)
    {
        if (!_sessionActors.ContainsKey(sid))
        {
            _sessionActors[sid] = new SessionActor(...);
        }
    }

    // 클라이언트 연결 해제
    public void OnDisconnect(long sid)
    {
        if (_sessionActors.TryGetValue(sid, out var sessionClient))
        {
            sessionClient.Disconnect();
            _sessionActors.TryRemove(sid, out _);
        }
    }

    // 패킷 수신
    public void OnReceive(long sid, ClientPacket clientPacket)
    {
        // Heartbeat 처리
        if (clientPacket.MsgId == PacketConst.HeartBeat)
        {
            sessionClient.SendHeartBeat(clientPacket);
            return;
        }

        // 일반 패킷 디스패치
        sessionClient.Dispatch(clientPacket);
    }

    // 클라이언트로 전송 (별도 Thread)
    private void SendingPacket()
    {
        foreach (var result in _sendQueueToClient.GetConsumingEnumerable())
        {
            var session = result.Key;
            var packet = result.Value;

            _buffer.Clear();
            RoutePacket.WriteClientPacketBytes(packet, _buffer);
            session.Send(packet);
        }
    }

    // Idle 타임아웃 체크
    private static void TimerCallback(object? o)
    {
        var dispatcher = (SessionDispatcher)o!;
        var keysToRemove = dispatcher._sessionActors
            .Where(k => k.Value.IsIdleState(idleTimeout))
            .Select(k => k.Key).ToList();

        foreach (var key in keysToRemove)
        {
            dispatcher._sessionActors.Remove(key, out var client);
            client?.ClientDisconnect();
        }
    }
}
```

**핵심 패턴**:
1. **ConcurrentDictionary**: Thread-safe 세션 관리
2. **BlockingCollection**: 전송 큐 (Producer-Consumer)
3. **별도 Send Thread**: 전송 병목 방지
4. **Timer 기반 Idle 체크**: 유휴 클라이언트 정리

---

## 4. 단순화가 필요한 부분

### 4.1 RouteHeader 단순화

**기존 코드** (복잡한 라우팅 헤더):
```csharp
public class RouteHeader
{
    public Header Header { get; }
    public long Sid { get; set; }
    public bool IsSystem { get; set; }
    public bool IsBase { get; set; }
    public bool IsBackend { get; set; }  // ← 멀티서버용
    public bool IsReply { get; set; }
    public long AccountId { get; set; }
    public long StageId { get; set; }
    public string From { get; set; }     // ← 멀티서버용
    public bool IsToClient { get; set; }
}
```

**단순화 제안** (단일 서버용):
```csharp
public class PacketHeader
{
    public required string MsgId { get; init; }
    public ushort MsgSeq { get; set; }
    public ushort ErrorCode { get; set; }
    public long StageId { get; set; }
    public long AccountId { get; set; }
    public long SessionId { get; set; }
    public bool IsBase { get; set; }
    public bool IsReply { get; set; }
}
```

**제거 가능 필드**:
- `IsBackend`, `From`: 멀티서버 라우팅용
- `IsSystem`: 시스템 메시지는 MsgId로 구분 가능

---

### 4.2 Sender 단순화

**기존 XSender** (멀티서버 지원):
```csharp
void SendToClient(string sessionNid, long sid, IPacket packet);
void SendToApi(string apiNid, long accountId, IPacket packet);
void SendToStage(string playNid, long stageId, long accountId, IPacket packet);
Task<IPacket> RequestToApi(string apiNid, IPacket packet);
Task<IPacket> RequestToStage(string playNid, long stageId, long accountId, IPacket packet);
```

**단순화 제안** (단일 서버용):
```csharp
public interface IStageSender
{
    // 응답
    void Reply(ushort errorCode);
    void Reply(IPacket packet);

    // 클라이언트 전송
    ValueTask SendToActorAsync(long accountId, IPacket packet);
    ValueTask BroadcastAsync(IPacket packet);
    ValueTask BroadcastAsync(IPacket packet, Func<IActor, bool> filter);

    // 타이머
    long AddRepeatTimer(TimeSpan interval, Func<Task> callback);
    long AddCountTimer(TimeSpan interval, int count, Func<Task> callback);
    void CancelTimer(long timerId);

    // Stage 제어
    void CloseStage();
}
```

---

## 5. 제거 대상 (단일 서버에서 불필요)

| 컴포넌트 | 이유 |
|---------|------|
| `ZMQ`, `IPlaySocket` | 멀티서버 통신용 |
| `ServerAddressResolver` | 서버 디스커버리용 |
| `IClientCommunicator.Send(nid, packet)` | 노드간 통신용 |
| `PacketContext.AsyncCore` | 디버깅/추적용 컨텍스트 |
| `IsBackend`, `From` 필드 | 서버간 라우팅용 |

---

## 6. 기술 스택 참고

### 6.1 기존 PlayHouse 의존성

```xml
<!-- 유지 권장 -->
<PackageReference Include="Google.Protobuf" Version="3.28.2" />
<PackageReference Include="K4os.Compression.LZ4" Version="1.3.8" />
<PackageReference Include="Microsoft.Extensions.DependencyInjection" Version="8.0.1" />

<!-- 제거 대상 -->
<PackageReference Include="ZMQ" Version="4.0.1.13" />  <!-- 멀티서버용 -->

<!-- 신규 추가 고려 -->
<PackageReference Include="System.IO.Pipelines" />  <!-- 네트워크 버퍼 -->
```

### 6.2 네트워크 스택

**기존 PlayHouse**: 외부 라이브러리 + 수동 RingBuffer

**PlayHouse-NET 결정**: `System.Net.Sockets` + `System.IO.Pipelines`

```csharp
// Pipelines 사용 시 버퍼 관리가 자동화됨
var pipe = new Pipe();
var reader = pipe.Reader;

while (true)
{
    ReadResult result = await reader.ReadAsync();
    ReadOnlySequence<byte> buffer = result.Buffer;

    // 패킷 파싱
    while (TryParsePacket(ref buffer, out var packet))
    {
        ProcessPacket(packet);
    }

    // 소비한 만큼만 Advance (불완전 패킷은 자동 유지)
    reader.AdvanceTo(buffer.Start, buffer.End);
}
```

**Pipelines 장점**:
- 자동 버퍼 풀링 (ArrayPool 내장)
- Zero-copy 파싱 지원
- Backpressure 자동 처리
- 불완전 패킷 처리 내장

**→ RingBuffer, PooledByteBuffer 불필요**

---

## 7. 구현 체크리스트

### 7.1 Phase 1: 핵심 인프라

- [ ] `AtomicBoolean`, `AtomicEnum` 복사
- [ ] `Lz4Holder` 복사
- [ ] 패킷 구조 정의 (단순화된 헤더)
- [ ] `System.IO.Pipelines` 기반 네트워크 레이어 설정

### 7.2 Phase 2: 이벤트 루프

- [ ] `BaseStage.Post()` 패턴 구현
- [ ] `TimerManager` 패턴 구현
- [ ] Stage-Timer 통합

### 7.3 Phase 3: 세션 관리

- [ ] `SessionDispatcher` 패턴 구현 (단순화)
- [ ] `PacketParser` 구현 (Pipelines + SequenceReader)
- [ ] Idle 타임아웃 처리

### 7.4 Phase 4: Request-Reply

- [ ] `RequestCache` 패턴 구현
- [ ] `ReplyObject` (TaskCompletionSource) 구현
- [ ] 타임아웃 처리

---

## 8. 요약

### 8.1 그대로 복사할 코드

| 파일 | 라인 수 | 용도 |
|------|--------|------|
| `AtomicBoolean.cs` | ~30 | CAS 플래그 |
| `AtomicEnum.cs` | ~30 | 상태 관리 |
| `AtomicShort.cs` | ~30 | 시퀀스 생성 |
| `Lz4Holder.cs` | ~25 | 압축 싱글톤 |

> **참고**: `RingBuffer.cs`, `PooledByteBuffer.cs`는 `System.IO.Pipelines`가 대체하므로 불필요

### 8.2 패턴만 참고할 코드

| 패턴 | 원본 파일 | 참고 사항 |
|------|----------|----------|
| 이벤트 루프 | `BaseStage.cs` | CAS + async Task |
| 타이머 통합 | `TimerManager.cs` | 콜백 → 메시지 변환 |
| Request-Reply | `RequestCache.cs` | TaskCompletionSource |
| 세션 관리 | `SessionDispatcher.cs` | Idle 타임아웃 |
| 패킷 파싱 | `PacketParser.cs` | 핵심 로직만 (Pipelines로 재작성) |

### 8.3 단순화 필요

- RouteHeader → PacketHeader (필드 축소)
- XSender → IStageSender (멀티서버 제거)
- 네트워크 스택 (ZMQ 제거)
