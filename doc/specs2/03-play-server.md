# 03. Play 서버 프레임워크 구현 가이드

## 문서 목적

이 문서는 Play 서버 **프레임워크 내부 구현**을 설명합니다. 컨텐츠 개발자가 구현할 코드가 아닌, 프레임워크가 제공해야 하는 핵심 컴포넌트의 구현 방법을 다룹니다.

---

## 1. Play 서버 아키텍처

### 1.1 핵심 컴포넌트

```
┌─────────────────────────────────────────────────────────────────┐
│                         Play 서버                               │
│                                                                 │
│  ┌──────────────┐      ┌──────────────┐      ┌──────────────┐  │
│  │ Client Layer │      │ Stage Layer  │      │  ZMQ Layer │  │
│  │              │      │              │      │              │  │
│  │ TCP/WebSocket│◄────►│ BaseStage    │◄────►│ PlaySocket   │  │
│  │ Authenticator│      │ XStageSender │      │ RouterSocket │  │
│  └──────────────┘      └──────────────┘      └──────────────┘  │
│         ▲                      ▲                      ▲        │
│         │                      │                      │        │
│         └──────────────────────┴──────────────────────┘        │
│                    PlayDispatcher                              │
└─────────────────────────────────────────────────────────────────┘
```

### 1.2 프레임워크 컴포넌트 구조

| 컴포넌트 | 역할 | 참조 파일 |
|---------|------|----------|
| **BaseStage** | Stage 이벤트 루프, Actor 관리 | `Core/Play/Base/BaseStage.cs` |
| **BaseActor** | Actor 래퍼, XActorSender 연결 | `Core/Play/Base/BaseActor.cs` |
| **XStageSender** | IStageSender 구현 | `Core/Play/XStageSender.cs` |
| **XActorSender** | IActorSender 구현 | `Core/Play/XActorSender.cs` |
| **PlayDispatcher** | 메시지 라우팅 | `Core/Play/PlayDispatcher.cs` |
| **XSender** | ISender 기본 구현 | `Core/Shared/XSender.cs` |

---

## 2. XSender 구현 (기본 통신)

