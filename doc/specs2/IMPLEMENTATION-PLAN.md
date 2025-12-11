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

- **현재 Phase**: 7 (통합 및 정리)
- **진행 방식**: 단일 에이전트 순차 진행
- **최근 완료**: Phase 6 - E2E 테스트 인프라 (2025-12-11)
- **완료된 Phase**:
  - Phase 1: NetMQ 통신 계층 ✅
  - Phase 2: 핵심 인터페이스 ✅
  - Phase 3: Play 서버 ⚠️ (BaseStageCmdHandler 미구현 - Stage Command 처리 필요)
  - Phase 4: API 서버 ✅
  - Phase 5: Connector ✅
  - Phase 6: E2E 테스트 인프라 ✅
- **남은 작업**:
  - **Phase 3 보완**: BaseStageCmdHandler 실제 구현 (JoinStageCmd, CreateJoinStageCmd, DisconnectNoticeCmd, ReconnectCmd)
  - Phase 7: 통합 및 정리, 레거시 코드 제거, 성능 벤치마크

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

### Phase 1: NetMQ 통신 계층 ✅
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
- [x] 1.11 XServerInfoCenter 구현 (Runtime/Discovery/XServerInfoCenter.cs)
- [x] 1.12 ServerAddressResolver 구현 (Runtime/Discovery/ServerAddressResolver.cs)
- [x] 1.13 CommunicatorOption/Builder 구현 (Runtime/Communicator/CommunicatorOption.cs)
- [ ] 1.14 PooledByteBuffer 구현 (선택적 - ArrayPool 사용으로 대체)
- [x] 1.15 AtomicBoolean 구현 (AtomicShort 대체)
- [x] 1.16 Communicator 구현 (PlayCommunicator가 담당)
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
- [x] **3.10 BaseStageCmdHandler** (Core/Play/Base/BaseStageCmdHandler.cs) - ✅ **구현 완료 (2025-12-11)**
  - [x] 3.10a JoinStageCmd (10단계 인증 플로우: XActorSender → IActor.OnCreate → OnAuthenticate → OnPostAuthenticate → IStage.OnJoinStage → OnPostJoinStage)
  - [x] 3.10b CreateJoinStageCmd (Stage 생성 + 입장 동시 처리)
  - [x] 3.10c GetOrCreateStageCmd (기존 Stage 반환 또는 생성)
  - [x] 3.10d DisconnectNoticeCmd (연결 끊김 알림 → IStage.OnConnectionChanged)
  - [x] 3.10e ReconnectCmd (재연결 처리 → IStage.OnConnectionChanged)
  - [x] 3.10f TimerMsg 처리 (BaseStage.PostTimerCallback으로 이미 구현됨)
- [x] 3.11 TimerManager
- [x] 3.12 PlayProducer
- [x] 3.13 PlayServerBootstrap
- [x] 3.14 TcpSessionHandler (Core/Session/TcpSessionHandler.cs)
- [ ] 3.15 WebSocketHandler (선택적 - TCP 우선)
- [x] 3.16 ClientSession
- [x] 3.17 SessionManager (Core/Session/SessionManager.cs)
- [x] 3.18 PlayCommunicator 통합
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
- [x] 4.12 SystemDispatcher (Abstractions/System/SystemDispatcher.cs)
- [x] 4.13 ISystemController 인터페이스 (Abstractions/System/ISystemController.cs)
- [x] 4.14 ISystemHandlerRegister (ISystemController.cs에 포함)
- [x] 4.15 ApiServerBootstrap
- [x] 4.16 단위 테스트 (ApiDispatcherTests, HandlerRegisterTests)

