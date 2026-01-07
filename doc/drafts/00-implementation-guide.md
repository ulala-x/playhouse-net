# PlayHouse-NET 구현 가이드

## 문서 개요

이 문서는 PlayHouse-NET 시스템의 에이전틱 코딩을 위한 진입점으로, 전체 변경 작업을 단계별로 안내합니다.

**작성일**: 2025-12-10
**대상 독자**: AI 코딩 에이전트, 개발자
**목적**: 현재 시스템을 새로운 아키텍처로 전환하기 위한 체계적인 구현 로드맵 제공

---

## 1. 시스템 개요

### 1.1 현재 시스템 (As-Is)

```
┌─────────────────────────────────┐
│      PlayHouseServer            │
│  (단일 서버 - 모든 책임 통합)    │
│                                 │
│  - HTTP API (REST Controller)  │
│  - TCP/WebSocket (Client)      │
│  - Stage/Actor 관리             │
└─────────────────────────────────┘
```

**특징**:
- 단일 프로세스 서버
- HTTP REST API로 Stage 생성
- TCP/WebSocket으로 클라이언트 직접 연결
- 서버 간 통신 없음

**주요 구성**:
- `PlayHouseServer`: 단일 서버 엔트리포인트
- `RoomController`: HTTP API 엔드포인트
- `IStage`, `IActor`: Stage/Actor 인터페이스
- TCP/WebSocket 연결 관리

### 1.2 목표 시스템 (To-Be)

```
┌──────────────────┐                    ┌──────────────────┐
│   API Server     │                    │   Play Server    │
│   (Stateless)    │◄──── ZMQ ──────►│   (Stateful)     │
│                  │    Router-Router   │                  │
│ - HTTP API       │                    │ - Stage 관리     │
│ - ZMQ Client   │                    │ - Actor 관리     │
│ - 요청 라우팅     │                    │ - Client 연결    │
└──────────────────┘                    └────────┬─────────┘
                                                 │
                                                 │ TCP/WebSocket
                                                 │
                                            ┌────▼────┐
                                            │ Clients │
                                            └─────────┘
```

**변경 사항**:
- **서버 분리**: Play 서버 + API 서버
- **통신 방식**: ZMQ Router-Router 패턴
- **클라이언트 연결**: Play 서버에 직접 연결
- **인증 흐름**: HTTP 토큰 → TCP 인증으로 변경

### 1.3 참조 시스템

**기존 PlayHouse 구현**: `D:\project\kairos\playhouse\playhouse-net\PlayHouse`

**재사용 가능 컴포넌트**:
- Session 서버 개념 **삭제** (Play 서버에 통합)
- API 서버 개념 **간소화** (단순 요청 처리)
- ZMQ 통신 레이어 **재사용 가능**

---

## 2. 핵심 변경 사항

### 2.1 변경 영역 요약

| 영역 | 현재 | 변경 후 | 영향도 |
|------|------|---------|--------|
| **서버 구성** | 단일 PlayHouseServer | Play 서버 + API 서버 | 🔴 High |
| **서버 간 통신** | 없음 (단일 프로세스) | ZMQ Router-Router | 🔴 High |
| **클라이언트 연결** | HTTP 토큰 → TCP | Play 서버 직접 연결 → 인증 | 🟡 Medium |
| **REST API** | RoomController | 삭제 (ZMQ로 대체) | 🔴 High |
| **Stage 생성** | HTTP API 직접 호출 | API 서버 → Play 서버 요청 | 🟡 Medium |
| **인터페이스** | IStage, IActor | OnAuthenticate 추가 | 🟢 Low |

### 2.2 삭제 대상 ❌

**Session 서버 개념 전체**:
- `ISessionActor` 인터페이스
- SessionServer 프로젝트
- Session 관련 통신 레이어
- 중간 계층 제거, Play 서버에서 직접 클라이언트 연결 관리

