# PlayHouse-NET 구현 계획서

> **문서 목적**: Context 초기화 시에도 독립적으로 작업 진행 가능한 구현 로드맵
> **생성일**: 2025-12-11
> **참조 스펙**: `doc/specs2/` 디렉토리

---

## 1. 프로젝트 개요

### 1.1 목표 아키텍처 전환

```
AS-IS (단일 프로세스)              →    TO-BE (분산 시스템)
─────────────────────────              ─────────────────────────
PlayHouseServer                        API Server (Stateless)
├─ HTTP API                            ├─ HTTP API
├─ TCP/WebSocket                       └─ NetMQ Client
└─ Stage/Actor                                ↕ NetMQ Router-Router
                                       Play Server (Stateful)
                                       ├─ Stage 관리
                                       ├─ Actor 관리
                                       └─ TCP/WebSocket (클라이언트 직접 연결)
```

### 1.2 핵심 변경 사항

| 항목 | 삭제 | 추가/변경 |
|------|------|----------|
| **Session 서버** | ❌ 전체 삭제 | Play 서버에서 직접 클라이언트 관리 |
| **REST API** | ❌ Play 서버에서 제거 | API 서버로 이동 |
| **통신 방식** | HTTP 기반 | NetMQ Router-Router 패턴 |
| **인증 방식** | 토큰 기반 | 직접 인증 (OnAuthenticate) |

---

## 2. 스펙 문서 참조 맵

| Phase | 주요 문서 | 보조 문서 | 핵심 내용 |
|-------|----------|----------|----------|
| **Phase 1** | [07-netmq-runtime.md](./07-netmq-runtime.md) | [02-server-communication.md](./02-server-communication.md) | NetMQ 통신 인프라 |
| **Phase 2** | [06-interfaces.md](./06-interfaces.md) | [new-request.md](./new-request.md) | 핵심 인터페이스 구현 |
| **Phase 3** | [03-play-server.md](./03-play-server.md) | [05-authentication-flow.md](./05-authentication-flow.md) | Play 서버 모듈 |
| **Phase 4** | [04-api-server.md](./04-api-server.md) | [10-request-reply-mechanism.md](./10-request-reply-mechanism.md) | API 서버 모듈 |
| **Phase 5** | [09-connector.md](./09-connector.md) | - | 클라이언트 Connector |
| **Phase 6** | [01-architecture-v2.md](./01-architecture-v2.md) | [00-implementation-guide.md](./00-implementation-guide.md) | 통합 및 검증 |

---

## 3. 의존성 그래프

```
                    ┌─────────────────────────────────────┐
                    │         Phase 1: NetMQ Runtime       │
                    │  PlaySocket, Message, Communicator   │
                    └──────────────────┬──────────────────┘
                                       │
                    ┌──────────────────▼──────────────────┐
                    │      Phase 2: 핵심 인터페이스        │
                    │  IPacket, ISender, RequestCache      │
                    └──────────────────┬──────────────────┘
                                       │
              ┌────────────────────────┼────────────────────────┐
              │                        │                        │
    ┌─────────▼─────────┐   ┌─────────▼─────────┐   ┌─────────▼─────────┐
    │   Phase 3: Play   │   │   Phase 4: API    │   │  Phase 5: Client  │
    │   서버 모듈       │   │   서버 모듈       │   │   Connector       │
    └─────────┬─────────┘   └─────────┬─────────┘   └─────────┬─────────┘
              │                        │                        │
              └────────────────────────┼────────────────────────┘
                                       │
                    ┌──────────────────▼──────────────────┐
                    │       Phase 6: 통합 및 검증         │
                    │   E2E 테스트, 성능 벤치마크         │
                    └─────────────────────────────────────┘
```

---

## 3.1 병렬 진행 가이드

### 권장 진행 방식: 하이브리드

