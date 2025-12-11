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
| **Phase 6** | - | - | E2E 테스트 (5개 시나리오) |
| **Phase 7** | [01-architecture-v2.md](./01-architecture-v2.md) | [00-implementation-guide.md](./00-implementation-guide.md) | 통합 및 정리 |

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
                    │       Phase 6: E2E 테스트           │
                    │   5개 시나리오 통합 검증            │
                    └──────────────────┬──────────────────┘
                                       │
                    ┌──────────────────▼──────────────────┐
                    │       Phase 7: 통합 및 정리         │
                    │   성능 벤치마크, 문서화, 샘플       │
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
| Phase 7 | Phase 6 완료 | ❌ 순차 |

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

- **현재 Phase**: 6 (E2E 테스트 - 종합 시스템 검증)
- **진행 방식**: 단일 에이전트 순차 진행
- **최근 완료**: Phase 5 - Connector (2025-12-11)
- **완료된 Phase**:
  - Phase 1: NetMQ 통신 계층 (기본 구조 완료)
  - Phase 2: 핵심 인터페이스 ✅
  - Phase 3: Play 서버 ✅ (일부 미구현)
  - Phase 4: API 서버 ✅ (일부 미구현)
  - Phase 5: Connector ✅ (일부 미구현)
- **남은 작업**:
  - Phase 1: XServerInfoCenter, ServerAddressResolver, Communicator
  - Phase 3: BaseStageCmdHandler, TcpSessionHandler, SessionManager
  - Phase 4: SystemDispatcher, ISystemController
  - Phase 5: PacketEncoder/Decoder
  - Phase 6: E2E 종합 테스트 (14개 카테고리, 60+ 테스트 케이스)
  - Phase 7: 통합 및 정리, 성능 벤치마크

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
| 1.11 | XServerInfoCenter 구현 | `Runtime/XServerInfoCenter.cs` | 서버 정보 캐시 및 관리 |
| 1.12 | ServerAddressResolver 구현 | `Runtime/ServerAddressResolver.cs` | 서버 디스커버리 (주기적 갱신) |
| 1.13 | CommunicatorOption 구현 | `Runtime/CommunicatorOption.cs` | Builder 패턴 설정 클래스 |
| 1.14 | PooledByteBuffer 구현 | `Infrastructure/Buffers/PooledByteBuffer.cs` | 버퍼 풀링, Zero-Copy 지원 |
| 1.15 | AtomicShort 구현 | `Infrastructure/Utils/AtomicShort.cs` | MsgSeq 생성기 (1~65535 순환) |
| 1.16 | Communicator 구현 | `Runtime/Communicator.cs` | 메시지 디스패치 오케스트레이터 |
| 1.17 | 단위 테스트 작성 | `Tests/Runtime/` | NetMQ 메시지 송수신 검증 |

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

#### 추가 구현 상세

**XServerInfoCenter (서버 정보 관리)**:
```csharp
internal class XServerInfoCenter
{
    private readonly ConcurrentDictionary<string, XServerInfo> _servers = new();

    // 서버 목록 갱신, 상태 변경된 서버 반환
    public List<XServerInfo> Update(List<XServerInfo> serverList);

    // 서비스별 서버 조회 (로드밸런싱용)
    public XServerInfo? GetServerBy(ushort serviceId);
    public IList<XServerInfo> GetServerListBy(ushort serviceId);
}
```

**ServerAddressResolver (서버 디스커버리)**:
```csharp
// 주기적으로 (3초) ISystemController.UpdateServerInfoAsync() 호출
// 새 서버 발견 시 XClientCommunicator.Connect()
// DISABLE 상태 서버는 Disconnect()
```

**CommunicatorOption (Builder 패턴)**:
```csharp
public class CommunicatorOption
{
    public string Nid { get; }
    public string BindEndpoint { get; }
    public ushort ServiceId { get; }
    public int ServerId { get; }
    public IServiceProvider ServiceProvider { get; }

    public class Builder { /* Fluent API */ }
}
```

#### 완료 조건
- [ ] NetMQ 메시지 송수신 테스트 통과
- [ ] Router-Router 패턴 양방향 통신 검증
- [ ] NID 기반 라우팅 동작 확인
- [ ] ServerAddressResolver를 통한 자동 연결 테스트
- [ ] Communicator 메시지 디스패치 테스트

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
| 2.4 | Header 클래스 구현 | `Runtime/Message/Header.cs` | ServiceId, MsgId, MsgSeq, ErrorCode, StageId |
| 2.5 | RouteHeader 확장 | `Runtime/Message/RouteHeader.cs` | From, IsReply, IsBackend 등 추가 |
| 2.6 | ISender 인터페이스 | `Abstractions/ISender.cs` | SendToApi, RequestToStage, Reply |
| 2.7 | ReplyCallback 델리게이트 | `Abstractions/ReplyCallback.cs` | `delegate void ReplyCallback(ushort errorCode, IPacket reply)` |
| 2.8 | RequestCache 구현 | `Runtime/RequestCache.cs` | MsgSeq 관리, 30초 타임아웃, 타임아웃 스레드 |
| 2.9 | ReplyObject 구현 | `Runtime/ReplyObject.cs` | Callback + TaskCompletionSource 동시 지원 |
| 2.10 | XSender 기본 구현 | `Core/Shared/XSender.cs` | ISender 구현, CurrentHeader 관리 |
| 2.11 | BaseErrorCode 정의 | `Abstractions/BaseErrorCode.cs` | 시스템 에러 코드 enum |
| 2.12 | 단위 테스트 작성 | `Tests/Core/` | Request-Reply 패턴 검증 |

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

**ReplyObject 구현 (콜백 + async/await 동시 지원)**:
```csharp
internal class ReplyObject
{
    private readonly ReplyCallback? _callback;
    private readonly TaskCompletionSource<RoutePacket>? _tcs;
    private readonly DateTime _requestTime = DateTime.UtcNow;

    public ReplyObject(
        ReplyCallback? callback = null,
        TaskCompletionSource<RoutePacket>? taskCompletionSource = null)
    {
        _callback = callback;
        _tcs = taskCompletionSource;
    }

    public void OnReceive(RoutePacket routePacket)
    {
        // 콜백 방식
        _callback?.Invoke(routePacket.ErrorCode, CPacket.Of(routePacket));

        // async/await 방식
        if (routePacket.ErrorCode == 0)
            _tcs?.TrySetResult(routePacket);
        else
            Throw(routePacket.ErrorCode);
    }

    public void Throw(ushort errorCode)
    {
        _tcs?.TrySetException(new PlayHouseException(errorCode));
    }

    public bool IsExpired(int timeoutMs)
        => (DateTime.UtcNow - _requestTime).TotalMilliseconds > timeoutMs;
}
```

