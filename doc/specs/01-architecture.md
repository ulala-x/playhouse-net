# PlayHouse-NET 아키텍처

## 1. 전체 시스템 아키텍처

### 1.1 단순화된 단일 서버 구조

기존 PlayHouse는 Session, API, Play 세 개의 서버로 구성되었으나, PlayHouse-NET은 단일 Room 서버로 통합되어 복잡도를 대폭 감소시켰습니다.

```
[기존 PlayHouse - 3-Tier Architecture]

┌──────────────┐     ┌──────────────┐     ┌──────────────┐
│   Session    │────▶│     API      │────▶│     Play     │
│   Server     │     │   Server     │     │   Server     │
│              │     │              │     │              │
│ - TCP/WS     │     │ - Stateless  │     │ - Stage Mgmt │
│ - Auth       │     │ - HTTP API   │     │ - Actor Mgmt │
│ - Routing    │     │ - Logic      │     │ - Game Logic │
└──────────────┘     └──────────────┘     └──────────────┘
       ▲                    ▲                    ▲
       │                    │                    │
    NetMQ               NetMQ                NetMQ
    Full-Mesh TCP Communication
    + Redis for Discovery


[PlayHouse-NET - Single Server Architecture]

┌─────────────────────────────────────────────────────────┐
│                    Room Server                          │
│                                                         │
│  ┌──────────────┐         ┌──────────────┐             │
│  │  HTTP API    │         │Socket Server │             │
│  │  (ASP.NET)   │         │ TCP/WS/HTTPS │             │
│  └──────┬───────┘         └──────┬───────┘             │
│         │                        │                      │
│         └────────┬───────────────┘                      │
│                  │                                      │
│         ┌────────▼────────┐                             │
│         │  Core Engine    │                             │
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
│         │ Stage/Actor     │                             │
│         │ (User Logic)    │                             │
│         └─────────────────┘                             │
│                                                         │
└─────────────────────────────────────────────────────────┘
```

### 1.2 구조 단순화의 이점

| 측면 | 이점 |
|------|------|
| **배포** | 단일 프로세스 배포, 설정 간소화 |
| **개발** | 서버 간 통신 로직 불필요, 디버깅 용이 |
| **운영** | 모니터링 포인트 감소, 장애 지점 최소화 |
| **성능** | 네트워크 홉 제거, 지연 시간 감소 |
| **비용** | Redis 불필요, 인프라 비용 절감 |

## 2. Room Server 상세 구조

### 2.1 계층별 컴포넌트

```
┌─────────────────────────────────────────────────────────┐
│                  Application Layer                      │
│  ┌────────────┐  ┌────────────┐  ┌────────────┐        │
│  │Custom Stage│  │Custom Actor│  │HTTP Handler│        │
│  └────────────┘  └────────────┘  └────────────┘        │
└──────────────────────┬──────────────────────────────────┘
                       │ implements/uses
┌──────────────────────▼──────────────────────────────────┐
│              Abstractions Layer                         │
│  ┌─────────┐  ┌─────────┐  ┌─────────┐  ┌─────────┐   │
│  │ IStage  │  │ IActor  │  │ ISender │  │ IPacket │   │
│  └─────────┘  └─────────┘  └─────────┘  └─────────┘   │
└──────────────────────┬──────────────────────────────────┘
                       │ uses
┌──────────────────────▼──────────────────────────────────┐
│                 Core Engine Layer                       │
│                                                         │
│  ┌──────────────────────────────────────────────┐      │
│  │            Message Pipeline                  │      │
│  │  ┌──────────┐  ┌──────────┐  ┌──────────┐   │      │
│  │  │ Receiver │→ │Dispatcher│→ │ Handler  │   │      │
│  │  └──────────┘  └──────────┘  └──────────┘   │      │
│  └──────────────────────────────────────────────┘      │
│                                                         │
│  ┌──────────────────────────────────────────────┐      │
│  │            Stage Management                  │      │
│  │  ┌──────────┐  ┌──────────┐  ┌──────────┐   │      │
│  │  │Stage Pool│  │Actor Pool│  │Queue Mgr │   │      │
│  │  └──────────┘  └──────────┘  └──────────┘   │      │
│  └──────────────────────────────────────────────┘      │
│                                                         │
│  ┌──────────────────────────────────────────────┐      │
│  │            Support Services                  │      │
│  │  ┌──────────┐  ┌──────────┐  ┌──────────┐   │      │
│  │  │Timer Mgr │  │Request   │  │Session   │   │      │
│  │  │          │  │Cache     │  │Manager   │   │      │
│  │  └──────────┘  └──────────┘  └──────────┘   │      │
│  └──────────────────────────────────────────────┘      │
└──────────────────────┬──────────────────────────────────┘
                       │ uses
┌──────────────────────▼──────────────────────────────────┐
│            Infrastructure Layer                         │
│                                                         │
│  ┌──────────────────┐  ┌──────────────────┐            │
│  │ Socket Transport │  │  HTTP Server     │            │
│  │  - TCP           │  │  - REST API      │            │
│  │  - WebSocket     │  │  - Swagger       │            │
│  │  - HTTPS/TLS     │  │  - Monitoring    │            │
│  └──────────────────┘  └──────────────────┘            │
│                                                         │
│  ┌──────────────────┐  ┌──────────────────┐            │
│  │  Serialization   │  │  Compression     │            │
│  │  - Binary        │  │  - LZ4           │            │
│  │  - JSON          │  │                  │            │
│  └──────────────────┘  └──────────────────┘            │
└─────────────────────────────────────────────────────────┘
```

