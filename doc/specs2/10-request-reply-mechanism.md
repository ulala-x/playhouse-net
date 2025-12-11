# Request-Reply 메커니즘 스펙

## 1. 개요

서버 간 통신에서 **Request-Reply 패턴**을 구현하기 위한 핵심 메커니즘입니다.
단순히 메시지를 보내는 것이 아니라, **요청에 대한 응답을 추적**하고 **원래 요청자에게 응답을 전달**해야 합니다.

### 1.1 문제 정의

```
API Server → Play Server: CreateStageRequest (MsgSeq=42)
Play Server → API Server: CreateStageResponse (MsgSeq=42)  ← 어떻게 매칭?
```

- 비동기 환경에서 여러 요청이 동시에 진행됨
- 응답이 도착했을 때 어떤 요청에 대한 응답인지 식별 필요
- 요청자의 콜백 또는 Task를 완료시켜야 함

### 1.2 참조 시스템 파일

| 파일 | 경로 | 용도 |
|------|------|------|
| **XSender.cs** | `PlayHouse/Core/Shared/XSender.cs` | Reply 로직, Request 전송 |
| **RequestCache.cs** | `PlayHouse/Runtime/RequestCache.cs` | 요청 추적, 타임아웃 관리 |
| **RoutePacket.cs** | `PlayHouse/Runtime/Message/RoutePacket.cs` | 패킷 헤더, ReplyOf 생성 |
| **ReplyObject.cs** | `PlayHouse/Runtime/RequestCache.cs` | 콜백/TaskCompletionSource 래퍼 |
| **NetMQPlaySocket.cs** | `PlayHouse/Runtime/PlaySocket/NetMQPlaySocket.cs` | ⭐ From 주소 수집 |

---

## 2. 핵심 원리: 비동기 컨텍스트에서 반환 주소 수집

### 2.1 문제: 응답을 어디로 보내야 하나?

비동기 환경에서 요청을 처리할 때, **응답을 보낼 주소를 어떻게 알 수 있는가?**

```
┌─────────────────┐         ┌─────────────────┐
│  API Server     │         │  Play Server    │
│  (nid: api-1)   │         │  (nid: play-1)  │
└────────┬────────┘         └────────┬────────┘
         │                           │
         │  CreateStageReq           │
         │  ──────────────────────►  │
         │                           │
         │        ??? ◄──────────────│  Reply를 어디로?
         │                           │
```

### 2.2 해결: NetMQ Router 소켓의 Identity 프레임 활용

**NetMQ Router 소켓**은 메시지를 수신할 때 **발신자의 Identity를 첫 번째 프레임에 자동으로 포함**합니다.

```
NetMQ Message 구조:
┌─────────────────┬─────────────────┬─────────────────┐
│  Frame[0]       │  Frame[1]       │  Frame[2]       │
│  Identity       │  RouteHeader    │  Payload        │
│  (발신자 NID)    │  (헤더 정보)     │  (메시지 본문)   │
│  "api-1"        │  MsgSeq, etc.   │  Protobuf 데이터 │
└─────────────────┴─────────────────┴─────────────────┘
```

### 2.3 구현: 수신 시 From 주소 자동 수집

```csharp
// NetMQPlaySocket.Receive() - 참조: PlayHouse/Runtime/PlaySocket/NetMQPlaySocket.cs
public RoutePacket? Receive()
{
    var message = new NetMQMessage();
    if (_socket.TryReceiveMultipartMessage(TimeSpan.FromSeconds(1), ref message))
    {
        // ⭐ Frame[0]: 발신자 Identity (NetMQ Router가 자동 추가)
        var target = Encoding.UTF8.GetString(message[0].Buffer);

        // Frame[1]: 헤더
        var header = RouteHeaderMsg.Parser.ParseFrom(message[1].Buffer);

        // Frame[2]: 페이로드
        var payload = new FramePayload(message[2]);

        var routePacket = RoutePacket.Of(new RouteHeader(header), payload);

        // ⭐ 핵심: 발신자 주소를 RouteHeader.From에 저장
        routePacket.RouteHeader.From = target;

        return routePacket;
    }
    return null;
}
```

### 2.4 Reply 시 From 주소 사용

```csharp
// XSender.Reply() - CurrentHeader.From으로 응답 전송
private void Reply(ushort errorCode, IPacket? reply)
{
    if (CurrentHeader == null || CurrentHeader.Header.MsgSeq == 0)
    {
        return; // 요청 컨텍스트 없음 또는 단방향 Send
    }

    // ⭐ CurrentHeader.From = 원래 요청자 주소 (NetMQ 수신 시 수집됨)
    var from = CurrentHeader.From;

    var routePacket = RoutePacket.ReplyOf(ServiceId, CurrentHeader, errorCode, reply);

    // ⭐ 원래 요청자에게 응답 전송
    ClientCommunicator.Send(from, routePacket);
}
```