```
┌─────────────────────────────────────────────────────────────────────────┐
│  [Phase 1-2] ────► [Phase 3 인터페이스] ────┬───► [Phase 3 구현]        │
│  단일 에이전트      단일 에이전트           │     main 브랜치           │
│  (순차 진행)       (IActor, IStage 확정)    │                           │
│                                             ├───► [Phase 4]             │
│                                             │     worktree: feature/api │
│                                             │                           │
│                                             └───► [Phase 5]             │
│                                                   worktree: feature/conn│
│                                                                         │
│  [Phase 6] ◄──────── 전체 병합 후 단일 에이전트 ─────────────────────── │
└─────────────────────────────────────────────────────────────────────────┘
```

### 병렬 진행 조건

| 단계 | 선행 조건 | 병렬 가능 |
|------|----------|----------|
| Phase 1 | 없음 | ❌ 순차 |
| Phase 2 | Phase 1 완료 | ❌ 순차 |
| Phase 3 | Phase 2 완료 | ❌ 순차 (핵심) |
| Phase 4 | Phase 3 인터페이스 확정 | ✅ worktree A |
| Phase 5 | Phase 3 인터페이스 확정 | ✅ worktree B |
| Phase 6 | Phase 3/4/5 완료 | ❌ 순차 |

### git worktree 설정 (Phase 4/5 병렬 시)

```bash
# Phase 3 인터페이스 완료 후 실행
git worktree add ../playhouse-net-api feature/api-server
git worktree add ../playhouse-net-connector feature/connector

# 작업 완료 후 병합
git checkout main
git merge feature/api-server
git merge feature/connector

# worktree 정리
git worktree remove ../playhouse-net-api
git worktree remove ../playhouse-net-connector
```

### 현재 진행 상태

- **현재 Phase**: 2 (핵심 인터페이스)
- **진행 방식**: 단일 에이전트 순차 진행
- **병렬 전환 시점**: Phase 3 인터페이스 확정 후
- **최근 완료**: Phase 1 (2025-12-11)

---

## 4. Phase별 구현 계획

### Phase 1: NetMQ 통신 계층 구현

**📖 참조 문서**: [07-netmq-runtime.md](./07-netmq-runtime.md), [02-server-communication.md](./02-server-communication.md)

**🎯 목표**: NetMQ 기반 서버 간 통신 인프라 구축

#### 작업 목록

| # | 작업 | 파일 경로 | 상세 |
|---|------|----------|------|
| 1.1 | PlaySocket 인터페이스 정의 | `Runtime/PlaySocket/IPlaySocket.cs` | Send, Receive, Bind, Connect 메서드 |
| 1.2 | NetMQPlaySocket 구현 | `Runtime/PlaySocket/NetMQPlaySocket.cs` | Router-Router 소켓 패턴, 3-Frame 메시지 |
| 1.3 | SocketConfig 정의 | `Runtime/PlaySocket/SocketConfig.cs` | 버퍼 크기, Watermark 설정 |
| 1.4 | Payload 클래스 구현 | `Runtime/Message/Payload.cs` | FramePayload, ByteStringPayload |
| 1.5 | RoutePacket 구현 | `Runtime/Message/RoutePacket.cs` | RouteHeader + Payload, Factory 메서드 |
| 1.6 | XServerCommunicator 구현 | `Runtime/XServerCommunicator.cs` | 수신 전용 스레드 |
| 1.7 | XClientCommunicator 구현 | `Runtime/XClientCommunicator.cs` | 송신 전용 스레드 (BlockingCollection) |
| 1.8 | MessageLoop 구현 | `Runtime/MessageLoop.cs` | 송수신 스레드 관리 |
| 1.9 | ServerConfig 정의 | `Abstractions/ServerConfig.cs` | NID, ServiceId, 포트, 바인드 주소 |
| 1.10 | Protobuf 메시지 정의 | `Proto/RouteHeader.proto` | RouteHeader, 시스템 메시지 |
| 1.11 | 단위 테스트 작성 | `Tests/Runtime/` | NetMQ 메시지 송수신 검증 |

#### 핵심 구현 상세