모든 Sender의 기본 클래스입니다. Request-Reply 패턴과 서버 간 통신을 처리합니다.

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

    // Request-Reply 응답 처리
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

    // API 서버로 메시지 전송
    public void SendToApi(string apiNid, IPacket packet)
    {
        var routePacket = RoutePacket.ApiOf(RoutePacket.Of(packet), false, true);
        _clientCommunicator.Send(apiNid, routePacket);
    }

    // API 서버로 Request-Reply 요청 (async/await)
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

    // API 서버로 Request-Reply 요청 (콜백)
    public void RequestToApi(string apiNid, IPacket packet, ReplyCallback replyCallback)
    {
        var seq = _reqCache.GetSequence();
        _reqCache.Put(seq, new ReplyObject(replyCallback));
        var routePacket = RoutePacket.ApiOf(RoutePacket.Of(packet), false, true);
        routePacket.SetMsgSeq(seq);
        _clientCommunicator.Send(apiNid, routePacket);
    }

    // Stage로 메시지 전송
    public void SendToStage(string playNid, long stageId, long accountId, IPacket packet)
    {
        var routePacket = RoutePacket.StageOf(stageId, accountId, RoutePacket.Of(packet), false, true);
        _clientCommunicator.Send(playNid, routePacket);
    }

    // Stage로 Request-Reply 요청
    public async Task<IPacket> RequestToStage(string playNid, long stageId, long accountId, IPacket packet)
    {
        var seq = _reqCache.GetSequence();
        var tcs = new TaskCompletionSource<RoutePacket>();
        _reqCache.Put(seq, new ReplyObject(taskCompletionSource: tcs));

        var routePacket = RoutePacket.StageOf(stageId, accountId, RoutePacket.Of(packet), false, true);
        routePacket.SetMsgSeq(seq);
        _clientCommunicator.Send(playNid, routePacket);

        var replyPacket = await tcs.Task;
        return CPacket.Of(replyPacket);
    }

    // 현재 요청 컨텍스트 관리
    public void SetCurrentPacketHeader(RouteHeader currentHeader)
    {
        CurrentHeader = currentHeader;
    }

    public void ClearCurrentPacketHeader()
    {
        CurrentHeader = null;
    }
}
```

---

## 3. XStageSender 구현 (IStageSender)

Stage에서 사용하는 Sender 구현입니다. 타이머, AsyncBlock, CloseStage 기능을 제공합니다.

```csharp
// 참조: D:\project\kairos\playhouse\playhouse-net\PlayHouse\PlayHouse\Core\Play\XStageSender.cs
internal class XStageSender : XSender, IStageSender
{
    private readonly IPlayDispatcher _dispatcher;
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
    }

    // 반복 타이머 추가
    public long AddRepeatTimer(TimeSpan initialDelay, TimeSpan period, TimerCallbackTask timerCallback)
    {
        var timerId = TimerIdMaker.MakeId();
        var packet = RoutePacket.AddTimerOf(
            TimerMsg.Types.Type.Repeat,
            StageId,
            timerId,
            timerCallback,
            initialDelay,
            period
        );
        _dispatcher.OnPost(packet);
        _timerIds.Add(timerId);
        return timerId;
    }

    // 카운트 타이머 추가
    public long AddCountTimer(TimeSpan initialDelay, int count, TimeSpan period, TimerCallbackTask timerCallback)
    {
        var timerId = TimerIdMaker.MakeId();
        var packet = RoutePacket.AddTimerOf(
            TimerMsg.Types.Type.Count,
            StageId,
            timerId,
            timerCallback,
            initialDelay,
            period,
            count
        );
        _dispatcher.OnPost(packet);
        _timerIds.Add(timerId);
        return timerId;
    }

    // 타이머 취소
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

    public bool HasTimer(long timerId)
    {
        return _timerIds.Contains(timerId);
    }

    // Stage 종료 (모든 타이머 취소 후 Destroy 패킷 전송)
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

    // 클라이언트로 메시지 전송 (StageId 포함)
    public override void SendToClient(string sessionNid, long sid, IPacket packet)
    {
        var routePacket = RoutePacket.ClientOf(ServiceId, sid, packet, StageId);
        _clientCommunicator.Send(sessionNid, routePacket);
    }

    // AsyncBlock: 외부 I/O 작업을 논블로킹으로 처리
    public void AsyncBlock(AsyncPreCallback preCallback, AsyncPostCallback? postCallback = null)
    {
        Task.Run(async () =>
        {
            var result = await preCallback.Invoke();
            if (postCallback != null)
            {
                var packet = AsyncBlockPacket.Of(StageId, postCallback, result!);
                _dispatcher.OnPost(packet);
            }
        });
    }

    public void SetStageType(string stageType)
    {
        StageType = stageType;
    }
}
```

---

## 4. XActorSender 구현 (IActorSender)

Actor가 사용하는 Sender 구현입니다.

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

    // Stage에서 퇴장
    public void LeaveStage()
    {
        _baseStage.LeaveStage(_routeAccountId, _sessionNid, _sid);
    }

    // 클라이언트로 메시지 전송
    public void SendToClient(IPacket packet)
    {
        _baseStage.StageSender.SendToClient(_sessionNid, _sid, packet);
    }

    // API 서버로 메시지 전송
    public void SendToApi(IPacket packet)
    {
        _baseStage.StageSender.SendToApi(_apiNid, packet);
    }

    // API 서버로 Request-Reply 요청
    public async Task<IPacket> RequestToApi(IPacket packet)
    {
        return await _baseStage.StageSender.RequestToApi(_apiNid, packet);
    }

    // 세션 정보 업데이트 (재연결 시)
    public void Update(string sessionNetworkId, long sessionId, string apiNetworkId)
    {
        _sessionNid = sessionNetworkId;
        _sid = sessionId;
        _apiNid = apiNetworkId;
    }
}
```

---

## 5. BaseActor 구현

Actor와 XActorSender를 연결하는 래퍼입니다.

```csharp
// 참조: D:\project\kairos\playhouse\playhouse-net\PlayHouse\PlayHouse\Core\Play\Base\BaseActor.cs
internal class BaseActor
{
    public IActor Actor { get; }
    public XActorSender ActorSender { get; }

    public BaseActor(IActor actor, XActorSender actorSender)
    {
        Actor = actor;
        ActorSender = actorSender;
    }
}
```

