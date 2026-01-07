# 04. API 서버 프레임워크 구현 가이드

## 문서 목적

이 문서는 API 서버 **프레임워크 내부 구현**을 설명합니다. 컨텐츠 개발자가 구현할 코드가 아닌, 프레임워크가 제공해야 하는 핵심 컴포넌트의 구현 방법을 다룹니다.

---

## 1. API 서버 아키텍처

### 1.1 핵심 컴포넌트

```
┌─────────────────────────────────────────────────────────────────┐
│                         API 서버                                │
│                                                                 │
│  ┌──────────────┐      ┌──────────────┐      ┌──────────────┐  │
│  │ Dispatcher   │      │   Sender     │      │  ZMQ Layer │  │
│  │              │      │              │      │              │  │
│  │ ApiDispatcher│◄────►│  ApiSender   │◄────►│ PlaySocket   │  │
│  │ ApiReflection│      │ RequestCache │      │ RouterSocket │  │
│  └──────────────┘      └──────────────┘      └──────────────┘  │
│         ▲                      ▲                      ▲        │
│         │                      │                      │        │
│         └──────────────────────┴──────────────────────┘        │
│                    Communicator                                │
└─────────────────────────────────────────────────────────────────┘
```

### 1.2 프레임워크 컴포넌트 구조

| 컴포넌트 | 역할 | 참조 파일 |
|---------|------|----------|
| **ApiDispatcher** | Stateless 요청 처리 | `Core/Api/ApiDispatcher.cs` |
| **ApiSender** | IApiSender 구현 (XSender 직접 상속) | `Core/Api/ApiSender.cs` |
| **ApiReflection** | 핸들러 리플렉션 호출 | `Core/Api/Reflection/ApiReflection.cs` |
| **RequestCache** | Request-Reply 매핑 | `Runtime/RequestCache.cs` |

---

## 2. ApiDispatcher 구현

메시지를 수신하고 Stateless로 처리하는 디스패처입니다.

```csharp
internal class ApiDispatcher
{
    private readonly ushort _serviceId;
    private readonly RequestCache _requestCache;
    private readonly IClientCommunicator _clientCommunicator;
    private readonly ApiReflection _apiReflection;

    public ApiDispatcher(
        ushort serviceId,
        RequestCache requestCache,
        IClientCommunicator clientCommunicator,
        IServiceProvider serviceProvider,
        ApiOption apiOption)
    {
        _serviceId = serviceId;
        _requestCache = requestCache;
        _clientCommunicator = clientCommunicator;
        _apiReflection = new ApiReflection(serviceProvider, apiOption.AspectifyManager);
    }

    // 메시지 수신 처리 (Stateless)
    internal void OnPost(RoutePacket routePacket)
    {
        using (routePacket)
        {
            Task.Run(async () =>
            {
                await DispatchAsync(RoutePacket.MoveOf(routePacket));
            });
        }
    }

    // Stateless 요청 처리
    private async Task DispatchAsync(RoutePacket routePacket)
    {
        var routeHeader = routePacket.RouteHeader;
        var apiSender = new ApiSender(_serviceId, _clientCommunicator, _requestCache);
        apiSender.SetCurrentPacketHeader(routeHeader);

        try
        {
            if (routePacket.IsBackend())
            {
                await _apiReflection.CallBackendMethodAsync(routePacket.ToContentsPacket(), apiSender);
            }
            else
            {
                await _apiReflection.CallMethodAsync(routePacket.ToContentsPacket(), apiSender);
            }
        }
        catch (ServiceException.NotRegisterMethod e)
        {
            if (routeHeader.Header.MsgSeq > 0)
            {
                apiSender.Reply((ushort)BaseErrorCode.NotRegisteredMessage);
            }
            _log.Error(() => $"{e}");
        }
        catch (ServiceException.NotRegisterInstance e)
        {
            if (routeHeader.Header.MsgSeq > 0)
            {
                apiSender.Reply((ushort)BaseErrorCode.SystemError);
            }
            _log.Error(() => $"{e}");
        }
        catch (Exception e)
        {
            if (routeHeader.Header.MsgSeq > 0)
            {
                apiSender.Reply((ushort)BaseErrorCode.UncheckedContentsError);
            }
            _log.Error(() => $"Packet processing failed - [msgId:{routeHeader.MsgId}]");
            _log.Error(() => $"[exception:{e.Message}]");
        }
    }
}
```

---

## 3. ApiSender 구현 (IApiSender)

IApiSender 인터페이스의 구현입니다. XSender를 직접 상속하여 단순화된 구조입니다.