### 2.5 전체 플로우

```
┌─────────────────┐                           ┌─────────────────┐
│  API Server     │                           │  Play Server    │
│  (nid: api-1)   │                           │  (nid: play-1)  │
└────────┬────────┘                           └────────┬────────┘
         │                                             │
         │  1. NetMQ Router로 전송                      │
         │     (Identity = "api-1" 자동 포함)           │
         │  ─────────────────────────────────────────► │
         │                                             │
         │                                             │  2. NetMQ Router 수신
         │                                             │     Frame[0] = "api-1" (From)
         │                                             │     routePacket.RouteHeader.From = "api-1"
         │                                             │
         │                                             │  3. 핸들러 실행 전
         │                                             │     SetCurrentPacketHeader(routeHeader)
         │                                             │     CurrentHeader.From = "api-1"
         │                                             │
         │                                             │  4. Reply(response) 호출
         │                                             │     from = CurrentHeader.From = "api-1"
         │                                             │     Send("api-1", replyPacket)
         │                                             │
         │  ◄───────────────────────────────────────── │
         │  5. 응답 수신                                │
         │                                             │
```

---

## 3. 핵심 컴포넌트

### 3.1 RouteHeader (요청 컨텍스트)

**요청을 처리할 때 현재 요청의 컨텍스트를 저장**해야 Reply 시 원래 요청자에게 응답 가능.

```csharp
public class RouteHeader
{
    public Header Header { get; }          // MsgId, MsgSeq, ServiceId, ErrorCode, StageId
    public string From { get; set; }       // 요청 발신자 주소 (응답 전송 대상)
    public long Sid { get; set; }          // 세션 ID
    public long AccountId { get; set; }    // 계정 ID
    public long StageId { get; set; }      // Stage ID
    public bool IsReply { get; set; }      // 응답 패킷 여부
    public bool IsBackend { get; set; }    // 서버 간 통신 여부
}

public class Header
{
    public ushort ServiceId { get; set; }
    public string MsgId { get; set; }
    public ushort MsgSeq { get; set; }     // ⭐ 핵심: 요청-응답 매칭 키
    public ushort ErrorCode { get; set; }
    public long StageId { get; set; }
}
```

### 3.2 RequestCache (요청 추적)

**진행 중인 요청을 MsgSeq로 추적**, 응답 도착 시 매칭하여 콜백 실행.

```csharp
internal class RequestCache
{
    private readonly ConcurrentDictionary<int, ReplyObject> _cache = new();
    private readonly AtomicShort _sequence = new();  // MsgSeq 생성기
    private readonly int _timeout;                    // 타임아웃 (ms)

    // 고유한 MsgSeq 생성 (1~65535 순환)
    public ushort GetSequence() => _sequence.IncrementAndGet();

    // 요청 등록
    public void Put(int seq, ReplyObject replyObject) => _cache[seq] = replyObject;

    // 응답 도착 시 처리
    public void OnReply(RoutePacket routePacket)
    {
        int msgSeq = routePacket.Header.MsgSeq;
        var replyObject = _cache.GetValueOrDefault(msgSeq);

        if (replyObject != null)
        {
            replyObject.OnReceive(routePacket);  // 콜백 실행 또는 Task 완료
            _cache.TryRemove(msgSeq, out _);
        }
    }

    // 타임아웃 체크 (백그라운드 스레드)
    private void CheckExpire()
    {
        foreach (var item in _cache)
        {
            if (item.Value.IsExpired(_timeout))
            {
                item.Value.Throw(BaseErrorCode.RequestTimeout);
                _cache.TryRemove(item.Key, out _);
            }
        }
    }
}
```

### 3.3 ReplyObject (콜백 래퍼)

**콜백 방식과 async/await 방식 모두 지원**.

```csharp
internal class ReplyObject
{
    private readonly ReplyCallback? _callback;
    private readonly TaskCompletionSource<RoutePacket>? _taskCompletionSource;
    private readonly DateTime _requestTime = DateTime.UtcNow;

    public ReplyObject(
        ReplyCallback? callback = null,
        TaskCompletionSource<RoutePacket>? taskCompletionSource = null)
    {
        _callback = callback;
        _taskCompletionSource = taskCompletionSource;
    }

    // 응답 수신 시
    public void OnReceive(RoutePacket routePacket)
    {
        // 콜백 방식
        _callback?.Invoke(routePacket.ErrorCode, CPacket.Of(routePacket));

        // async/await 방식
        if (routePacket.ErrorCode == 0)
        {
            _taskCompletionSource?.TrySetResult(routePacket);
        }
        else
        {
            Throw(routePacket.ErrorCode);
        }
    }

    // 에러 발생 시 (타임아웃 포함)
    public void Throw(ushort errorCode)
    {
        _taskCompletionSource?.TrySetException(
            new PlayHouseException($"errorCode:{errorCode}", errorCode));
    }

    // 타임아웃 체크
    public bool IsExpired(int timeoutMs)
    {
        return (DateTime.UtcNow - _requestTime).TotalMilliseconds > timeoutMs;
    }
}

// 콜백 델리게이트
public delegate void ReplyCallback(ushort errorCode, IPacket reply);
```