**MsgSeq = 0 의 의미**:
- `MsgSeq = 0`: 단방향 Send (응답 불필요)
- `MsgSeq > 0`: Request-Reply 패턴 (응답 필요)

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
| 3.16 | ClientSession 구현 | `Core/Play/Session/ClientSession.cs` | 클라이언트 세션 상태 관리 |
| 3.17 | SessionManager 구현 | `Core/Play/Session/SessionManager.cs` | 세션 생명주기 관리 |
| 3.18 | PlayCommunicator 통합 | `Core/Play/PlayCommunicator.cs` | Communicator + 클라이언트 연결 통합 |
| 3.19 | E2E 테스트 작성 | `Tests/Play/` | 클라이언트 직접 연결 검증 |

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
| 4.4 | ApiHandler 델리게이트 | `Abstractions/Api/ApiHandler.cs` | `delegate Task ApiHandler(IPacket, IApiSender)` |
| 4.5 | StageResult 기본 클래스 | `Abstractions/Shared/StageResult.cs` | Result 포함 기본 클래스 |
| 4.6 | CreateStageResult 클래스 | `Abstractions/Shared/CreateStageResult.cs` | StageResult + CreateStageRes |
| 4.7 | GetOrCreateStageResult 클래스 | `Abstractions/Shared/GetOrCreateStageResult.cs` | StageResult + IsCreated + Res |
| 4.8 | ApiDispatcher 구현 | `Core/Api/ApiDispatcher.cs` | Stateless 요청 처리, 핸들러 디스패치 |
| 4.9 | ApiSender 구현 | `Core/Api/ApiSender.cs` | IApiSender 구현 (XSender 직접 상속) |
| 4.10 | HandlerRegister 구현 | `Core/Api/Reflection/HandlerRegister.cs` | MsgId → Handler 매핑 |
| 4.11 | ApiReflection 구현 | `Core/Api/Reflection/ApiReflection.cs` | DI 기반 핸들러 자동 등록 |
| 4.12 | SystemDispatcher 구현 | `Core/Shared/SystemDispatcher.cs` | 시스템 메시지 (ServerInfo 등) 처리 |
| 4.13 | ISystemController 인터페이스 | `Abstractions/Shared/ISystemController.cs` | 서버 디스커버리 (컨텐츠 구현) |
| 4.14 | ISystemHandlerRegister 인터페이스 | `Abstractions/Shared/ISystemHandlerRegister.cs` | 시스템 핸들러 등록 |
| 4.15 | ApiServerBootstrap 구현 | `Core/Api/ApiServerBootstrap.cs` | 빌더 패턴, ASP.NET Core 통합 |
| 4.16 | 통합 테스트 작성 | `Tests/Api/` | HTTP API → NetMQ → Play 서버 |

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

#### ISystemController 구현 가이드 (컨텐츠 개발자용)

**컨텐츠 개발자가 구현해야 하는 인터페이스**:
```csharp
public interface ISystemController
{
    // 시스템 메시지 핸들러 등록 (선택적)
    void Handles(ISystemHandlerRegister handlerRegister);

    // 내 서버 정보 등록 → 전체 서버 목록 반환
    // ServerAddressResolver가 주기적(3초)으로 호출
    Task<IReadOnlyList<IServerInfo>> UpdateServerInfoAsync(IServerInfo serverInfo);
}
```

**구현 예시 (Redis)**:
```csharp
public class RedisSystemController : ISystemController
{
    public async Task<IReadOnlyList<IServerInfo>> UpdateServerInfoAsync(IServerInfo serverInfo)
    {
        // 1. 내 서버 정보 저장 (TTL 10초)
        await db.StringSetAsync($"server:{serverInfo.GetNid()}", serverData, _ttl);

        // 2. 전체 서버 목록 조회 후 반환
        return await GetAllServersAsync();
    }
}
```

**구현 예시 (메모리 기반 - 개발/테스트용)**:
```csharp
public class InMemorySystemController : ISystemController
{
    private static readonly ConcurrentDictionary<string, ServerInfoEntry> _servers = new();

    public Task<IReadOnlyList<IServerInfo>> UpdateServerInfoAsync(IServerInfo serverInfo)
    {
        _servers[serverInfo.GetNid()] = new(serverInfo, DateTimeOffset.UtcNow);
        CleanupExpired();
        return Task.FromResult(_servers.Values.Select(e => e.ServerInfo).ToList());
    }
}
```

#### 완료 조건
- [ ] HTTP API → NetMQ → Play 서버 통합 테스트 통과
- [ ] CreateStage, GetOrCreateStage 동작 확인
- [ ] ISystemController 구현체를 통한 서버 디스커버리 검증

---

### Phase 5: 클라이언트 Connector 구현

**📖 참조 문서**: [09-connector.md](./09-connector.md)

**🎯 목표**: Unity/. NET 클라이언트용 Connector 라이브러리

#### 작업 목록