```csharp
internal class ApiSender : XSender, IApiSender
{
    public ApiSender(
        ushort serviceId,
        IClientCommunicator clientCommunicator,
        RequestCache reqCache)
        : base(serviceId, clientCommunicator, reqCache)
    {
    }

    // Stage 생성 요청
    public async Task<CreateStageResult> CreateStage(
        string playNid,
        string stageType,
        long stageId,
        IPacket packet)
    {
        var req = new CreateStageReq
        {
            StageType = stageType,
            PayloadId = packet.MsgId,
            Payload = ByteString.CopyFrom(packet.Payload.DataSpan)
        };

        using var reply = await RequestToBaseStage(playNid, stageId, RoutePacket.Of(req));

        var res = CreateStageRes.Parser.ParseFrom(reply.Span);

        return new CreateStageResult(
            reply.ErrorCode == 0,
            CPacket.Of(res.PayloadId, new ByteStringPayload(res.Payload))
        );
    }

    // Stage 조회 또는 생성 요청
    public async Task<GetOrCreateStageResult> GetOrCreateStage(
        string playNid,
        string stageType,
        long stageId,
        IPacket createPacket,
        IPacket joinPacket)
    {
        var req = new GetOrCreateStageReq
        {
            StageType = stageType,
            CreatePayloadId = createPacket.MsgId,
            CreatePayload = ByteString.CopyFrom(createPacket.Payload.DataSpan),
            JoinPayloadId = joinPacket.MsgId,
            JoinPayload = ByteString.CopyFrom(joinPacket.Payload.DataSpan)
        };

        using var reply = await RequestToBaseStage(playNid, stageId, RoutePacket.Of(req));

        var res = GetOrCreateStageRes.Parser.ParseFrom(reply.Span);

        return new GetOrCreateStageResult(
            reply.ErrorCode == 0,
            res.IsCreated,
            CPacket.Of(res.PayloadId, new ByteStringPayload(res.Payload))
        );
    }
}
```

---

## 4. RequestCache 구현

Request-Reply 패턴의 요청 추적 및 타임아웃 관리입니다.

```csharp
// 참조: D:\project\kairos\playhouse\playhouse-net\PlayHouse\PlayHouse\Runtime\RequestCache.cs
internal class RequestCache
{
    private readonly ConcurrentDictionary<ushort, ReplyObject> _cache = new();
    private readonly AtomicShort _sequence = new();
    private readonly int _timeout;

    public RequestCache(int timeoutMs = 30000)
    {
        _timeout = timeoutMs;

        // 타임아웃 체크 스레드
        var thread = new Thread(() =>
        {
            while (true)
            {
                CheckExpire();
                Thread.Sleep(1000);
            }
        });
        thread.IsBackground = true;
        thread.Start();
    }

    // 고유한 MsgSeq 생성 (1~65535 순환)
    public ushort GetSequence()
    {
        return _sequence.IncrementAndGet();
    }

    // 요청 등록
    public void Put(ushort seq, ReplyObject replyObject)
    {
        _cache[seq] = replyObject;
    }

    // 응답 도착 시 처리
    public void OnReply(RoutePacket routePacket)
    {
        var msgSeq = routePacket.Header.MsgSeq;
        if (_cache.TryRemove(msgSeq, out var replyObject))
        {
            replyObject.OnReceive(routePacket);
        }
    }

    // 타임아웃 체크
    private void CheckExpire()
    {
        var now = DateTime.UtcNow;
        var expiredKeys = _cache
            .Where(item => item.Value.IsExpired(_timeout))
            .Select(item => item.Key)
            .ToList();

        foreach (var key in expiredKeys)
        {
            if (_cache.TryRemove(key, out var replyObject))
            {
                replyObject.Throw(BaseErrorCode.RequestTimeout);
            }
        }
    }
}

// 콜백/TaskCompletionSource 래퍼
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

    // 에러 발생 시
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

## 5. ApiReflection 구현

컨텐츠 핸들러를 리플렉션으로 호출하는 시스템입니다.

```csharp
// 참조: D:\project\kairos\playhouse\playhouse-net\PlayHouse\PlayHouse\Core\Api\Reflection\ApiReflection.cs
internal class ApiReflection
{
    private readonly Dictionary<string, ApiHandleReflectionInvoker> _handlers = new();
    private readonly Dictionary<string, ApiHandleReflectionInvoker> _backendHandlers = new();

