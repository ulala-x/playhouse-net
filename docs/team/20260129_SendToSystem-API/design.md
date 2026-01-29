# SendToSystem/RequestToSystem 상세 설계

## 1. 목적 및 범위
- **목적:** `ISender`에 시스템 메시지 전송 API를 추가하여 `ISystemController.Handles()`로 등록된 핸들러로 메시지를 보낼 수 있도록 한다.
- **범위:** 프로토콜(`RouteHeader`), Sender 인터페이스/구현, 서버 수신 라우팅(Api/Play), PlaySocket 헤더 풀 초기화.

## 2. 용어 정의
- **System Message:** `ISystemController`가 등록한 핸들러에서 처리되는 서버-레벨 메시지.
- **SystemDispatcher:** 시스템 메시지 핸들러 레지스트리 및 디스패처.
- **is_system:** `RouteHeader`에 추가되는 시스템 메시지 여부 플래그.
- **ServerId:** 시스템 메시지 전송 대상 식별자(문자열). ServiceId 기반 라우팅은 사용하지 않음.

## 3. 설계 결정 및 전제
1. **ServerId 기반 라우팅**
   - `SendToSystem/RequestToSystem`은 `serverId`로만 전송한다.
2. **프로토콜 명시 플래그**
   - `RouteHeader.is_system` 필드로 시스템 메시지를 명시적으로 구분한다.
3. **하위 호환성 유지**
   - 수신 측은 `header.IsSystem == true` **또는** `SystemDispatcher.IsSystemMessage(msgId)` 조건을 충족하면 시스템 메시지로 처리한다.
4. **Phase 1 제한**
   - `ISystemController` 핸들러는 `ISender` 컨텍스트가 없으므로 **Reply 미지원**.
   - `RequestToSystem`은 응답을 기다리는 형태만 제공하되, 실제 응답은 수신 측이 별도 Reply 메커니즘을 갖춘 경우에만 성공 가능.

## 4. 인터페이스 변경
### 4.1 ISender 확장
```csharp
#region System Communication
void SendToSystem(string serverId, IPacket packet);
void RequestToSystem(string serverId, IPacket packet, ReplyCallback replyCallback);
Task<IPacket> RequestToSystem(string serverId, IPacket packet);
#endregion
```
- `IApiSender`, `IStageSender`, `IActorSender`는 `ISender` 상속이므로 별도 변경 없음.

### 4.2 프로토콜(RouteHeader)
```proto
// RouteHeader
bool is_system = 11;
```
- `RouteHeader.IsSystem` 프로퍼티가 생성된다.

## 5. 클래스 구조
### 5.1 Sender 계층
```
ISender
├── SendToApi / RequestToApi
├── SendToStage / RequestToStage
├── SendToApiService / RequestToApiService
├── SendToSystem / RequestToSystem   (신규)
└── Reply

XSender (abstract)
├── CreateSystemHeader()             (신규)
├── SendToSystem / RequestToSystem   (신규 구현)
└── SendInternal / SendRequest

XStageSender : XSender
ApiSender    : XSender
XActorSender : ISender (StageSender 위임)
```

### 5.2 SystemDispatcher 통합
```
SystemDispatcher
├── Register(ISystemController)
├── Add<TMessage>(handler)
└── DispatchAsync(RoutePacket)

ApiServer / PlayServer
└── SystemDispatcher 인스턴스 생성 + Register(systemController)
```

## 6. 데이터 흐름
### 6.1 SendToSystem/RequestToSystem 송신
1. 콘텐츠 코드에서 `ISender.SendToSystem(serverId, packet)` 호출
2. `XSender.CreateSystemHeader()`가 `RouteHeader` 생성 (`IsSystem=true`)
3. `RoutePacket.Of(header, payload)`로 패킷 생성
4. `IClientCommunicator.Send()` → `ZmqPlaySocket.Send()`

### 6.2 수신 라우팅 (ApiServer/PlayServer 공통)
1. `ZmqPlaySocket.Receive()` → `RoutePacket`
2. `OnReceive(RoutePacket packet)`
   - **Reply 패킷**이면 `RequestCache.TryComplete()` 우선 처리
   - **시스템 메시지 판별**
     - `packet.Header.IsSystem == true` **또는** `SystemDispatcher.IsSystemMessage(packet.MsgId)`
   - 시스템 메시지면 `SystemDispatcher.DispatchAsync(packet)`
   - 일반 메시지는 기존 `ApiDispatcher` / `PlayDispatcher`로 전달

### 6.3 시스템 메시지 처리
1. `SystemDispatcher`가 `payload.DataSpan`을 직접 역직렬화 (zero-copy)
2. 등록된 핸들러 실행 (`ISystemController.Handles()`에서 추가된 핸들러)
3. 예외 시 로깅 후 종료

## 7. 구체 코드 변경 사항 (핵심 요구 반영)