**NID (Node ID) 구조**:
```
형식: "{ServiceId}:{ServerId}"
예시: "1:1" (Play Server #1), "2:1" (API Server #1)
```

**3-Frame 메시지 구조**:
```
Frame 0: Target NID (UTF-8) - "1:1"
Frame 1: RouteHeader (Protobuf) - MsgSeq, ServiceId, MsgId, ErrorCode
Frame 2: Payload (바이너리) - Protobuf 메시지 직렬화
```

**소켓 옵션 설정**:
```csharp
_socket.Options.Identity = Encoding.UTF8.GetBytes(nid);
_socket.Options.RouterHandover = true;
_socket.Options.RouterMandatory = true;
_socket.Options.TcpKeepalive = true;
```

#### 완료 조건
- [ ] NetMQ 메시지 송수신 테스트 통과
- [ ] Router-Router 패턴 양방향 통신 검증
- [ ] NID 기반 라우팅 동작 확인

---

### Phase 2: 핵심 인터페이스 구현

**📖 참조 문서**: [06-interfaces.md](./06-interfaces.md), [new-request.md](./new-request.md), [10-request-reply-mechanism.md](./10-request-reply-mechanism.md)

**🎯 목표**: Packet 시스템 및 Sender 인터페이스 구현

#### 작업 목록

| # | 작업 | 파일 경로 | 상세 |
|---|------|----------|------|
| 2.1 | IPayload 인터페이스 | `Abstractions/IPayload.cs` | ReadOnlyMemory<byte> Data |
| 2.2 | IPacket 인터페이스 | `Abstractions/IPacket.cs` | MsgId, Payload, IDisposable |
| 2.3 | CPacket 구현 | `Core/Shared/CPacket.cs` | RoutePacket → IPacket 변환 |
| 2.4 | ISender 인터페이스 | `Abstractions/ISender.cs` | SendToApi, RequestToStage, Reply |
| 2.5 | RequestCache 구현 | `Runtime/RequestCache.cs` | MsgSeq 관리, 30초 타임아웃 |
| 2.6 | ReplyObject 구현 | `Runtime/ReplyObject.cs` | TaskCompletionSource/Callback 래핑 |
| 2.7 | XSender 기본 구현 | `Core/Shared/XSender.cs` | ISender 구현, CurrentHeader 관리 |
| 2.8 | BaseErrorCode 정의 | `Abstractions/BaseErrorCode.cs` | 시스템 에러 코드 enum |
| 2.9 | 단위 테스트 작성 | `Tests/Core/` | Request-Reply 패턴 검증 |

#### 핵심 인터페이스 정의

**ISender 인터페이스**:
```csharp
public interface ISender
{
    ushort ServiceId { get; }

    // API 서버 통신
    void SendToApi(string apiNid, IPacket packet);
    Task<IPacket> RequestToApi(string apiNid, IPacket packet);
    void RequestToApi(string apiNid, IPacket packet, ReplyCallback callback);

    // Stage 통신
    void SendToStage(string playNid, long stageId, IPacket packet);
    Task<IPacket> RequestToStage(string playNid, long stageId, IPacket packet);

    // 응답
    void Reply(ushort errorCode);
    void Reply(IPacket reply);
    void Reply(ushort errorCode, IPacket reply);
}
```

**Request-Reply 매칭 로직**:
```csharp
// 요청 전송
seq = reqCache.GetSequence();  // 1~65535 순환
reqCache.Put(seq, new ReplyObject(tcs));
routePacket.SetMsgSeq(seq);
communicator.Send(targetNid, routePacket);

// 응답 수신
reqCache.OnReply(routePacket);  // MsgSeq로 매칭
tcs.SetResult(packet);
```

#### 완료 조건
- [ ] IPacket/IPayload 단위 테스트 통과
- [ ] Request-Reply 패턴 async/await 동작 확인
- [ ] 30초 타임아웃 처리 검증

---

### Phase 3: Play 서버 모듈 구현