| # | 작업 | 파일 경로 | 상세 |
|---|------|----------|------|
| 5.1 | IPayload/IPacket 정의 | `Connector/Protocol/IPacket.cs` | 클라이언트용 패킷 인터페이스 |
| 5.2 | Payload 구현 | `Connector/Protocol/Payload.cs` | ProtoPayload, BytePayload, EmptyPayload |
| 5.3 | Packet 구현 | `Connector/Protocol/Packet.cs` | IPacket 구현, Protobuf 지원 |
| 5.4 | Connector 클래스 구현 | `Connector/Connector.cs` | 메인 API (Send, Request, Authenticate) |
| 5.5 | ConnectorConfig 정의 | `Connector/ConnectorConfig.cs` | Host, Port, 타임아웃 설정 |
| 5.6 | ConnectorErrorCode 정의 | `Connector/ConnectorErrorCode.cs` | DISCONNECTED, TIMEOUT, UNAUTHENTICATED |
| 5.7 | PacketEncoder 수정 | `Connector/Protocol/PacketEncoder.cs` | ServiceId 제거, 새 패킷 포맷 |
| 5.8 | PacketDecoder 수정 | `Connector/Protocol/PacketDecoder.cs` | ServiceId 파싱 제거, LZ4 압축 해제 |
| 5.9 | RequestTracker 수정 | `Connector/Protocol/RequestTracker.cs` | IPacket 인터페이스 지원 |
| 5.10 | AsyncManager 구현 | `Connector/Internal/AsyncManager.cs` | Unity 메인 스레드 콜백 처리 |
| 5.11 | TcpConnection 유지 | `Connector/Connection/TcpConnection.cs` | TCP 연결 (기존 코드 유지) |
| 5.12 | WebSocketConnection 유지 | `Connector/Connection/WebSocketConnection.cs` | WebSocket 연결 (기존 코드 유지) |
| 5.13 | 통합 테스트 작성 | `Tests/Connector/` | TCP/WebSocket 연결, Request-Response |

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
public enum ConnectorErrorCode : ushort
{
    DISCONNECTED = 60201,      // 연결 끊김 상태에서 요청
    REQUEST_TIMEOUT = 60202,   // 요청 타임아웃
    UNAUTHENTICATED = 60203    // 미인증 상태 요청
}
```

**LZ4 압축 해제 (PacketDecoder)**:
```csharp
// 서버 → 클라이언트 패킷에서 OriginalSize > 0 이면 압축됨
if (originalSize > 0)
{
    bodyData = LZ4Pickler.Unpickle(compressedData);
}
```

**AsyncManager (Unity 메인 스레드 처리)**:
```csharp
public class AsyncManager
{
    private readonly ConcurrentQueue<Action> _mainThreadQueue = new();

    // 백그라운드 스레드에서 호출
    public void Post(Action action) => _mainThreadQueue.Enqueue(action);

    // Unity Update()에서 호출
    public void ProcessMainThread()
    {
        while (_mainThreadQueue.TryDequeue(out var action))
            action();
    }
}
```

**인증 메시지 등록 방식**:
```csharp
// 인증 메시지 이름 등록 (인증 전에는 이 메시지만 전송 가능)
connector.SetAuthenticateMessageId("MyAuthRequest");

// 인증 성공 후에만 다른 메시지 전송 가능
connector.Authenticate(authPacket, response => { ... });
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
| 6.3 | 레거시 인터페이스 제거 | `Abstractions/Session/` | ISessionActor 등 삭제 |
| 6.4 | E2E 테스트 작성 | `Tests/E2E/` | 전체 플로우 검증 |
| 6.5 | 성능 벤치마크 | `Benchmarks/` | 처리량, 지연시간, 동시접속 |
| 6.6 | ISystemController 샘플 | `Samples/SystemController/` | Redis, InMemory 구현 예제 |
| 6.7 | 문서화 업데이트 | `doc/` | API 문서, 사용 가이드 |
| 6.8 | 샘플 프로젝트 작성 | `Samples/` | Play 서버, API 서버 예제 |

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

**시나리오 3: 서버 디스커버리**
```
1. API Server 시작 → ISystemController.UpdateServerInfoAsync() 호출
2. Play Server 시작 → ISystemController.UpdateServerInfoAsync() 호출
3. API Server: 새 Play Server 발견 → 자동 Connect
4. Play Server: 새 API Server 발견 → 자동 Connect
5. Full-Mesh 연결 완성 확인
```

**시나리오 4: 클라이언트 재연결**
```
1. Client 연결 → 인증 → Stage 입장
2. 네트워크 끊김 시뮬레이션
3. Stage.OnConnectionChanged(actor, false) 호출 확인
4. Client 재연결 → 동일 AccountId로 인증
5. Stage.OnConnectionChanged(actor, true) 호출 확인
6. 기존 상태 유지 확인
```

