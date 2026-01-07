# PlayHouse-NET 핵심 인터페이스 프레임워크 구현 가이드

## 문서 목적

이 문서는 PlayHouse-NET 핵심 인터페이스의 **프레임워크 내부 구현**을 설명합니다. 컨텐츠 개발자가 사용하는 예제가 아닌, 프레임워크가 이 인터페이스들을 어떻게 구현하는지를 다룹니다.

**기준 문서**: `new-request.md`

---

## 1. 인터페이스 분류

| 분류 | 인터페이스 | 프레임워크 구현 클래스 |
|------|-----------|----------------------|
| **패킷 시스템** | `IPacket`, `IPayload` | `CPacket`, `ByteStringPayload` |
| **기본 통신** | `ISender` | `XSender` |
| **Actor 통신** | `IActorSender` | `XActorSender` |
| **Stage 통신** | `IStageSender` | `XStageSender` |
| **API 통신** | `IApiSender` | `ApiSender` (XSender 직접 상속) |
| **Play 서버** | `IActor`, `IStage` | 컨텐츠 구현 (프레임워크는 호출만) |

---

## 2. IPacket / IPayload 인터페이스

### 2.1 인터페이스 정의

```csharp
public interface IPayload : IDisposable
{
    ReadOnlyMemory<byte> Data { get; }
    ReadOnlySpan<byte> DataSpan => Data.Span;
}

public interface IPacket : IDisposable
{
    string MsgId { get; }
    IPayload Payload { get; }
}
```

### 2.2 프레임워크 구현: CPacket

```csharp
// 참조: PlayHouse/Core/Shared/CPacket.cs
internal class CPacket : IPacket
{
    private readonly string _msgId;
    private readonly IPayload _payload;
    private bool _disposed;

    public string MsgId => _msgId;
    public IPayload Payload => _payload;

    private CPacket(string msgId, IPayload payload)
    {
        _msgId = msgId;
        _payload = payload;
    }

    // RoutePacket에서 CPacket 생성
    public static CPacket Of(RoutePacket routePacket)
    {
        return new CPacket(routePacket.MsgId, routePacket.Payload);
    }

    // MsgId와 Payload로 직접 생성
    public static CPacket Of(string msgId, ByteString payload)
    {
        return new CPacket(msgId, new ByteStringPayload(payload));
    }

    public static CPacket Of(string msgId, IPayload payload)
    {
        return new CPacket(msgId, payload);
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _payload?.Dispose();
            _disposed = true;
        }
    }
}
```

### 2.3 프레임워크 구현: ByteStringPayload

```csharp
// 참조: PlayHouse/Core/Shared/ByteStringPayload.cs
internal class ByteStringPayload : IPayload
{
    private readonly ByteString _data;

    public ByteStringPayload(ByteString data)
    {
        _data = data;
    }

    public ReadOnlyMemory<byte> Data => _data.Memory;

    public void Dispose()
    {
        // ByteString은 immutable이므로 별도 정리 불필요
    }
}
```

---

## 3. ISender 인터페이스

### 3.1 인터페이스 정의

```csharp
public delegate void ReplyCallback(IPacket reply);

public interface ISender
{
    ushort ServiceId { get; }

    // API 서버 통신
    void SendToApi(string apiNid, IPacket packet);
    void RequestToApi(string apiNid, IPacket packet, ReplyCallback replyCallback);
    Task<IPacket> RequestToApi(string apiNid, IPacket packet);

    // Stage 통신
    void SendToStage(string playNid, long stageId, IPacket packet);
    void RequestToStage(string playNid, long stageId, IPacket packet, ReplyCallback replyCallback);
    Task<IPacket> RequestToStage(string playNid, long stageId, IPacket packet);

    // 응답
    void Reply(ushort errorCode);
    void Reply(IPacket reply);
}
```

### 3.2 프레임워크 구현: XSender