---

## 4. Request-Reply 플로우

### 4.1 Request 전송 (요청자 측)

```csharp
// XSender.RequestToApi() - 콜백 방식
public void RequestToApi(string apiNid, IPacket packet, ReplyCallback replyCallback)
{
    // 1. 고유 MsgSeq 생성
    var seq = reqCache.GetSequence();

    // 2. 콜백 등록 (응답 대기)
    reqCache.Put(seq, new ReplyObject(replyCallback));

    // 3. 패킷에 MsgSeq 설정
    var routePacket = RoutePacket.ApiOf(RoutePacket.Of(packet), false, true);
    routePacket.SetMsgSeq(seq);

    // 4. 전송
    ClientCommunicator.Send(apiNid, routePacket);
}

// XSender.RequestToApi() - async/await 방식
public async Task<IPacket> RequestToApi(string apiNid, IPacket packet)
{
    var seq = reqCache.GetSequence();
    var tcs = new TaskCompletionSource<RoutePacket>();

    reqCache.Put(seq, new ReplyObject(taskCompletionSource: tcs));

    var routePacket = RoutePacket.ApiOf(RoutePacket.Of(packet), false, true);
    routePacket.SetMsgSeq(seq);
    ClientCommunicator.Send(apiNid, routePacket);

    // 응답 대기 (타임아웃 시 예외 발생)
    var replyPacket = await tcs.Task;
    return CPacket.Of(replyPacket);
}
```

### 4.2 요청 처리 및 Reply (처리자 측)

```csharp
// XSender 내부
internal class XSender : ISender
{
    protected RouteHeader? CurrentHeader;  // ⭐ 현재 처리 중인 요청의 헤더

    // 요청 처리 전 헤더 설정
    public void SetCurrentPacketHeader(RouteHeader currentHeader)
    {
        CurrentHeader = currentHeader;
    }

    // 요청 처리 후 헤더 클리어
    public void ClearCurrentPacketHeader()
    {
        CurrentHeader = null;
    }

    // Reply 호출 시
    public void Reply(IPacket reply)
    {
        Reply((ushort)BaseErrorCode.Success, reply);
    }

    public void Reply(ushort errorCode)
    {
        Reply(errorCode, null);
    }

    private void Reply(ushort errorCode, IPacket? reply)
    {
        if (CurrentHeader == null)
        {
            _log.Error(() => "Not exist request packet");
            return;
        }

        var msgSeq = CurrentHeader.Header.MsgSeq;
        if (msgSeq == 0)
        {
            // MsgSeq=0 이면 단방향 Send (응답 필요 없음)
            _log.Error(() => "Not exist request packet (MsgSeq=0)");
            return;
        }

        // 응답 패킷 생성
        var from = CurrentHeader.From;  // ⭐ 원래 요청자 주소
        var routePacket = RoutePacket.ReplyOf(ServiceId, CurrentHeader, errorCode, reply);
        routePacket.RouteHeader.AccountId = CurrentHeader.AccountId;

        // 원래 요청자에게 전송
        ClientCommunicator.Send(from, routePacket);
    }
}
```

### 4.3 Reply 패킷 생성

```csharp
// RoutePacket.ReplyOf()
public static RoutePacket ReplyOf(ushort serviceId, RouteHeader sourceHeader, ushort errorCode, IPacket? reply)
{
    Header header = new(msgId: reply?.MsgId ?? "")
    {
        ServiceId = serviceId,
        MsgSeq = sourceHeader.Header.MsgSeq  // ⭐ 원본 MsgSeq 복사
    };

    var routeHeader = RouteHeader.Of(header);
    routeHeader.IsReply = true;                      // 응답임을 표시
    routeHeader.IsToClient = !sourceHeader.IsBackend;
    routeHeader.Sid = sourceHeader.Sid;
    routeHeader.IsBackend = sourceHeader.IsBackend;
    routeHeader.AccountId = sourceHeader.AccountId;

    var routePacket = reply != null
        ? new RoutePacket(routeHeader, reply.Payload)
        : new RoutePacket(routeHeader, new EmptyPayload());

    routePacket.RouteHeader.Header.ErrorCode = errorCode;
    return routePacket;
}
```

---

## 4. 전체 시퀀스 다이어그램