**시나리오 5: Request-Reply 타임아웃**
```
1. API Server → Play Server: Request 전송
2. Play Server: Reply 없이 30초 대기
3. API Server: RequestTimeout 예외 발생 확인
4. RequestCache에서 해당 요청 정리 확인
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

### Phase 1: NetMQ 통신 계층 (기본 구조 완료)
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
- [ ] 1.11 XServerInfoCenter 구현 (미구현)
- [ ] 1.12 ServerAddressResolver 구현 (미구현)
- [ ] 1.13 CommunicatorOption/Builder 구현 (미구현)
- [ ] 1.14 PooledByteBuffer 구현 (미구현)
- [x] 1.15 AtomicBoolean 구현 (AtomicShort 대체)
- [ ] 1.16 Communicator 구현 (미구현)
- [x] 1.17 단위 테스트

### Phase 2: 핵심 인터페이스 ✅
- [x] 2.1 IPayload 인터페이스
- [x] 2.2 IPacket 인터페이스
- [x] 2.3 CPacket 구현
- [x] 2.4 Header 클래스 구현 (PacketHeader.cs)
- [x] 2.5 RouteHeader 확장 (Proto/RouteHeader)
- [x] 2.6 ISender 인터페이스
- [x] 2.7 ReplyCallback 델리게이트 (ReplyObject에 포함)
- [x] 2.8 RequestCache
- [x] 2.9 ReplyObject (콜백 + TCS 동시 지원)
- [x] 2.10 XSender
- [x] 2.11 BaseErrorCode 정의
- [x] 2.12 단위 테스트

### Phase 3: Play 서버 ✅
- [x] 3.1 IActor 확장
- [x] 3.2 IActorSender
- [x] 3.3 XActorSender
- [x] 3.4 IStage 확장
- [x] 3.5 IStageSender
- [x] 3.6 XStageSender
- [x] 3.7 BaseStage (Lock-free 이벤트 루프)
- [x] 3.8 BaseActor
- [x] 3.9 PlayDispatcher
- [ ] 3.10 BaseStageCmdHandler (미구현)
- [x] 3.11 TimerManager
- [x] 3.12 PlayProducer
- [x] 3.13 PlayServerBootstrap
- [ ] 3.14 TcpSessionHandler (미구현)
- [ ] 3.15 WebSocketHandler (미구현)
- [x] 3.16 ClientSession
- [ ] 3.17 SessionManager (미구현)
- [ ] 3.18 PlayCommunicator 통합 (미구현)
- [x] 3.19 E2E 테스트 (BootstrapServerE2ETests.cs)

### Phase 4: API 서버 ✅
- [x] 4.1 IApiSender
- [x] 4.2 IApiController
- [x] 4.3 IHandlerRegister (HandlerRegister에 포함)
- [x] 4.4 ApiHandler 델리게이트
- [x] 4.5 StageResult 기본 클래스
- [x] 4.6 CreateStageResult (StageResult에 포함)
- [x] 4.7 GetOrCreateStageResult (StageResult에 포함)
- [x] 4.8 ApiDispatcher
- [x] 4.9 ApiSender (XSender 직접 상속)
- [x] 4.10 HandlerRegister
- [x] 4.11 ApiReflection
- [ ] 4.12 SystemDispatcher (미구현)
- [ ] 4.13 ISystemController 인터페이스 (미구현)
- [ ] 4.14 ISystemHandlerRegister (미구현)
- [x] 4.15 ApiServerBootstrap
- [x] 4.16 단위 테스트 (ApiDispatcherTests, HandlerRegisterTests)

### Phase 5: Connector ✅
- [x] 5.1 IPayload/IPacket
- [x] 5.2 Payload 구현 (ProtoPayload, BytePayload, EmptyPayload)
- [x] 5.3 Packet 구현
- [x] 5.4 Connector 클래스
- [x] 5.5 ConnectorConfig
- [x] 5.6 ConnectorErrorCode
- [ ] 5.7 PacketEncoder (미구현 - ClientNetwork에 통합 예정)
- [ ] 5.8 PacketDecoder (미구현 - ClientNetwork에 통합 예정)
- [ ] 5.9 RequestTracker (미구현 - Connector 내부 구현)
- [x] 5.10 AsyncManager (Unity 메인 스레드)
- [x] 5.11 TcpConnection
- [x] 5.12 WebSocketConnection

### Phase 6: E2E 테스트 (종합 시스템 검증)

> **📖 테스트 작성 가이드**: [architecture-guide.md](../architecture-guide.md) 참조
> - 테스트는 **API 사용 가이드처럼** 읽혀야 함
> - Given-When-Then 구조, 명시적 셋업
> - 테스트 목록만 출력해도 **기능 명세서처럼** 읽혀야 함

---

#### ⚠️ E2E 테스트 핵심 원칙

| 원칙 | 설명 |
|------|------|
| **Fake/Mock 절대 금지** | E2E 테스트에서는 Fake, Mock, Stub 등을 **절대 사용하지 않음** |
| **실제 서버 사용** | PlayServer, ApiServer를 Bootstrap으로 실제 구동 |
| **실제 네트워크** | TCP/WebSocket 실제 연결 사용 (localhost) |
| **실제 메시지** | Proto 메시지로 정의된 실제 메시지 사용 |
| **사용자 관점** | 실제 사용자가 API를 사용하는 것처럼 테스트 작성 |

---

#### Proto 메시지 사용

> 모든 E2E 테스트 메시지는 Proto 파일로 정의하고, `IPacket.Parse<T>()` 확장 메서드로 파싱

**테스트용 Proto 정의** (`Tests/E2E/Protos/test_messages.proto`):
```protobuf
syntax = "proto3";
package PlayHouse.Tests.E2E;

// ============================================
// Client → Play Server (Connector 직접 통신)
// ============================================

// 인증 요청 (Connector → Play)
message AuthRequest {
    string account_id = 1;
    string token = 2;
}

// 인증 응답 (Play → Connector)
message AuthResponse {
    bool success = 1;
    string session_id = 2;
}

// 게임 액션 메시지
message GameActionRequest {
    string action = 1;
    bytes data = 2;
}

message GameActionResponse {
    bool success = 1;
    string result = 2;
}

// 채팅 메시지
message ChatMessage {
    string sender = 1;
    string content = 2;
    int64 timestamp = 3;
}

// ============================================
// Client → API Server (HTTP API 호출)
// ============================================

// 방 생성 API 요청 (HTTP POST /api/rooms/create)
message ApiCreateRoomRequest {
    string room_name = 1;
    int32 max_players = 2;
    string room_type = 3;  // stage type
}

// 방 생성 API 응답
message ApiCreateRoomResponse {
    int64 room_id = 1;
    bool success = 2;
    string play_server_nid = 3;
}

// 방 참가 API 요청 (HTTP POST /api/rooms/join)
message ApiJoinRoomRequest {
    int64 room_id = 1;
    string account_id = 2;
}

// 방 참가 API 응답
message ApiJoinRoomResponse {
    bool success = 1;
    string play_server_host = 2;
    int32 play_server_port = 3;
}

// 방 목록 조회 API 요청 (HTTP GET /api/rooms)
message ApiListRoomsRequest {
    int32 page = 1;
    int32 page_size = 2;
}

message ApiListRoomsResponse {
    repeated RoomInfo rooms = 1;
    int32 total_count = 2;
}

message RoomInfo {
    int64 room_id = 1;
    string room_name = 2;
    int32 current_players = 3;
    int32 max_players = 4;
}

// ============================================
// API Server → Play Server (내부 통신)
// ============================================

// Stage 생성 패킷 (API → Play, IApiSender.CreateStage)
message CreateStagePacket {
    string room_name = 1;
    int32 max_players = 2;
    map<string, string> metadata = 3;
}

// Stage 생성 응답 (Play → API)
message CreateStageResponsePacket {
    bool success = 1;
    int64 stage_id = 2;
}

// Stage 조회/요청 패킷 (API → Play, IApiSender.RequestToStage)
message StageQueryPacket {
    string query_type = 1;
    bytes query_data = 2;
}

message StageQueryResponsePacket {
    bool success = 1;
    bytes response_data = 2;
}

// ============================================
// Play Server → API Server (내부 통신)
// ============================================

// 이벤트 알림 (Play → API, ISender.SendToApi)
message GameEventNotification {
    int64 stage_id = 1;
    string event_type = 2;
    bytes event_data = 3;
}

// 데이터 조회 요청 (Play → API, ISender.RequestToApi)
message DataQueryRequest {
    string account_id = 1;
    string data_type = 2;
}

message DataQueryResponse {
    bool found = 1;
    bytes data = 2;
}

// ============================================
// Server ↔ Server (Play-Play, API-API)
// ============================================

// 서버 간 메시지
message ServerToServerMessage {
    string source_nid = 1;
    string message_type = 2;
    bytes payload = 3;
}