```csharp
// 참조: D:\project\kairos\playhouse\playhouse-net\PlayHouse\PlayHouse\Core\Shared\XSender.cs
internal class XSender : ISender
{
    private readonly IClientCommunicator _clientCommunicator;
    private readonly RequestCache _reqCache;
    protected RouteHeader? CurrentHeader;

    public ushort ServiceId { get; }

    public XSender(ushort serviceId, IClientCommunicator clientCommunicator, RequestCache reqCache)
    {
        ServiceId = serviceId;
        _clientCommunicator = clientCommunicator;
        _reqCache = reqCache;
    }

    #region Reply 구현

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
        if (CurrentHeader == null) return;

        var msgSeq = CurrentHeader.Header.MsgSeq;
        if (msgSeq == 0) return;  // Send는 MsgSeq=0, Reply 불필요

        var from = CurrentHeader.From;
        var routePacket = RoutePacket.ReplyOf(ServiceId, CurrentHeader, errorCode, reply);
        routePacket.RouteHeader.AccountId = CurrentHeader.AccountId;
        _clientCommunicator.Send(from, routePacket);
    }

    #endregion

    #region API 서버 통신 구현

    public void SendToApi(string apiNid, IPacket packet)
    {
        var routePacket = RoutePacket.ApiOf(RoutePacket.Of(packet), false, true);
        _clientCommunicator.Send(apiNid, routePacket);
    }

    public void RequestToApi(string apiNid, IPacket packet, ReplyCallback replyCallback)
    {
        var seq = _reqCache.GetSequence();
        _reqCache.Put(seq, new ReplyObject(replyCallback));

        var routePacket = RoutePacket.ApiOf(RoutePacket.Of(packet), false, true);
        routePacket.SetMsgSeq(seq);
        _clientCommunicator.Send(apiNid, routePacket);
    }

    public async Task<IPacket> RequestToApi(string apiNid, IPacket packet)
    {
        var seq = _reqCache.GetSequence();
        var tcs = new TaskCompletionSource<RoutePacket>();
        _reqCache.Put(seq, new ReplyObject(taskCompletionSource: tcs));

        var routePacket = RoutePacket.ApiOf(RoutePacket.Of(packet), false, true);
        routePacket.SetMsgSeq(seq);
        _clientCommunicator.Send(apiNid, routePacket);

        var replyPacket = await tcs.Task;
        return CPacket.Of(replyPacket);
    }

    #endregion

    #region Stage 통신 구현

    public void SendToStage(string playNid, long stageId, IPacket packet)
    {
        var routePacket = RoutePacket.StageOf(stageId, 0, RoutePacket.Of(packet), false, true);
        _clientCommunicator.Send(playNid, routePacket);
    }

    public void RequestToStage(string playNid, long stageId, IPacket packet, ReplyCallback replyCallback)
    {
        var seq = _reqCache.GetSequence();
        _reqCache.Put(seq, new ReplyObject(replyCallback));

        var routePacket = RoutePacket.StageOf(stageId, 0, RoutePacket.Of(packet), false, true);
        routePacket.SetMsgSeq(seq);
        _clientCommunicator.Send(playNid, routePacket);
    }

    public async Task<IPacket> RequestToStage(string playNid, long stageId, IPacket packet)
    {
        var seq = _reqCache.GetSequence();
        var tcs = new TaskCompletionSource<RoutePacket>();
        _reqCache.Put(seq, new ReplyObject(taskCompletionSource: tcs));

        var routePacket = RoutePacket.StageOf(stageId, 0, RoutePacket.Of(packet), false, true);
        routePacket.SetMsgSeq(seq);
        _clientCommunicator.Send(playNid, routePacket);

        var replyPacket = await tcs.Task;
        return CPacket.Of(replyPacket);
    }

    #endregion

    #region 컨텍스트 관리

    public void SetCurrentPacketHeader(RouteHeader currentHeader)
    {
        CurrentHeader = currentHeader;
    }

    public void ClearCurrentPacketHeader()
    {
        CurrentHeader = null;
    }

    #endregion
}
```

---

## 4. IActorSender 인터페이스

### 4.1 인터페이스 정의