### Phase 5: Connector ✅
- [x] 5.1 IPayload/IPacket
- [x] 5.2 Payload 구현 (ProtoPayload, BytePayload, EmptyPayload)
- [x] 5.3 Packet 구현
- [x] 5.4 Connector 클래스
- [x] 5.5 ConnectorConfig
- [x] 5.6 ConnectorErrorCode
- [x] 5.7 PacketEncoder (Core/Messaging/PacketEncoder.cs)
- [x] 5.8 PacketDecoder (Core/Messaging/PacketDecoder.cs)
- [x] 5.9 RequestTracker (Core/Messaging/RequestTracker.cs)
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
| **사용자가 접근 가능한 것만 검증** | Connector 공개 API, 콜백만 사용 |
| **서버 내부 상태는 검증 불가** | `SessionManager.SessionCount` 등은 통합테스트로 이동 |
| **Request 패킷** | 응답 메시지 내용 검증 |
| **Send 패킷** | 서버에서 Push 응답 → `OnReceive`로 확인 |
| **서버만 검증 가능한 것** | 통합테스트 목록에 추가, E2E에서는 "→ 통합테스트" 표기 |

---

#### 6.1 Connector 연결/인증

##### Connector 공개 API
- **Properties**: `ConnectorConfig`, `StageId`
- **Events**: `OnConnect`, `OnReceive`, `OnError`, `OnDisconnect`
- **Methods**: `Init()`, `Connect()`, `ConnectAsync()`, `Disconnect()`, `IsConnected()`, `IsAuthenticated()`, `Authenticate()`, `AuthenticateAsync()`, `Send()`, `Request()`, `RequestAsync()`

##### 6.1.1 연결 테스트

| 테스트 | 검증 방법 |
|--------|----------|
| TCP 연결 성공 | `IsConnected() == true`, `OnConnect(true)` 콜백 |
| TCP 연결 실패 (잘못된 host) | `IsConnected() == false`, `OnConnect(false)` 콜백 |
| ConnectAsync 성공 | `await ConnectAsync() == true`, `IsConnected() == true` |
| ConnectAsync 실패 | `await ConnectAsync() == false` |
| Disconnect 호출 | `IsConnected() == false`, `OnDisconnect` 콜백 없음 (클라이언트 주도 해제) |
| 서버 연결 해제 | `OnDisconnect` 콜백 발생 |

> 서버측 세션 생성/제거 → **통합테스트**

##### 6.1.2 인증 테스트

| 테스트 | 검증 방법 |
|--------|----------|
| Authenticate (callback) 성공 | 콜백 호출, 응답 패킷 내용, `IsAuthenticated() == true` |
| AuthenticateAsync 성공 | 응답 패킷 내용, `IsAuthenticated() == true` |
| 인증 실패 | `OnDisconnect` 콜백, `IsAuthenticated() == false` |
| 미인증 상태에서 Send | `OnError(Unauthenticated)` 콜백 |
| 미인증 상태에서 Request | `OnError(Unauthenticated)` 콜백 |

> 서버 IActor.OnAuthenticate 콜백 → **통합테스트**

---

#### 6.2 Connector 메시지 송수신

##### 6.2.1 Send (Fire-and-Forget)

| 테스트 | 검증 방법 |
|--------|----------|
| Send 후 연결 유지 | `IsConnected() == true` |
| Send 메시지 도착 확인 | 서버에서 에코 Push → `OnReceive(stageId, packet)` 확인 |

> 서버 IStage.OnDispatch 호출 → **통합테스트**

##### 6.2.2 Request (Callback)

| 테스트 | 검증 방법 |
|--------|----------|
| Request 성공 | 콜백 호출, 응답 패킷 내용 검증 |
| Request 에러 응답 | `OnError(stageId, errorCode, request)` 콜백 |

##### 6.2.3 RequestAsync

| 테스트 | 검증 방법 |
|--------|----------|
| RequestAsync 성공 | 응답 패킷 내용 검증 |
| RequestAsync 타임아웃 | `ConnectorException` 발생, `ErrorCode == RequestTimeout` |
| RequestAsync 에러 응답 | `ConnectorException` 발생, `ErrorCode` 확인 |

##### 6.2.4 OnReceive 이벤트

| 테스트 | 검증 방법 |
|--------|----------|
| Push 메시지 수신 | `OnReceive(stageId, packet)` 콜백, stageId/packet 내용 검증 |
| 여러 Push 수신 | 모든 `OnReceive` 콜백 순서대로 호출 |

---

#### 6.3 ISender 메서드

> **트리거 방식**: Client Request → Stage/API 핸들러에서 ISender 메서드 호출 → 결과를 Client에게 Reply

##### 6.3.1 SendToApi

