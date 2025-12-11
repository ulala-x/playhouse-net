# PlayHouse-NET 아키텍처 V2 - 구현 가이드

## 문서 목적

본 문서는 에이전틱 코딩(Agentic Coding)을 통한 구현이 가능하도록, **현재 시스템(AS-IS)**과 **목표 시스템(TO-BE)**을 명확히 비교하고 변경 방법을 구체적으로 제시합니다.

## 목차

1. [시스템 개요](#1-시스템-개요)
2. [핵심 변경 사항](#2-핵심-변경-사항)
3. [참조 시스템 코드 위치](#3-참조-시스템-코드-위치)
4. [폴더 구조 변경](#4-폴더-구조-변경)
5. [인터페이스 변경](#5-인터페이스-변경)
6. [구현 체크리스트](#6-구현-체크리스트)

---

## 1. 시스템 개요

### 1.1 현재 시스템 (AS-IS)

#### 시스템 구조
```
┌──────────────────────────────────────────────────────────┐
│                        Client                            │
└────────────────┬─────────────────────────────────────────┘
                 │
                 │ HTTP POST /api/rooms/create
                 │ (RoomToken 발급)
                 │
                 ▼
┌────────────────────────────────────────────────────────────┐
│              PlayHouseServer (단일 프로세스)                │
│                                                             │
│  ┌──────────────────────────────────────────────────┐     │
│  │ HTTP API (ASP.NET Core)                          │     │
│  │  - RoomController.CreateRoom()                   │     │
│  │  - RoomController.GetOrCreateRoom()              │     │
│  │  - RoomTokenManager (토큰 생성/검증)             │     │
│  └──────────────────────────────────────────────────┘     │
│                           │                                │
│                           │                                │
│  ┌────────────────────────┴──────────────────────────┐    │
│  │ Stage 관리                                        │    │
│  │  - StagePool (Stage 저장소)                       │    │
│  │  - StageFactory (Stage 생성/삭제)                 │    │
│  │  - PacketDispatcher (메시지 라우팅)               │    │
│  └───────────────────────────────────────────────────┘    │
│                           │                                │
│                           │                                │
│  ┌────────────────────────┴──────────────────────────┐    │
│  │ Client 연결 관리                                  │    │
│  │  - TcpServer (TCP 연결)                           │    │
│  │  - WebSocketServer (WebSocket 연결)               │    │
│  │  - SessionManager (세션 관리)                     │    │
│  └───────────────────────────────────────────────────┘    │
│                           │                                │
└───────────────────────────┼────────────────────────────────┘
                            │
                            │ TCP/WebSocket
                            │ (AuthenticateRequest + RoomToken)
                            │
                            ▼
                  ┌──────────────────┐
                  │      Client      │
                  └──────────────────┘
```

#### 현재 플로우
1. **클라이언트**: HTTP POST /api/rooms/create → RoomToken 수신
2. **클라이언트**: TCP 연결 → AuthenticateRequest (RoomToken 포함) 전송
3. **서버**: RoomToken 검증 → Actor 생성 → Stage 입장
4. **클라이언트-서버**: TCP/WebSocket으로 실시간 통신

#### 현재 구성 요소
- **PlayHouseServer**: 단일 프로세스, HTTP API + TCP/WebSocket 서버
- **RoomController**: HTTP REST API 엔드포인트
- **RoomTokenManager**: 토큰 발급 및 검증
- **StagePool**: 모든 Stage 관리
- **SessionManager**: 클라이언트 세션 추적
- **TcpServer/WebSocketServer**: 클라이언트 연결 처리

### 1.2 목표 시스템 (TO-BE)

#### 시스템 구조
```
                    ┌─────────────────────────────────────┐
                    │          External Clients           │
                    │        (Web, Mobile, Game)          │
                    └──────────┬──────────────┬───────────┘
                               │              │
              HTTP/REST        │              │  TCP/WebSocket
          (정보 요청, Stage 생성)              │  (실시간 통신)
                               │              │
           ┌───────────────────┘              └───────────────────┐
           │                                                      │
           ▼                                                      ▼
┌─────────────────────────────────┐          ┌─────────────────────────────────┐
│          Web Server             │          │          Play Server            │
│  (ASP.NET Core, Express, etc.)  │          │        (독립 프로세스)           │
│                                 │          │                                 │
│  ┌───────────────────────────┐  │          │  - Stage 관리                   │
│  │    API Server 모듈        │  │  NetMQ   │  - Actor 실행                   │
│  │  (PlayHouse.Api 라이브러리)│──┼─────────►│  - Client 연결 (TCP/WS)         │
│  │                           │  │ Router   │  - ISender로 API 요청 가능      │
│  │  - IApiSender (DI 주입)   │◄─┼──────────┤                                 │
│  │  - Stage 생성 요청        │  │          │                                 │
│  │  - 서버 목록 조회         │  │          │                                 │
│  └───────────────────────────┘  │          └─────────────────────────────────┘
│                                 │                        ▲
└─────────────────────────────────┘                        │
                                                           │ NetMQ
                                              ┌────────────┴────────────┐
                                              │                         │
                                              ▼                         ▼
                                   ┌─────────────────┐       ┌─────────────────┐
                                   │  Play Server 2  │◄─────►│  Play Server N  │
                                   │                 │ NetMQ │                 │
                                   └─────────────────┘       └─────────────────┘
```

#### 목표 플로우
1. **클라이언트** → **웹서버**: HTTP로 정보 요청 (로그인, 데이터 조회 등)
2. **웹서버** → **Play Server**: API 모듈의 `IApiSender`로 Stage 생성 요청
3. **웹서버** → **클라이언트**: Stage 정보 응답 (PlayServerNid, StageId 등)
4. **클라이언트** → **Play Server**: TCP/WebSocket 직접 연결
5. **클라이언트**: OnAuthenticate() → OnPostAuthenticate() → OnJoinStage()
6. **클라이언트-Play Server**: 실시간 통신
7. **Play Server** → **API Server**: 필요시 데이터 요청 (IApiSender 사용)

#### 목표 구성 요소

**API Server 모듈 (PlayHouse.Api)**
- **역할**: 웹서버에 통합되는 라이브러리 모듈
- **통합**: .NET Core DI로 `IApiSender` 주입
- **통신**: NetMQ (Play 서버와 통신)
- **기능**: Stage 생성/조회 요청, Play 서버 목록 관리

**Play Server (PlayHouse.Play)**
- **역할**: Stage/Actor 관리, 클라이언트 직접 연결
- **통신**: NetMQ (서버 간) + TCP/WebSocket (클라이언트)
- **기능**: Stage 실행, Actor 관리, 인증 처리
- **API 요청**: `IApiSender`로 API 서버에 데이터 요청 가능

---

## 2. 핵심 변경 사항

### 2.1 서버 분리

| 구분 | Play 서버 (현재) | Play 서버 (목표) | API 서버 (신규) |
|-----|----------------|----------------|----------------|
| HTTP API | ✅ 포함 (RoomController) | ❌ 제거 | ✅ 전담 |
| Stage/Actor 관리 | ✅ | ✅ | ❌ |
| 클라이언트 연결 | ✅ TCP/WebSocket | ✅ TCP/WebSocket | ❌ |
| 서버 간 통신 | ❌ | ✅ NetMQ Router | ✅ NetMQ Router |
| 상태 관리 | Stateful | Stateful | Stateless |

### 2.2 통신 방식 변경

#### 삭제 대상
- ❌ HTTP REST API (RoomController)
  - `/api/rooms/create`
  - `/api/rooms/get-or-create`
  - `/api/rooms/{stageId}`
  - `/api/rooms/{stageId}/join`
  - `/api/rooms/{stageId}/leave`

#### 추가 대상

**1. NetMQ 통신 모듈**
- Play Server: NetMQ Router 소켓 바인드 (서버 간 메시지 수신)
- API Server 모듈: NetMQ Router 소켓 (Play Server로 요청 전송)
- 참조 구현: `D:\project\kairos\playhouse\playhouse-net\PlayHouse\PlayHouse\Runtime\PlaySocket`

**2. API Server 인터페이스 (`IApiSender`)**
```csharp
// Web Server에서 DI로 주입받아 Play Server에 요청
public interface IApiSender : ISender
{
    // Stage 생성 요청 → Play Server가 Stage.OnCreate() 호출
    Task<CreateStageResult> CreateStage(string playNid, string stageType, long stageId, IPacket packet);

    // Stage 조회 또는 생성 요청
    Task<GetOrCreateStageResult> GetOrCreateStage(string playNid, string stageType, long stageId, IPacket createPacket, IPacket joinPacket);
}
```

**3. 서버 간 내부 메시지 (NetMQ로 전송)**
| 메시지 | 방향 | 용도 |
|--------|------|------|
| CreateStageReq/Res | API → Play | Stage 생성 |
| DestroyStageReq/Res | API → Play | Stage 삭제 |
| RouteToStageReq/Res | Play → Play | 다른 서버 Stage로 메시지 전달 |
| SendToApiReq/Res | Play → API | Play에서 API로 데이터 요청 |

> 📄 **상세 스펙**: [08-api-server.md](./08-api-server.md) 참조
>
> ⚠️ **중요**: 서버 간 Request-Reply 패턴 구현 시 [10-request-reply-mechanism.md](./10-request-reply-mechanism.md) 필수 참조
> - `MsgSeq`로 요청-응답 매칭
> - `RequestCache`로 진행 중 요청 추적
> - `RouteHeader.From`으로 응답 대상 식별

#### 유지 대상
- ✅ TCP/WebSocket (클라이언트-Play 서버)
- ✅ AuthenticateRequest/Response (프로토콜 변경)

### 2.3 인증 플로우 변경

#### 현재 (AS-IS)
```
1. Client → HTTP POST /api/rooms/create → Server
2. Server → RoomToken 생성 → Client
3. Client → TCP 연결 → Server
4. Client → AuthenticateRequest(RoomToken) → Server
5. Server → RoomToken 검증 → Actor 생성 → Stage 입장
6. Server → AuthenticateReply(Success) → Client
```

#### 목표 (TO-BE)
```
1. Client → TCP 연결 → Play Server
2. Client → AuthenticateRequest(credentials) → Play Server
3. Play Server → Actor.OnAuthenticate() 호출
4. Play Server → Actor.OnPostAuthenticate() 호출
5. Play Server → Stage.OnJoinStage(actor) 호출
6. Play Server → Stage.OnPostJoinStage(actor) 호출
7. Play Server → AuthenticateReply(Success) → Client
```

### 2.4 인터페이스 변경

**⚠️ 중요**: 모든 인터페이스 정의는 `new-request.md`를 기준으로 합니다.

#### IActor 인터페이스 변경

**현재 (AS-IS)**:
```csharp
public interface IActor : IAsyncDisposable
{
    IActorSender ActorSender { get; }
    bool IsConnected { get; }

    Task OnCreate();
    Task OnDestroy();
    Task OnAuthenticate(IPacket? authData);
}
```

**목표 (TO-BE)** - `new-request.md` 기준:
```csharp
public interface IActor
{
    IActorSender ActorSender { get; }

    Task OnCreate();
    Task OnDestroy();
    Task<bool> OnAuthenticate(IPacket authPacket);  // 🔄 반환값 변경: Task → Task<bool>
    Task OnPostAuthenticate();  // 🆕 추가
}
```

**변경 사항**:
| 항목 | AS-IS | TO-BE | 설명 |
|-----|-------|-------|------|
| `IAsyncDisposable` | 상속 | 제거 | 불필요 |
| `IsConnected` | 있음 | 제거 | 불필요 |
| `OnAuthenticate` | `Task` | `Task<bool>` | `false` 반환 시 연결 종료 |
| `OnPostAuthenticate` | 없음 | 추가 | API 서버에서 정보 로드 용도 |

#### IActorSender 인터페이스 변경

**현재 (AS-IS)**:
```csharp
public interface IActorSender : ISender
{
    long AccountId { get; }
    long SessionId { get; }
}
```

**목표 (TO-BE)** - `new-request.md` 기준:
```csharp
public interface IActorSender : ISender
{
    string AccountId { get; set; }   // 🔄 계정 ID (OnAuthenticate에서 설정 필수)
    void LeaveStage();               // 🆕 추가
    void SendToClient(IPacket packet); // 🆕 추가
}
```

**AccountId 설정 규칙**:
- `OnAuthenticate()` 성공 시 컨텐츠 개발자가 반드시 설정
- 인증 성공(`true` 반환) 시 `AccountId`가 빈 문자열이면 예외 발생 및 접속 끊김

#### IStage 인터페이스 변경

**현재 (AS-IS)**:
```csharp
public interface IStage : IAsyncDisposable
{
    IStageSender StageSender { get; init; }

    Task<(ushort errorCode, IPacket? reply)> OnCreate(IPacket packet);
    Task OnPostCreate();
    Task<(ushort errorCode, IPacket? reply)> OnJoinRoom(IActor actor, IPacket userInfo);
    Task OnPostJoinRoom(IActor actor);
    ValueTask OnLeaveRoom(IActor actor, LeaveReason reason);
    ValueTask OnActorConnectionChanged(IActor actor, bool isConnected, DisconnectReason? reason);
    ValueTask OnDispatch(IActor actor, IPacket packet);
}
```

**목표 (TO-BE)** - `new-request.md` 기준:
```csharp
public interface IStage
{
    IStageSender StageSender { get; }

    Task<(bool result, IPacket reply)> OnCreate(IPacket packet);  // 🔄 반환값 변경
    Task OnPostCreate();
    Task OnDestory();  // 🆕 추가 (원본 오타 유지)

    Task<bool> OnJoinStage(IActor actor);  // 🔄 OnJoinRoom → OnJoinStage, 매개변수/반환값 변경
    Task OnPostJoinStage(IActor actor);    // 🔄 OnPostJoinRoom → OnPostJoinStage
    // OnLeaveStage 제거: 퇴장은 컨텐츠에서 처리 후 actor.ActorSender.LeaveStage() 호출

    ValueTask OnConnectionChanged(IActor actor, bool isConnected);  // 🔄 매개변수 간소화

    Task OnDispatch(IActor actor, IPacket packet);  // 클라이언트 메시지
    Task OnDispatch(IPacket packet);                 // 🆕 서버 간 메시지
}
```

**변경 사항**:
| 항목 | AS-IS | TO-BE | 설명 |
|-----|-------|-------|------|
| `IAsyncDisposable` | 상속 | 제거 | 불필요 |
| `OnCreate` 반환 | `(ushort, IPacket?)` | `(bool, IPacket)` | bool로 성공/실패 표현 |
| `OnDestory` | 없음 | 추가 | Stage 종료 콜백 |
| `OnJoinRoom` | 있음 | `OnJoinStage`로 변경 | 이름 변경, 매개변수 간소화 |
| `OnPostJoinRoom` | 있음 | `OnPostJoinStage`로 변경 | 이름 변경 |
| `OnLeaveRoom` | 있음 | 제거 | 퇴장은 컨텐츠에서 `LeaveStage()` 호출로 처리 |
| `OnActorConnectionChanged` | 있음 | `OnConnectionChanged`로 간소화 | 매개변수 간소화 |
| `OnDispatch(packet)` | 없음 | 추가 | 서버 간 메시지 처리용 |

#### IStageSender 인터페이스 변경

**목표 (TO-BE)** - `new-request.md` 기준:
```csharp
public interface IStageSender : ISender  // 🆕 ISender 상속 추가
{
    long StageId { get; }        // 🔄 int → long
    string StageType { get; }

    long AddRepeatTimer(TimeSpan initialDelay, TimeSpan period, Func<Task> callback);
    long AddCountTimer(TimeSpan initialDelay, TimeSpan period, int count, Func<Task> callback);
    void CancelTimer(long timerId);
    bool HasTimer(long timerId);
    void CloseStage();
    void AsyncBlock(AsyncPreCallback preCallback, AsyncPostCallback? postCallback = null);
}
```

**중요**: IStageSender가 ISender를 상속받아 서버 간 통신 기능을 갖춤.

#### IPacket 인터페이스 변경

**목표 (TO-BE)** - `new-request.md` 기준:
```csharp
public interface IPacket : IDisposable
{
    string MsgId { get; }       // Protobuf 메시지 이름을 직접 사용
    IPayload Payload { get; }
}
```

**변경 사항**: `MsgSeq`, `StageId`, `ErrorCode` 필드 제거 (간소화)

---

## 3. 참조 시스템 코드 위치

참조 시스템: `D:\project\kairos\playhouse\playhouse-net\PlayHouse`

### 3.1 NetMQ 통신 계층 (그대로 복사)

| 영역 | 파일 경로 | 재사용 방법 |
|-----|---------|-----------|
| NetMQ 소켓 | `Runtime/PlaySocket/*.cs` | 전체 복사 |
| 메시지 구조 | `Runtime/Message/*.cs` | 전체 복사 |
| 통신 관리자 | `Runtime/XClientCommunicator.cs` | 전체 복사 (송신) |
| 통신 관리자 | `Runtime/XServerCommunicator.cs` | 전체 복사 (수신) |
| 서버 디스커버리 | `Runtime/XServerInfoCenter.cs` | 전체 복사 |
| 메시지 루프 | `Runtime/MessageLoop.cs` | 전체 복사 |

#### 핵심 파일 목록
```
PlayHouse/Runtime/
├── PlaySocket/
│   ├── IPlaySocket.cs              # 소켓 인터페이스
│   ├── NetMQPlaySocket.cs          # Router 소켓 구현
│   └── PlaySocketConfig.cs         # 소켓 설정
├── Message/
│   ├── RoutePacket.cs              # 라우팅 패킷
│   ├── RouteHeader.cs              # 패킷 헤더
│   ├── Payload.cs                  # 페이로드
│   └── FramePayload.cs             # Zero-Copy 페이로드
├── XClientCommunicator.cs          # 메시지 송신
├── XServerCommunicator.cs          # 메시지 수신
├── XServerInfoCenter.cs            # 서버 디스커버리
└── MessageLoop.cs                  # 송수신 스레드 관리
```

### 3.2 API 서버 인터페이스 (참조하여 새로 작성)

| 영역 | 참조 파일 경로 | 구현 방법 |
|-----|--------------|----------|
| API 인터페이스 | `Runtime/IApiSender.cs` | 참조하여 새로 작성 |
| API 컨트롤러 | `Runtime/IApiController.cs` | 참조하여 새로 작성 |
| 핸들러 등록 | `Runtime/IHandlerRegister.cs` | 참조하여 새로 작성 |

### 3.3 시스템 관리 (참조하여 새로 작성)

| 영역 | 참조 파일 경로 | 구현 방법 |
|-----|--------------|----------|
| 서버 정보 | `Runtime/IServerInfo.cs` | 참조하여 새로 작성 |
| 시스템 패널 | `Runtime/ISystemPanel.cs` | 참조하여 새로 작성 |
| 시스템 컨트롤러 | `Runtime/ISystemController.cs` | 참조하여 새로 작성 |

---

## 4. 폴더 구조 변경

### 4.1 현재 폴더 구조 (AS-IS)

```
D:\project\ulalax\playhouse-net\
├── src\PlayHouse\
│   ├── Abstractions\              # 핵심 인터페이스
│   │   ├── IActor.cs
│   │   ├── IStage.cs
│   │   ├── IPacket.cs
│   │   └── ISender.cs
│   ├── Core\
│   │   ├── Stage\
│   │   │   ├── StagePool.cs      # Stage 저장소
│   │   │   ├── StageFactory.cs   # Stage 생성/삭제
│   │   │   ├── ActorPool.cs      # Actor 저장소
│   │   │   └── ActorContext.cs
│   │   ├── Session\
│   │   │   ├── SessionManager.cs # 세션 관리
│   │   │   └── RoomTokenManager.cs # 토큰 관리
│   │   └── Messaging\
│   │       ├── PacketDispatcher.cs
│   │       └── MessageHandler.cs
│   └── Infrastructure\
│       ├── Http\
│       │   ├── PlayHouseServer.cs     # ✅ TCP/WebSocket 서버
│       │   ├── RoomController.cs      # ❌ 삭제 대상 (API 서버로 이동)
│       │   └── HealthController.cs    # ✅ 유지
│       ├── Transport\
│       │   ├── Tcp\
│       │   │   ├── TcpServer.cs
│       │   │   └── TcpSession.cs
│       │   └── WebSocket\
│       │       ├── WebSocketServer.cs
│       │       └── WebSocketSession.cs
│       └── Serialization\
│           └── PacketSerializer.cs
└── tests\
    ├── PlayHouse.Tests.Unit\
    ├── PlayHouse.Tests.Integration\
    └── PlayHouse.Tests.E2E\
```

### 4.2 목표 폴더 구조 (TO-BE)

**⚠️ 중요**: 별도 프로젝트로 분리하지 않고, 기존 PlayHouse 프로젝트에 `/Play`, `/Api` 모듈로 추가합니다.
Bootstrap 패턴으로 Play 서버와 API 서버를 각각 생성할 수 있도록 제공합니다.

```
D:\project\ulalax\playhouse-net\
├── src\PlayHouse\
│   │
│   ├── Abstractions\                    # 핵심 인터페이스 (변경)
│   │   ├── IActor.cs                    # ⚠️ 변경 (OnAuthenticate, OnPostAuthenticate 추가)
│   │   ├── IStage.cs                    # ⚠️ 변경 (OnJoinStage, OnDispatch 오버로드)
│   │   ├── IPacket.cs                   # ⚠️ 변경 (MsgId: string)
│   │   ├── ISender.cs                   # ⚠️ 변경 (SendToApi, SendToStage 추가)
│   │   ├── IActorSender.cs              # 🆕 추가
│   │   ├── IStageSender.cs              # 🆕 추가
│   │   ├── IApiSender.cs                # 🆕 추가
│   │   └── IApiController.cs            # 🆕 추가
│   │
│   ├── Runtime\                         # 🆕 NetMQ 통신 계층 (참조 시스템에서 복사)
│   │   ├── Communicator.cs
│   │   ├── XServerCommunicator.cs
│   │   ├── XClientCommunicator.cs
│   │   ├── MessageLoop.cs
│   │   ├── RequestCache.cs
│   │   ├── PlaySocket\
│   │   │   ├── IPlaySocket.cs
│   │   │   ├── NetMQPlaySocket.cs
│   │   │   └── PlaySocketConfig.cs
│   │   └── Message\
│   │       ├── RoutePacket.cs
│   │       ├── RouteHeader.cs
│   │       └── FramePayload.cs
│   │
│   ├── Play\                            # 🆕 Play 서버 모듈
│   │   ├── PlayServerBootstrap.cs       # Play 서버 부트스트랩
│   │   ├── PlayServerOption.cs          # Play 서버 설정
│   │   ├── PlayServer.cs                # Play 서버 인스턴스
│   │   ├── Stage\
│   │   │   ├── StageManager.cs          # Stage 생명주기 관리
│   │   │   ├── StageContext.cs          # Stage 실행 컨텍스트
│   │   │   └── StageSender.cs           # IStageSender 구현
│   │   ├── Actor\
│   │   │   ├── ActorManager.cs          # Actor 생명주기 관리
│   │   │   ├── ActorContext.cs          # Actor 실행 컨텍스트
│   │   │   └── ActorSender.cs           # IActorSender 구현
│   │   └── Client\
│   │       ├── ClientConnectionManager.cs
│   │       ├── ClientSession.cs
│   │       └── AuthenticationHandler.cs
│   │
│   ├── Api\                             # 🆕 API 서버 모듈
│   │   ├── ApiServerBootstrap.cs        # API 서버 부트스트랩
│   │   ├── ApiServerOption.cs           # API 서버 설정
│   │   ├── ApiServer.cs                 # API 서버 인스턴스
│   │   ├── ApiSender.cs                 # IApiSender 구현
│   │   ├── ApiDispatcher.cs             # 메시지 핸들러 디스패처
│   │   └── HandlerRegister.cs           # IHandlerRegister 구현
│   │
│   ├── Core\                            # ✅ 기존 코드 유지
│   │   ├── Stage\
│   │   ├── Session\                     # ⚠️ 일부 변경 (RoomTokenManager 제거)
│   │   └── Messaging\
│   │
│   └── Infrastructure\                  # ✅ 기존 코드 유지
│       ├── Transport\
│       │   ├── Tcp\
│       │   └── WebSocket\
│       └── Serialization\
│
└── tests\
    ├── PlayHouse.Tests.Unit\
    ├── PlayHouse.Tests.Integration\
    ├── PlayHouse.Tests.E2E\
    └── PlayHouse.Tests.NetMQ\           # 🆕 NetMQ 통신 테스트
```

### 4.3 폴더 변경 요약

| 상태 | 경로 | 설명 |
|-----|------|-----|
| 🆕 추가 | `Core/Runtime/` | NetMQ 통신 계층 (참조 시스템에서 복사) |
| 🆕 추가 | `Core/Play/` | Play 서버 모듈 + Bootstrap |
| 🆕 추가 | `Core/Api/` | API 서버 모듈 + Bootstrap |
| 📦 이동 | `Core/Stage/` → `Core/Play/Stage/` | Stage를 Play 하위로 이동 |
| 📦 이동 | `Core/Session/` → `Core/Play/Session/` | Session을 Play 하위로 이동 |
| ❌ 삭제 | `Infrastructure/Http/RoomController.cs` | REST API 제거 |
| ⚠️ 변경 | `Abstractions/*.cs` | 인터페이스 변경 (new-request.md 기준) |
| ✅ 유지 | `Core/Messaging/`, `Core/Timer/` | 기존 코드 유지 |
| ✅ 유지 | `Infrastructure/Transport/` | TCP/WebSocket 유지 |

### 4.4 Bootstrap 사용 예시

> **설계 원칙**: Play 서버와 API 서버 모두 .NET Core DI 컨테이너에 서비스로 등록하여 사용

**Play 서버** (.NET Core 서비스로 등록):
```csharp
var builder = Host.CreateApplicationBuilder(args);

// Play 서버 Bootstrap 및 DI 등록
var playServer = new PlayServerBootstrap()
    .Configure(options =>
    {
        options.ServiceId = 1;
        options.ServerId = 1;
        options.BindEndpoint = "tcp://0.0.0.0:5000";      // NetMQ 서버 간 통신
        options.ClientEndpoint = "tcp://0.0.0.0:6000";    // 클라이언트 TCP
        options.WebSocketEndpoint = "ws://0.0.0.0:6001";  // 클라이언트 WebSocket (옵션)
    })
    .UseStage<GameRoomStage>("GameRoom")
    .UseStage<LobbyStage>("Lobby")
    .UseActor<PlayerActor>()
    .Build();

// DI 컨테이너에 등록
builder.Services.AddSingleton(playServer);
builder.Services.AddSingleton<IStageSender>(playServer.StageSender);
builder.Services.AddHostedService<PlayServerHostedService>();  // IHostedService로 시작/종료 관리

var host = builder.Build();
await host.RunAsync();
```

**API 서버** (.NET Core 웹서버에 서비스로 등록):
```csharp
var builder = WebApplication.CreateBuilder(args);

// API 서버 Bootstrap 및 DI 등록
var apiServer = new ApiServerBootstrap()
    .Configure(options =>
    {
        options.ServiceId = 2;
        options.ServerId = 1;
        options.BindEndpoint = "tcp://0.0.0.0:5100";      // NetMQ 서버 간 통신
    })
    .UseController<GameApiController>()
    .Build();

// DI 컨테이너에 등록
builder.Services.AddSingleton(apiServer);
builder.Services.AddSingleton<IApiSender>(apiServer.ApiSender);
builder.Services.AddHostedService<ApiServerHostedService>();  // IHostedService로 시작/종료 관리

var app = builder.Build();

// HTTP API 엔드포인트 (Web Server가 제공)
app.MapPost("/api/rooms/create", async (CreateRoomRequest req, IApiSender sender) =>
{
    // IApiSender를 통해 Play 서버에 Stage 생성 요청
    var result = await sender.CreateStage("1:1", "GameRoom", 0, req.ToPacket());
    return Results.Ok(result);
});

app.Run();
```

**IHostedService 구현 예시**:
```csharp
// Play 서버용
public class PlayServerHostedService(PlayServer playServer) : IHostedService
{
    public async Task StartAsync(CancellationToken ct) => await playServer.StartAsync();
    public async Task StopAsync(CancellationToken ct) => await playServer.StopAsync();
}

// API 서버용
public class ApiServerHostedService(ApiServer apiServer) : IHostedService
{
    public async Task StartAsync(CancellationToken ct) => await apiServer.StartAsync();
    public async Task StopAsync(CancellationToken ct) => await apiServer.StopAsync();
}
```

---

## 5. 인터페이스 변경

**⚠️ 중요**: 모든 인터페이스 정의는 `new-request.md`를 기준으로 합니다. 상세 정의는 `06-interfaces.md`를 참조하세요.

### 5.1 IActor 인터페이스 (변경 필요)

**현재 구현**: `src/PlayHouse/Abstractions/IActor.cs`

**목표 인터페이스** (`new-request.md` 기준):
```csharp
public interface IActor
{
    IActorSender ActorSender { get; }

    Task OnCreate();
    Task OnDestroy();
    Task<bool> OnAuthenticate(IPacket authPacket);  // 반환값 변경
    Task OnPostAuthenticate();  // 🆕 추가
}
```

**주요 변경**:
- `OnAuthenticate`: `Task` → `Task<bool>` (false 반환 시 연결 종료)
- `OnPostAuthenticate`: 신규 추가 (API 서버에서 정보 로드 용도)
- `IAsyncDisposable`: 상속 제거
- `IsConnected`: 프로퍼티 제거

### 5.2 IStage 인터페이스 (변경 필요)

**현재 구현**: `src/PlayHouse/Abstractions/IStage.cs`

**목표 인터페이스** (`new-request.md` 기준):
```csharp
public interface IStage
{
    IStageSender StageSender { get; }

    Task<(bool result, IPacket reply)> OnCreate(IPacket packet);
    Task OnPostCreate();
    Task OnDestory();

    Task<bool> OnJoinStage(IActor actor);  // OnJoinRoom → OnJoinStage
    Task OnPostJoinStage(IActor actor);    // OnPostJoinRoom → OnPostJoinStage

    ValueTask OnConnectionChanged(IActor actor, bool isConnected);

    Task OnDispatch(IActor actor, IPacket packet);  // 클라이언트 메시지
    Task OnDispatch(IPacket packet);                 // 🆕 서버 간 메시지
}
```

**주요 변경**:
- `OnCreate` 반환값: `(ushort errorCode, IPacket?)` → `(bool result, IPacket reply)`
- `OnJoinRoom` → `OnJoinStage`: 메서드 이름 변경, 매개변수 간소화 (IPacket 제거)
- `OnLeaveRoom` 제거: 퇴장은 컨텐츠에서 처리 후 `actor.ActorSender.LeaveStage()` 호출
- `OnDispatch(IPacket)`: 서버 간 메시지 처리용 오버로드 추가

### 5.3 Play 서버 Bootstrap 패턴

#### 현재 (AS-IS)
```csharp
// src/PlayHouse/Infrastructure/Http/PlayHouseServer.cs
public sealed class PlayHouseServer : IHostedService, IAsyncDisposable
{
    // HTTP + TCP/WebSocket 서버
    private TcpServer? _tcpServer;
    private WebSocketServer? _webSocketServer;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        // TCP 서버 시작
        _tcpServer = new TcpServer(...);
        await _tcpServer.StartAsync(endpoint);

        // WebSocket 서버 시작
        _webSocketServer = new WebSocketServer(...);
    }
}
```

#### 목표 (TO-BE) - Bootstrap 패턴
```csharp
// src/PlayHouse/Core/Play/PlayServerBootstrap.cs
public sealed class PlayServerBootstrap
{
    private readonly PlayServerOption _options = new();
    private readonly Dictionary<string, Type> _stageTypes = new();
    private Type? _actorType;

    public PlayServerBootstrap Configure(Action<PlayServerOption> configure)
    {
        configure(_options);
        return this;
    }

    public PlayServerBootstrap UseStage<TStage>(string stageType) where TStage : IStage
    {
        _stageTypes[stageType] = typeof(TStage);
        return this;
    }

    public PlayServerBootstrap UseActor<TActor>() where TActor : IActor
    {
        _actorType = typeof(TActor);
        return this;
    }

    public PlayServer Build()
    {
        return new PlayServer(_options, _stageTypes, _actorType);
    }
}

// 사용 예시
var playServer = new PlayServerBootstrap()
    .Configure(options =>
    {
        options.ServiceId = 1;
        options.ServerId = 1;
        options.BindEndpoint = "tcp://0.0.0.0:5000";
        options.ClientEndpoint = "tcp://0.0.0.0:6000";
    })
    .UseStage<GameRoomStage>("GameRoom")
    .UseActor<PlayerActor>()
    .Build();

await playServer.StartAsync();
```

### 5.4 API 서버 Bootstrap 패턴

#### 현재 (AS-IS)
```csharp
// src/PlayHouse/Infrastructure/Http/RoomController.cs
[ApiController]
[Route("api/rooms")]
public sealed class RoomController : ControllerBase
{
    [HttpPost("create")]
    public async Task<IActionResult> CreateRoom([FromBody] CreateRoomRequest request)
    {
        // Stage 직접 생성
        var (stageContext, errorCode, _) = await _stageFactory.CreateStageAsync(...);
        var roomToken = _tokenManager.GenerateToken(stageContext.StageId, request.Nickname);
        return Ok(new CreateRoomResponse { RoomToken = roomToken, ... });
    }
}
```

#### 목표 (TO-BE) - Bootstrap 패턴 + .NET Core 웹서버 연동
```csharp
// API 서버는 PlayHouse 모듈 + .NET Core 웹서버 조합으로 사용
var builder = WebApplication.CreateBuilder(args);

// PlayHouse API 서버 Bootstrap
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

var app = builder.Build();
await apiServer.StartAsync();

// ASP.NET Core Minimal API에서 IApiSender 사용
app.MapPost("/api/rooms/create", async (CreateRoomRequest req, IApiSender sender) =>
{
    var result = await sender.CreateStage("1:1", "GameRoom", 0, req.ToPacket());
    return result.Result
        ? Results.Ok(new CreateRoomResponse { RoomId = result.StageId })
        : Results.BadRequest(new { Error = "Stage creation failed" });
});

app.Run();
```

**참고**: RoomController는 삭제하고, .NET Core 웹서버의 Minimal API 또는 Controller에서 `IApiSender`를 주입받아 사용합니다.

---

## 6. 구현 체크리스트

### Phase 1: NetMQ 통신 계층 구현

- [ ] **1.1 참조 시스템에서 파일 복사**
  - [ ] `D:\project\kairos\playhouse\playhouse-net\PlayHouse\Runtime\PlaySocket\*.cs` → `src\PlayHouse\Runtime\PlaySocket\`
  - [ ] `D:\project\kairos\playhouse\playhouse-net\PlayHouse\Runtime\Message\*.cs` → `src\PlayHouse\Runtime\Message\`
  - [ ] `D:\project\kairos\playhouse\playhouse-net\PlayHouse\Runtime\XClientCommunicator.cs` → `src\PlayHouse\Runtime\`
  - [ ] `D:\project\kairos\playhouse\playhouse-net\PlayHouse\Runtime\XServerCommunicator.cs` → `src\PlayHouse\Runtime\`
  - [ ] `D:\project\kairos\playhouse\playhouse-net\PlayHouse\Runtime\XServerInfoCenter.cs` → `src\PlayHouse\Runtime\`
  - [ ] `D:\project\kairos\playhouse\playhouse-net\PlayHouse\Runtime\MessageLoop.cs` → `src\PlayHouse\Runtime\`

- [ ] **1.2 NetMQ NuGet 패키지 추가**
  ```bash
  dotnet add src/PlayHouse/PlayHouse.csproj package NetMQ
  ```

- [ ] **1.3 네임스페이스 변경**
  - 복사한 파일의 네임스페이스를 `PlayHouse.Runtime`으로 변경

- [ ] **1.4 단위 테스트 작성**
  - [ ] NetMQPlaySocket 테스트
  - [ ] RoutePacket 직렬화/역직렬화 테스트
  - [ ] XClientCommunicator/XServerCommunicator 테스트

### Phase 2: Play 서버 모듈 구현

- [ ] **2.1 디렉토리 구조 재배치**
  - [ ] `Core/Play/` 디렉토리 생성
  - [ ] `Core/Stage/` → `Core/Play/Stage/` 이동
  - [ ] `Core/Session/` → `Core/Play/Session/` 이동
  - [ ] 네임스페이스 변경 (`PlayHouse.Core.Play.*`)

- [ ] **2.2 PlayServerBootstrap 구현**
  - [ ] `Core/Play/PlayServerBootstrap.cs` 생성
  - [ ] `Core/Play/PlayServerOption.cs` 설정 클래스
  - [ ] NetMQ Router 소켓 통합
  - [ ] TCP/WebSocket 리스너 통합

- [ ] **2.3 Actor/Stage 관리자 구현**
  - [ ] `Core/Play/Actor/ActorManager.cs` 생성
  - [ ] `Core/Play/Actor/ActorSender.cs` (IActorSender 구현)
  - [ ] `Core/Play/Stage/StageManager.cs` 확장
  - [ ] `Core/Play/Stage/StageSender.cs` (IStageSender 구현)

- [ ] **2.4 인증 플로우 변경**
  - [ ] `HandleAuthenticateRequest()` 수정
    - RoomToken 검증 제거
    - Actor.OnAuthenticate() → OnPostAuthenticate() 호출
  - [ ] Stage.OnJoinStage() → OnPostJoinStage() 호출

### Phase 3: API 서버 모듈 구현

- [ ] **3.1 디렉토리 생성**
  - [ ] `Core/Api/` 디렉토리 생성
  - [ ] 네임스페이스: `PlayHouse.Core.Api`

- [ ] **3.2 ApiServerBootstrap 구현**
  - [ ] `Core/Api/ApiServerBootstrap.cs` 생성
  - [ ] `Core/Api/ApiServerOption.cs` 설정 클래스
  - [ ] NetMQ Router 소켓 (Play 서버와 통신)

- [ ] **3.3 ApiSender 구현**
  - [ ] `Core/Api/ApiSender.cs` (IApiSender 구현)
  - [ ] `CreateStage()`, `GetOrCreateStage()` 메서드
  - [ ] Request-Response 패턴 (MsgSeq 매칭)

- [ ] **3.4 Handler 시스템 구현**
  - [ ] `Core/Api/ApiDispatcher.cs` 메시지 라우팅
  - [ ] `Core/Api/HandlerRegister.cs` (IHandlerRegister 구현)
  - [ ] Play 서버로부터 오는 요청 처리

- [ ] **3.5 서버 디스커버리**
  - [ ] `ISystemPanel` 구현 (Play 서버 목록 관리)
  - [ ] 로드 밸런싱 로직

### Phase 4: 기존 코드 정리

- [ ] **4.1 HTTP API 제거**
  - [ ] `Infrastructure/Http/RoomController.cs` 삭제
  - [ ] 관련 DTO 클래스 제거

- [ ] **4.2 토큰 인증 제거**
  - [ ] RoomTokenManager 제거
  - [ ] 토큰 기반 인증 로직 제거

- [ ] **4.3 의존성 정리**
  - [ ] 불필요한 ASP.NET Core 의존성 검토
  - [ ] 네임스페이스 정리

### Phase 5: 통합 테스트 및 검증

- [ ] **5.1 E2E 테스트 작성**
  - [ ] API 서버 → Play 서버 → Client 전체 플로우
  - [ ] NetMQ 통신 검증
  - [ ] 인증 플로우 검증

- [ ] **5.2 기존 테스트 수정**
  - [ ] `PlayHouse.Tests.E2E` 수정
    - HTTP API 호출 제거
    - 직접 TCP 연결로 변경
  - [ ] `PlayHouse.Tests.Integration` 수정

- [ ] **5.3 성능 테스트**
  - [ ] NetMQ 메시지 처리량 측정
  - [ ] 서버 간 통신 지연 시간 측정

- [ ] **5.4 문서 업데이트**
  - [ ] README.md 업데이트
  - [ ] API 문서 작성
  - [ ] 배포 가이드 작성

### Phase 6: 배포 및 운영

- [ ] **6.1 설정 파일**
  - [ ] Play 서버 appsettings.json
  - [ ] API 서버 appsettings.json
  - [ ] NetMQ 설정 (NID, Bind/Connect 주소)

- [ ] **6.2 Docker 이미지**
  - [ ] Play 서버 Dockerfile
  - [ ] API 서버 Dockerfile
  - [ ] Docker Compose 설정

- [ ] **6.3 Kubernetes 배포**
  - [ ] Play 서버 Deployment
  - [ ] API 서버 Deployment
  - [ ] Service 정의
  - [ ] Ingress 설정

---

## 7. 구현 우선순위

### 높음 (High Priority)
1. NetMQ 통신 계층 구현 (Phase 1)
2. Play 서버 분리 (Phase 2)
3. 인증 플로우 변경 (Phase 2.4)

### 중간 (Medium Priority)
4. API 서버 구현 (Phase 3)
5. HTTP API 제거 (Phase 4)

### 낮음 (Low Priority)
6. 통합 테스트 (Phase 5)
7. 배포 및 운영 (Phase 6)

---

## 8. 주의 사항

### 8.1 호환성
- 기존 클라이언트와의 호환성 유지 필요 시, Feature Flag 사용
- 점진적 마이그레이션 전략 고려

### 8.2 테스트
- 각 Phase 완료 후 반드시 테스트 실행
- 기존 기능 회귀 테스트 필수

### 8.3 성능
- NetMQ 메시지 처리량 모니터링
- 서버 간 통신 지연 시간 측정

### 8.4 보안
- NetMQ CurveZMQ 암호화 고려
- 서버 간 상호 인증 구현

---

## 9. 참고 자료

- [NetMQ Documentation](https://netmq.readthedocs.io/)
- [ZeroMQ Guide](https://zguide.zeromq.org/)
- 참조 시스템: `D:\project\kairos\playhouse\playhouse-net\PlayHouse`

---

**문서 버전**: 2.0
**작성일**: 2025-12-10
**작성자**: Architecture Team
**상태**: Draft