```csharp
public interface IActorSender : ISender
{
    string AccountId { get; set; }   // OnAuthenticate 성공 시 설정 필수
    void LeaveStage();
    void SendToClient(IPacket packet);
}
```

**AccountId 규칙**:
- `OnAuthenticate()`에서 인증 성공 시 반드시 설정
- 빈 문자열(`""`)이면 예외 발생 및 연결 종료

**LeaveStage() 호출 후 동작**:

컨텐츠에서 `actorSender.LeaveStage()`를 호출하면 다음 순서로 처리됩니다:

1. **Actor 제거**: `BaseStage._actors`에서 해당 Actor 삭제
2. **IActor.OnDestroy() 호출**: Actor 정리 콜백 (컨텐츠에서 구현)
3. **클라이언트 연결 유지**: 연결은 끊지 않음 (클라이언트가 다른 Stage로 이동 가능)
4. **이후 메시지**: 해당 Actor로 오는 메시지는 `OnDispatch(IPacket)` (서버 간)으로 라우팅

```csharp
// BaseStage.LeaveStage() 내부 로직
public void LeaveStage(long accountId, string sessionNid, long sid)
{
    if (_actors.TryGetValue(accountId, out var baseActor))
    {
        _actors.Remove(accountId);              // 1. Actor 딕셔너리에서 제거
        _ = baseActor.Actor.OnDestroy();        // 2. IActor.OnDestroy() 호출
        // 연결은 끊지 않음                       // 3. 클라이언트 연결 유지
    }
}
```

**주의**: `LeaveStage()` 호출 후에도 클라이언트 TCP 연결은 유지됩니다. 연결을 끊으려면 별도로 클라이언트 연결 종료 로직이 필요합니다.

### 4.2 프레임워크 구현: XActorSender

```csharp
// 참조: D:\project\kairos\playhouse\playhouse-net\PlayHouse\PlayHouse\Core\Play\XActorSender.cs
internal class XActorSender : IActorSender
{
    private readonly BaseStage _baseStage;

    // 내부 라우팅용 (RouteHeader에서 받은 값, 서버 간 통신에 사용)
    private readonly long _routeAccountId;
    private string _sessionNid;
    private long _sid;
    private string _apiNid;

    /// <summary>
    /// 컨텐츠 개발자가 OnAuthenticate()에서 설정하는 사용자 계정 ID
    /// 빈 문자열("")이면 예외 발생 및 연결 종료
    /// </summary>
    public string AccountId { get; set; } = "";

    public ushort ServiceId => _baseStage.StageSender.ServiceId;

    public XActorSender(
        long routeAccountId,   // 내부 라우팅용 (RouteHeader.AccountId)
        string sessionNid,
        long sid,
        string apiNid,
        BaseStage baseStage)
    {
        _routeAccountId = routeAccountId;
        _sessionNid = sessionNid;
        _sid = sid;
        _apiNid = apiNid;
        _baseStage = baseStage;
    }

    #region IActorSender 구현

    /// <summary>
    /// Actor를 Stage에서 퇴장시킴
    /// 연결 종료 및 Actor 정리 작업 수행
    /// </summary>
    public void LeaveStage()
    {
        _baseStage.LeaveStage(_routeAccountId, _sessionNid, _sid);
    }

    /// <summary>
    /// 연결된 클라이언트로 메시지 전송
    /// </summary>
    public void SendToClient(IPacket packet)
    {
        _baseStage.StageSender.SendToClient(_sessionNid, _sid, packet);
    }

    #endregion

    #region ISender 위임 구현

    public void SendToApi(string apiNid, IPacket packet)
    {
        _baseStage.StageSender.SendToApi(apiNid, packet);
    }

    public async Task<IPacket> RequestToApi(string apiNid, IPacket packet)
    {
        return await _baseStage.StageSender.RequestToApi(apiNid, packet);
    }

    public void RequestToApi(string apiNid, IPacket packet, ReplyCallback replyCallback)
    {
        _baseStage.StageSender.RequestToApi(apiNid, packet, replyCallback);
    }

    public void SendToStage(string playNid, long stageId, IPacket packet)
    {
        _baseStage.StageSender.SendToStage(playNid, stageId, _routeAccountId, packet);
    }

    public async Task<IPacket> RequestToStage(string playNid, long stageId, IPacket packet)
    {
        return await _baseStage.StageSender.RequestToStage(playNid, stageId, _routeAccountId, packet);
    }

    public void RequestToStage(string playNid, long stageId, IPacket packet, ReplyCallback replyCallback)
    {
        _baseStage.StageSender.RequestToStage(playNid, stageId, packet, replyCallback);
    }

    public void Reply(ushort errorCode)
    {
        _baseStage.StageSender.Reply(errorCode);
    }

    public void Reply(IPacket reply)
    {
        _baseStage.StageSender.Reply(reply);
    }

    #endregion

    #region 세션 관리

    /// <summary>
    /// 재연결 시 세션 정보 업데이트
    /// </summary>
    public void Update(string sessionNetworkId, long sessionId, string apiNetworkId)
    {
        _sessionNid = sessionNetworkId;
        _sid = sessionId;
        _apiNid = apiNetworkId;
    }

    #endregion
}
```

