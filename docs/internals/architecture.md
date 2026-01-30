# PlayHouse-NET 아키텍처

## 1. PlayHouse-NET 프레임워크 소개

### 1.1 프레임워크 핵심 기능

PlayHouse-NET 프레임워크는 다음 기능을 제공합니다:

- **메시지 기반 액터 모델**: Stage/Actor 패턴을 통한 동시성 제어
- **다중 프로토콜 지원**: TCP, WebSocket, HTTPS 동시 지원
- **비동기 메시지 처리**: Lock-Free 큐 기반 메시지 파이프라인
- **타이머 시스템**: Stage별 타이머 관리 및 콜백
- **세션 관리**: 클라이언트 연결 및 인증 상태 관리
- **HTTP API 통합**: ASP.NET Core 기반 REST API와 소켓 서버 통합

### 1.2 기술 스택

#### 런타임 및 언어

| 구분 | 기술 | 버전 | 용도 |
|------|------|------|------|
| Runtime | .NET | 8.0 / 9.0 / 10.0 (멀티 타겟) | 서버 런타임 |
| Language | C# | 12.0+ | 주 개발 언어 |
| Language Server | N/A | - | IDE 지원 (Visual Studio, Rider, VSCode) |

#### 프레임워크 및 라이브러리

| 구분 | 기술 | 용도 | 레이어 |
|------|------|------|--------|
| Web Framework | ASP.NET Core | HTTP API, WebSocket 호스팅 | Infrastructure |
| Web Server | Kestrel | 고성능 HTTP/HTTPS 서버 | Infrastructure |
| DI Container | Microsoft.Extensions.DependencyInjection | 의존성 주입 | 전체 |
| Configuration | Microsoft.Extensions.Configuration | 설정 관리 | 전체 |
| Logging | Microsoft.Extensions.Logging | 로깅 추상화 | 전체 |
| Options | Microsoft.Extensions.Options | 옵션 패턴 | 전체 |

#### 네트워크 및 I/O

| 구분 | 기술 | 용도 | 레이어 |
|------|------|------|--------|
| TCP | System.Net.Sockets | TCP 소켓 통신 | Infrastructure |
| WebSocket | System.Net.WebSockets | WebSocket 통신 | Infrastructure |
| I/O Pipeline | System.IO.Pipelines | 고성능 I/O 처리, 백프레셔 | Infrastructure |
| Memory | System.Buffers | ArrayPool, 메모리 효율화 | Infrastructure |
| TLS/SSL | System.Net.Security | 암호화 통신 | Infrastructure |

#### 직렬화 및 압축[11-connector-refactoring-plan.md](../specs2/11-connector-refactoring-plan.md)

| 구분 | 기술 | 버전 | 용도 | 레이어 |
|------|------|------|------|--------|
| Binary Serialization | Google.Protobuf | 3.x | 패킷 직렬화 | Infrastructure |
| JSON Serialization | System.Text.Json | Built-in | HTTP API 직렬화 | Infrastructure |
| Compression | K4os.Compression.LZ4 | 1.3.x | 패킷 압축 | Infrastructure |

#### 동시성 및 비동기

| 구분 | 기술 | 용도 | 레이어 |
|------|------|------|--------|
| Concurrent Collections | System.Collections.Concurrent | Lock-Free 큐 (ConcurrentQueue) | Core |
| Threading | System.Threading | Timer, ThreadPool | Core |
| Async/Await | Task, ValueTask | 비동기 처리 | 전체 |
| Channels | System.Threading.Channels | 생산자-소비자 패턴 | Core |

#### 관측성 (Observability)

| 구분 | 기술 | 용도 | 레이어 |
|------|------|------|--------|
| Metrics | System.Diagnostics.Metrics | 메트릭 수집 (.NET 8+) | Core |
| Tracing | System.Diagnostics.Activity | 분산 추적 | Core |
| OpenTelemetry | OpenTelemetry.Extensions.Hosting | 메트릭/추적 내보내기 | Infrastructure |
| Health Checks | Microsoft.Extensions.Diagnostics.HealthChecks | 헬스 체크 | Infrastructure |

#### 테스트

| 구분 | 기술 | 용도 | 프로젝트 |
|------|------|------|----------|
| Test Framework | xUnit | 테스트 프레임워크 | Tests.* |
| Assertions | FluentAssertions | 가독성 높은 단언문 | Tests.* |
| Mocking | NSubstitute | Mock/Stub 생성 | Tests.Unit |
| Test Server | Microsoft.AspNetCore.TestHost | HTTP 통합 테스트 | Tests.Integration |
| Code Coverage | Coverlet | 커버리지 측정 | Tests.* |

#### 빌드 및 배포

| 구분 | 기술 | 용도 |
|------|------|------|
| Build | dotnet CLI | 빌드, 테스트, 패키징 |
| Package Manager | NuGet | 패키지 관리 |
| Container | Docker | 컨테이너 배포 |
| Base Image | mcr.microsoft.com/dotnet/aspnet | 런타임 이미지 |
| CI/CD | GitHub Actions | 지속적 통합/배포 |

#### 개발 도구

| 구분 | 기술 | 용도 |
|------|------|------|
| IDE | Visual Studio 2022 / JetBrains Rider | 개발 환경 |
| Code Analysis | .NET Analyzers | 정적 분석 |
| Formatting | dotnet format | 코드 포맷팅 |
| API Documentation | Swagger/OpenAPI | HTTP API 문서화 |