```
┌──────────────────┐                        ┌──────────────────┐
│   API Server     │                        │   Play Server    │
│   (요청자)        │                        │   (처리자)        │
└────────┬─────────┘                        └────────┬─────────┘
         │                                           │
         │  1. RequestToApi(packet)                  │
         │     - seq = GetSequence() → 42            │
         │     - reqCache.Put(42, ReplyObject)       │
         │     - routePacket.SetMsgSeq(42)           │
         │                                           │
         │  ─────────────────────────────────────►   │
         │      RoutePacket                          │
         │      - MsgSeq: 42                         │
         │      - From: "api-server-1"               │
         │      - MsgId: "CreateStageReq"            │
         │                                           │
         │                                           │  2. 요청 수신
         │                                           │     - SetCurrentPacketHeader(routeHeader)
         │                                           │     - Stage.OnCreate() 호출
         │                                           │
         │                                           │  3. Reply(response) 호출
         │                                           │     - CurrentHeader.MsgSeq = 42
         │                                           │     - CurrentHeader.From = "api-server-1"
         │                                           │     - ReplyOf(42, response)
         │                                           │
         │  ◄─────────────────────────────────────   │
         │      RoutePacket (Reply)                  │
         │      - MsgSeq: 42                         │
         │      - IsReply: true                      │
         │      - MsgId: "CreateStageRes"            │
         │      - ErrorCode: 0                       │
         │                                           │
         │  4. 응답 수신                              │
         │     - reqCache.OnReply(routePacket)       │
         │     - replyObject = cache[42]             │
         │     - replyObject.OnReceive(packet)       │
         │     - TaskCompletionSource.SetResult()    │
         │                                           │
         │  5. await 완료, 결과 반환                  │
         │                                           │
```

---

## 5. 구현 체크리스트

### Phase 1: 기본 구조
- [ ] `RouteHeader` 클래스 구현
  - MsgSeq, From, IsReply 등 필드
- [ ] `Header` 클래스 구현
  - ServiceId, MsgId, MsgSeq, ErrorCode, StageId
- [ ] `RoutePacket` 클래스 구현
  - ReplyOf() 정적 메서드

### Phase 2: 요청 추적
- [ ] `ReplyObject` 클래스 구현
  - 콜백, TaskCompletionSource 지원
  - 타임아웃 체크
- [ ] `RequestCache` 클래스 구현
  - 시퀀스 생성기
  - 요청 등록/조회/삭제
  - 타임아웃 스레드

### Phase 3: Sender 구현
- [ ] `ISender` 인터페이스
  - Reply(errorCode), Reply(packet) 메서드
- [ ] `XSender` 구현
  - CurrentHeader 관리
  - RequestToApi(), RequestToStage() 메서드
  - Reply() 메서드

### Phase 4: 수신 측 통합
- [ ] 요청 수신 시 `SetCurrentPacketHeader()` 호출
- [ ] 핸들러 실행 후 `ClearCurrentPacketHeader()` 호출
- [ ] Reply 패킷 수신 시 `RequestCache.OnReply()` 호출

---

## 6. 주의사항

### 6.1 MsgSeq = 0 의미
- **MsgSeq = 0**: 단방향 Send (응답 불필요)
- **MsgSeq > 0**: Request-Reply 패턴 (응답 필요)

```csharp
// Send (단방향) - MsgSeq 설정 안 함
public void SendToApi(string apiNid, IPacket packet)
{
    var routePacket = RoutePacket.ApiOf(RoutePacket.Of(packet), false, true);
    // routePacket.SetMsgSeq() 호출 안 함 → MsgSeq = 0
    ClientCommunicator.Send(apiNid, routePacket);
}

// Request (양방향) - MsgSeq 설정
public void RequestToApi(string apiNid, IPacket packet, ReplyCallback callback)
{
    var seq = reqCache.GetSequence();  // MsgSeq > 0
    reqCache.Put(seq, new ReplyObject(callback));
    var routePacket = RoutePacket.ApiOf(RoutePacket.Of(packet), false, true);
    routePacket.SetMsgSeq(seq);  // ⭐ MsgSeq 설정
    ClientCommunicator.Send(apiNid, routePacket);
}
```

### 6.2 Reply 호출 시점
- **반드시 요청 처리 중에만 Reply 가능** (CurrentHeader가 설정된 상태)
- 핸들러 밖에서 Reply 호출 시 에러 로그

### 6.3 타임아웃 처리
- RequestCache가 백그라운드 스레드에서 주기적으로 만료 체크
- 만료된 요청은 `TaskCompletionSource.TrySetException()` 으로 예외 발생

### 6.4 동시성
- `ConcurrentDictionary` 사용으로 스레드 안전
- `AtomicShort`로 MsgSeq 생성 시 경합 방지