---

## 5. IStageSender 인터페이스

### 5.1 인터페이스 정의

```csharp
public delegate Task<object> AsyncPreCallback();
public delegate Task AsyncPostCallback(object result);

public interface IStageSender : ISender
{
    long StageId { get; }
    string StageType { get; }

    // 타이머
    long AddRepeatTimer(TimeSpan initialDelay, TimeSpan period, Func<Task> callback);
    long AddCountTimer(TimeSpan initialDelay, TimeSpan period, int count, Func<Task> callback);
    void CancelTimer(long timerId);
    bool HasTimer(long timerId);

    // Stage 관리
    void CloseStage();

    // 비동기 블록
    void AsyncBlock(AsyncPreCallback preCallback, AsyncPostCallback? postCallback = null);
}
```

### 5.2 프레임워크 구현: XStageSender

```csharp
// 참조: D:\project\kairos\playhouse\playhouse-net\PlayHouse\PlayHouse\Core\Play\XStageSender.cs
internal class XStageSender : XSender, IStageSender
{
    private readonly IPlayDispatcher _dispatcher;
    private readonly IClientCommunicator _clientCommunicator;
    private readonly HashSet<long> _timerIds = new();

    public long StageId { get; }
    public string StageType { get; private set; } = "";

    public XStageSender(
        ushort serviceId,
        long stageId,
        IPlayDispatcher dispatcher,
        IClientCommunicator clientCommunicator,
        RequestCache reqCache)
        : base(serviceId, clientCommunicator, reqCache)
    {
        StageId = stageId;
        _dispatcher = dispatcher;
        _clientCommunicator = clientCommunicator;
    }

    #region 타이머 구현

    /// <summary>
    /// 무한 반복 타이머 등록
    /// </summary>
    public long AddRepeatTimer(TimeSpan initialDelay, TimeSpan period, Func<Task> callback)
    {
        var timerId = TimerIdMaker.MakeId();
        var packet = RoutePacket.AddTimerOf(
            TimerMsg.Types.Type.Repeat,
            StageId,
            timerId,
            callback,
            initialDelay,
            period
        );
        _dispatcher.OnPost(packet);
        _timerIds.Add(timerId);
        return timerId;
    }

    /// <summary>
    /// 지정 횟수 반복 타이머 등록
    /// </summary>
    public long AddCountTimer(TimeSpan initialDelay, TimeSpan period, int count, Func<Task> callback)
    {
        var timerId = TimerIdMaker.MakeId();
        var packet = RoutePacket.AddTimerOf(
            TimerMsg.Types.Type.Count,
            StageId,
            timerId,
            callback,
            initialDelay,
            period,
            count
        );
        _dispatcher.OnPost(packet);
        _timerIds.Add(timerId);
        return timerId;
    }

    /// <summary>
    /// 타이머 취소
    /// </summary>
    public void CancelTimer(long timerId)
    {
        var packet = RoutePacket.AddTimerOf(
            TimerMsg.Types.Type.Cancel,
            StageId,
            timerId,
            () => Task.CompletedTask,
            TimeSpan.Zero,
            TimeSpan.Zero
        );
        _dispatcher.OnPost(packet);
        _timerIds.Remove(timerId);
    }

    /// <summary>
    /// 타이머 존재 여부 확인
    /// </summary>
    public bool HasTimer(long timerId)
    {
        return _timerIds.Contains(timerId);
    }

    #endregion

    #region Stage 관리

    /// <summary>
    /// Stage 종료 - 모든 타이머 취소 후 Destroy 메시지 전송
    /// </summary>
    public void CloseStage()
    {
        // 모든 타이머 취소
        foreach (var timerId in _timerIds)
        {
            var packet = RoutePacket.AddTimerOf(
                TimerMsg.Types.Type.Cancel,
                StageId,
                timerId,
                () => Task.CompletedTask,
                TimeSpan.Zero,
                TimeSpan.Zero
            );
            _dispatcher.OnPost(packet);
        }
        _timerIds.Clear();

        // Stage Destroy 패킷 전송
        var destroyPacket = RoutePacket.StageOf(
            StageId, 0,
            RoutePacket.Of(DestroyStage.Descriptor.Name, new EmptyPayload()),
            true, false
        );
        _dispatcher.OnPost(destroyPacket);
    }

    #endregion

    #region AsyncBlock 구현

    /// <summary>
    /// 외부 I/O 작업을 Event Loop 외부에서 실행 후 결과를 Event Loop로 전달
    /// </summary>
    public void AsyncBlock(AsyncPreCallback preCallback, AsyncPostCallback? postCallback = null)
    {
        Task.Run(async () =>
        {
            // preCallback: 별도 스레드에서 실행 (DB 쿼리, HTTP 호출 등)
            var result = await preCallback.Invoke();

            if (postCallback != null)
            {
                // postCallback: Event Loop에서 실행 (Stage 상태 안전하게 접근)
                var packet = AsyncBlockPacket.Of(StageId, postCallback, result!);
                _dispatcher.OnPost(packet);
            }
        });
    }

    #endregion

    #region 클라이언트 전송

    /// <summary>
    /// 특정 클라이언트로 메시지 전송
    /// </summary>
    public void SendToClient(string sessionNid, long sid, IPacket packet)
    {
        var routePacket = RoutePacket.ClientOf(ServiceId, sid, packet, StageId);
        _clientCommunicator.Send(sessionNid, routePacket);
    }

    #endregion

    public void SetStageType(string stageType)
    {
        StageType = stageType;
    }
}
```