**Play 서버의 REST API**:
- `RoomController` 및 모든 HTTP 컨트롤러
- ASP.NET Core 호스팅 코드
- HTTP 미들웨어

**HTTP 기반 서버 간 통신**:
- REST API 기반 서버 간 호출
- HTTP 클라이언트 코드

### 2.3 변경 대상 ⚠️

**인터페이스 변경**:

```csharp
// 기존
public interface IActor
{
    IActorSender ActorSender { get; }
    Task OnCreate();
    Task OnDestroy();
}

// 변경 후
public interface IActor
{
    IActorSender ActorSender { get; }
    Task OnCreate();
    Task OnDestroy();
    Task<bool> OnAuthenticate(IPacket authPacket);  // 추가
    Task OnPostAuthenticate();                       // 추가
}
```

```csharp
// 기존
public interface IStage
{
    Task<bool> OnJoinStage(IActor actor, IPacket packet);
}

// 변경 후
public interface IStage
{
    Task<bool> OnJoinStage(IActor actor);  // packet 제거
    Task OnPostJoinStage(IActor actor);     // 추가
    ValueTask OnConnectionChanged(IActor actor, bool isConnected);  // 추가
}
```

**인증 흐름 변경**:

```
[기존]
Client → HTTP API (토큰 인증) → Session 서버 → Play 서버

[변경]
Client → Play 서버 (TCP 직접 연결 + 인증)
```

**Stage 생성 흐름 변경**:

```
[기존]
External System → HTTP POST /api/rooms/create → Play 서버

[변경]
External System → HTTP POST → API 서버 → ZMQ → Play 서버
```

### 2.4 신규 구현 🆕

**ZMQ 통신 레이어** (재사용):
```
PlayHouse/Runtime/
├── Communicator.cs          # 메인 통신 오케스트레이터
├── XServerCommunicator.cs   # 메시지 수신 (Bind)
├── XClientCommunicator.cs   # 메시지 송신 (Connect)
├── MessageLoop.cs           # 송수신 스레드 관리
├── RequestCache.cs          # Request-Response 매칭
├── PlaySocket/
│   ├── IPlaySocket.cs
│   ├── ZMQPlaySocket.cs   # Router 소켓 구현
│   └── PlaySocketConfig.cs
└── Message/
    ├── RoutePacket.cs
    ├── RouteHeader.cs
    └── Payload.cs (FramePayload 등)
```

**Play 서버 모듈** (`Core/Play/` - Stage, Session 통합):
```
PlayHouse/Core/Play/
├── PlayServerBootstrap.cs       # 🆕 Play 서버 부트스트랩
├── PlayServerOption.cs          # Play 서버 설정
├── Stage/                       # ← Core/Stage/ 이동
│   ├── StageManager.cs          # Stage 생명주기 관리
│   ├── StageContext.cs          # Stage 실행 컨텍스트
│   └── StageSender.cs           # IStageSender 구현
├── Actor/
│   ├── ActorManager.cs          # Actor 생명주기 관리
│   ├── ActorContext.cs          # Actor 실행 컨텍스트
│   └── ActorSender.cs           # IActorSender 구현
└── Session/                     # ← Core/Session/ 이동
    ├── ClientConnectionManager.cs  # TCP/WebSocket 연결 관리
    ├── ClientSession.cs            # 개별 클라이언트 세션
    └── AuthenticationHandler.cs    # 인증 처리
```

**API 서버 모듈** (`Core/Api/` - 신규):
```
PlayHouse/Core/Api/
├── ApiServerBootstrap.cs        # 🆕 API 서버 부트스트랩
├── ApiServerOption.cs           # API 서버 설정
├── ApiSender.cs                 # IApiSender 구현
├── ApiDispatcher.cs             # 메시지 핸들러 디스패처
└── HandlerRegister.cs           # IHandlerRegister 구현
```

**Bootstrap 사용 예시** (.NET Core DI 서비스로 등록):