---

## 6. BaseStage 이벤트 루프 구현

Lock-free 이벤트 루프를 사용하여 Stage 메시지를 순차 처리합니다.

```csharp
// 참조: D:\project\kairos\playhouse\playhouse-net\PlayHouse\PlayHouse\Core\Play\Base\BaseStage.cs
internal class BaseStage
{
    private readonly ConcurrentQueue<RoutePacket> _msgQueue = new();
    private readonly AtomicBoolean _isProcessing = new(false);
    private readonly Dictionary<long, BaseActor> _actors = new();
    private readonly BaseStageCmdHandler _cmdHandler;

    public XStageSender StageSender { get; }
    public IStage Stage { get; }

    public BaseStage(IStage stage, XStageSender stageSender, BaseStageCmdHandler cmdHandler)
    {
        Stage = stage;
        StageSender = stageSender;
        _cmdHandler = cmdHandler;
    }

    // 메시지를 큐에 추가하고 처리 루프 시작
    public void Post(RoutePacket routePacket)
    {
        _msgQueue.Enqueue(routePacket);

        // CAS로 단일 처리 루프 보장
        if (_isProcessing.CompareAndSet(false, true))
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await ProcessMessageLoopAsync();
                }
                catch (Exception ex)
                {
                    _log.LogError(ex, "Unhandled exception in stage {StageId}", StageSender.StageId);
                }
            });
        }
    }

    private async Task ProcessMessageLoopAsync()
    {
        do
        {
            // 큐의 모든 메시지 처리
            while (_msgQueue.TryDequeue(out var packet))
            {
                using (packet)
                {
                    await DispatchAsync(packet);
                }
            }

            _isProcessing.Set(false);

            // 이중 체크: 처리 중 새 메시지 도착 시 재개
        } while (!_msgQueue.IsEmpty && _isProcessing.CompareAndSet(false, true));
    }

    private async Task DispatchAsync(RoutePacket packet)
    {
        StageSender.SetCurrentPacketHeader(packet.RouteHeader);

        try
        {
            if (packet.IsBase())
            {
                // 시스템 메시지 (CreateStage, JoinStage 등)
                await _cmdHandler.Dispatch(this, packet);
            }
            else if (packet.IsTimer())
            {
                // 타이머 콜백
                await packet.TimerCallback!.Invoke();
            }
            else if (packet.IsAsyncBlock())
            {
                // AsyncBlock 결과 처리
                await packet.AsyncPostCallback!.Invoke(packet.AsyncResult!);
            }
            else
            {
                // 컨텐츠 메시지
                var accountId = packet.AccountId;
                if (accountId != 0 && _actors.TryGetValue(accountId, out var baseActor))
                {
                    // 클라이언트 메시지 → OnDispatch(IActor, IPacket)
                    await Stage.OnDispatch(baseActor.Actor, packet.ToContentsPacket());
                }
                else
                {
                    // 서버 간 메시지 → OnDispatch(IPacket)
                    await Stage.OnDispatch(packet.ToContentsPacket());
                }
            }
        }
        finally
        {
            StageSender.ClearCurrentPacketHeader();
        }
    }

    // Actor 추가
    public void AddActor(BaseActor baseActor)
    {
        _actors[baseActor.ActorSender.AccountId] = baseActor;
    }

    // Actor 제거
    public void RemoveActor(long accountId)
    {
        _actors.Remove(accountId);
    }

    // Actor 조회
    public BaseActor? GetActor(long accountId)
    {
        return _actors.GetValueOrDefault(accountId);
    }

    // Actor의 Stage 퇴장 처리
    public void LeaveStage(long accountId, string sessionNid, long sid)
    {
        if (_actors.TryGetValue(accountId, out var baseActor))
        {
            _actors.Remove(accountId);
            // 추가 정리 로직...
        }
    }
}
```

---

## 7. BaseStageCmdHandler 구현

시스템 메시지(CreateStage, JoinStage 등)를 처리하는 디스패처입니다.