**📖 참조 문서**: [03-play-server.md](./03-play-server.md), [05-authentication-flow.md](./05-authentication-flow.md)

**🎯 목표**: Play 서버 모듈 구현 및 Bootstrap 제공

#### 작업 목록

| # | 작업 | 파일 경로 | 상세 |
|---|------|----------|------|
| 3.1 | IActor 인터페이스 확장 | `Abstractions/Play/IActor.cs` | OnAuthenticate, OnPostAuthenticate 추가 |
| 3.2 | IActorSender 인터페이스 | `Abstractions/Play/IActorSender.cs` | AccountId, LeaveStage, SendToClient |
| 3.3 | XActorSender 구현 | `Core/Play/XActorSender.cs` | 세션 정보 관리, 재연결 지원 |
| 3.4 | IStage 인터페이스 확장 | `Abstractions/Play/IStage.cs` | OnJoinStage, OnPostJoinStage, OnConnectionChanged |
| 3.5 | IStageSender 인터페이스 | `Abstractions/Play/IStageSender.cs` | 타이머, AsyncBlock, CloseStage |
| 3.6 | XStageSender 구현 | `Core/Play/XStageSender.cs` | TimerManager 통합 |
| 3.7 | BaseStage 구현 | `Core/Play/Base/BaseStage.cs` | Lock-free 이벤트 루프 (CAS 패턴) |
| 3.8 | BaseActor 구현 | `Core/Play/Base/BaseActor.cs` | IActor + XActorSender 래퍼 |
| 3.9 | PlayDispatcher 구현 | `Core/Play/PlayDispatcher.cs` | Stage 라우팅, 생성 관리 |
| 3.10 | BaseStageCmdHandler 구현 | `Core/Play/Base/BaseStageCmdHandler.cs` | CreateStage, JoinStage 등 시스템 명령 |
| 3.11 | TimerManager 구현 | `Core/Shared/TimerManager.cs` | Repeat/Count/Cancel 타이머 |
| 3.12 | PlayProducer 구현 | `Abstractions/Play/PlayProducer.cs` | Stage/Actor 팩토리 |
| 3.13 | PlayServerBootstrap 구현 | `Core/Play/PlayServerBootstrap.cs` | 빌더 패턴, DI 통합 |
| 3.14 | TcpSessionHandler 구현 | `Core/Play/Transport/TcpSessionHandler.cs` | TCP 클라이언트 연결 처리 |
| 3.15 | WebSocketHandler 구현 | `Core/Play/Transport/WebSocketHandler.cs` | WebSocket 연결 지원 |
| 3.16 | E2E 테스트 작성 | `Tests/Play/` | 클라이언트 직접 연결 검증 |

#### 핵심 구현 상세

**Lock-Free 이벤트 루프 (BaseStage)**:
```csharp
public void Post(RoutePacket routePacket)
{
    _msgQueue.Enqueue(routePacket);

    if (_isProcessing.CompareAndSet(false, true))
    {
        _ = Task.Run(async () => await ProcessMessageLoopAsync());
    }
}

private async Task ProcessMessageLoopAsync()
{
    do {
        while (_msgQueue.TryDequeue(out var packet)) {
            await DispatchAsync(packet);
        }
        _isProcessing.Set(false);
    } while (!_msgQueue.IsEmpty && _isProcessing.CompareAndSet(false, true));
}
```

**JoinStage 처리 흐름 (10단계)**:
```
1. XActorSender 생성
2. IActor 생성 (PlayProducer)
3. IActor.OnCreate() 호출
4. IActor.OnAuthenticate(authPacket) 호출
5. AccountId 검증 (빈 문자열 → 예외)
6. IActor.OnPostAuthenticate() 호출
7. IStage.OnJoinStage(actor) 호출
8. Actor 등록 (BaseStage.AddActor())
9. IStage.OnPostJoinStage(actor) 호출
10. 성공 응답 전송
```