> **설계 원칙**: Play 서버와 API 서버 모두 .NET Core DI 컨테이너에 서비스로 등록하여 사용

```csharp
// ===== Play 서버 (독립 프로세스) =====
var builder = Host.CreateApplicationBuilder(args);

var playServer = new PlayServerBootstrap()
    .Configure(options =>
    {
        options.ServiceId = 1;
        options.ServerId = 1;
        options.BindEndpoint = "tcp://0.0.0.0:5000";      // ZMQ 서버 간 통신
        options.ClientEndpoint = "tcp://0.0.0.0:6000";    // 클라이언트 TCP
    })
    .UseStage<GameRoomStage>("GameRoom")
    .UseActor<PlayerActor>()
    .Build();

builder.Services.AddSingleton(playServer);
builder.Services.AddHostedService<PlayServerHostedService>();

var host = builder.Build();
await host.RunAsync();

// ===== API 서버 (웹서버에 통합) =====
var builder = WebApplication.CreateBuilder(args);

var apiServer = new ApiServerBootstrap()
    .Configure(options =>
    {
        options.ServiceId = 2;
        options.ServerId = 1;
        options.BindEndpoint = "tcp://0.0.0.0:5100";
    })
    .UseController<GameApiController>()
    .Build();

builder.Services.AddSingleton(apiServer);
builder.Services.AddSingleton<IApiSender>(apiServer.ApiSender);
builder.Services.AddHostedService<ApiServerHostedService>();

var app = builder.Build();
app.Run();
```

---

## 3. 참조 시스템 코드 재사용 가이드

### 3.1 그대로 복사 가능 (Copy)