```csharp
// 참조: D:\project\kairos\playhouse\playhouse-net\PlayHouse\PlayHouse\Core\Play\Base\BaseStageCmdHandler.cs
internal class BaseStageCmdHandler
{
    private readonly Dictionary<string, IBaseStageCmd> _maps = new();

    public void Register(string msgId, IBaseStageCmd baseStageCmd)
    {
        if (!_maps.TryAdd(msgId, baseStageCmd))
        {
            throw new InvalidOperationException($"Already exist command - [msgId:{msgId}]");
        }
    }

    public async Task Dispatch(BaseStage baseStage, RoutePacket request)
    {
        var msgId = request.MsgId;
        if (request.IsBase())
        {
            if (_maps.TryGetValue(msgId, out var cmd))
            {
                await cmd.Execute(baseStage, request);
            }
            else
            {
                _log.Error(() => $"not registered message - [msgId:{msgId}]");
            }
        }
        else
        {
            _log.Error(() => $"Invalid packet - [msgId:{msgId}]");
        }
    }
}

// 명령 인터페이스
internal interface IBaseStageCmd
{
    Task Execute(BaseStage baseStage, RoutePacket routePacket);
}
```

---

## 8. Stage 생성 명령 구현

```csharp
// 참조: D:\project\kairos\playhouse\playhouse-net\PlayHouse\PlayHouse\Core\Play\Base\Command\CreateStageCmd.cs
internal class CreateStageCmd : IBaseStageCmd
{
    public async Task Execute(BaseStage baseStage, RoutePacket routePacket)
    {
        var req = CreateStageReq.Parser.ParseFrom(routePacket.Span);

        try
        {
            // 1. IStage.OnCreate() 호출
            var (result, reply) = await baseStage.Stage.OnCreate(
                CPacket.Of(req.PayloadId, req.Payload)
            );

            if (result)
            {
                // 2. IStage.OnPostCreate() 호출
                await baseStage.Stage.OnPostCreate();

                // 3. 성공 응답
                baseStage.StageSender.Reply(new CreateStageRes
                {
                    PayloadId = reply.MsgId,
                    Payload = ByteString.CopyFrom(reply.Payload.DataSpan)
                });
            }
            else
            {
                // 실패 응답
                baseStage.StageSender.Reply((ushort)BaseErrorCode.StageCreateFailed);
            }
        }
        catch (Exception ex)
        {
            _log.Error(() => $"Stage creation failed: {ex.Message}");
            baseStage.StageSender.Reply((ushort)BaseErrorCode.UncheckedContentsError);
        }
    }
}
```

### 8.1 Stage 생성 동시성 처리

동일한 StageId로 동시에 CreateStage 요청이 들어올 경우의 처리 정책입니다.

#### PlayDispatcher에서 Stage 관리

```csharp
// PlayDispatcher 내부 Stage 저장소
internal class PlayDispatcher : IPlayDispatcher
{
    private readonly ConcurrentDictionary<long, BaseStage> _stages = new();

    public void OnPost(RoutePacket routePacket)
    {
        var stageId = routePacket.RouteHeader.StageId;

        if (routePacket.MsgId == CreateStageReq.Descriptor.Name)
        {
            // Stage 생성 요청: GetOrAdd로 원자적 생성 보장
            var baseStage = _stages.GetOrAdd(stageId, id => CreateNewStage(id, routePacket));

            // 이미 존재하는 Stage인 경우
            if (baseStage.IsCreated)
            {
                // 중복 요청 - 에러 응답
                ReplyError(routePacket, BaseErrorCode.StageAlreadyExists);
                return;
            }

            baseStage.Post(routePacket);
        }
        else
        {
            // 일반 메시지: 기존 Stage로 라우팅
            if (_stages.TryGetValue(stageId, out var baseStage))
            {
                baseStage.Post(routePacket);
            }
            else
            {
                ReplyError(routePacket, BaseErrorCode.StageNotFound);
            }
        }
    }

    private BaseStage CreateNewStage(long stageId, RoutePacket routePacket)
    {
        var req = CreateStageReq.Parser.ParseFrom(routePacket.Span);
        var stageSender = new XStageSender(ServiceId, stageId, this, _clientCommunicator, _reqCache);
        stageSender.SetStageType(req.StageType);

        var stage = _playProducer.GetStage(req.StageType, stageSender);
        return new BaseStage(stage, stageSender, _cmdHandler);
    }
}
```