message ServerToServerResponse {
    bool success = 1;
    bytes response_payload = 2;
}
```

**Proto 메시지 파싱 사용법**:
```csharp
// IPacket.Parse<T>() 확장 메서드 사용
// 위치: PlayHouse.Abstractions.PacketExtensions

// Stage에서 메시지 수신 시
public Task OnDispatch(IPacket packet, IStageSender sender)
{
    // Parse<T>() 확장 메서드로 Proto 메시지 파싱
    var message = packet.Parse<GameMessage>();

    // 응답 전송
    var response = new GameResponse { Success = true };
    sender.Reply(CPacket.Of(response));
    return Task.CompletedTask;
}

// Connector에서 응답 수신 시
var response = await connector.RequestAsync(stageId, CPacket.Of(request));
var result = response.Parse<CreateRoomResponse>();
Assert.True(result.Success);
```

**TryParse 사용법** (안전한 파싱):
```csharp
if (packet.TryParse<GameMessage>(out var message))
{
    // 파싱 성공
    ProcessMessage(message);
}
else
{
    // 파싱 실패 처리
    sender.Reply(BaseErrorCode.InvalidMessage);
}
```

---

#### 6.1 테스트 환경 구성 (서버 인프라)

> ⚠️ **선행 조건**: API ↔ Play 서버 연결을 위해 `ISystemController` 구현 필수

| 순서 | 항목 | 구현 내용 |
|-----|------|----------|
| 6.1.1 | **InMemorySystemController** | `ISystemController` 테스트용 구현 (서버 주소 수집/반환) |
| 6.1.2 | 서버 디스커버리 검증 | `UpdateServerInfoAsync()` → 서버 목록 반환 확인 |
| 6.1.3 | API ↔ Play 연결 확인 | NetMQ Router-Router 연결 성공 확인 |
| 6.1.4 | E2E 테스트 픽스처 | PlayServer + ApiServer Bootstrap 구동 |
| 6.1.5 | 테스트용 Stage | TestGameStage (IStage) 구현 |
| 6.1.6 | 테스트용 Actor | TestPlayerActor (IActor) 구현 |
| 6.1.7 | 테스트용 Proto | 테스트 메시지 정의 |
| 6.1.8 | 테스트용 ApiController | IApiController 구현 |

```csharp
// InMemorySystemController 예시
public class InMemorySystemController : ISystemController
{
    private static readonly ConcurrentDictionary<string, IServerInfo> _servers = new();

    public Task<IReadOnlyList<IServerInfo>> UpdateServerInfoAsync(IServerInfo serverInfo)
    {
        _servers[serverInfo.GetNid()] = serverInfo;
        return Task.FromResult<IReadOnlyList<IServerInfo>>(_servers.Values.ToList());
    }
}
```

---

#### 6.2 연결 및 인증 (Connection & Authentication)

| 테스트 | Connector 메소드 | 확인 사항 |
|--------|-----------------|----------|
| 6.2.1 TCP 연결 성공 | `Connect()`, `ConnectAsync()` | `IsConnected() = true` |
| 6.2.2 TCP 연결 실패 | `ConnectAsync()` | `OnConnect(false)` 콜백 |
| 6.2.3 WebSocket 연결 | `ConnectAsync()` | `IsConnected() = true` |
| 6.2.4 인증 성공 | `AuthenticateAsync(IPacket)` | `IsAuthenticated() = true` |
| 6.2.5 인증 실패 | `Authenticate(IPacket, callback)` | 연결 종료, `OnDisconnect` |
| 6.2.6 미인증 Send | `Send(IPacket)` | `OnError(ConnectorErrorCode.Unauthenticated)` |

---

#### 6.3 Connector 콜백 검증 (Callback Verification)

| 테스트 | 트리거 | 확인할 콜백 |
|--------|--------|------------|
| 6.3.1 연결 콜백 | `ConnectAsync()` | `OnConnect(bool success)` |
| 6.3.2 연결 끊김 콜백 | `Disconnect()` / 서버 종료 | `OnDisconnect()` |
| 6.3.3 메시지 수신 콜백 | 서버 Push 메시지 | `OnReceive(long stageId, IPacket packet)` |
| 6.3.4 에러 콜백 | 서버 에러 응답 | `OnError(long stageId, ushort errorCode, IPacket request)` |

---

#### 6.4 Client → Play 메시지 (Connector → Play Server)

| 테스트 | Connector 메소드 | Stage 콜백 | 확인 사항 |
|--------|-----------------|-----------|----------|
| 6.4.1 Send (단방향, stageId=0) | `Send(IPacket)` | `IStage.OnDispatch(IActor, IPacket)` | 메시지 도달 확인 |
| 6.4.2 Send (단방향, stageId>0) | `Send(long stageId, IPacket)` | `IStage.OnDispatch(IActor, IPacket)` | StageId 라우팅 |
| 6.4.3 Request async | `RequestAsync(IPacket)` | `IStage.OnDispatch` → `IStageSender.Reply(IPacket)` | 응답 수신 |
| 6.4.4 Request callback | `Request(IPacket, Action<IPacket>)` | `IStage.OnDispatch` → `IStageSender.Reply` | 콜백 호출 |
| 6.4.5 Request + stageId | `RequestAsync(long, IPacket)` | `OnDispatch` → `Reply` | StageId 포함 라우팅 |

---

#### 6.5 Play → Client 메시지 (Play Server → Connector)

| 테스트 | Play Server 메소드 | Connector 콜백 | 확인 사항 |
|--------|-------------------|---------------|----------|
| 6.5.1 Push 메시지 | `IActorSender.SendToClient(IPacket)` | `OnReceive(stageId, packet)` | 클라이언트 수신 |
| 6.5.2 Reply 응답 | `IStageSender.Reply(IPacket)` | `RequestAsync` 반환값 | 응답 매칭 |
| 6.5.3 Reply + ErrorCode | `IStageSender.Reply(ushort errorCode)` | `OnError` 콜백 | 에러코드 전파 |
| 6.5.4 Stage Push | `IStageSender.SendToClient(nid, sid, IPacket)` | `OnReceive` | 특정 클라이언트 |

---

#### 6.6 API → Play 메시지 (Client → API → Play 전체 흐름)

> ⚠️ **중요**: API → Play 메시지 테스트는 반드시 **Client → API → Play** 전체 흐름으로 테스트해야 합니다.
> API 서버는 Stateless이므로, 클라이언트의 HTTP 요청을 받아 Play 서버로 전달하는 구조입니다.

**테스트 흐름**:
```
┌──────────┐     HTTP      ┌──────────┐     NetMQ      ┌──────────┐
│  Client  │ ──────────→  │   API    │ ──────────→   │   Play   │
│ (HTTP)   │ ←────────── │  Server  │ ←────────────  │  Server  │
└──────────┘   Response   └──────────┘    Reply      └──────────┘
```

**테스트 코드 예시**:
```csharp
[Fact]
public async Task CreateRoom_ClientToApiToPlay_StageCreated()
{
    // Given: API Server + Play Server 구동 상태
    // HTTP 클라이언트로 API 서버 호출
    var httpClient = new HttpClient { BaseAddress = new Uri("http://localhost:5000") };

    // When: Client → API (HTTP POST)
    var request = new ApiCreateRoomRequest
    {
        RoomName = "TestRoom",
        MaxPlayers = 4,
        RoomType = "GameRoom"
    };
    var response = await httpClient.PostAsJsonAsync("/api/rooms/create", request);

    // Then: API → Play (내부 NetMQ 통신)으로 Stage 생성됨
    var result = await response.Content.ReadFromJsonAsync<ApiCreateRoomResponse>();
    Assert.True(result.Success);
    Assert.True(result.RoomId > 0);

    // Play Server의 IStage.OnCreate, OnPostCreate 콜백 호출 확인
}
```

**API Controller 구현 예시**:
```csharp
// IApiController 구현 - API Server에서 HTTP 요청을 받아 Play Server로 전달
public class RoomApiController : IApiController
{
    public void Handles(IHandlerRegister register)
    {
        register.Add<ApiCreateRoomRequest>(OnCreateRoom);
        register.Add<ApiJoinRoomRequest>(OnJoinRoom);
    }