---

## 6. IApiSender 인터페이스

### 6.1 인터페이스 정의

```csharp
public class StageResult(bool result)
{
    public bool Result { get; } = result;
}

public class CreateStageResult(bool result, IPacket createStageRes)
    : StageResult(result)
{
    public IPacket CreateStageRes { get; } = createStageRes;
}

public class GetOrCreateStageResult(bool result, bool isCreated, IPacket createStageRes)
    : StageResult(result)
{
    public bool IsCreated { get; } = isCreated;
    public IPacket CreateStageRes { get; } = createStageRes;
}
```

**GetOrCreateStageResult의 Result/IsCreated 조합 의미**:

| Result | IsCreated | 의미 |
|--------|-----------|------|
| `true` | `false` | Stage가 이미 존재함 (기존 Stage 반환) |
| `true` | `true` | 새 Stage 생성 성공 |
| `false` | `false` | Stage 생성 실패 (생성 시도했으나 실패) |

```csharp
public interface IApiSender : ISender
{
    Task<CreateStageResult> CreateStage(string playNid, string stageType, long stageId, IPacket packet);
    Task<GetOrCreateStageResult> GetOrCreateStage(string playNid, string stageType, long stageId, IPacket createPacket, IPacket joinPacket);
}
```

### 6.2 프레임워크 구현: ApiSender