    public ApiReflection(IServiceProvider serviceProvider, ApiControllAspectifyManager? aspectifyManager)
    {
        // IApiController 구현체들을 찾아서 핸들러 등록
        var controllers = serviceProvider.GetServices<IApiController>();
        foreach (var controller in controllers)
        {
            var handlerRegister = new HandlerRegister(_handlers);
            controller.Handles(handlerRegister);
        }

        // IApiBackendController 구현체들
        var backendControllers = serviceProvider.GetServices<IApiBackendController>();
        foreach (var controller in backendControllers)
        {
            var handlerRegister = new HandlerRegister(_backendHandlers);
            controller.Handles(handlerRegister);
        }
    }

    // 컨텐츠 핸들러 호출
    public async Task CallMethodAsync(IPacket packet, IApiSender apiSender)
    {
        var msgId = packet.MsgId;
        if (_handlers.TryGetValue(msgId, out var invoker))
        {
            await invoker.InvokeAsync(packet, apiSender);
        }
        else
        {
            throw new ServiceException.NotRegisterMethod($"Not registered handler: {msgId}");
        }
    }

    // 백엔드 핸들러 호출
    public async Task CallBackendMethodAsync(IPacket packet, IApiSender apiSender)
    {
        var msgId = packet.MsgId;
        if (_backendHandlers.TryGetValue(msgId, out var invoker))
        {
            await invoker.InvokeAsync(packet, apiSender);
        }
        else
        {
            throw new ServiceException.NotRegisterMethod($"Not registered backend handler: {msgId}");
        }
    }
}

// 핸들러 등록기
internal class HandlerRegister : IHandlerRegister
{
    private readonly Dictionary<string, ApiHandleReflectionInvoker> _handlers;

    public HandlerRegister(Dictionary<string, ApiHandleReflectionInvoker> handlers)
    {
        _handlers = handlers;
    }

    public void Add(string msgId, ApiHandler handler)
    {
        if (!_handlers.TryAdd(msgId, new ApiHandleReflectionInvoker(handler)))
        {
            throw new InvalidOperationException($"Already registered handler: {msgId}");
        }
    }
}
```

---

## 6. StageResult 타입들

Stage 생성/입장 결과 타입입니다.

```csharp
// 참조: D:\project\kairos\playhouse\playhouse-net\PlayHouse\PlayHouse\Abstractions\Shared\StageResult.cs
public class StageResult
{
    public bool Result { get; }

    public StageResult(bool result)
    {
        Result = result;
    }
}

public class CreateStageResult : StageResult
{
    public IPacket CreateStageRes { get; }

    public CreateStageResult(bool result, IPacket createStageRes)
        : base(result)
    {
        CreateStageRes = createStageRes;
    }
}

public class JoinStageResult : StageResult
{
    public IPacket JoinStageRes { get; }

    public JoinStageResult(bool result, IPacket joinStageRes)
        : base(result)
    {
        JoinStageRes = joinStageRes;
    }
}

public class CreateJoinStageResult : StageResult
{
    public bool IsCreated { get; }
    public IPacket CreateStageRes { get; }
    public IPacket JoinStageRes { get; }

    public CreateJoinStageResult(
        bool result,
        bool isCreated,
        IPacket createStageRes,
        IPacket joinStageRes)
        : base(result)
    {
        IsCreated = isCreated;
        CreateStageRes = createStageRes;
        JoinStageRes = joinStageRes;
    }
}

// new-request.md에 정의된 결과 타입
public class GetOrCreateStageResult : StageResult
{
    public bool IsCreated { get; }
    public IPacket CreateStageRes { get; }

    public GetOrCreateStageResult(bool result, bool isCreated, IPacket createStageRes)
        : base(result)
    {
        IsCreated = isCreated;
        CreateStageRes = createStageRes;
    }
}
```

**GetOrCreateStageResult의 Result/IsCreated 조합 의미**:

| Result | IsCreated | 의미 |
|--------|-----------|------|
| `true` | `false` | Stage가 이미 존재함 (기존 Stage 반환) |
| `true` | `true` | 새 Stage 생성 성공 |
| `false` | `false` | Stage 생성 실패 (생성 시도했으나 실패) |

---

## 7. 에러 코드 정의

```csharp
public enum BaseErrorCode : ushort
{
    Success = 0,

    // 요청 관련 (1-99)
    NotRegisteredMessage = 1,
    InvalidParameter = 2,

    // 시스템 관련 (100-199)
    SystemError = 100,
    RequestTimeout = 101,
    UncheckedContentsError = 102,

    // Stage 관련 (200-299)
    StageNotFound = 200,
    StageAlreadyExists = 201,
    StageCreateFailed = 202,