**인터페이스 변경 요약**:
```csharp
// IActor 추가 메서드
Task<bool> OnAuthenticate(IPacket authPacket);  // 인증 처리
Task OnPostAuthenticate();                       // 인증 후 초기화

// IStage 변경 메서드
Task<bool> OnJoinStage(IActor actor);           // packet 파라미터 제거
Task OnPostJoinStage(IActor actor);             // 입장 후 처리
ValueTask OnConnectionChanged(IActor actor, bool isConnected);  // 재연결 처리
```

#### 완료 조건
- [ ] 클라이언트 TCP 직접 연결 E2E 테스트 통과
- [ ] 인증 플로우 (OnAuthenticate → OnPostAuthenticate → OnJoinStage) 검증
- [ ] 타이머 및 AsyncBlock 동작 확인

---

### Phase 4: API 서버 모듈 구현

**📖 참조 문서**: [04-api-server.md](./04-api-server.md), [10-request-reply-mechanism.md](./10-request-reply-mechanism.md)

**🎯 목표**: API 서버 모듈 구현 및 Bootstrap 제공

#### 작업 목록

| # | 작업 | 파일 경로 | 상세 |
|---|------|----------|------|
| 4.1 | IApiSender 인터페이스 | `Abstractions/Api/IApiSender.cs` | CreateStage, GetOrCreateStage |
| 4.2 | IApiController 인터페이스 | `Abstractions/Api/IApiController.cs` | Handles(IHandlerRegister) |
| 4.3 | IHandlerRegister 인터페이스 | `Abstractions/Api/IHandlerRegister.cs` | Add(msgId, handler) |
| 4.4 | ApiDispatcher 구현 | `Core/Api/ApiDispatcher.cs` | Stateless 요청 처리, 핸들러 디스패치 |
| 4.5 | ApiSender 구현 | `Core/Api/ApiSender.cs` | IApiSender 구현 (XSender 직접 상속) |
| 4.6 | HandlerRegister 구현 | `Core/Api/Reflection/HandlerRegister.cs` | MsgId → Handler 매핑 |
| 4.7 | ApiReflection 구현 | `Core/Api/Reflection/ApiReflection.cs` | DI 기반 핸들러 자동 등록 |
| 4.8 | StageResult 타입 정의 | `Abstractions/Shared/StageResult.cs` | Create/Join/GetOrCreate Result |
| 4.9 | ApiServerBootstrap 구현 | `Core/Api/ApiServerBootstrap.cs` | 빌더 패턴, ASP.NET Core 통합 |
| 4.10 | 통합 테스트 작성 | `Tests/Api/` | HTTP API → NetMQ → Play 서버 |

#### 핵심 구현 상세

**IApiSender 인터페이스**:
```csharp
public interface IApiSender : ISender
{
    Task<CreateStageResult> CreateStage(
        string playNid, string stageType, long stageId, IPacket packet);

    Task<GetOrCreateStageResult> GetOrCreateStage(
        string playNid, string stageType, long stageId,
        IPacket createPacket, IPacket joinPacket);
}
```

**ASP.NET Core 통합 예시**:
```csharp
var builder = WebApplication.CreateBuilder(args);
var apiServer = new ApiServerBootstrap()
    .Configure(options => { /* 설정 */ })
    .UseController<GameApiController>()
    .Build();

builder.Services.AddSingleton<IApiSender>(apiServer.ApiSender);
builder.Services.AddHostedService<ApiServerHostedService>();

var app = builder.Build();
app.MapPost("/api/rooms/create", async (CreateRoomRequest req, IApiSender sender) =>
{
    var result = await sender.CreateStage("1:1", "GameRoom", 0, req.ToPacket());
    return Results.Ok(result);
});
```

#### 완료 조건
- [ ] HTTP API → NetMQ → Play 서버 통합 테스트 통과
- [ ] CreateStage, GetOrCreateStage 동작 확인

---

### Phase 5: 클라이언트 Connector 구현

**📖 참조 문서**: [09-connector.md](./09-connector.md)