```csharp
// XSender를 직접 상속하여 단순화된 구조
internal class ApiSender : XSender, IApiSender
{
    public ApiSender(
        ushort serviceId,
        IClientCommunicator clientCommunicator,
        RequestCache reqCache)
        : base(serviceId, clientCommunicator, reqCache)
    {
    }

    /// <summary>
    /// Play 서버에 새 Stage 생성 요청
    /// </summary>
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

    /// <summary>
    /// Stage가 없으면 생성, 있으면 기존 Stage 반환
    /// </summary>
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

## 7. IActor / IStage 인터페이스

이 인터페이스들은 **컨텐츠 개발자가 구현**합니다. 프레임워크는 이를 호출만 합니다.

### 7.1 IActor 인터페이스

```csharp
public interface IActor
{
    IActorSender ActorSender { get; }

    Task OnCreate();
    Task OnDestroy();
    Task<bool> OnAuthenticate(IPacket authPacket);
    Task OnPostAuthenticate();
}
```

**프레임워크 호출 순서** (JoinStageCmd에서):
1. `PlayProducer.GetActor()` → Actor 인스턴스 생성
2. `actor.OnCreate()` → 초기화
3. `actor.OnAuthenticate(authPacket)` → 인증 처리
4. `actor.OnPostAuthenticate()` → 후처리 (API 서버에서 정보 로드 등)

### 7.2 IStage 인터페이스

```csharp
public interface IStage
{
    IStageSender StageSender { get; }

    Task<(bool result, IPacket reply)> OnCreate(IPacket packet);
    Task OnPostCreate();
    Task OnDestory();

    Task<bool> OnJoinStage(IActor actor);
    Task OnPostJoinStage(IActor actor);

    ValueTask OnConnectionChanged(IActor actor, bool isConnected);

    Task OnDispatch(IActor actor, IPacket packet);  // 클라이언트 메시지
    Task OnDispatch(IPacket packet);                 // 서버 간 메시지
}
```

**프레임워크 호출 순서** (CreateStageCmd에서):
1. `PlayProducer.GetStage()` → Stage 인스턴스 생성
2. `stage.OnCreate(packet)` → 생성 처리
3. `stage.OnPostCreate()` → 타이머 설정 등 후처리

**메시지 디스패치** (BaseStage에서):

프레임워크는 메시지의 `RouteHeader.AccountId` 값을 기준으로 두 콜백을 구분합니다:

```csharp
// BaseStage.DispatchAsync() 내부 로직
if (accountId != 0 && _actors.TryGetValue(accountId, out var baseActor))
{
    // AccountId가 있고 해당 Actor가 존재 → 클라이언트 메시지
    await Stage.OnDispatch(baseActor.Actor, packet);
}
else
{
    // AccountId가 없거나 Actor가 없음 → 서버 간 메시지
    await Stage.OnDispatch(packet);
}
```

| 호출 조건 | 콜백 | 설명 |
|----------|------|------|
| `AccountId != 0` AND Actor 존재 | `OnDispatch(IActor, IPacket)` | 클라이언트가 보낸 메시지 |
| `AccountId == 0` OR Actor 없음 | `OnDispatch(IPacket)` | `ISender.RequestToStage()` 등 서버 간 통신 |

---

## 8. IHandlerRegister / IApiController 인터페이스

### 8.1 인터페이스 정의

```csharp
public delegate Task ApiHandler(IPacket packet, IApiSender apiSender);

public interface IHandlerRegister
{
    void Add(string msgId, ApiHandler handler);
}

public interface IApiController
{
    void Handles(IHandlerRegister handlerRegister);
}
```

### 8.2 프레임워크 구현: HandlerRegister

```csharp
// 참조: PlayHouse/Core/Api/Reflection/HandlerRegister.cs
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

### 8.3 프레임워크에서 호출하는 방식