    // 인증 관련 (300-399)
    AuthenticationFailed = 300,
    JoinStageFailed = 301,
}
```

---

## 8. 통신 흐름 다이어그램

### 8.1 CreateStage 처리 흐름

```
┌──────────────┐         ┌──────────────┐         ┌──────────────┐
│ApiDispatcher │         │  ApiSender   │         │ Play Server  │
└──────┬───────┘         └──────┬───────┘         └──────┬───────┘
       │                        │                        │
       │  CreateStage(...)      │                        │
       │ ──────────────────────>│                        │
       │                        │                        │
       │                        │  RequestToBaseStage    │
       │                        │  ──────────────────────>
       │                        │  MsgSeq: 42            │
       │                        │  StageType, Payload    │
       │                        │                        │
       │                        │                        │  IStage.OnCreate()
       │                        │                        │  IStage.OnPostCreate()
       │                        │                        │
       │                        │  CreateStageRes        │
       │                        │ <──────────────────────│
       │                        │  MsgSeq: 42            │
       │                        │  ErrorCode, Payload    │
       │                        │                        │
       │  CreateStageResult     │                        │
       │ <──────────────────────│                        │
       │                        │                        │
```

### 8.2 Request-Reply 처리 흐름

```
┌──────────────┐         ┌──────────────┐         ┌──────────────┐
│   XSender    │         │ RequestCache │         │ Remote Server│
└──────┬───────┘         └──────┬───────┘         └──────┬───────┘
       │                        │                        │
       │  GetSequence()         │                        │
       │ ──────────────────────>│                        │
       │  seq = 42              │                        │
       │ <──────────────────────│                        │
       │                        │                        │
       │  Put(42, ReplyObject)  │                        │
       │ ──────────────────────>│                        │
       │                        │                        │
       │  Send(routePacket)     │                        │
       │ ──────────────────────────────────────────────>│
       │                        │                        │
       │                        │  ... 처리 ...          │
       │                        │                        │
       │  OnReply(replyPacket)  │                        │
       │ <──────────────────────────────────────────────│
       │                        │                        │
       │                        │  TryRemove(42)         │
       │                        │  ReplyObject.OnReceive │
       │                        │                        │
       │  TaskCompletionSource  │                        │
       │  .SetResult()          │                        │
       │                        │                        │
```

---

## 9. 구현 체크리스트

### 9.1 Core 컴포넌트

- [ ] **ApiDispatcher** - Stateless 요청 처리
  - [ ] OnPost() - 메시지 수신 및 디스패치
  - [ ] DispatchAsync() - 핸들러 호출

- [ ] **ApiSender** - IApiSender 구현 (XSender 직접 상속)
  - [ ] CreateStage()
  - [ ] GetOrCreateStage()

### 9.2 Request-Reply 시스템

- [ ] **RequestCache** - 요청 추적
  - [ ] GetSequence() - MsgSeq 생성
  - [ ] Put(), OnReply()
  - [ ] CheckExpire() - 타임아웃 처리

- [ ] **ReplyObject** - 콜백 래퍼
  - [ ] OnReceive() - 콜백/Task 처리
  - [ ] Throw() - 에러 전파
  - [ ] IsExpired() - 타임아웃 체크

### 9.3 리플렉션 시스템

- [ ] **ApiReflection** - 핸들러 호출
  - [ ] CallMethodAsync()
  - [ ] CallBackendMethodAsync()

- [ ] **HandlerRegister** - 핸들러 등록
  - [ ] Add(msgId, handler)

---

## 10. 참조 파일

| 파일 | 경로 | 용도 |
|------|------|------|
| **ApiDispatcher.cs** | `Core/Api/ApiDispatcher.cs` | Stateless 요청 처리 |
| **ApiSender.cs** | `Core/Api/ApiSender.cs` | IApiSender 구현 (XSender 직접 상속) |
| **RequestCache.cs** | `Runtime/RequestCache.cs` | Request-Reply 매핑 |
| **ApiReflection.cs** | `Core/Api/Reflection/ApiReflection.cs` | 핸들러 리플렉션 |
| **HandlerRegister.cs** | `Core/Api/Reflection/HandlerRegister.cs` | 핸들러 등록 |
| **StageResult.cs** | `Abstractions/Shared/StageResult.cs` | 결과 타입 |

---

## 변경 이력

| 버전 | 날짜 | 변경 내역 |
|------|------|-----------|
| 1.0 | 2025-12-10 | 초안 작성 |
| 2.0 | 2025-12-11 | 프레임워크 구현 코드로 전환 (컨텐츠 샘플 코드 제거) |