#### 동시성 시나리오 및 처리

| 시나리오 | 처리 | 에러 코드 |
|---------|------|-----------|
| 새 StageId로 생성 요청 | 정상 생성 | - |
| 이미 존재하는 StageId로 생성 요청 | 에러 응답 | `StageAlreadyExists (201)` |
| 생성 중인 StageId로 동시 요청 | 첫 번째 요청만 생성, 나머지 에러 | `StageAlreadyExists (201)` |
| 존재하지 않는 StageId로 메시지 | 에러 응답 | `StageNotFound (200)` |

#### GetOrCreateStage 처리

API 서버에서 `GetOrCreateStage()`를 호출할 경우:

```csharp
// GetOrCreateStageCmd 내부
public async Task Execute(BaseStage baseStage, RoutePacket routePacket)
{
    var req = GetOrCreateStageReq.Parser.ParseFrom(routePacket.Span);

    bool isCreated = false;

    // Stage가 이미 생성되었는지 확인
    if (!baseStage.IsCreated)
    {
        // 첫 번째 요청: Stage 생성
        var (result, reply) = await baseStage.Stage.OnCreate(
            CPacket.Of(req.CreatePayloadId, req.CreatePayload)
        );

        if (result)
        {
            await baseStage.Stage.OnPostCreate();
            baseStage.MarkAsCreated();
            isCreated = true;
        }
        else
        {
            baseStage.StageSender.Reply((ushort)BaseErrorCode.StageCreateFailed);
            return;
        }
    }

    // 응답
    baseStage.StageSender.Reply(new GetOrCreateStageRes
    {
        IsCreated = isCreated,
        PayloadId = reply?.MsgId ?? "",
        Payload = reply != null ? ByteString.CopyFrom(reply.Payload.DataSpan) : ByteString.Empty
    });
}
```

---

## 9. Actor 입장 명령 구현

```csharp
// 참조: D:\project\kairos\playhouse\playhouse-net\PlayHouse\PlayHouse\Core\Play\Base\Command\JoinStageCmd.cs
internal class JoinStageCmd : IBaseStageCmd
{
    public async Task Execute(BaseStage baseStage, RoutePacket routePacket)
    {
        var req = JoinStageReq.Parser.ParseFrom(routePacket.Span);
        var routeHeader = routePacket.RouteHeader;

        try
        {
            // 1. XActorSender 생성
            var actorSender = new XActorSender(
                routeHeader.AccountId,
                req.SessionNid,
                req.Sid,
                routeHeader.From,
                baseStage
            );

            // 2. IActor 생성 (PlayProducer 사용)
            var actor = _playProducer.GetActor(baseStage.StageSender.StageType, actorSender);

            // 3. IActor.OnCreate() 호출
            await actor.OnCreate();

            // 4. IActor.OnAuthenticate() 호출
            var authPacket = CPacket.Of(req.PayloadId, req.Payload);
            var authResult = await actor.OnAuthenticate(authPacket);

            if (!authResult)
            {
                await actor.OnDestroy();
                baseStage.StageSender.Reply((ushort)BaseErrorCode.AuthenticationFailed);
                return;
            }

            // 5. AccountId 검증 (빈 문자열이면 예외)
            if (string.IsNullOrEmpty(actorSender.AccountId))
            {
                await actor.OnDestroy();
                throw new InvalidOperationException("AccountId must be set in OnAuthenticate");
            }

            // 6. IActor.OnPostAuthenticate() 호출
            await actor.OnPostAuthenticate();

            // 7. IStage.OnJoinStage() 호출
            var joinResult = await baseStage.Stage.OnJoinStage(actor);

            if (!joinResult)
            {
                await actor.OnDestroy();
                baseStage.StageSender.Reply((ushort)BaseErrorCode.JoinStageFailed);
                return;
            }

            // 8. Actor 등록
            var baseActor = new BaseActor(actor, actorSender);
            baseStage.AddActor(baseActor);

            // 9. IStage.OnPostJoinStage() 호출
            await baseStage.Stage.OnPostJoinStage(actor);

            // 10. 성공 응답
            baseStage.StageSender.Reply(new JoinStageRes { Success = true });
        }
        catch (Exception ex)
        {
            _log.Error(() => $"Join stage failed: {ex.Message}");
            baseStage.StageSender.Reply((ushort)BaseErrorCode.UncheckedContentsError);
        }
    }
}
```