**🎯 목표**: Unity/. NET 클라이언트용 Connector 라이브러리

#### 작업 목록

| # | 작업 | 파일 경로 | 상세 |
|---|------|----------|------|
| 5.1 | IPayload/IPacket 정의 | `Connector/IPacket.cs` | 클라이언트용 패킷 인터페이스 |
| 5.2 | Packet 구현 | `Connector/Packet.cs` | ProtoPayload, BytePayload, EmptyPayload |
| 5.3 | Connector 클래스 구현 | `Connector/Connector.cs` | 메인 API (Send, Request, Authenticate) |
| 5.4 | ConnectorConfig 정의 | `Connector/ConnectorConfig.cs` | Host, Port, 타임아웃 설정 |
| 5.5 | PacketEncoder 수정 | `Connector/Protocol/PacketEncoder.cs` | ServiceId 제거, IPacket 지원 |
| 5.6 | PacketDecoder 수정 | `Connector/Protocol/PacketDecoder.cs` | ServiceId 파싱 제거 |
| 5.7 | RequestTracker 수정 | `Connector/RequestTracker.cs` | IPacket 인터페이스 지원 |
| 5.8 | AsyncManager 구현 | `Connector/AsyncManager.cs` | Unity 메인 스레드 처리 |
| 5.9 | 통합 테스트 작성 | `Tests/Connector/` | TCP/WebSocket 연결, Request-Response |

#### 핵심 구현 상세

**패킷 구조 (ServiceId 제거)**:
```
클라이언트 → 서버:
Length(4) + MsgIdLen(1) + MsgId(N) + MsgSeq(2) + StageId(8) + Payload(N)

서버 → 클라이언트:
Length(4) + MsgIdLen(1) + MsgId(N) + MsgSeq(2) + StageId(8) +
ErrorCode(2) + OriginalSize(4) + Payload(N)
```

**Connector API**:
```csharp
public class Connector
{
    // 이벤트
    Action<bool> OnConnect;
    Action OnDisconnect;
    Action<long, IPacket> OnReceive;
    Action<long, ushort, IPacket> OnError;

    // 연결 관리
    void Connect();
    void Disconnect();
    bool IsConnected();
    bool IsAuthenticated();

    // 인증
    void SetAuthenticateMessageId(string msgId);
    void Authenticate(IPacket request, Action<IPacket> callback);
    Task<IPacket> AuthenticateAsync(IPacket request);

    // 메시지 전송
    void Send(long stageId, IPacket packet);
    void Request(long stageId, IPacket request, Action<IPacket> callback);
    Task<IPacket> RequestAsync(long stageId, IPacket request);

    // Unity 통합
    void MainThreadAction();  // Update()에서 호출
}
```

**에러 코드**:
```csharp
public enum ConnectorErrorCode
{
    DISCONNECTED = 60201,      // 연결 끊김 상태에서 요청
    REQUEST_TIMEOUT = 60202,   // 요청 타임아웃
    UNAUTHENTICATED = 60203    // 미인증 상태 요청
}
```

#### 완료 조건
- [ ] TCP/WebSocket 연결 테스트 통과
- [ ] Request-Response 패턴 검증
- [ ] Unity 메인 스레드 처리 확인

---

### Phase 6: 통합 및 검증

**📖 참조 문서**: [01-architecture-v2.md](./01-architecture-v2.md), [00-implementation-guide.md](./00-implementation-guide.md)

**🎯 목표**: 전체 시스템 통합 및 성능 검증

#### 작업 목록

| # | 작업 | 파일 경로 | 상세 |
|---|------|----------|------|
| 6.1 | Session 서버 코드 제거 | `Core/Session/` | 전체 삭제 |
| 6.2 | HTTP API 코드 제거 | `Infrastructure/Http/` | RoomController 등 삭제 |
| 6.3 | E2E 테스트 작성 | `Tests/E2E/` | 전체 플로우 검증 |
| 6.4 | 성능 벤치마크 | `Benchmarks/` | 처리량, 지연시간, 동시접속 |
| 6.5 | 문서화 업데이트 | `doc/` | API 문서, 사용 가이드 |
| 6.6 | 샘플 프로젝트 작성 | `Samples/` | Play 서버, API 서버 예제 |