| 테스트 | 트리거 | E2E 검증 |
|--------|--------|----------|
| SendToApi 호출 | Client Request("TriggerSendToApi") → Stage에서 SendToApi 호출 후 Reply | Client Request 응답 수신 확인 |

> API 서버 메시지 수신 → **통합테스트**

##### 6.3.2 RequestToApi (callback)

| 테스트 | 트리거 | E2E 검증 |
|--------|--------|----------|
| RequestToApi 콜백 | Client Request → Stage에서 RequestToApi(callback) → callback에서 Reply | Client Reply에 API 응답 데이터 포함 |

##### 6.3.3 RequestToApi (async)

| 테스트 | 트리거 | E2E 검증 |
|--------|--------|----------|
| await RequestToApi | Client Request → Stage에서 await RequestToApi → Reply | Client Reply에 API 응답 데이터 포함 |

##### 6.3.4 SendToStage

| 테스트 | 트리거 | E2E 검증 |
|--------|--------|----------|
| SendToStage 호출 | HTTP API → API에서 SendToStage 호출 | HTTP 응답 성공 |

> Stage 메시지 수신 → **통합테스트**

##### 6.3.5 RequestToStage (callback)

| 테스트 | 트리거 | E2E 검증 |
|--------|--------|----------|
| RequestToStage 콜백 | HTTP API → API에서 RequestToStage(callback) | HTTP 응답에 Stage 데이터 포함 |

##### 6.3.6 RequestToStage (async)

| 테스트 | 트리거 | E2E 검증 |
|--------|--------|----------|
| await RequestToStage | HTTP API → API에서 await RequestToStage | HTTP 응답에 Stage 데이터 포함 |

##### 6.3.7 Reply

| 테스트 | 트리거 | E2E 검증 |
|--------|--------|----------|
| Reply(errorCode) | Client Request → Stage에서 Reply(500) | `OnError(stageId, 500, request)` 또는 `ConnectorException` |
| Reply(packet) | Client Request → Stage에서 Reply(packet) | RequestAsync 응답 또는 콜백에서 packet 내용 검증 |

---

#### 6.4 IStageSender 메서드

##### 6.4.1 타이머 메서드

> 타이머는 서버 내부 동작 → **통합테스트**로 이동

| 메서드 | 통합테스트 검증 |
|--------|---------------|
| AddRepeatTimer | 콜백 반복 호출 확인 |
| AddCountTimer | 지정 횟수만큼 콜백 호출 확인 |
| CancelTimer | 콜백 중지 확인 |
| HasTimer | 타이머 존재 여부 확인 |

##### 6.4.2 AsyncBlock

> AsyncBlock은 서버 내부 동작 → **통합테스트**로 이동

| 메서드 | 통합테스트 검증 |
|--------|---------------|
| AsyncBlock(pre, post) | preCallback ThreadPool 실행, postCallback EventLoop 실행 |

##### 6.4.3 SendToClient

| 테스트 | 트리거 | E2E 검증 |
|--------|--------|----------|
| Stage에서 클라이언트로 Push | Client Send → Stage.OnDispatch에서 SendToClient 호출 | `OnReceive(stageId, packet)` 콜백, packet 내용 검증 |

##### 6.4.4 CloseStage

| 테스트 | 트리거 | E2E 검증 |
|--------|--------|----------|
| Stage 종료 후 요청 | Stage에서 CloseStage() 호출 → Client Request | 에러 응답 수신 |

> IStage.OnDestroy 콜백 → **통합테스트**

---

#### 6.5 IActorSender 메서드

##### 6.5.1 AccountId

| 테스트 | 트리거 | E2E 검증 |
|--------|--------|----------|
| AccountId 설정 | 인증 시 AccountId 설정 | 이후 Request에서 AccountId 기반 처리 확인 (Reply에 AccountId 포함) |

> IActor.OnAuthenticate에서 설정 → **통합테스트**

##### 6.5.2 SendToClient

| 테스트 | 트리거 | E2E 검증 |
|--------|--------|----------|
| Actor에서 클라이언트로 Push | Client Request → Actor에서 SendToClient 호출 | `OnReceive(stageId, packet)` 콜백 |

##### 6.5.3 LeaveStage