### 2.2 의존성 규칙

```
Application Layer ──depends on──▶ Abstractions Layer
                                         ▲
                                         │
Core Engine Layer ───depends on──────────┘
        ▲
        │
Infrastructure Layer ──depends on────────┘
```

- **상위 레이어는 하위 레이어에만 의존**
- **하위 레이어는 상위 레이어를 알지 못함**
- **Abstractions Layer는 외부 의존성 없음**

## 3. Core Engine 구성요소

### 3.1 Message Pipeline

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
│   Sender    │  - 클라이언트로 응답 전송
│             │  - 다른 Stage로 메시지 전송
└─────────────┘
```

#### 3.1.1 Dispatcher

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

#### 3.1.2 Message Queue

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

### 3.2 Stage Management

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

#### 3.2.1 Stage Pool

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

#### 3.2.2 Actor Pool

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

### 3.3 Support Services

#### 3.3.1 Timer Manager

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

#### 3.3.2 Request Cache

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

#### 3.3.3 Session Manager

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

## 4. HTTP API + Socket Server 통합 구조

### 4.1 통합 아키텍처

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

### 4.2 포트 구성

```yaml
# 기본 포트 구성
HTTP_API_PORT: 8080       # REST API
TCP_SOCKET_PORT: 9000     # TCP Socket
WS_SOCKET_PORT: 9001      # WebSocket
HTTPS_API_PORT: 8443      # HTTPS (옵션)
WSS_SOCKET_PORT: 9443     # WebSocket Secure (옵션)
```

### 4.3 요청 처리 흐름

```
[HTTP 요청]
Client → HTTP Controller → Core Engine → Response

[Socket 메시지]
Client → Socket Handler → Dispatcher → Stage → Response
```

## 5. 동시성 및 스레딩 모델

### 5.1 스레드 풀 구성

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

### 5.2 Lock-Free 메시지 처리

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

## 6. 확장성 고려사항

### 6.1 수평 확장 (Scale-Out)

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

### 6.2 수직 확장 (Scale-Up)

- **CPU**: 코어 수만큼 병렬 처리 증가
- **메모리**: Stage/Actor 수 증가
- **네트워크**: NIC 대역폭에 비례

## 7. 모니터링 및 관리

### 7.1 메트릭 수집

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

### 7.2 Health Check

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

## 8. 장애 처리

### 8.1 클라이언트 연결 끊김

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

### 8.2 Stage 예외 처리

```csharp
try {
    await stage.OnDispatch(actor, packet);
} catch (Exception ex) {
    // 로깅
    LOG.Error(ex);

    // 클라이언트에 에러 응답
    sender.Reply(ErrorCode.InternalError);

    // Stage는 계속 유지 (격리)
}
```

## 9. 보안 고려사항

### 9.1 전송 보안

- **TLS/SSL**: HTTPS, WSS 지원
- **인증서 관리**: Let's Encrypt 연동

### 9.2 애플리케이션 보안

- **인증**: Token 기반 인증
- **권한**: Stage 접근 제어
- **검증**: 패킷 크기 제한, 속도 제한

## 10. 설정 및 배포

### 10.1 설정 파일 예시

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

### 10.2 Docker 배포

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