**ZMQ 통신 레이어** (95% 재사용):
- `D:\project\kairos\playhouse\playhouse-net\PlayHouse\PlayHouse\Runtime\PlaySocket\` → 그대로 복사
- `D:\project\kairos\playhouse\playhouse-net\PlayHouse\PlayHouse\Runtime\Message\` → 그대로 복사
- `D:\project\kairos\playhouse\playhouse-net\PlayHouse\PlayHouse\Runtime\XClientCommunicator.cs` → 그대로 복사
- `D:\project\kairos\playhouse\playhouse-net\PlayHouse\PlayHouse\Runtime\XServerCommunicator.cs` → 그대로 복사
- `D:\project\kairos\playhouse\playhouse-net\PlayHouse\PlayHouse\Runtime\MessageLoop.cs` → 그대로 복사
- `D:\project\kairos\playhouse\playhouse-net\PlayHouse\PlayHouse\Runtime\RequestCache.cs` → 그대로 복사

**주요 특징**:
- Router-Router 소켓 패턴
- 3-프레임 멀티파트 메시지 구조
- NID 기반 Identity 라우팅
- Zero-Copy 최적화 (FramePayload)

### 3.2 수정 후 사용 (Adapt)

**XSender 계열**:
- `D:\project\kairos\playhouse\playhouse-net\PlayHouse\PlayHouse\Core\Shared\XSender.cs`
- 변경 사항:
  - `ISender` 인터페이스에 맞춰 메서드 시그니처 조정
  - `SendToApi`, `RequestToApi` 메서드 추가
  - `SendToStage`, `RequestToStage` 메서드 추가

**Communicator**:
- `D:\project\kairos\playhouse\playhouse-net\PlayHouse\PlayHouse\Runtime\Communicator.cs`
- 변경 사항:
  - Session 서버 관련 로직 제거
  - Play 서버와 API 서버 구분 로직 추가

### 3.3 참조만 (Reference)

**아키텍처 패턴**:
- Stage/Actor 생명주기 관리 패턴
- Lock-Free 이벤트 루프 (CAS 기반)
- Timer 관리 시스템
- AsyncBlock 패턴

**설정 및 구조**:
- ZMQ 소켓 옵션 설정
- 버퍼 크기 및 워터마크 설정
- 스레드 모델 (Server Thread + Client Thread)

---

## 4. 구현 우선순위 (Phase별)

### Phase 1: 인프라 구축 (ZMQ 통신 계층)

**목표**: ZMQ 기반 서버 간 통신 인프라 구축

**작업 항목**:
1. **ZMQ 통신 레이어 복사** ✅
   - `PlaySocket` 디렉토리 복사
   - `Message` 디렉토리 복사
   - `Communicator`, `XServerCommunicator`, `XClientCommunicator` 복사
   - `MessageLoop`, `RequestCache` 복사

2. **인터페이스 정의** ✅
   - `ISender` 인터페이스 정의 (`SendToApi`, `RequestToApi`, `SendToStage`, `RequestToStage`)
   - `IApiSender` 인터페이스 정의 (`CreateStage`, `GetOrCreateStage`)
   - `ISystemPanel`, `IServerInfo` 인터페이스 정의

3. **단위 테스트 작성** ✅
   - ZMQ 메시지 송수신 테스트
   - Request-Response 패턴 테스트
   - Timeout 처리 테스트

**산출물**:
- `PlayHouse.Runtime` 프로젝트 완성
- ZMQ 통신 단위 테스트 통과

**참조 문서**:
- [07-zmq-runtime.md](./07-zmq-runtime.md) - ZMQ Runtime 상세 스펙
- [02-server-communication.md](./02-server-communication.md) - 서버 간 통신 프로토콜

---

### Phase 2: 인터페이스 구현 (new-request.md 기준)

**목표**: 새로운 인터페이스 정의 및 구현

**작업 항목**:
1. **Packet 시스템 구현** ✅
   - `IPacket`, `IPayload` 인터페이스
   - `RoutePacket`, `RouteHeader` 구조
   - Protobuf 메시지 정의

2. **Sender 인터페이스 구현** ✅
   - `ISender` 구현 (기본 전송 및 응답)
   - `IActorSender` 구현 (Actor 식별 정보 포함)
   - `IStageSender` 구현 (Stage 관리 기능 포함)

3. **API Controller 인터페이스** ✅
   - `IApiController` 구현
   - `IHandlerRegister` 구현
   - `ApiHandler` 델리게이트 정의

**산출물**:
- `PlayHouse.Abstractions` 프로젝트 업데이트
- 인터페이스 단위 테스트 통과

**참조 문서**:
- [06-interfaces.md](./06-interfaces.md) - 핵심 인터페이스 정의
- [new-request.md](./new-request.md) - 인터페이스 요구사항

---

### Phase 3: Play 서버 모듈 구현 (Stage/Actor)

**목표**: Play 서버 모듈(`Core/Play/`) 구현 및 Bootstrap 제공

**작업 항목**:
1. **Play 서버 모듈 생성 및 재배치** 🆕
   - `Core/Play/` 디렉토리 생성
   - `Core/Stage/` → `Core/Play/Stage/` 이동
   - `Core/Session/` → `Core/Play/Session/` 이동
   - `PlayServerBootstrap.cs` 구현
   - `PlayServerOption.cs` 설정 클래스

2. **IActor 인터페이스 확장** ✅
   - `OnAuthenticate(IPacket authPacket)` 메서드 추가
   - `OnPostAuthenticate()` 메서드 추가

3. **IStage 인터페이스 확장** ✅
   - `OnJoinStage(IActor actor)` 메서드 변경 (packet 제거)
   - `OnPostJoinStage(IActor actor)` 메서드 추가
   - `OnConnectionChanged(IActor actor, bool isConnected)` 메서드 추가
   - `OnDispatch(IPacket packet)` 서버 메시지 처리 추가

4. **Stage/Actor 관리** 🆕
   - `StageManager.cs`: Stage 생명주기 관리
   - `ActorManager.cs`: Actor 생명주기 관리
   - `StageSender.cs`, `ActorSender.cs`: Sender 구현

5. **클라이언트 연결 관리** 🆕
   - `ClientConnectionManager.cs`: TCP/WebSocket 리스너
   - `ClientSession.cs`: 개별 세션 관리
   - `AuthenticationHandler.cs`: 인증 처리

6. **Bootstrap 빌더 패턴** 🆕
   - `PlayServerBootstrap.Configure()` - 설정
   - `PlayServerBootstrap.UseStage<T>(stageType)` - Stage 타입 등록
   - `PlayServerBootstrap.UseActor<T>()` - Actor 타입 등록
   - `PlayServerBootstrap.Build()` - 서버 인스턴스 생성

**사용 예시** (.NET Core DI 서비스로 등록):
```csharp
var builder = Host.CreateApplicationBuilder(args);