| 테스트 | 트리거 | E2E 검증 |
|--------|--------|----------|
| Actor 퇴장 | Client Request → Actor에서 LeaveStage() 호출 → 재요청 | 새 Actor 생성 확인 (다른 AccountId 또는 상태 초기화) |

> IActor.OnDestroy 콜백 → **통합테스트**

---

#### 6.6 IApiSender 메서드

> **트리거 방식**: HTTP Client → API 서버 → IApiSender 메서드 호출 → HTTP 응답

##### 6.6.1 CreateStage

| 테스트 | E2E 검증 |
|--------|----------|
| CreateStage 성공 | `CreateStageResult.ErrorCode == 0`, `CreateStageRes` 내용 검증 |
| CreateStage 실패 (중복 StageId) | `CreateStageResult.ErrorCode != 0` |

> IStage.OnCreate 콜백 → **통합테스트**

##### 6.6.2 JoinStage

| 테스트 | E2E 검증 |
|--------|----------|
| JoinStage 성공 | `JoinStageResult.ErrorCode == 0`, `JoinStageRes` 내용 검증 |
| JoinStage 실패 (미존재 Stage) | `JoinStageResult.ErrorCode != 0` |

> IActor 콜백들 → **통합테스트**

##### 6.6.3 GetOrCreateStage

| 테스트 | E2E 검증 |
|--------|----------|
| 새 Stage 생성 | `ErrorCode == 0`, `IsCreated == true` |
| 기존 Stage 사용 | `ErrorCode == 0`, `IsCreated == false` |

##### 6.6.4 CreateJoinStage

| 테스트 | E2E 검증 |
|--------|----------|
| CreateJoin 성공 | `ErrorCode == 0`, `CreateStageRes`, `JoinStageRes` 내용 검증 |

##### 6.6.5 SendToClient

| 테스트 | 트리거 | E2E 검증 |
|--------|--------|----------|
| API에서 클라이언트로 Push | HTTP API → SendToClient 호출 | Connector `OnReceive` 콜백 |
| 특정 세션에 Push | HTTP API → SendToClient(sessionNid, sid, packet) | 해당 클라이언트 `OnReceive` 콜백 |

---

### 6.15 Integration 테스트 (E2E 검증 불가 항목)

> 공개 API로 검증 불가능한 **서버 내부 콜백**은 통합 테스트에서 Fake 구현체로 검증
>
> **검증 방식**: Fake 구현체가 콜백 호출을 기록하고, 테스트에서 기록을 검증

---

#### 세션 관리

| 항목 | Fake/Mock 검증 방법 |
|------|------------------|
| 세션 생성 | `SessionManager.SessionCount` 증가 |
| 세션 제거 | `SessionManager.SessionCount` 감소 |

---

#### IActor 콜백

| 콜백 | Fake 검증 방법 |
|------|---------------|
| OnCreate | `FakeActor.OnCreateCalled == true` |
| OnAuthenticate | `FakeActor.OnAuthenticateCalled == true`, authPacket 내용 |
| OnPostAuthenticate | `FakeActor.OnPostAuthenticateCalled == true` |
| OnDestroy | `FakeActor.OnDestroyCalled == true` |

---

#### IStage 콜백

| 콜백 | Fake 검증 방법 |
|------|---------------|
| OnCreate | `FakeStage.OnCreateCalled == true`, createPacket 내용 |
| OnPostCreate | `FakeStage.OnPostCreateCalled == true` |
| OnJoinStage | `FakeStage.JoinedActors` 목록, actor 정보 |
| OnPostJoinStage | `FakeStage.PostJoinedActors` 목록 |
| OnConnectionChanged | `FakeStage.ConnectionChanges` 목록, isConnected 값 |
| OnDispatch (Client) | `FakeStage.ReceivedClientPackets` 목록, actor/packet 정보 |
| OnDispatch (Server) | `FakeStage.ReceivedServerPackets` 목록, packet 정보 |
| OnDestroy | `FakeStage.OnDestroyCalled == true` |

---

#### IStageSender 내부 기능