#### 외부 의존성 요약

```xml
<!-- PlayHouse.csproj - 주요 패키지 -->
<PackageReference Include="Google.Protobuf" Version="3.28.*" />
<PackageReference Include="K4os.Compression.LZ4" Version="1.3.*" />
<PackageReference Include="OpenTelemetry.Extensions.Hosting" Version="1.9.*" />
<PackageReference Include="OpenTelemetry.Exporter.Prometheus.AspNetCore" Version="1.9.*-*" />

<!-- PlayHouse.Tests.* - 테스트 패키지 -->
<PackageReference Include="xunit" Version="2.9.*" />
<PackageReference Include="FluentAssertions" Version="6.12.*" />
<PackageReference Include="NSubstitute" Version="5.1.*" />
<PackageReference Include="coverlet.collector" Version="6.0.*" />
```

#### 버전 정책

- **.NET LTS 우선**: .NET 8.0 (LTS), .NET 10.0 (LTS 예정) 우선 지원
- **최신 C# 기능 활용**: required members, file-scoped types, primary constructors
- **NuGet 패키지**: SemVer 준수, 보안 업데이트 즉시 적용
- **Breaking Changes**: Major 버전에서만 허용

## 2. 프레임워크 아키텍처

### 2.1 Clean Architecture 기반 레이어 구조

PlayHouse-NET 프레임워크는 Clean Architecture 원칙에 따라 설계되었습니다.

```
┌─────────────────────────────────────────────────────────┐
│              의존성 방향 (Dependency Direction)          │
├─────────────────────────────────────────────────────────┤
│                                                         │
│          Application (서버 개발자 코드)                  │
│                       │                                 │
│                       ▼                                 │
│              ┌─────────────────┐                        │
│              │  Abstractions   │  ← 중심 (Domain Layer)  │
│              │    (Domain)     │     의존성 없음         │
│              └─────────────────┘                        │
│                   ▲         ▲                           │
│                   │         │                           │
│              ┌────┴────┐ ┌──┴──────────┐               │
│              │  Core   │ │Infrastructure│               │
│              │ Engine  │ │    Layer     │               │
│              └─────────┘ └──────────────┘               │
│                                                         │
└─────────────────────────────────────────────────────────┘
```

**의존성 규칙** (architecture-guide.md 기준):

```
Core Engine → Abstractions ← Infrastructure
              (Domain은 아무것도 의존하지 않음)
```

### 2.2 프레임워크 레이어별 역할

#### Abstractions Layer (Domain Layer)

**역할**: 프레임워크의 핵심 추상화 및 계약 정의