    private async Task OnCreateRoom(IPacket packet, IApiSender sender)
    {
        var request = packet.Parse<ApiCreateRoomRequest>();

        // API → Play: CreateStage 호출
        var stagePacket = CPacket.Of(new CreateStagePacket
        {
            RoomName = request.RoomName,
            MaxPlayers = request.MaxPlayers
        });
        var result = await sender.CreateStage(playNid, request.RoomType, 0, stagePacket);

        // Client에게 응답
        sender.Reply(CPacket.Of(new ApiCreateRoomResponse
        {
            Success = result.ErrorCode == 0,
            RoomId = result.StageId
        }));
    }
}
```

| 테스트 | 클라이언트 호출 | IApiSender 메소드 | Stage 콜백 | 확인 사항 |
|--------|---------------|------------------|-----------|----------|
| 6.6.1 CreateStage | `POST /api/rooms/create` | `CreateStage(playNid, stageType, stageId, IPacket)` | `IStage.OnCreate` → `OnPostCreate` | `CreateStageResult` |
| 6.6.2 GetOrCreateStage | `POST /api/rooms/get-or-create` | `GetOrCreateStage(...)` | `OnCreate` (신규) or 없음 (기존) | `GetOrCreateStageResult.IsCreated` |
| 6.6.3 JoinStage | `POST /api/rooms/join` | `JoinStage(playNid, stageId, IPacket)` | `IStage.OnJoinStage` → `OnPostJoinStage` | `JoinStageResult` |
| 6.6.4 CreateJoinStage | `POST /api/rooms/create-join` | `CreateJoinStage(...)` | `OnCreate` → `OnJoinStage` | `CreateJoinStageResult` |
| 6.6.5 SendToStage | `POST /api/rooms/{id}/send` | `SendToStage(playNid, stageId, IPacket)` | `IStage.OnDispatch(IPacket)` | 단방향 전달 |
| 6.6.6 RequestToStage async | `POST /api/rooms/{id}/request` | `RequestToStage(playNid, stageId, IPacket)` | `OnDispatch` → `Reply` | 응답 반환 |
| 6.6.7 RequestToStage callback | (내부 테스트) | `RequestToStage(..., ReplyCallback)` | `OnDispatch` → `Reply` | 콜백 호출 |

---

#### 6.7 Play → API 메시지 (Client 트리거 → Play → API)

> ⚠️ **중요**: Play → API 통신도 **Client에서 트리거**되어야 합니다.
> 클라이언트 액션 → Play Server 처리 → API Server로 요청/알림 흐름

**테스트 흐름**:
```
┌──────────┐   Connector    ┌──────────┐     NetMQ      ┌──────────┐
│  Client  │ ──────────→   │   Play   │ ──────────→   │   API    │
│(Connector)│ ←────────── │  Server  │ ←────────────  │  Server  │
└──────────┘    Push       └──────────┘    Reply      └──────────┘
```

**시나리오 예시**:
- 클라이언트가 게임 액션 전송 → Play Server가 결과를 API Server에 기록 요청
- 클라이언트가 아이템 사용 → Play Server가 API Server에 인벤토리 업데이트 요청

**테스트 코드 예시**:
```csharp
[Fact]
public async Task GameAction_ClientTriggersPlayToApiCommunication()
{
    // Given: Client가 Play Server에 연결/인증된 상태
    var connector = CreateConnector();
    await connector.ConnectAsync();
    await connector.AuthenticateAsync(CPacket.Of(new AuthRequest { AccountId = "user1" }));

    // When: Client → Play (게임 액션 전송)
    var request = new GameActionRequest { Action = "use_item", Data = itemData };
    var response = await connector.RequestAsync(stageId, CPacket.Of(request));

    // Then: Play → API (내부 통신) 발생 확인
    // Play Server의 OnDispatch에서 ISender.RequestToApi 호출됨
    var result = response.Parse<GameActionResponse>();
    Assert.True(result.Success);

    // API Server의 핸들러가 호출되었는지 확인 (로그/콜백 검증)
}
```

**Play Server Stage 구현 (Play → API 호출)**:
```csharp
public class GameStage : IStage
{
    public async Task OnDispatch(IPacket packet, IStageSender sender)
    {
        var action = packet.Parse<GameActionRequest>();

        if (action.Action == "use_item")
        {
            // Play → API: 데이터 조회/저장 요청
            var queryPacket = CPacket.Of(new DataQueryRequest
            {
                AccountId = sender.AccountId,
                DataType = "inventory"
            });
            var apiResponse = await sender.RequestToApi(apiNid, queryPacket);
            var data = apiResponse.Parse<DataQueryResponse>();

            // 클라이언트에게 응답
            sender.Reply(CPacket.Of(new GameActionResponse { Success = data.Found }));
        }
    }
}
```

| 테스트 | Client 트리거 | Play 내부 호출 | API 콜백 | 확인 사항 |
|--------|-------------|---------------|---------|----------|
| 6.7.1 SendToApi | `Connector.Send(GameAction)` | `ISender.SendToApi(apiNid, IPacket)` | `IApiController` 핸들러 | 단방향 전달 |
| 6.7.2 RequestToApi async | `Connector.RequestAsync(GameAction)` | `ISender.RequestToApi(apiNid, IPacket)` | 핸들러 → `Reply` | 응답 반환 |
| 6.7.3 RequestToApi callback | `Connector.Request(GameAction, cb)` | `ISender.RequestToApi(..., ReplyCallback)` | 핸들러 → `Reply` | 콜백 호출 |

---

#### 6.8 API ↔ API 메시지 (Client → API #1 → API #2)

> ⚠️ **중요**: API ↔ API 통신도 **Client HTTP 요청에서 트리거**됩니다.
> Client → API #1 (HTTP) → API #2 (NetMQ) 흐름

**테스트 흐름**:
```
┌──────────┐     HTTP      ┌──────────┐     NetMQ      ┌──────────┐
│  Client  │ ──────────→  │  API #1  │ ──────────→   │  API #2  │
│ (HTTP)   │ ←────────── │  Server  │ ←────────────  │  Server  │
└──────────┘   Response   └──────────┘    Reply      └──────────┘
```

**시나리오 예시**:
- 클라이언트가 유저 프로필 조회 → API #1이 API #2 (유저 서비스)에 데이터 요청
- 클라이언트가 결제 요청 → API #1이 API #2 (결제 서비스)에 처리 요청

**테스트 코드 예시**:
```csharp
[Fact]
public async Task GetUserProfile_ClientToApi1ToApi2()
{
    // Given: API Server #1, #2 구동 상태
    var httpClient = new HttpClient { BaseAddress = new Uri("http://localhost:5000") };

    // When: Client → API #1 (HTTP)
    var response = await httpClient.GetAsync("/api/users/profile?userId=123");

    // Then: API #1 → API #2 (내부 NetMQ 통신) 발생
    var result = await response.Content.ReadFromJsonAsync<UserProfileResponse>();
    Assert.NotNull(result);
    Assert.Equal("123", result.UserId);
}
```

| 테스트 | Client 트리거 | API #1 내부 호출 | API #2 수신 | 확인 사항 |
|--------|-------------|-----------------|------------|----------|
| 6.8.1 SendToApi | `GET /api/events/broadcast` | `IApiSender.SendToApi(api2Nid, IPacket)` | `IApiController` 핸들러 | 단방향 전달 |
| 6.8.2 RequestToApi async | `GET /api/users/profile` | `IApiSender.RequestToApi(api2Nid, IPacket)` | 핸들러 → `Reply` | 응답 반환 |
| 6.8.3 RequestToApi callback | `POST /api/payments/process` | `IApiSender.RequestToApi(..., ReplyCallback)` | 핸들러 → `Reply` | 콜백 호출 |

---

#### 6.9 Play ↔ Play 메시지 (Client → Play #1 → Play #2)

> ⚠️ **중요**: Play ↔ Play 통신도 **Client Connector에서 트리거**됩니다.
> Client → Play #1 (TCP/WebSocket) → Play #2 (NetMQ) 흐름

**테스트 흐름**:
```
┌──────────┐   Connector    ┌──────────┐     NetMQ      ┌──────────┐
│  Client  │ ──────────→   │  Play #1 │ ──────────→   │  Play #2 │
│(Connector)│ ←────────── │  Server  │ ←────────────  │  Server  │
└──────────┘    Push       └──────────┘    Reply      └──────────┘
```

**시나리오 예시**:
- 클라이언트가 다른 서버의 방에 메시지 전송 (크로스 서버 채팅)
- 클라이언트가 다른 서버의 Stage 정보 조회 (매치메이킹)
- 클라이언트가 다른 서버의 Actor에게 아이템 전송

**테스트 코드 예시**:
```csharp
[Fact]
public async Task CrossServerChat_ClientToPlay1ToPlay2()
{
    // Given: Client가 Play #1 Server에 연결된 상태
    //        Play #2 Server에 다른 Stage가 존재
    var connector = CreateConnector(play1Host, play1Port);
    await connector.ConnectAsync();
    await connector.AuthenticateAsync(CPacket.Of(new AuthRequest { AccountId = "user1" }));

    // When: Client → Play #1 (크로스 서버 채팅 요청)
    var chatRequest = new ServerToServerMessage
    {
        SourceNid = play1Nid,
        MessageType = "chat",
        Payload = ByteString.CopyFrom(chatData)
    };
    var response = await connector.RequestAsync(stageId, CPacket.Of(chatRequest));

    // Then: Play #1 → Play #2 (내부 NetMQ 통신) 발생
    var result = response.Parse<ServerToServerResponse>();
    Assert.True(result.Success);
}
```

**Play Server Stage 구현 (Play → Play 호출)**:
```csharp
public class LobbyStage : IStage
{
    public async Task OnDispatch(IPacket packet, IStageSender sender)
    {
        var msg = packet.Parse<ServerToServerMessage>();

        if (msg.MessageType == "chat")
        {
            // Play #1 → Play #2: 다른 서버의 Stage로 메시지 전송
            var targetStageId = GetTargetStageId(msg);
            var crossPacket = CPacket.Of(new ChatMessage { Content = "Hello" });
            var response = await sender.RequestToStage(play2Nid, targetStageId, crossPacket);

            // 클라이언트에게 응답
            sender.Reply(CPacket.Of(new ServerToServerResponse { Success = true }));
        }
    }
}
```

| 테스트 | Client 트리거 | Play #1 내부 호출 | Play #2 수신 | 확인 사항 |
|--------|-------------|-----------------|-------------|----------|
| 6.9.1 SendToStage | `Connector.Send(CrossServerMsg)` | `IStageSender.SendToStage(play2Nid, stageId, IPacket)` | `IStage.OnDispatch(IPacket)` | 단방향 전달 |
| 6.9.2 RequestToStage async | `Connector.RequestAsync(CrossServerMsg)` | `IStageSender.RequestToStage(play2Nid, stageId, IPacket)` | `OnDispatch` → `Reply` | 응답 반환 |
| 6.9.3 RequestToStage callback | `Connector.Request(CrossServerMsg, cb)` | `IStageSender.RequestToStage(..., ReplyCallback)` | `OnDispatch` → `Reply` | 콜백 호출 |
| 6.9.4 Cross-Server Actor | `Connector.Send(ActorAction)` | `IActorSender.SendToStage(...)` | `IStage.OnDispatch` | Actor에서 다른 서버로 |

---

#### 6.10 Stage/Actor 생명주기 (Lifecycle Callbacks)

| 테스트 | 트리거 | 확인할 콜백 순서 |
|--------|--------|----------------|
| 6.10.1 Stage 생성 | `IApiSender.CreateStage()` | `IStage.OnCreate(IPacket)` → `OnPostCreate()` |
| 6.10.2 Actor 생성/인증 | 클라이언트 인증 | `IActor.OnCreate()` → `OnAuthenticate(IPacket)` → `OnPostAuthenticate()` |
| 6.10.3 Stage 입장 | 인증 후 자동 | `IStage.OnJoinStage(IActor)` → `OnPostJoinStage(IActor)` |
| 6.10.4 연결 상태 변경 | 클라이언트 연결/끊김 | `IStage.OnConnectionChanged(IActor, bool isConnected)` |
| 6.10.5 Actor 퇴장 | `IActorSender.LeaveStage()` | `IActor.OnDestroy()` |
| 6.10.6 Stage 종료 | `IStageSender.CloseStage()` | `IStage.OnDestroy()` |

---

#### 6.11 타이머 및 AsyncBlock (IStageSender 기능)

| 테스트 | IStageSender 메소드 | 확인 사항 |
|--------|-------------------|----------|
| 6.11.1 반복 타이머 | `AddRepeatTimer(delay, period, TimerCallback)` | 주기적 콜백 호출 |
| 6.11.2 횟수 제한 타이머 | `AddCountTimer(delay, period, count, callback)` | count회 후 자동 종료 |
| 6.11.3 타이머 취소 | `CancelTimer(timerId)` | 콜백 중지, `HasTimer() = false` |
| 6.11.4 AsyncBlock | `AsyncBlock(preCallback, postCallback)` | pre→post 순서, Stage 스레드에서 post 실행 |

---

#### 6.12 에러 및 예외 처리 (Error Handling)

| 테스트 | 조건 | 확인 사항 |
|--------|------|----------|
| 6.12.1 Request 타임아웃 | 30초 내 응답 없음 | `ConnectorException` / `OnError(RequestTimeout)` |
| 6.12.2 존재하지 않는 Stage | `SendToStage(잘못된 stageId)` | 에러 응답 |
| 6.12.3 인증 실패 | `OnAuthenticate() = false` | 연결 종료 |
| 6.12.4 AccountId 미설정 | `OnAuthenticate()` 후 AccountId = "" | 연결 종료 |
| 6.12.5 서버 에러 코드 | `Reply(ushort errorCode)` | 클라이언트 `OnError` |

---

#### 6.13 재연결 시나리오 (Reconnection)

| 테스트 | 시나리오 | 확인할 콜백 |
|--------|---------|------------|
| 6.13.1 연결 끊김 감지 | 서버 강제 종료 | `OnDisconnect()` |
| 6.13.2 재연결 | `ConnectAsync()` 재호출 | `OnConnect(true)` |
| 6.13.3 재인증 | 동일 AccountId로 `AuthenticateAsync()` | `IsAuthenticated() = true` |
| 6.13.4 연결 상태 알림 | 재연결 시 | `IStage.OnConnectionChanged(actor, false)` → `OnConnectionChanged(actor, true)` |
| 6.13.5 상태 유지 | Stage 상태 | 기존 Actor 정보 유지 확인 |

---

#### 6.14 통합 테스트 재분류 (기존 테스트)
- [x] 6.14.1 ConnectorE2ETests.cs → Integration 테스트로 이동
- [x] 6.14.2 BootstrapServerE2ETests.cs → Integration 테스트로 이동

### Phase 7: 통합 및 정리
- [ ] 7.1 Session 코드 제거
- [ ] 7.2 HTTP API 제거
- [ ] 7.3 레거시 인터페이스 제거
- [ ] 7.4 성능 벤치마크
- [ ] 7.5 ISystemController 샘플
- [ ] 7.6 문서화
- [ ] 7.7 샘플 프로젝트

---

## 8. 용어 정의

| 용어 | 설명 |
|------|------|
| **NID** | Node ID, `{ServiceId}:{ServerId}` 형식의 서버 식별자 |
| **Stage** | 게임 룸, 로비 등 Actor들이 모인 논리적 단위 |
| **Actor** | Stage 내에서 활동하는 개별 참여자 (플레이어) |
| **MsgSeq** | Request-Reply 매칭을 위한 시퀀스 번호 (1~65535, 0=단방향) |
| **RoutePacket** | 서버 간 통신용 내부 패킷 (RouteHeader + Payload) |
| **RouteHeader** | 라우팅 정보 (From, IsReply, IsBackend, AccountId, StageId 등) |
| **CAS** | Compare-And-Set, Lock-free 동시성 제어 기법 |
| **AsyncBlock** | Event Loop 외부에서 I/O 처리 후 결과를 Event Loop로 반환하는 패턴 |
| **ISystemController** | 서버 디스커버리를 위해 컨텐츠 개발자가 구현하는 인터페이스 |
| **ServerAddressResolver** | 주기적으로 서버 목록을 갱신하고 자동 Connect/Disconnect 처리 |
| **ReplyObject** | Request-Reply 패턴에서 콜백 또는 TaskCompletionSource를 래핑 |
| **Full-Mesh** | 모든 서버가 서로 연결된 토폴로지 (Router-Router 패턴) |

---

> **다음 단계**: Phase 1부터 순차적으로 구현을 시작하세요.
> 각 Phase 완료 후 해당 체크리스트를 업데이트하고, 다음 Phase로 진행하세요.