#### E2E 테스트 시나리오

**시나리오 1: Stage 생성 및 입장**
```
1. API Server: CreateStage 요청
2. Play Server: Stage.OnCreate() → OnPostCreate()
3. Client: TCP 연결
4. Client: 인증 패킷 전송
5. Play Server: Actor.OnAuthenticate() → OnPostAuthenticate()
6. Play Server: Stage.OnJoinStage() → OnPostJoinStage()
7. Client: 실시간 통신 시작
```

**시나리오 2: 서버 간 통신**
```
1. Play Server A: SendToStage(Play Server B, stageId, packet)
2. Play Server B: Stage.OnDispatch(IPacket) 처리
3. Play Server B: Reply(response)
4. Play Server A: 응답 수신
```

#### 성능 목표

| 지표 | 목표 | 측정 방법 |
|------|------|----------|
| 동시 접속 | 10,000 CCU | 부하 테스트 |
| 메시지 처리량 | 100,000 msg/sec | 벤치마크 |
| 응답 지연 P95 | < 100ms | 성능 모니터링 |
| 메모리 사용량 | < 2GB @ 10K CCU | 리소스 모니터링 |

#### 완료 조건
- [ ] 전체 E2E 테스트 통과
- [ ] 성능 목표 달성
- [ ] 문서화 완료
- [ ] 샘플 프로젝트 동작 확인

---

## 5. 삭제 대상 목록

### 5.1 완전 삭제

| 경로 | 설명 |
|------|------|
| `Core/Session/` | Session 서버 전체 |
| `Abstractions/Session/ISessionActor.cs` | Session Actor 인터페이스 |
| `Infrastructure/Http/RoomController.cs` | REST API 컨트롤러 |
| `Infrastructure/Http/RoomTokenManager.cs` | 토큰 관리자 |

### 5.2 수정 후 유지

| 경로 | 변경 사항 |
|------|----------|
| `Abstractions/Play/IActor.cs` | OnAuthenticate, OnPostAuthenticate 추가 |
| `Abstractions/Play/IStage.cs` | OnJoinStage 시그니처 변경, OnDestory 추가 |
| `Abstractions/Play/IActorSender.cs` | AccountId (long→string), LeaveStage 추가 |

---

## 6. 참조 시스템 활용 전략

**참조 경로**: `D:\project\kairos\playhouse\playhouse-net\PlayHouse`

### 6.1 그대로 복사 (95% 재사용)

```
Runtime/PlaySocket/*.cs          → 전체 복사 (NetMQ 소켓)
Runtime/Message/*.cs             → 전체 복사 (메시지 구조)
Runtime/XClientCommunicator.cs   → 전체 복사 (송신)
Runtime/XServerCommunicator.cs   → 전체 복사 (수신)
Runtime/XServerInfoCenter.cs     → 전체 복사 (서버 디스커버리)
Runtime/MessageLoop.cs           → 전체 복사 (송수신 스레드)
```

### 6.2 수정 후 사용

```
Runtime/Communicator.cs          → SystemDispatcher, IService 교체
Runtime/RoutePacket.cs           → 불필요 메서드 제거
XSender 계열                      → ISender 인터페이스에 맞춰 조정
```

### 6.3 참조만 (새로 구현)

```
Stage/Actor 생명주기 관리 패턴
Lock-Free 이벤트 루프 (CAS 기반)
AsyncBlock 패턴
IApiSender, IApiController
```

---

## 7. 체크리스트 요약