---

## 10. PlayProducer (Stage/Actor 팩토리)

컨텐츠 개발자가 Stage와 Actor 생성 함수를 등록하는 팩토리입니다.

```csharp
// 참조: D:\project\kairos\playhouse\playhouse-net\PlayHouse\PlayHouse\Abstractions\Play\PlayProducer.cs
public class PlayProducer
{
    private readonly Dictionary<string, Func<IStageSender, IStage>> _stages = new();
    private readonly Dictionary<string, Func<IActorSender, IActor>> _actors = new();

    // Stage/Actor 타입 등록
    public void Register(
        string stageType,
        Func<IStageSender, IStage> stageFactory,
        Func<IActorSender, IActor> actorFactory)
    {
        _stages[stageType] = stageFactory;
        _actors[stageType] = actorFactory;
    }

    // Stage 인스턴스 생성
    public IStage GetStage(string stageType, IStageSender stageSender)
    {
        if (_stages.TryGetValue(stageType, out var factory))
        {
            return factory(stageSender);
        }
        throw new KeyNotFoundException($"Stage type {stageType} not registered");
    }

    // Actor 인스턴스 생성
    public IActor GetActor(string stageType, IActorSender actorSender)
    {
        if (_actors.TryGetValue(stageType, out var factory))
        {
            return factory(actorSender);
        }
        throw new KeyNotFoundException($"Actor type {stageType} not registered");
    }

    internal bool IsValidType(string stageType)
    {
        return _stages.ContainsKey(stageType);
    }
}
```

---

## 11. 연결 상태 변경 처리

```csharp
// DisconnectNoticeCmd.cs
internal class DisconnectNoticeCmd : IBaseStageCmd
{
    public async Task Execute(BaseStage baseStage, RoutePacket routePacket)
    {
        var msg = DisconnectNoticeMsg.Parser.ParseFrom(routePacket.Span);
        var accountId = routePacket.AccountId;

        var baseActor = baseStage.GetActor(accountId);
        if (baseActor != null)
        {
            // IStage.OnConnectionChanged() 호출
            await baseStage.Stage.OnConnectionChanged(baseActor.Actor, false);
        }
    }
}

// 재연결 시 호출되는 로직
internal class ReconnectCmd : IBaseStageCmd
{
    public async Task Execute(BaseStage baseStage, RoutePacket routePacket)
    {
        var msg = ReconnectMsg.Parser.ParseFrom(routePacket.Span);
        var accountId = routePacket.AccountId;

        var baseActor = baseStage.GetActor(accountId);
        if (baseActor != null)
        {
            // 세션 정보 업데이트
            baseActor.ActorSender.Update(msg.SessionNid, msg.Sid, msg.ApiNid);

            // IStage.OnConnectionChanged() 호출
            await baseStage.Stage.OnConnectionChanged(baseActor.Actor, true);
        }
    }
}
```

---

## 12. TimerManager 구현