**특징**:
- 외부 의존성 없음 (순수 C# 인터페이스와 값 객체만)
- 프레임워크의 핵심 개념 정의 (IStage, IActor, ILink, IPacket 등)
- 도메인 모델과 에러 코드 정의

**주요 컴포넌트**:
- `IStage`: Stage 인터페이스
- `IActor`: Actor 인터페이스
- `ILink`: 메시지 전송 인터페이스
- `IPacket`: 패킷 추상화
- `RoutePacket`: 라우팅 정보를 포함한 값 객체
- `ErrorCode`: 프레임워크 에러 코드 정의

#### Core Engine Layer

**역할**: 프레임워크의 핵심 비즈니스 로직

**특징**:
- Abstractions Layer만 의존
- 인프라 구현 방식에 독립적
- Stage/Actor 생명주기 관리
- 메시지 디스패칭 및 큐 관리

**주요 컴포넌트**:
- **Bootstrap**: 시스템 초기화 및 구성 (PlayHouseBootstrap, Builder, Registry)
- **Message Pipeline**: Dispatcher, Handler, Message Queue
- **Stage Management**: Stage Pool, Actor Pool, Stage Factory
- **Support Services**: Timer Manager, Request Cache, Session Manager

#### Infrastructure Layer

**역할**: 외부 시스템 연동 및 기술적 구현

**특징**:
- Core Engine과 Abstractions에 의존
- 외부 시스템별로 하위 디렉토리 분리
- 전송, 직렬화, 압축 등 기술적 관심사 처리

**주요 컴포넌트**:
- **Transport**: TCP, WebSocket, HTTPS 서버
- **Serialization**: Protobuf, JSON 직렬화
- **Compression**: LZ4 압축
- **HTTP**: ASP.NET Core 통합

## 3. 프레임워크 프로젝트 구조

### 3.1 디렉토리 구조

```
playhouse-net/
├── src/
│   └── PlayHouse/                 # 서버 프레임워크
│       ├── Abstractions/          # Domain Layer (의존성 없음)
│       │   ├── IStage.cs
│       │   ├── IActor.cs
│       │   ├── ILink.cs
│       │   ├── IPacket.cs
│       │   ├── RoutePacket.cs
│       │   └── ErrorCode.cs
│       │
│       ├── Core/                  # Core Engine Layer
│       │   ├── Bootstrap/        # 시스템 초기화
│       │   │   ├── PlayHouseBootstrap.cs        # 진입점
│       │   │   ├── PlayHouseBootstrapBuilder.cs # Fluent API
│       │   │   ├── IPlayHouseHost.cs            # 호스트 인터페이스
│       │   │   └── PlayHouseHostImpl.cs         # 호스트 구현
│       │   ├── Stage/
│       │   │   ├── StagePool.cs
│       │   │   ├── StageFactory.cs
│       │   │   ├── StageTypeRegistry.cs         # 타입 레지스트리
│       │   │   └── ActorPool.cs
│       │   ├── Messaging/
│       │   │   ├── Dispatcher.cs
│       │   │   ├── MessageQueue.cs
│       │   │   └── Handler.cs
│       │   ├── Timer/
│       │   │   └── TimerManager.cs
│       │   └── Session/
│       │       ├── SessionManager.cs
│       │       └── RequestCache.cs
│       │
│       ├── Infrastructure/        # Infrastructure Layer
│       │   ├── Transport/
│       │   │   ├── Tcp/
│       │   │   ├── WebSocket/
│       │   │   └── Https/
│       │   ├── Serialization/
│       │   │   ├── Protobuf/
│       │   │   └── Json/
│       │   ├── Compression/
│       │   │   └── Lz4/
│       │   └── Http/
│       │       └── AspNetCore/
│       │
│       └── PlayHouse.csproj
│
├── connector/
│   └── PlayHouse.Connector/       # 클라이언트 라이브러리 (테스트용)
│       ├── IPlayHouseClient.cs    # 메인 클라이언트 인터페이스
│       ├── PlayHouseClient.cs     # 클라이언트 구현
│       ├── PlayHouseClientOptions.cs
│       ├── Connection/
│       │   ├── IConnection.cs
│       │   ├── TcpConnection.cs
│       │   └── WebSocketConnection.cs
│       ├── Protocol/
│       │   ├── PacketEncoder.cs
│       │   ├── PacketDecoder.cs
│       │   └── RequestTracker.cs
│       ├── Events/
│       │   ├── ConnectionEventArgs.cs
│       │   ├── MessageEventArgs.cs
│       │   └── ErrorEventArgs.cs
│       ├── Extensions/
│       │   └── ServiceCollectionExtensions.cs
│       └── PlayHouse.Connector.csproj
│
├── tests/
│   ├── PlayHouse.Tests.Shared/    # 공유 테스트 인프라
│   │   ├── Fixtures/
│   │   │   ├── TestServerFixture.cs  # 테스트 서버 Fixture
│   │   │   └── TestServer.cs         # 서버 래퍼
│   │   └── TestImplementations/
│   │       ├── TestStage.cs          # 공용 테스트 Stage
│   │       └── TestActor.cs          # 공용 테스트 Actor
│   ├── PlayHouse.Tests.E2E/       # E2E 테스트 (Connector 사용)
│   ├── PlayHouse.Tests.Integration/  # 통합 테스트 (우선)
│   └── PlayHouse.Tests.Unit/      # 유닛 테스트 (통합으로 커버 어려운 부분만)
│
└── playhouse-net.sln              # 솔루션 파일
```

### 3.2 프로젝트 구성

| 프로젝트 | 역할 | 의존성 |
|---------|------|--------|
| PlayHouse | 서버 프레임워크 | 없음 (독립) |
| PlayHouse.Connector | 클라이언트 라이브러리 | 없음 (독립) |
| PlayHouse.Tests.Shared | 공유 테스트 인프라 | PlayHouse, Connector |
| PlayHouse.Tests.E2E | E2E 테스트 | PlayHouse, Connector, Tests.Shared |
| PlayHouse.Tests.Integration | 통합 테스트 | PlayHouse, Tests.Shared |
| PlayHouse.Tests.Unit | 유닛 테스트 | PlayHouse |

### 3.3 레이어 간 의존성 규칙

```
Infrastructure → Core → Abstractions
                        (Abstractions는 의존성 없음)
```

- `Abstractions/`: 외부 의존성 없음, 순수 인터페이스와 값 객체
- `Core/`: Abstractions만 참조
- `Infrastructure/`: Core와 Abstractions 참조

### 3.4 PlayHouse.Connector 역할

E2E/통합 테스트에서 실제 서버에 연결하여 패킷을 주고받는 클라이언트 라이브러리입니다.

**주요 기능**:
- TCP/WebSocket 연결 관리
- 패킷 인코딩/디코딩 (서버와 동일한 프로토콜)
- Request-Reply 패턴 지원
- 세션 상태 관리

**사용 예시**:
```csharp
// E2E 테스트에서 Connector 사용
var connector = new PlayHouseConnector();
await connector.ConnectAsync("localhost", 9000);
await connector.AuthenticateAsync(accountId, token);

var response = await connector.RequestToStageAsync(stageId, new JoinRoomRequest());
Assert.Equal(ErrorCode.Success, response.ErrorCode);
```

## 4. 프레임워크를 사용한 서버 구조 (예시)

### 4.1 Sample Room Server 구조

프레임워크를 사용하여 구현한 Room 서버의 예시 구조입니다. (이는 프레임워크 자체가 아니라 **사용 예시**입니다)

```
┌─────────────────────────────────────────────────────────┐
│              SampleRoomServer (User Application)        │
│                                                         │
│  ┌──────────────┐         ┌──────────────┐             │
│  │  HTTP API    │         │Socket Server │             │
│  │  (ASP.NET)   │         │ TCP/WS/HTTPS │             │
│  └──────┬───────┘         └──────┬───────┘             │
│         │                        │                      │
│         └────────┬───────────────┘                      │
│                  │                                      │
│         ┌────────▼────────┐                             │
│         │  Core Engine    │ ← PlayHouse 프레임워크       │
│         │                 │                             │
│         │  ┌───────────┐  │                             │
│         │  │Dispatcher │  │                             │
│         │  └─────┬─────┘  │                             │
│         │        │        │                             │
│         │  ┌─────▼─────┐  │                             │
│         │  │Stage Pool │  │                             │
│         │  └─────┬─────┘  │                             │
│         │        │        │                             │
│         │  ┌─────▼─────┐  │                             │
│         │  │Timer Mgr  │  │                             │
│         │  └───────────┘  │                             │
│         └─────────────────┘                             │
│                                                         │
│         ┌─────────────────┐                             │
│         │ Custom Stages   │ ← 사용자 구현                │
│         │ Custom Actors   │                             │
│         │ HTTP Handlers   │                             │
│         └─────────────────┘                             │
│                                                         │
└─────────────────────────────────────────────────────────┘
```

### 4.2 사용자 애플리케이션 레이어

사용자가 프레임워크 위에 구현하는 애플리케이션 계층:

```
SampleRoomServer/
├── Stages/
│   ├── LobbyStage.cs          # IStage 구현
│   └── GameRoomStage.cs       # IStage 구현
├── Actors/
│   ├── PlayerActor.cs         # IActor 구현
│   └── BotActor.cs            # IActor 구현
├── Controllers/
│   └── GameApiController.cs   # ASP.NET Core Controller
└── Program.cs                 # 서버 진입점
```

## 5. Core Engine 구성요소

### 5.1 Message Pipeline

메시지 처리의 핵심 파이프라인

```
[메시지 흐름]

Client/HTTP
    │
    ▼
┌─────────────┐
│  Receiver   │  - Socket/HTTP로부터 메시지 수신
│             │  - 패킷 파싱 및 검증
└──────┬──────┘
       │
       ▼
┌─────────────┐
│ Dispatcher  │  - 목적지 Stage 결정
│             │  - 메시지 큐에 전달
└──────┬──────┘
       │
       ▼
┌─────────────┐
│Stage Queue  │  - Lock-Free 큐
│ (per Stage) │  - FIFO 순서 보장
└──────┬──────┘
       │
       ▼
┌─────────────┐
│   Handler   │  - Stage/Actor 메서드 호출
│             │  - 응답 생성
└──────┬──────┘
       │
       ▼
┌─────────────┐
│   Link      │  - 클라이언트로 응답 전송
│             │  - 다른 Stage로 메시지 전송
└─────────────┘
```

#### 5.1.1 Dispatcher

```csharp
// Dispatcher 역할
- 수신된 패킷의 목적지 Stage 식별
- Stage별 메시지 큐에 메시지 전달
- Request-Reply 상관관계 관리
- 타이머 이벤트 라우팅

// 핵심 메서드
void OnPost(IPacket packet)           // 메시지 디스패치
void OnTimer(long stageId, long timerId)  // 타이머 이벤트
```

#### 5.1.2 Message Queue

```csharp
// Stage별 독립 큐
class StageMessageQueue
{
    ConcurrentQueue<IPacket> _queue;  // Lock-Free 큐

    void Enqueue(IPacket packet);     // 메시지 추가
    bool TryDequeue(out IPacket packet); // 메시지 꺼내기
}

// 특징
- Lock-Free: ConcurrentQueue 사용
- FIFO 순서 보장
- Actor 간 경쟁 조건 방지
```

### 5.2 Stage Management

Stage 생명주기 및 풀 관리

```
[Stage 생명주기]

Create Request
    │
    ▼
┌─────────────┐
│  Allocate   │  - Stage 인스턴스 생성
│   Stage     │  - StageId 할당
└──────┬──────┘
       │
       ▼
┌─────────────┐
│  OnCreate   │  - 사용자 초기화 로직
│  Callback   │  - 초기 상태 설정
└──────┬──────┘
       │
       ▼
┌─────────────┐
│OnPostCreate │  - 생성 완료 후 처리
│  Callback   │  - 초기 타이머 등록 등
└──────┬──────┘
       │
       ▼
┌─────────────┐
│   Active    │  - 메시지 처리
│   State     │  - Actor Join/Leave
│             │  - Timer 실행
└──────┬──────┘
       │
       ▼
┌─────────────┐
│   Destroy   │  - 타이머 정리
│   Stage     │  - Actor 정리
│             │  - 자원 해제
└─────────────┘
```

#### 5.2.1 Stage Pool

```csharp
class StagePool
{
    // Stage 관리
    Dictionary<long, IStage> _stages;  // StageId → Stage

    // Stage 생성
    Task<IStage> CreateStage(long stageId, string stageType, IPacket initPacket);

    // Stage 조회
    IStage? GetStage(long stageId);

    // Stage 삭제
    void RemoveStage(long stageId);

    // 통계
    int GetStageCount();
    IEnumerable<StageInfo> GetStageInfos();
}
```

#### 5.2.2 Actor Pool

```csharp
class ActorPool
{
    // Actor 관리 (Stage 내)
    Dictionary<long, IActor> _actors;  // AccountId → Actor

    // Actor 생성
    Task<IActor> CreateActor(long accountId);

    // Actor 조회
    IActor? GetActor(long accountId);

    // Actor 삭제
    void RemoveActor(long accountId);
}
```

### 5.3 Support Services

#### 5.3.1 Timer Manager

```csharp
class TimerManager
{
    // 타이머 등록
    long RegisterRepeatTimer(long stageId, long timerId,
        TimeSpan initialDelay, TimeSpan period,
        TimerCallbackTask callback);

    long RegisterCountTimer(long stageId, long timerId,
        TimeSpan initialDelay, int count, TimeSpan period,
        TimerCallbackTask callback);

    // 타이머 취소
    void CancelTimer(long timerId);

    // 내부 구현
    // - System.Threading.Timer 사용
    // - Dispatcher를 통해 콜백 전달
}
```

#### 5.3.2 Request Cache

```csharp
class RequestCache
{
    // Request-Reply 매핑
    Dictionary<int, ReplyCallback> _pendingRequests;  // MsgSeq → Callback

    // 요청 등록
    void RegisterRequest(int msgSeq, ReplyCallback callback, TimeSpan timeout);

    // 응답 처리
    void HandleReply(int msgSeq, ushort errorCode, IPacket reply);

    // 타임아웃 처리
    void CheckTimeout();
}
```

#### 5.3.3 Session Manager

```csharp
class SessionManager
{
    // 세션 관리
    Dictionary<long, SessionInfo> _sessions;  // SessionId → SessionInfo

    // 세션 생성
    void CreateSession(long sessionId, ISession session);

    // 세션 조회
    SessionInfo? GetSession(long sessionId);

    // 세션 삭제
    void RemoveSession(long sessionId);

    // AccountId 매핑
    void MapAccountId(long sessionId, long accountId);
    long GetAccountId(long sessionId);
}
```

## 6. HTTP API + Socket Server 통합 구조

### 6.1 통합 아키텍처

```
┌─────────────────────────────────────────────────────────┐
│                   ASP.NET Core Host                     │
│                                                         │
│  ┌──────────────┐                ┌──────────────┐      │
│  │              │                │              │      │
│  │  HTTP Stack  │                │ Socket Stack │      │
│  │              │                │              │      │
│  │  - Kestrel   │                │ - TCP Server │      │
│  │  - Controller│                │ - WS Server  │      │
│  │  - Routing   │                │ - TLS        │      │
│  │              │                │              │      │
│  └──────┬───────┘                └──────┬───────┘      │
│         │                               │              │
│         └───────────┬───────────────────┘              │
│                     │                                  │
│            ┌────────▼────────┐                         │
│            │  Core Engine    │                         │
│            │  - Dispatcher   │                         │
│            │  - Stage Pool   │                         │
│            └─────────────────┘                         │
└─────────────────────────────────────────────────────────┘
```

### 6.2 포트 구성

```yaml
# 기본 포트 구성
HTTP_API_PORT: 8080       # REST API
TCP_SOCKET_PORT: 9000     # TCP Socket
WS_SOCKET_PORT: 9001      # WebSocket
HTTPS_API_PORT: 8443      # HTTPS (옵션)
WSS_SOCKET_PORT: 9443     # WebSocket Secure (옵션)
```

### 6.3 요청 처리 흐름

```
[HTTP 요청]
Client → HTTP Controller → Core Engine → Response

[Socket 메시지]
Client → Socket Handler → Dispatcher → Stage → Response
```

## 7. ServerType과 ServiceId

### 7.1 개요

PlayHouse-NET은 서버 간 통신을 위해 **ServerType**과 **ServiceId**라는 두 가지 식별자를 사용합니다.

- **ServerType**: 서버의 역할을 구분 (Play, Api)
- **ServiceId**: 같은 ServerType 내에서 서버 그룹을 구분 (예: 지역별, 환경별)

이 구조를 통해 유연한 서버 배치와 효율적인 라우팅이 가능합니다.

### 7.2 ServerType

서버의 역할을 정의하는 열거형입니다.

```csharp
/// <summary>
/// Defines the server types in the PlayHouse framework.
/// </summary>
/// <remarks>
/// ServerType distinguishes between different kinds of servers.
/// ServiceId is used to group servers within the same ServerType.
/// </remarks>
public enum ServerType : ushort
{
    /// <summary>
    /// Play Server - handles game logic and real-time communication.
    /// </summary>
    Play = 1,

    /// <summary>
    /// API Server - handles stateless API requests.
    /// </summary>
    Api = 2,
}
```

#### 7.2.1 Play Server (ServerType.Play)

**역할**: 실시간 게임 로직 및 상태 관리

**특징**:
- Stage/Actor 모델 기반
- Stateful 통신 (클라이언트 세션 유지)
- 실시간 메시지 처리
- 타이머 및 이벤트 관리

**사용 사례**:
- 게임 로비 관리
- 게임 룸 (배틀, 매칭 등)
- 실시간 채팅
- 플레이어 상태 관리

**메시지 API**:
```csharp
// Play Server의 Stage로 메시지 전송
link.SendToStage(playServerId, stageId, packet);
link.RequestToStage(playServerId, stageId, packet);
```

#### 7.2.2 API Server (ServerType.Api)

**역할**: Stateless 요청 처리

**특징**:
- HTTP API 스타일의 요청-응답 패턴
- Stateless (세션 상태 없음)
- 빠른 응답 시간
- 수평 확장 용이

**사용 사례**:
- 데이터베이스 조회/수정
- 외부 서비스 연동
- 통계 조회
- 관리자 API

**메시지 API**:
```csharp
// API Server로 메시지 전송
link.SendToApi(apiServerId, packet);
link.RequestToApi(apiServerId, packet);

// ServiceId로 API Server 선택
link.SendToApiService(serviceId, packet);
link.RequestToApiService(serviceId, packet);
```

### 7.3 ServiceId

같은 ServerType 내에서 서버 그룹을 구분하는 식별자입니다.

```csharp
/// <summary>
/// Default service group identifiers.
/// </summary>
public static class ServiceIdDefaults
{
    /// <summary>
    /// Default service group.
    /// </summary>
    public const ushort Default = 1;
}
```

#### 7.3.1 ServiceId 사용 목적

**1. 지역별 서버 구분**
```
ServerType.Api + ServiceId=1 → 서울 리전 API 서버
ServerType.Api + ServiceId=2 → 도쿄 리전 API 서버
ServerType.Api + ServiceId=3 → 싱가포르 리전 API 서버
```

**2. 환경별 서버 구분**
```
ServerType.Play + ServiceId=1 → 프로덕션 환경
ServerType.Play + ServiceId=2 → 스테이징 환경
ServerType.Play + ServiceId=3 → 개발 환경
```

**3. 기능별 서버 구분**
```
ServerType.Api + ServiceId=1 → 게임 데이터 API
ServerType.Api + ServiceId=2 → 결제 API
ServerType.Api + ServiceId=3 → 소셜 API
```

**4. 부하 분산**
```
ServerType.Api + ServiceId=1 → API 서버 그룹 A (일반 부하)
ServerType.Api + ServiceId=2 → API 서버 그룹 B (고부하 처리)
```

#### 7.3.2 서버 정보 구조

```csharp
public interface IServerInfo
{
    ServerType ServerType { get; }   // Play or Api
    ushort ServiceId { get; }        // 서비스 그룹 ID
    string ServerId { get; }         // 서버 인스턴스 ID (고유)
    string Address { get; }          // 연결 주소
    ServerState State { get; }       // Running or Disabled
    int Weight { get; }              // 로드밸런싱 가중치
}
```

**예시 서버 정보**:
```csharp
// 서울 리전 API 서버
var seoulApi = new XServerInfo(
    serverType: ServerType.Api,
    serviceId: 1,
    serverId: "api-seoul-1",
    address: "tcp://192.168.1.100:5000",
    state: ServerState.Running,
    weight: 100
);

// 도쿄 리전 API 서버
var tokyoApi = new XServerInfo(
    serverType: ServerType.Api,
    serviceId: 2,
    serverId: "api-tokyo-1",
    address: "tcp://192.168.2.100:5000",
    state: ServerState.Running,
    weight: 100
);

// Play 서버
var playServer = new XServerInfo(
    serverType: ServerType.Play,
    serviceId: ServiceIdDefaults.Default,
    serverId: "play-1",
    address: "tcp://192.168.3.100:5000",
    state: ServerState.Running,
    weight: 100
);
```

### 7.4 ServerSelectionPolicy

서비스 그룹 내에서 특정 서버를 선택하는 정책입니다.

```csharp
/// <summary>
/// 서버 선택 정책.
/// </summary>
public enum ServerSelectionPolicy
{
    /// <summary>
    /// Round-Robin 방식 (기본값).
    /// 순차적으로 서버 선택.
    /// </summary>
    RoundRobin = 0,

    /// <summary>
    /// 가중치 기반 선택 (내림차순).
    /// Weight가 가장 높은 서버가 우선 선택됨.
    /// </summary>
    Weighted = 1
}
```

#### 7.4.1 RoundRobin 정책

**동작 방식**: 서비스 그룹 내 서버들을 순차적으로 선택

**특징**:
- 균등 분배 (서버마다 동일한 요청 수)
- 예측 가능한 부하 분산
- 기본 정책

**사용 예시**:
```csharp
// RoundRobin 방식으로 API 서버 선택 (기본값)
link.SendToApiService(serviceId: 1, packet);

// 명시적으로 RoundRobin 지정
link.SendToApiService(
    serviceId: 1,
    packet,
    ServerSelectionPolicy.RoundRobin
);
```

**시나리오**:
```
서비스 그룹 1에 API 서버 3대가 있을 때:
요청 1 → api-1
요청 2 → api-2
요청 3 → api-3
요청 4 → api-1 (순환)
요청 5 → api-2
```

#### 7.4.2 Weighted 정책

**동작 방식**: Weight가 높은 서버를 우선 선택

**특징**:
- 서버 성능 차이 반영
- 고성능 서버에 더 많은 트래픽 할당
- 점진적 배포 시 유용 (카나리 배포)

**사용 예시**:
```csharp
// 가중치 기반으로 API 서버 선택
link.SendToApiService(
    serviceId: 1,
    packet,
    ServerSelectionPolicy.Weighted
);
```

**시나리오**:
```
서비스 그룹 1에 API 서버 3대:
- api-1 (Weight=200) → 고성능 서버
- api-2 (Weight=100) → 일반 서버
- api-3 (Weight=50)  → 신규/테스트 서버

Weighted 정책 적용 시:
- api-1이 가장 많은 요청 처리
- api-3은 제한적으로만 트래픽 받음 (카나리)
```

### 7.5 API Service 통신 패턴

#### 7.5.1 특정 서버로 직접 전송

**ServerID를 명시**하여 특정 서버에 직접 메시지 전송:

```csharp
// 특정 API 서버로 전송
link.SendToApi(apiServerId: "api-seoul-1", packet);

// 특정 API 서버로 요청
var reply = await link.RequestToApi(
    apiServerId: "api-seoul-1",
    packet
);
```

**사용 사례**:
- 특정 리전의 데이터베이스 접근
- 관리자 명령 실행
- 디버깅/모니터링

#### 7.5.2 서비스 그룹으로 전송 (자동 선택)

**ServiceId**만 지정하고 프레임워크가 서버 선택:

```csharp
// ServiceId로 전송 (RoundRobin)
link.SendToApiService(serviceId: 1, packet);

// ServiceId로 요청 (RoundRobin)
var reply = await link.RequestToApiService(
    serviceId: 1,
    packet
);

// ServiceId로 전송 (Weighted)
link.SendToApiService(
    serviceId: 1,
    packet,
    ServerSelectionPolicy.Weighted
);

// ServiceId로 요청 (Weighted)
var reply = await link.RequestToApiService(
    serviceId: 1,
    packet,
    ServerSelectionPolicy.Weighted
);
```

**사용 사례**:
- 일반적인 API 요청 (DB 조회, 저장 등)
- 로드밸런싱이 필요한 경우
- 서버 추가/제거가 빈번한 경우

### 7.6 실제 배치 예시

#### 7.6.1 멀티 리전 배치

```
┌────────────────────────────────────────────────────────┐
│                     Global Network                     │
│                                                        │
│  ┌─────────────────┐  ┌─────────────────┐  ┌─────────────────┐
│  │  Seoul Region   │  │  Tokyo Region   │  │Singapore Region │
│  │                 │  │                 │  │                 │
│  │ Api ServiceId=1 │  │ Api ServiceId=2 │  │ Api ServiceId=3 │
│  │ - api-seoul-1   │  │ - api-tokyo-1   │  │ - api-sg-1      │
│  │ - api-seoul-2   │  │ - api-tokyo-2   │  │ - api-sg-2      │
│  │                 │  │                 │  │                 │
│  │Play ServiceId=1 │  │Play ServiceId=2 │  │Play ServiceId=3 │
│  │ - play-seoul-1  │  │ - play-tokyo-1  │  │ - play-sg-1     │
│  │ - play-seoul-2  │  │ - play-tokyo-2  │  │ - play-sg-2     │
│  └─────────────────┘  └─────────────────┘  └─────────────────┘
│                                                        │
└────────────────────────────────────────────────────────┘
```

**라우팅 예시**:
```csharp
// 서울 리전 API 사용
await link.RequestToApiService(serviceId: 1, packet);

// 도쿄 리전 API 사용
await link.RequestToApiService(serviceId: 2, packet);

// 싱가포르 리전 API 사용
await link.RequestToApiService(serviceId: 3, packet);
```

#### 7.6.2 기능별 분리 배치

```
┌────────────────────────────────────────────────────────┐
│                   Service Mesh                         │
│                                                        │
│  ┌─────────────────┐  ┌─────────────────┐  ┌─────────────────┐
│  │  Game Data API  │  │   Payment API   │  │   Social API    │
│  │                 │  │                 │  │                 │
│  │ Api ServiceId=1 │  │ Api ServiceId=2 │  │ Api ServiceId=3 │
│  │ - gameapi-1     │  │ - payapi-1      │  │ - socialapi-1   │
│  │ - gameapi-2     │  │ - payapi-2      │  │ - socialapi-2   │
│  │ - gameapi-3     │  │                 │  │                 │
│  └─────────────────┘  └─────────────────┘  └─────────────────┘
│                                                        │
└────────────────────────────────────────────────────────┘
```

**사용 예시**:
```csharp
// 게임 데이터 조회
var gameData = await link.RequestToApiService(
    serviceId: 1,
    new GetPlayerDataRequest()
);

// 결제 처리
var paymentResult = await link.RequestToApiService(
    serviceId: 2,
    new ProcessPaymentRequest()
);

// 친구 목록 조회
var friends = await link.RequestToApiService(
    serviceId: 3,
    new GetFriendsRequest()
);
```

### 7.7 설계 원칙

#### 7.7.1 명확한 역할 분리

- **ServerType**: 서버의 기술적 역할 (Play vs Api)
- **ServiceId**: 비즈니스적 그룹핑 (지역, 기능, 환경)
- **ServerId**: 물리적 인스턴스 식별

#### 7.7.2 유연한 확장성

```
단일 서비스 → 멀티 서비스 확장:
  ServiceId=1 (모든 기능)
    ↓
  ServiceId=1 (게임 데이터) + ServiceId=2 (결제) + ServiceId=3 (소셜)

지역 확장:
  ServiceId=1 (글로벌)
    ↓
  ServiceId=1 (서울) + ServiceId=2 (도쿄) + ServiceId=3 (싱가포르)
```

#### 7.7.3 투명한 로드밸런싱

**개발자 관점**:
```csharp
// 서버 선택 로직 불필요
await link.RequestToApiService(serviceId: 1, packet);
```

**프레임워크 내부**:
- 서버 상태 모니터링 (Running/Disabled)
- 자동 서버 선택 (RoundRobin/Weighted)
- 실패 서버 제외

### 7.8 마이그레이션 가이드

#### 7.8.1 기존 코드에서 전환

**Before** (특정 서버 ID 사용):
```csharp
// 하드코딩된 서버 ID
link.SendToApi("hardcoded-api-server", packet);
```

**After** (ServiceId 사용):
```csharp
// 서비스 그룹으로 추상화
link.SendToApiService(ServiceIdDefaults.Default, packet);
```

#### 7.8.2 점진적 도입

**Phase 1**: 모든 서버를 Default ServiceId로 설정
```csharp
const ushort DEFAULT_SERVICE = ServiceIdDefaults.Default;
link.SendToApiService(DEFAULT_SERVICE, packet);
```

**Phase 2**: 기능별로 ServiceId 분리
```csharp
const ushort GAME_DATA_SERVICE = 1;
const ushort PAYMENT_SERVICE = 2;
const ushort SOCIAL_SERVICE = 3;

link.SendToApiService(GAME_DATA_SERVICE, packet);
```

**Phase 3**: 리전별 분리 (필요시)
```csharp
const ushort SEOUL_SERVICE = 1;
const ushort TOKYO_SERVICE = 2;

var serviceId = GetRegionServiceId(playerRegion);
link.SendToApiService(serviceId, packet);
```

## 8. 동시성 및 스레딩 모델

### 8.1 스레드 풀 구성

```
┌─────────────────────────────────────────────────────────┐
│                    Thread Pool                          │
│                                                         │
│  ┌──────────────┐  ┌──────────────┐  ┌──────────────┐  │
│  │   I/O Pool   │  │  CPU Pool    │  │  Timer Pool  │  │
│  │              │  │              │  │              │  │
│  │ - Socket I/O │  │ - Dispatcher │  │ - System     │  │
│  │ - HTTP I/O   │  │ - Handler    │  │   Timer      │  │
│  │              │  │              │  │              │  │
│  └──────────────┘  └──────────────┘  └──────────────┘  │
└─────────────────────────────────────────────────────────┘
```

### 7.2 Lock-Free 메시지 처리

```
[동시성 제어 전략]

1. Stage 단위 격리
   - 각 Stage는 독립된 메시지 큐
   - 큐 처리는 단일 스레드 보장
   - 공유 상태 없음

2. Actor 단위 격리
   - Actor는 Stage 내에서만 존재
   - Stage 메시지 큐를 통한 접근만 허용
   - 동시 수정 불가능

3. ConcurrentQueue 활용
   - .NET ConcurrentQueue 사용
   - Lock-Free 알고리즘
   - 높은 처리량
```

## 8. 확장성 고려사항

### 8.1 수평 확장 (Scale-Out)

```
┌──────────────┐     ┌──────────────┐     ┌──────────────┐
│ Room Server  │     │ Room Server  │     │ Room Server  │
│   Instance 1 │     │   Instance 2 │     │   Instance 3 │
│              │     │              │     │              │
│ Stage 1-1000 │     │Stage 1001-   │     │Stage 2001-   │
│              │     │      2000    │     │      3000    │
└──────▲───────┘     └──────▲───────┘     └──────▲───────┘
       │                    │                    │
       └────────────────────┴────────────────────┘
                            │
                    ┌───────▼────────┐
                    │ Load Balancer  │
                    │ (Stage-based)  │
                    └───────▲────────┘
                            │
                         Clients
```

### 8.2 수직 확장 (Scale-Up)

- **CPU**: 코어 수만큼 병렬 처리 증가
- **메모리**: Stage/Actor 수 증가
- **네트워크**: NIC 대역폭에 비례

## 9. 모니터링 및 관리

### 9.1 메트릭 수집

```
- Stage 수
- Actor 수 (Stage별, 전체)
- 메시지 처리량 (msg/sec)
- 평균 메시지 처리 시간
- 큐 대기 길이
- 타이머 수
- 연결된 세션 수
- 메모리 사용량
- CPU 사용률
```

### 9.2 Health Check

```
GET /health
{
  "status": "healthy",
  "uptime": 3600,
  "stages": 100,
  "actors": 500,
  "sessions": 500
}
```

## 10. 장애 처리

### 10.1 클라이언트 연결 끊김

```
Client Disconnect
    │
    ▼
Session Cleanup
    │
    ▼
OnDisconnect(actor) 호출
    │
    ▼
Actor 제거
```

### 10.2 Stage 예외 처리

```csharp
try {
    await stage.OnDispatch(actor, packet);
} catch (Exception ex) {
    // 로깅
    LOG.Error(ex);

    // 클라이언트에 에러 응답
    link.Reply(ErrorCode.InternalError);

    // Stage는 계속 유지 (격리)
}
```

## 11. 보안 고려사항

### 11.1 전송 보안

- **TLS/SSL**: HTTPS, WSS 지원
- **인증서 관리**: Let's Encrypt 연동

### 11.2 애플리케이션 보안

- **인증**: Token 기반 인증
- **권한**: Stage 접근 제어
- **검증**: 패킷 크기 제한, 속도 제한

## 12. 설정 및 배포

### 12.1 설정 파일 예시

```json
{
  "server": {
    "name": "room-server-1",
    "httpPort": 8080,
    "tcpPort": 9000,
    "wsPort": 9001
  },
  "limits": {
    "maxSessions": 10000,
    "maxStages": 1000,
    "maxActorsPerStage": 100,
    "maxPacketSize": 2097152
  },
  "performance": {
    "workerThreads": 8,
    "ioThreads": 4
  }
}
```

### 12.2 Docker 배포

```dockerfile
FROM mcr.microsoft.com/dotnet/aspnet:8.0
COPY . /app
WORKDIR /app
EXPOSE 8080 9000 9001
ENTRYPOINT ["dotnet", "RoomServer.dll"]
```

## 다음 단계

- `02-packet-structure.md`: 패킷 구조 상세 설계
- `03-stage-actor-model.md`: Stage/Actor 상세 동작 방식