// Play 서버 Bootstrap
var playServer = new PlayServerBootstrap()
    .Configure(options =>
    {
        options.ServiceId = 1;
        options.ServerId = 1;
        options.BindEndpoint = "tcp://0.0.0.0:5000";      // ZMQ 서버 간 통신
        options.ClientEndpoint = "tcp://0.0.0.0:6000";    // 클라이언트 TCP 연결
        options.WebSocketEndpoint = "ws://0.0.0.0:6001";  // 클라이언트 WebSocket (옵션)
    })
    .UseStage<GameRoomStage>("GameRoom")
    .UseStage<LobbyStage>("Lobby")
    .UseActor<PlayerActor>()
    .Build();

// DI 컨테이너에 등록
builder.Services.AddSingleton(playServer);
builder.Services.AddSingleton<ISystemPanel>(playServer.SystemPanel);
builder.Services.AddHostedService<PlayServerHostedService>();

var host = builder.Build();
await host.RunAsync();  // IHostedService가 StartAsync/StopAsync 관리
```

**산출물**:
- `Core/Play/` 모듈 완성 (Stage, Session 통합)
- `PlayServerBootstrap` 완성
- 클라이언트 직접 연결 기능 완성
- E2E 테스트 (클라이언트 → Play 서버 → Stage → Actor)

**참조 문서**:
- [03-play-server.md](./03-play-server.md) - Play 서버 상세 스펙
- [05-authentication-flow.md](./05-authentication-flow.md) - 인증 흐름

---

### Phase 4: API 서버 모듈 구현 (Stateless)

**목표**: API 서버 모듈(`Core/Api/`) 구현 및 Bootstrap 제공

**작업 항목**:
1. **API 서버 모듈 생성** 🆕
   - `Core/Api/` 디렉토리 생성
   - `ApiServerBootstrap.cs` 구현
   - `ApiServerOption.cs` 설정 클래스

2. **ApiSender 구현** 🆕
   - `IApiSender` 인터페이스 구현
   - `CreateStage()`, `GetOrCreateStage()` 메서드
   - ZMQ Request-Response 패턴 구현

3. **API Controller 등록 시스템** 🆕
   - `IApiController.Handles()` 구현
   - `HandlerRegister.cs` 구현
   - `ApiDispatcher.cs` 메시지 라우팅

4. **서버 디스커버리** 🆕
   - `ISystemPanel` 구현
   - Play 서버 선택 로직 (로드밸런싱)

5. **Bootstrap 빌더 패턴** 🆕
   - `ApiServerBootstrap.Configure()` - 설정
   - `ApiServerBootstrap.UseController<T>()` - 컨트롤러 등록
   - `ApiServerBootstrap.Build()` - 서버 인스턴스 생성

**사용 예시** (.NET Core 웹서버에 서비스로 등록):
```csharp
var builder = WebApplication.CreateBuilder(args);

// API 서버 Bootstrap
var apiServer = new ApiServerBootstrap()
    .Configure(options =>
    {
        options.ServiceId = 2;
        options.ServerId = 1;
        options.BindEndpoint = "tcp://0.0.0.0:5100";
    })
    .UseController<GameApiController>()
    .Build();