### 7.1 `src/PlayHouse/Core/Play/XActorSender.cs`
**목표:** `ISender` 확장 메서드 위임 추가
```csharp
// ISender Delegation 영역에 추가
public void SendToSystem(string serverId, IPacket packet)
    => StageSender.SendToSystem(serverId, packet);

public void RequestToSystem(string serverId, IPacket packet, ReplyCallback replyCallback)
    => StageSender.RequestToSystem(serverId, packet, replyCallback);

public Task<IPacket> RequestToSystem(string serverId, IPacket packet)
    => StageSender.RequestToSystem(serverId, packet);
```

### 7.2 `src/PlayHouse/Runtime/ServerMesh/PlaySocket/ZmqPlaySocket.cs`
**목표:** 헤더 풀 반환 시 `IsSystem` 초기화
```csharp
public bool Return(Proto.RouteHeader obj)
{
    ...
    obj.IsReply = false;
    obj.IsSystem = false; // 신규 필드 초기화
    obj.PayloadSize = 0;
    return true;
}
```

### 7.3 `src/PlayHouse/Abstractions/System/SystemDispatcher.cs`
- 기존 구현 유지
- **ApiServer/PlayServer에서 생성 및 Register 호출로 활성화**

### 7.4 `src/PlayHouse/Core/Api/Bootstrap/ApiServer.cs`
**추가 필드/초기화**
```csharp
private readonly SystemDispatcher _systemDispatcher;

_systemDispatcher = new SystemDispatcher(loggerFactory.CreateLogger<SystemDispatcher>());
_systemDispatcher.Register(systemController);
```

**OnReceive 라우팅 변경**
```csharp
if (packet.Header.IsSystem || _systemDispatcher.IsSystemMessage(packet.MsgId))
{
    _ = Task.Run(async () =>
    {
        try { await _systemDispatcher.DispatchAsync(packet); }
        finally { packet.Dispose(); }
    });
    return;
}

_dispatcher.Post(packet);
```

### 7.5 `src/PlayHouse/Core/Play/Bootstrap/PlayServer.cs`
**추가 필드/초기화**
```csharp
private readonly SystemDispatcher _systemDispatcher;

_systemDispatcher = new SystemDispatcher(loggerFactory.CreateLogger<SystemDispatcher>());
_systemDispatcher.Register(systemController);
```

**OnReceive 라우팅 변경**
```csharp
if (packet.Header.IsSystem || _systemDispatcher.IsSystemMessage(packet.MsgId))
{
    _ = Task.Run(async () =>
    {
        try { await _systemDispatcher.DispatchAsync(packet); }
        finally { packet.Dispose(); }
    });
    return;
}

_dispatcher?.OnPost(new RouteMessage(packet));
```

### 7.6 `src/PlayHouse/Core/Shared/XSender.cs`
**System Header/메서드 추가**
```csharp
private RouteHeader CreateSystemHeader(string msgId, ushort msgSeq)
{
    var header = CreateApiHeader(msgId, msgSeq);
    header.IsSystem = true;
    return header;
}

public void SendToSystem(string serverId, IPacket packet)
{
    var header = CreateSystemHeader(packet.MsgId, msgSeq: 0);
    SendInternal(serverId, header, packet.Payload);
}

public void RequestToSystem(string serverId, IPacket packet, ReplyCallback replyCallback)
{
    var msgSeq = NextMsgSeq();
    var replyObject = ReplyObject.CreateCallback(msgSeq, replyCallback, GetPostToStageCallback());
    var header = CreateSystemHeader(packet.MsgId, msgSeq);
    SendRequest(serverId, header, packet.Payload, replyObject);
}

public Task<IPacket> RequestToSystem(string serverId, IPacket packet)
{
    var msgSeq = NextMsgSeq();
    var (replyObject, task) = ReplyObject.CreateAsync(msgSeq);
    var header = CreateSystemHeader(packet.MsgId, msgSeq);
    SendRequest(serverId, header, packet.Payload, replyObject);
    return task;
}
```

### 7.7 `src/PlayHouse/Proto/route_header.proto`
```proto
bool is_system = 11;
```

## 8. 호환성 및 마이그레이션
- **신규 송신자:** `is_system=true`로 전송
- **구형 송신자:** `is_system` 누락 → 수신 측이 `SystemDispatcher.IsSystemMessage(msgId)`로 보완
- 모든 서버가 새 proto를 사용하면 `is_system` 기반 판별이 기본 경로가 됨

## 9. 검증 포인트
- `RouteHeader.IsSystem` 생성 여부 확인 (proto 컴파일 결과)
- 시스템 메시지가 Api/Play 모두에서 `SystemDispatcher`로 라우팅되는지 확인
- 헤더 풀 재사용 시 `IsSystem` 값이 누수되지 않는지 확인