```csharp
// 참조: D:\project\kairos\playhouse\playhouse-net\PlayHouse\PlayHouse\Core\Shared\TimerManager.cs
internal class TimerManager : IDisposable
{
    private readonly ConcurrentDictionary<long, TimerEntry> _timers = new();
    private readonly Action<RoutePacket> _dispatchAction;

    public TimerManager(Action<RoutePacket> dispatchAction)
    {
        _dispatchAction = dispatchAction;
    }

    public void AddTimer(RoutePacket timerPacket)
    {
        var timerMsg = timerPacket.TimerMsg!;
        var timerId = timerMsg.TimerId;

        switch (timerMsg.Type)
        {
            case TimerMsg.Types.Type.Repeat:
                AddRepeatTimer(timerPacket);
                break;
            case TimerMsg.Types.Type.Count:
                AddCountTimer(timerPacket);
                break;
            case TimerMsg.Types.Type.Cancel:
                CancelTimer(timerId);
                break;
        }
    }

    private void AddRepeatTimer(RoutePacket timerPacket)
    {
        var timerMsg = timerPacket.TimerMsg!;
        var timerId = timerMsg.TimerId;

        var systemTimer = new System.Threading.Timer(
            _ => OnTimerTick(timerId),
            null,
            timerMsg.InitialDelay,
            timerMsg.Period
        );

        var entry = new TimerEntry(
            timerId,
            timerMsg.StageId,
            TimerType.Repeat,
            timerPacket.TimerCallback!,
            systemTimer
        );
        _timers.TryAdd(timerId, entry);
    }

    private void OnTimerTick(long timerId)
    {
        if (!_timers.TryGetValue(timerId, out var entry))
            return;

        // Stage 이벤트 루프로 디스패치
        var routePacket = RoutePacket.TimerCallbackOf(entry.StageId, timerId, entry.Callback);
        _dispatchAction(routePacket);

        // Count Timer 완료 확인
        if (entry.Type == TimerType.Count)
        {
            entry.DecrementCount();
            if (entry.IsCompleted)
            {
                CancelTimer(timerId);
            }
        }
    }

    private void CancelTimer(long timerId)
    {
        if (_timers.TryRemove(timerId, out var entry))
        {
            entry.Timer.Dispose();
        }
    }

    public void Dispose()
    {
        foreach (var entry in _timers.Values)
        {
            entry.Timer.Dispose();
        }
        _timers.Clear();
    }
}
```

---

## 13. 구현 체크리스트

### 13.1 Core 컴포넌트

- [ ] **XSender** - ISender 기본 구현
  - [ ] Reply(), SendToApi(), RequestToApi()
  - [ ] SendToStage(), RequestToStage()
  - [ ] CurrentHeader 관리

- [ ] **XStageSender** - IStageSender 구현
  - [ ] AddRepeatTimer(), AddCountTimer(), CancelTimer()
  - [ ] AsyncBlock()
  - [ ] CloseStage()

- [ ] **XActorSender** - IActorSender 구현
  - [ ] AccountId 속성 (get/set)
  - [ ] LeaveStage()
  - [ ] SendToClient(), SendToApi(), RequestToApi()

### 13.2 Stage 시스템

- [ ] **BaseStage** - Stage 이벤트 루프
  - [ ] ConcurrentQueue + AtomicBoolean 기반 이벤트 루프
  - [ ] Actor 관리 (추가/제거/조회)
  - [ ] 메시지 디스패치

- [ ] **BaseStageCmdHandler** - 시스템 명령 디스패치
  - [ ] CreateStageCmd, JoinStageCmd
  - [ ] DisconnectNoticeCmd, ReconnectCmd

- [ ] **PlayProducer** - Stage/Actor 팩토리
  - [ ] Register(), GetStage(), GetActor()

### 13.3 타이머 시스템

- [ ] **TimerManager** - 타이머 관리
  - [ ] Repeat/Count/Cancel 타이머
  - [ ] Stage 이벤트 루프로 콜백 디스패치

---

## 14. 참조 파일

| 파일 | 경로 | 용도 |
|------|------|------|
| **XSender.cs** | `Core/Shared/XSender.cs` | ISender 기본 구현 |
| **XStageSender.cs** | `Core/Play/XStageSender.cs` | IStageSender 구현 |
| **XActorSender.cs** | `Core/Play/XActorSender.cs` | IActorSender 구현 |
| **BaseStage.cs** | `Core/Play/Base/BaseStage.cs` | Stage 이벤트 루프 |
| **BaseActor.cs** | `Core/Play/Base/BaseActor.cs` | Actor 래퍼 |
| **BaseStageCmdHandler.cs** | `Core/Play/Base/BaseStageCmdHandler.cs` | 시스템 명령 디스패치 |
| **PlayProducer.cs** | `Abstractions/Play/PlayProducer.cs` | Stage/Actor 팩토리 |
| **TimerManager.cs** | `Core/Shared/TimerManager.cs` | 타이머 관리 |

---

## 변경 이력

| 버전 | 날짜 | 변경 내역 |
|------|------|-----------|
| 1.0 | 2025-12-10 | 초안 작성 |
| 2.0 | 2025-12-11 | 프레임워크 구현 코드로 전환 (컨텐츠 샘플 코드 제거) |