### Phase 1: NetMQ 통신 계층 ✅ COMPLETED
- [x] 1.1 IPlaySocket 인터페이스
- [x] 1.2 NetMQPlaySocket 구현
- [x] 1.3 SocketConfig 정의 (PlaySocketConfig)
- [x] 1.4 Payload 클래스 (RuntimePayload)
- [x] 1.5 RoutePacket 구현 (RuntimeRoutePacket)
- [x] 1.6 XServerCommunicator
- [x] 1.7 XClientCommunicator
- [x] 1.8 MessageLoop (PlayCommunicator)
- [x] 1.9 ServerConfig 정의
- [x] 1.10 Protobuf 메시지 정의 (route_header.proto)
- [x] 1.11 단위 테스트 (25개 테스트 통과)

### Phase 2: 핵심 인터페이스
- [ ] 2.1 IPayload 인터페이스
- [ ] 2.2 IPacket 인터페이스
- [ ] 2.3 CPacket 구현
- [ ] 2.4 ISender 인터페이스
- [ ] 2.5 RequestCache
- [ ] 2.6 ReplyObject
- [ ] 2.7 XSender
- [ ] 2.8 BaseErrorCode 정의
- [ ] 2.9 단위 테스트

### Phase 3: Play 서버
- [ ] 3.1 IActor 확장
- [ ] 3.2 IActorSender
- [ ] 3.3 XActorSender
- [ ] 3.4 IStage 확장
- [ ] 3.5 IStageSender
- [ ] 3.6 XStageSender
- [ ] 3.7 BaseStage
- [ ] 3.8 BaseActor
- [ ] 3.9 PlayDispatcher
- [ ] 3.10 BaseStageCmdHandler
- [ ] 3.11 TimerManager
- [ ] 3.12 PlayProducer
- [ ] 3.13 PlayServerBootstrap
- [ ] 3.14 TcpSessionHandler
- [ ] 3.15 WebSocketHandler
- [ ] 3.16 E2E 테스트

### Phase 4: API 서버
- [ ] 4.1 IApiSender
- [ ] 4.2 IApiController
- [ ] 4.3 IHandlerRegister
- [ ] 4.4 ApiDispatcher
- [ ] 4.5 ApiSender (XSender 직접 상속)
- [ ] 4.6 HandlerRegister
- [ ] 4.7 ApiReflection
- [ ] 4.8 StageResult 타입
- [ ] 4.9 ApiServerBootstrap
- [ ] 4.10 통합 테스트

### Phase 5: Connector
- [ ] 5.1 IPayload/IPacket
- [ ] 5.2 Packet 구현
- [ ] 5.3 Connector 클래스
- [ ] 5.4 ConnectorConfig
- [ ] 5.5 PacketEncoder
- [ ] 5.6 PacketDecoder
- [ ] 5.7 RequestTracker
- [ ] 5.8 AsyncManager
- [ ] 5.9 통합 테스트

### Phase 6: 통합
- [ ] 6.1 Session 코드 제거
- [ ] 6.2 HTTP API 제거
- [ ] 6.3 E2E 테스트
- [ ] 6.4 성능 벤치마크
- [ ] 6.5 문서화
- [ ] 6.6 샘플 프로젝트

---

## 8. 용어 정의

| 용어 | 설명 |
|------|------|
| **NID** | Node ID, `{ServiceId}:{ServerId}` 형식의 서버 식별자 |
| **Stage** | 게임 룸, 로비 등 Actor들이 모인 논리적 단위 |
| **Actor** | Stage 내에서 활동하는 개별 참여자 (플레이어) |
| **MsgSeq** | Request-Reply 매칭을 위한 시퀀스 번호 (1~65535) |
| **RoutePacket** | 서버 간 통신용 내부 패킷 (RouteHeader + Payload) |
| **CAS** | Compare-And-Set, Lock-free 동시성 제어 기법 |
| **AsyncBlock** | Event Loop 외부에서 I/O 처리 후 결과를 Event Loop로 반환하는 패턴 |

---

> **다음 단계**: Phase 1부터 순차적으로 구현을 시작하세요.
> 각 Phase 완료 후 해당 체크리스트를 업데이트하고, 다음 Phase로 진행하세요.