| 기능 | 검증 방법 |
|------|----------|
| AddRepeatTimer | `FakeTimerCallback.CallCount` 시간 경과 후 증가 |
| AddCountTimer | `FakeTimerCallback.CallCount == count` |
| CancelTimer | 콜백 호출 중지 확인 |
| HasTimer | `true/false` 반환값 |
| AsyncBlock | preCallback ThreadId ≠ postCallback ThreadId |

---

### 6.16 Unit 테스트 (단위 테스트)

> 개별 컴포넌트 단위 검증

---

#### 6.16.1 RequestCache - Unit

| 테스트 | 검증 항목 |
|--------|----------|
| MsgSeq 순환 | 1~65535 순환, 0 미사용 |
| Put/Get | 저장 후 조회 |
| 타임아웃 | 만료된 요청 정리 |
| OnReply 매칭 | MsgSeq로 ReplyObject 찾기 |

---

#### 6.16.2 TimerManager - Unit

| 테스트 | 검증 항목 |
|--------|----------|
| 타이머 등록 | timerId 반환, ActiveTimerCount 증가 |
| 타이머 취소 | 콜백 중지, ActiveTimerCount 감소 |
| Stage별 취소 | CancelAllForStage(stageId) |

---

#### 6.16.3 AtomicBoolean - Unit

| 테스트 | 검증 항목 |
|--------|----------|
| CompareAndSet | true/false 반환값 |
| Get/Set | 현재 값 조회/설정 |
| 스레드 안전성 | 동시 접근 시 정확성 |

---

#### 6.16.4 Packet/Payload - Unit

| 테스트 | 검증 항목 |
|--------|----------|
| Packet 생성 | MsgId, Payload 설정 |
| Protobuf 직렬화 | `Packet(IMessage)` 생성자 |
| Payload 종류 | BytePayload, ProtoPayload, EmptyPayload |
| Dispose | 리소스 해제 |

---

#### 6.16.5 RuntimeRoutePacket - Unit

| 테스트 | 검증 항목 |
|--------|----------|
| 팩토리 메서드 | `Of()`, `FromFrames()`, `Empty()` |
| Reply 생성 | `CreateReply()`, `CreateErrorReply()` |
| Header 접근 | MsgId, MsgSeq, StageId, AccountId 등 |
| 직렬화 | `SerializeHeader()`, `GetPayloadBytes()` |

---

#### 6.16.6 ApiDispatcher - Unit

| 테스트 | 검증 항목 |
|--------|----------|
| 핸들러 등록 | `Register(msgId, handler)` |
| 디스패치 | MsgId로 핸들러 찾아 실행 |
| 미등록 메시지 | 에러 처리 |

---

#### 6.16.7 PlayDispatcher - Unit

| 테스트 | 검증 항목 |
|--------|----------|
| Stage 라우팅 | StageId로 Stage 찾기 |
| Stage 생성 | CreateStageReq 처리 |
| Stage 미존재 | 에러 응답 |
| StageCount | 활성 Stage 수 |

---

### 테스트 파일 구조

```
tests/
├── PlayHouse.Tests.E2E/
│   ├── ConnectorTests/
│   │   ├── ConnectionTests.cs          # 6.1
│   │   └── MessagingTests.cs           # 6.2
│   ├── SenderTests/
│   │   ├── ISenderTests.cs             # 6.3
│   │   ├── IStageSenderTests.cs        # 6.4
│   │   ├── IActorSenderTests.cs        # 6.5
│   │   └── IApiSenderTests.cs          # 6.6
│   └── Infrastructure/
│       ├── TestPlayServer.cs
│       ├── TestApiServer.cs
│       └── TestStageImpl.cs            # E2E용 Stage (에코, Push 응답)
│
└── PlayHouse.Tests.Integration/
    ├── StageLifecycleTests.cs          # Stage 콜백 테스트
    ├── ActorLifecycleTests.cs          # Actor 콜백 테스트
    ├── MessageDispatchTests.cs         # 메시지 디스패치 테스트
    ├── ConnectionStateTests.cs         # 연결 상태 변경 테스트
    ├── TimerTests.cs                   # IStageSender 타이머
    ├── AsyncBlockTests.cs              # AsyncBlock
    └── Fakes/
        ├── FakeStage.cs
        ├── FakeActor.cs
        └── TestPlayProducer.cs
```

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