```csharp
// ApiReflection.cs
internal class ApiReflection
{
    private readonly Dictionary<string, ApiHandleReflectionInvoker> _handlers = new();

    public ApiReflection(IServiceProvider serviceProvider)
    {
        // DI 컨테이너에서 모든 IApiController 구현체 조회
        var controllers = serviceProvider.GetServices<IApiController>();

        foreach (var controller in controllers)
        {
            var handlerRegister = new HandlerRegister(_handlers);
            // 컨텐츠 개발자가 구현한 Handles() 호출
            controller.Handles(handlerRegister);
        }
    }

    public async Task CallMethodAsync(IPacket packet, IApiSender apiSender)
    {
        if (_handlers.TryGetValue(packet.MsgId, out var invoker))
        {
            await invoker.InvokeAsync(packet, apiSender);
        }
        else
        {
            throw new ServiceException.NotRegisterMethod(packet.MsgId);
        }
    }
}
```

---

## 9. 시스템 관리 인터페이스

### 9.1 IServerInfo

```csharp
public interface IServerInfo
{
    string GetBindEndpoint();       // "tcp://0.0.0.0:5000"
    string GetNid();                // "1:1"
    int GetServerId();
    ServiceType GetServiceType();
    ushort GetServiceId();
    ServerState GetState();
    long GetLastUpdate();
    int GetActorCount();            // 현재 Stage/Actor 수 (로드밸런싱용)
}
```

### 9.2 ISystemPanel

```csharp
public interface ISystemPanel
{
    IServerInfo GetServerInfo();
    IServerInfo GetServerInfoBy(ushort serviceId);
    IServerInfo GetServerInfoByNid(string nid);
    IList<IServerInfo> GetServers();
    void Pause();
    void Resume();
    Task ShutdownAsync();
    ServerState GetServerState();

    public static string MakeNid(ushort serviceId, int serverId)
    {
        return $"{serviceId}:{serverId}";
    }
}
```

---

## 10. 구현 체크리스트

### 10.1 패킷 시스템
- [ ] **CPacket** - IPacket 구현
- [ ] **ByteStringPayload** - IPayload 구현

### 10.2 Sender 시스템
- [ ] **XSender** - ISender 기본 구현
  - [ ] Reply(), SendToApi(), RequestToApi()
  - [ ] SendToStage(), RequestToStage()

- [ ] **XActorSender** - IActorSender 구현
  - [ ] AccountId 속성
  - [ ] LeaveStage(), SendToClient()
  - [ ] Update() (재연결용)

- [ ] **XStageSender** - IStageSender 구현
  - [ ] AddRepeatTimer(), AddCountTimer()
  - [ ] CancelTimer(), HasTimer()
  - [ ] CloseStage(), AsyncBlock()

- [ ] **ApiSender** - IApiSender 구현 (XSender 직접 상속)
  - [ ] CreateStage(), GetOrCreateStage()

### 10.3 핸들러 시스템
- [ ] **HandlerRegister** - IHandlerRegister 구현
- [ ] **ApiReflection** - 핸들러 호출

---

## 11. 참조 파일

| 파일 | 경로 | 용도 |
|------|------|------|
| **XSender.cs** | `Core/Shared/XSender.cs` | ISender 구현 |
| **XActorSender.cs** | `Core/Play/XActorSender.cs` | IActorSender 구현 |
| **XStageSender.cs** | `Core/Play/XStageSender.cs` | IStageSender 구현 |
| **ApiSender.cs** | `Core/Api/ApiSender.cs` | IApiSender 구현 (XSender 직접 상속) |
| **CPacket.cs** | `Core/Shared/CPacket.cs` | IPacket 구현 |
| **HandlerRegister.cs** | `Core/Api/Reflection/HandlerRegister.cs` | IHandlerRegister 구현 |

---

## 변경 이력

| 버전 | 날짜 | 변경 내역 |
|------|------|-----------|
| 1.0 | 2025-12-10 | 초안 작성 |
| 2.0 | 2025-12-11 | 프레임워크 구현 코드로 전환 (컨텐츠 샘플 코드 제거) |