// DI 컨테이너에 등록
builder.Services.AddSingleton(apiServer);
builder.Services.AddSingleton<IApiSender>(apiServer.ApiSender);
builder.Services.AddHostedService<ApiServerHostedService>();  // IHostedService로 시작/종료 관리

var app = builder.Build();

// ASP.NET Core 엔드포인트에서 IApiSender 사용
app.MapPost("/api/rooms/create", async (CreateRoomRequest req, IApiSender sender) =>
{
    var result = await sender.CreateStage("1:1", "GameRoom", 0, req.ToPacket());
    return Results.Ok(result);
});

app.Run();  // IHostedService가 ApiServer의 StartAsync/StopAsync 관리
```

**산출물**:
- `Core/Api/` 모듈 완성
- `ApiServerBootstrap` 완성
- .NET Core 웹서버 연동 예제

**참조 문서**:
- [04-api-server.md](./04-api-server.md) - API 서버 상세 스펙
- [02-server-communication.md](./02-server-communication.md) - 서버 간 통신

---

### Phase 5: 통합 및 테스트

**목표**: 전체 시스템 통합 및 검증

**작업 항목**:
1. **Session 서버 제거** ❌
   - Session 관련 코드 제거
   - Client 인증 로직 Play 서버로 이관
   - 테스트 코드 업데이트

2. **E2E 테스트 작성** ✅
   - 클라이언트 → API 서버 → Play 서버 → Stage → Actor
   - Stage 생성 플로우
   - Actor 인증 및 입장 플로우
   - 메시지 송수신 플로우
   - 재연결 플로우

3. **성능 벤치마크** ✅
   - ZMQ 처리량 측정 (> 100,000 messages/sec 목표)
   - 지연 시간 측정 (< 100ms P95 목표)
   - 동시 접속 테스트 (10,000 CCU 목표)

4. **문서화 업데이트** ✅
   - API 문서 작성
   - 배포 가이드 작성
   - 마이그레이션 가이드 작성

**산출물**:
- 전체 시스템 통합 완료
- E2E 테스트 통과
- 성능 목표 달성
- 문서 완성

**참조 문서**:
- [01-architecture-v2.md](./01-architecture-v2.md) - 전체 아키텍처 개요

---

## 5. 관련 스펙 문서

### 5.1 Phase별 참조 문서

| Phase | 주요 문서 | 보조 문서 |
|-------|----------|----------|
| **Phase 1** | [07-zmq-runtime.md](./07-zmq-runtime.md) | [02-server-communication.md](./02-server-communication.md) |
| **Phase 2** | [06-interfaces.md](./06-interfaces.md) | [new-request.md](./new-request.md) |
| **Phase 3** | [03-play-server.md](./03-play-server.md) | [05-authentication-flow.md](./05-authentication-flow.md) |
| **Phase 4** | [04-api-server.md](./04-api-server.md) | [02-server-communication.md](./02-server-communication.md) |
| **Phase 5** | [01-architecture-v2.md](./01-architecture-v2.md) | 전체 문서 |

### 5.2 전체 문서 목록

1. **[01-architecture-v2.md](./01-architecture-v2.md)** - 시스템 아키텍처 개요
2. **[02-server-communication.md](./02-server-communication.md)** - ZMQ 서버 간 통신
3. **[03-play-server.md](./03-play-server.md)** - Play 서버 상세 스펙
4. **[04-api-server.md](./04-api-server.md)** - API 서버 상세 스펙
5. **[05-authentication-flow.md](./05-authentication-flow.md)** - 인증 흐름
6. **[06-interfaces.md](./06-interfaces.md)** - 핵심 인터페이스 정의
7. **[07-zmq-runtime.md](./07-zmq-runtime.md)** - ZMQ Runtime 상세 스펙 ⭐
8. **[new-request.md](./new-request.md)** - 인터페이스 요구사항

---

## 6. 구현 체크리스트

> **최종 업데이트**: 2025-12-11
> **상태**: ✅ Phase 1-5 모두 완료

### Phase 1: 인프라 구축 ✅ (완료)
- [x] `PlayHouse/Runtime/` 디렉토리 생성
- [x] ZMQ 통신 레이어 구현 (PlaySocket, Message, Communicator)
  - `Runtime/PlaySocket/IPlaySocket.cs`, `ZMQPlaySocket.cs`
  - `Runtime/Message/RuntimePayload.cs`, `RuntimeRoutePacket.cs`
  - `Runtime/Communicator/XClientCommunicator.cs`, `XServerCommunicator.cs`, `PlayCommunicator.cs`
- [x] `ISender`, `IApiSender` 인터페이스 정의
  - `Abstractions/ISender.cs`
  - `Abstractions/Api/IApiSender.cs`
- [x] ZMQ 단위 테스트 작성 및 통과
  - `tests/PlayHouse.Tests.Unit/Runtime/`

### Phase 2: 인터페이스 구현 ✅ (완료)
- [x] `IPacket`, `IPayload` 인터페이스 구현
  - `Abstractions/IPacket.cs`, `Abstractions/IPayload.cs`
- [x] `RoutePacket`, `RouteHeader` 구조 정의
  - `Runtime/Message/RuntimeRoutePacket.cs`
  - Protobuf: `Proto/RouteHeader.proto`
- [x] Sender 인터페이스 구현 (`ISender`, `IActorSender`, `IStageSender`)
  - `Abstractions/ISender.cs`
  - `Abstractions/Play/IActorSender.cs`
  - `Abstractions/Play/IStageSender.cs`
- [x] API Controller 인터페이스 구현
  - `Abstractions/Api/IApiController.cs`

### Phase 3: Play 서버 모듈 구현 ✅ (완료)
- [x] `Core/Play/` 디렉토리 생성
- [x] `IActor` 인터페이스 확장 (`OnAuthenticate`, `OnPostAuthenticate`)
  - `Abstractions/Play/IActor.cs`
- [x] `IStage` 인터페이스 확장 (`OnPostJoinStage`, `OnConnectionChanged`, `OnDispatch`)
  - `Abstractions/Play/IStage.cs`
- [x] Stage/Actor 관리자 구현
  - `Core/Play/PlayDispatcher.cs` - Stage 라우팅 및 관리
  - `Core/Play/Base/BaseStage.cs` - Event Loop 구현
  - `Core/Play/Base/BaseActor.cs` - Actor 래퍼
- [x] Sender 구현
  - `Core/Play/XStageSender.cs` - IStageSender 구현
  - `Core/Play/XActorSender.cs` - IActorSender 구현
- [x] 타이머 관리 구현
  - `Core/Play/TimerManager.cs`
- [x] PlayProducer (Stage/Actor 팩토리)
  - `Abstractions/Play/PlayProducer.cs`
- [x] E2E 테스트
  - `tests/PlayHouse.Tests.Unit/Core/Play/`

### Phase 4: API 서버 모듈 구현 ✅ (완료)
- [x] `Core/Api/` 디렉토리 생성
- [x] `ApiSender.cs` 구현 (`IApiSender`)
  - `Core/Api/ApiSender.cs`
- [x] `ApiDispatcher.cs` 구현
  - `Core/Api/ApiDispatcher.cs`
- [x] Handler 등록 시스템 구현
  - `Core/Api/Reflection/HandlerRegister.cs`
  - `Core/Api/Reflection/ApiReflection.cs`
- [x] API Controller 인터페이스
  - `Abstractions/Api/IApiController.cs`
- [x] 단위 테스트
  - `tests/PlayHouse.Tests.Unit/Core/Api/`

### Phase 5: Connector 클라이언트 라이브러리 ✅ (완료)
- [x] Connector 메인 클래스
  - `connector/PlayHouse.Connector/Connector.cs`
- [x] 설정 및 에러 코드
  - `connector/PlayHouse.Connector/ConnectorConfig.cs`
  - `connector/PlayHouse.Connector/ConnectorErrorCode.cs`
  - `connector/PlayHouse.Connector/ConnectorException.cs`
- [x] 연결 관리
  - `connector/PlayHouse.Connector/Connection/IConnection.cs`
  - `connector/PlayHouse.Connector/Connection/TcpConnection.cs`
  - `connector/PlayHouse.Connector/Connection/WebSocketConnection.cs`
- [x] 프로토콜 레이어
  - `connector/PlayHouse.Connector/Protocol/IPacket.cs`
  - `connector/PlayHouse.Connector/Protocol/IPayload.cs`
  - `connector/PlayHouse.Connector/Protocol/Payload.cs`
  - `connector/PlayHouse.Connector/Protocol/Packet.cs`
- [x] Unity 지원 (AsyncManager)
  - `connector/PlayHouse.Connector/Internal/AsyncManager.cs`
- [x] 단위 테스트
  - `tests/PlayHouse.Tests.Unit/Connector/`
- [x] E2E 테스트
  - `tests/PlayHouse.Tests.E2E/ConnectorE2ETests.cs`

---

## 7. 주의사항 및 권장사항

### 7.1 ZMQ 사용 시 주의사항

**Router 소켓 사용** (Dealer 아님):
- 모든 서버는 Router 소켓을 사용
- 각 서버는 Bind(수신)와 Connect(송신)를 동시에 수행
- Identity는 NID("serviceId:serverId") 사용

**메시지 구조**:
- 3-프레임 멀티파트 메시지: [Target NID | RouteHeader | Payload]
- Zero-Copy 최적화: FramePayload 사용

**스레드 모델**:
- Server Thread: 메시지 수신 전용 (Busy-Wait + 1ms Sleep)
- Client Thread: 메시지 송신 전용 (BlockingCollection)

### 7.2 인터페이스 변경 시 주의사항

**IActor 변경**:
- `OnCreate()`는 최초 입장 시에만 호출
- `OnAuthenticate()`는 최초 입장과 재연결 시 모두 호출
- 예외 발생 시 Actor 생성/재연결 실패

**IStage 변경**:
- `OnCreate()`, `OnJoinRoom()`에서 `errorCode != 0` 반환 시 생성/입장 실패
- 모든 메서드는 Event Loop 내에서 실행 (Thread-safe 보장)

### 7.3 성능 목표

**지연 시간**:
- Client ↔ Play Server: < 50ms (P95)
- Play Server ↔ API Server: < 100ms (P95)
- Play Server ↔ Play Server: < 80ms (P95)

**처리량**:
- Play Server: 10,000 CCU per instance
- API Server: 5,000 requests/sec per instance
- ZMQ: > 100,000 messages/sec

### 7.4 코드 재사용 원칙

**그대로 복사 (Copy)**:
- ZMQ 통신 레이어 (PlaySocket, Message, Communicator)
- 검증된 코드이므로 수정 최소화

**수정 후 사용 (Adapt)**:
- XSender 계열 (인터페이스 맞춤)
- Communicator (Session 로직 제거)

**참조만 (Reference)**:
- 아키텍처 패턴
- 설정 및 옵션

---

## 8. 다음 단계

**구현 시작**:
1. Phase 1부터 순차적으로 진행
2. 각 Phase 완료 후 테스트 통과 확인
3. 문서 업데이트

**협업 가이드**:
- 각 Phase는 독립적으로 개발 가능
- Phase 간 인터페이스 의존성 명확히 정의
- 단위 테스트 우선 작성 (TDD)

**문의 및 지원**:
- 스펙 문서 참조: `doc/specs2/` 디렉토리
- 참조 시스템 코드: `D:\project\kairos\playhouse\playhouse-net\PlayHouse`

---

**문서 버전**: 1.0
**최종 수정**: 2025-12-10
**작성자**: Architecture Team
**상태**: Draft
