# PlayHouse-NET 미구현 사항 구현 계획 (2025-12-09 업데이트)

> 이 문서는 스펙 대비 실제 구현 리뷰 결과를 바탕으로 남은 미구현 사항과 구현 계획을 정리합니다.

## 목차
1. [현재 구현 상태 요약](#1-현재-구현-상태-요약)
2. [미구현 사항 목록](#2-미구현-사항-목록)
3. [구현 우선순위](#3-구현-우선순위)
4. [상세 구현 계획](#4-상세-구현-계획)
5. [테스트 보강 계획](#5-테스트-보강-계획)
6. [체크리스트](#6-체크리스트)

---

## 1. 현재 구현 상태 요약

### 1.1 완성된 영역 (✅ 100%)

| 영역 | 완성도 | 핵심 파일 |
|------|--------|----------|
| Architecture | 100% | Clean Architecture 레이어 구조 완성 |
| Packet Structure | 100% | `SimplePacket.cs` - Parse<T>(), Descriptor.Name 사용 |
| Timer System | 100% | `TimerManager.cs` - RepeatTimer, CountTimer |
| Event Loop | 100% | `BaseStage.cs` - Lock-free CAS 패턴 |
| Connector | 90% | `PlayHouseClient.cs`, `IPlayHouseClient.cs` |

### 1.2 대부분 완성된 영역 (⚠️ 70-95%)

| 영역 | 완성도 | 완성 항목 | 미완성 항목 |
|------|--------|----------|------------|
| Stage/Actor Model | 95% | IStage, IActor, Sender 구현체 | AsyncBlock 확인 |
| HTTP API | 70% | RoomController, HealthController | Token 시스템 |
| Socket Transport | 75% | TcpServer, WebSocketServer | TLS, Heartbeat 확인 |
| Testing | 70% | Unit/Integration 테스트 | E2E, HTTP API 테스트 |

### 1.3 부분 구현 영역 (⚠️ 50-60%)

| 영역 | 완성도 | 문제점 |
|------|--------|--------|
| Client Protocol | 60% | Reconnection Actor 유지 미확인 |
| Metrics | 50% | System.Diagnostics.Metrics 미구현 |

---

## 2. 미구현 사항 목록

### 2.1 High Priority (즉시 구현 필요)

| ID | 기능 | 현재 상태 | 관련 파일 | 스펙 문서 |
|----|------|----------|----------|----------|
| H1 | **Room Token 발급** | ❌ 미구현 | `RoomController.cs` | 05-http-api.md |
| H2 | **Token 검증** | ❌ 미구현 | `PlayHouseServer.cs` | 07-client-protocol.md |
| H3 | **Reconnection Actor 유지** | ❓ 확인 필요 | `StageContext.cs`, `SessionManager.cs` | 07-client-protocol.md |

### 2.2 Medium Priority (개선 권장)

| ID | 기능 | 현재 상태 | 관련 파일 | 스펙 문서 |
|----|------|----------|----------|----------|
| M1 | TLS/SSL 지원 | ❓ 확인 필요 | `TcpServer.cs`, `WebSocketServer.cs` | 06-socket-transport.md |
| M2 | Heartbeat 메커니즘 | ❓ 확인 필요 | `TcpSession.cs` | 06-socket-transport.md |
| M3 | Connection Timeout | ❓ 확인 필요 | `TcpSession.cs` | 06-socket-transport.md |
| M4 | System.Diagnostics.Metrics | ❌ 미구현 | 신규 생성 | 08-metrics-observability.md |
| M5 | E2E 테스트 | ❌ 미구현 | 신규 생성 | 10-testing-spec.md |
| M6 | HTTP API 테스트 | ❌ 미구현 | 신규 생성 | 10-testing-spec.md |
| M7 | Connector TODO 완료 | ⚠️ 부분 구현 | `PlayHouseClient.cs:155-158, 560-564` | 09-connector.md |
| M8 | Connector 테스트 | ❌ 미구현 | 신규 생성 | 09-connector.md |

### 2.3 Low Priority (향후 개선)

| ID | 기능 | 현재 상태 | 관련 파일 |
|----|------|----------|----------|
| L1 | OpenTelemetry 통합 | ❌ 미구현 | 신규 생성 |
| L2 | `/api/server/info` | ❌ 미구현 | `RoomController.cs` |
| L3 | `/api/server/stats` | ❌ 미구현 | `RoomController.cs` |
| L4 | Health messageRate | ❌ 미구현 | `HealthController.cs` |

---

## 3. 구현 우선순위

```
Phase 1: Token & Authentication (High) ──────────────────────────────┐
│  H1. Room Token 발급 (HTTP API 응답에 token 추가)                   │
│  H2. Token 검증 (소켓 연결 시 검증 로직)                            │
│  H3. Reconnection Actor 유지 확인/구현                              │
└────────────────────────────────────────────────────────────────────┘
                              │
                              ▼
Phase 2: Infrastructure Hardening (Medium) ──────────────────────────┐
│  M1. TLS/SSL 지원 확인/구현                                         │
│  M2. Heartbeat 메커니즘 확인/구현                                   │
│  M3. Connection Timeout 확인/구현                                   │
│  M4. System.Diagnostics.Metrics 구현                                │
└────────────────────────────────────────────────────────────────────┘
                              │
                              ▼
Phase 3: Testing (Medium) ───────────────────────────────────────────┐
│  M5. E2E 테스트 추가                                                │
│  M6. HTTP API 테스트 추가                                           │
│  M7. Connector TODO 완료                                            │
│  M8. Connector 테스트 추가                                          │
└────────────────────────────────────────────────────────────────────┘
                              │
                              ▼
Phase 4: Operations (Low) ───────────────────────────────────────────┐
│  L1. OpenTelemetry 통합                                             │
│  L2-L4. Admin API 엔드포인트                                        │
└────────────────────────────────────────────────────────────────────┘
```

---

## 4. 상세 구현 계획

### Phase 1: Token & Authentication

#### H1. Room Token 발급

**수정 파일**: `src/PlayHouse/Infrastructure/Http/RoomController.cs`

**현재 상태**: GetOrCreateRoom 응답에 token 필드 없음

**구현 내용**:
1. JWT 또는 간단한 HMAC 기반 토큰 생성
2. 토큰에 포함할 정보: stageId, accountId, expiry
3. GetOrCreateRoom 응답에 `roomToken` 필드 추가

```csharp
// GetOrCreateRoomResponse에 추가
public sealed class GetOrCreateRoomResponse
{
    public int StageId { get; init; }
    public required string StageType { get; init; }
    public bool IsNew { get; init; }
    public required string RoomToken { get; init; }  // 추가
    public string Endpoint { get; init; }            // TCP/WS 접속 주소
    public DateTime Timestamp { get; init; }
}

// Token 생성 서비스
public interface IRoomTokenService
{
    string GenerateToken(int stageId, long accountId, TimeSpan expiry);
    RoomTokenPayload? ValidateToken(string token);
}
```

**신규 파일**: `src/PlayHouse/Infrastructure/Auth/RoomTokenService.cs`

---

#### H2. Token 검증

**수정 파일**: `src/PlayHouse/Infrastructure/Http/PlayHouseServer.cs`

**현재 상태**: 소켓 연결 시 토큰 검증 없음

**구현 내용**:
1. 첫 번째 메시지에서 토큰 추출 (인증 패킷)
2. 토큰 검증 및 accountId/stageId 추출
3. SessionInfo에 accountId 매핑
4. 검증 실패 시 연결 종료

```csharp
// PlayHouseServer.cs OnTcpMessageReceived 수정
private void OnTcpMessageReceived(long sessionId, ReadOnlyMemory<byte> data)
{
    var session = _sessionManager.GetSession(sessionId);
    if (session == null) return;

    // 인증되지 않은 세션인 경우 첫 패킷은 인증 패킷이어야 함
    if (!session.IsAuthenticated)
    {
        var authResult = ProcessAuthPacket(sessionId, data);
        if (!authResult.Success)
        {
            _tcpServer.DisconnectSession(sessionId);
        }
        return;
    }

    // 인증된 세션은 일반 메시지 처리
    ProcessGamePacket(session, data);
}
```

---

#### H3. Reconnection Actor 유지

**확인 필요 파일**:
- `src/PlayHouse/Core/Stage/StageContext.cs`
- `src/PlayHouse/Core/Session/SessionManager.cs`

**확인 사항**:
1. Actor가 Stage에 남아있는 상태에서 Session 연결이 끊어지는 경우
2. 새 Session으로 재연결 시 기존 Actor에 재매핑되는지
3. OnActorConnectionChanged(false) → OnActorConnectionChanged(true) 플로우

**구현 필요 시**:
```csharp
// SessionManager에 추가
public bool ReassociateSession(long oldSessionId, long newSessionId, long accountId)
{
    // 1. 기존 세션의 accountId 확인
    // 2. 새 세션에 accountId 매핑
    // 3. Stage에 연결 상태 변경 알림
}
```

---

### Phase 2: Infrastructure Hardening

#### M4. System.Diagnostics.Metrics 구현

**신규 파일**: `src/PlayHouse/Infrastructure/Metrics/PlayHouseMetrics.cs`

```csharp
using System.Diagnostics.Metrics;

namespace PlayHouse.Infrastructure.Metrics;

public static class PlayHouseMetrics
{
    public static readonly Meter Meter = new("PlayHouse", "1.0.0");

    // Counters (monotonic)
    public static readonly Counter<long> MessagesReceived =
        Meter.CreateCounter<long>("playhouse.messages.received", "messages");
    public static readonly Counter<long> MessagesSent =
        Meter.CreateCounter<long>("playhouse.messages.sent", "messages");
    public static readonly Counter<long> ConnectionsTotal =
        Meter.CreateCounter<long>("playhouse.connections.total", "connections");
    public static readonly Counter<long> ErrorsTotal =
        Meter.CreateCounter<long>("playhouse.errors.total", "errors");

    // UpDownCounters
    public static readonly UpDownCounter<int> ActiveConnections =
        Meter.CreateUpDownCounter<int>("playhouse.connections.active", "connections");
    public static readonly UpDownCounter<int> ActiveStages =
        Meter.CreateUpDownCounter<int>("playhouse.stages.active", "stages");
    public static readonly UpDownCounter<int> ActiveActors =
        Meter.CreateUpDownCounter<int>("playhouse.actors.active", "actors");

    // Histograms
    public static readonly Histogram<double> MessageProcessingDuration =
        Meter.CreateHistogram<double>("playhouse.message.processing.duration", "ms");
    public static readonly Histogram<int> PacketSize =
        Meter.CreateHistogram<int>("playhouse.packet.size", "bytes");
}
```

**사용 예시**:
```csharp
// PacketDispatcher.cs에서
PlayHouseMetrics.MessagesReceived.Add(1, new KeyValuePair<string, object?>("stage_type", stageType));

// 시간 측정
var sw = Stopwatch.StartNew();
await DispatchAsync(packet);
PlayHouseMetrics.MessageProcessingDuration.Record(sw.Elapsed.TotalMilliseconds);
```

---

### Phase 3: Testing

#### M5. E2E 테스트

**신규 파일**: `tests/PlayHouse.Tests.E2E/ClientServerTests.cs`

```csharp
public class ClientServerTests : IAsyncLifetime
{
    private IHost _serverHost = null!;
    private IPlayHouseClient _client = null!;

    public async Task InitializeAsync()
    {
        // 서버 시작
        _serverHost = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddPlayHouse(options =>
                {
                    options.Ip = "127.0.0.1";
                    options.Port = 0; // 랜덤 포트
                });
                services.AddStageType<TestStage>("TestRoom");
            })
            .Build();

        await _serverHost.StartAsync();

        // 클라이언트 생성
        _client = new PlayHouseClient();
    }

    [Fact]
    public async Task FullGameFlowTest()
    {
        // 1. HTTP로 Room 생성
        // 2. 토큰으로 소켓 연결
        // 3. 메시지 송수신
        // 4. 연결 종료
    }
}
```

#### M6. HTTP API 테스트

**신규 파일**: `tests/PlayHouse.Tests.Integration/Http/RoomControllerTests.cs`

```csharp
public class RoomControllerTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;

    public RoomControllerTests(WebApplicationFactory<Program> factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task GetOrCreateRoom_ReturnsToken()
    {
        var response = await _client.PostAsJsonAsync("/api/rooms/get-or-create", new
        {
            StageType = "TestRoom"
        });

        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<GetOrCreateRoomResponse>();

        Assert.NotNull(result);
        Assert.True(result.StageId > 0);
        Assert.False(string.IsNullOrEmpty(result.RoomToken));
    }
}
```

---

## 5. 테스트 보강 계획

### 현재 테스트 현황

| 테스트 유형 | 파일 수 | 테스트 수 | 커버리지 |
|------------|--------|----------|---------|
| Unit Tests | 4 | 35 | 낮음 |
| Integration Tests | 5 | 68 | 중간 |
| E2E Tests | 0 | 0 | 없음 |
| HTTP API Tests | 0 | 0 | 없음 |
| Connector Tests | 0 | 0 | 없음 |

### 목표 테스트 구조

```
tests/
├── PlayHouse.Tests.Unit/              # 유닛 테스트
│   ├── Core/
│   │   ├── Session/
│   │   ├── Stage/
│   │   ├── Timer/
│   │   └── Messaging/
│   └── Infrastructure/
│       ├── Serialization/
│       └── Auth/                      # 신규
│
├── PlayHouse.Tests.Integration/       # 통합 테스트
│   ├── Core/                          # 기존
│   └── Http/                          # 신규
│       ├── RoomControllerTests.cs
│       └── HealthControllerTests.cs
│
├── PlayHouse.Tests.E2E/               # E2E 테스트 (신규)
│   ├── ClientServerTests.cs
│   └── ReconnectionTests.cs
│
└── PlayHouse.Connector.Tests/         # Connector 테스트 (신규)
    ├── PlayHouseClientTests.cs
    ├── ConnectionTests.cs
    └── ProtocolTests.cs
```

---

## 6. 체크리스트

### Phase 1: Token & Authentication

- [ ] **H1. Room Token 발급**
  - [ ] `IRoomTokenService` 인터페이스 정의
  - [ ] `RoomTokenService` 구현 (HMAC-SHA256)
  - [ ] `GetOrCreateRoomResponse`에 `RoomToken` 추가
  - [ ] `RoomController` 수정
  - [ ] DI 등록

- [ ] **H2. Token 검증**
  - [ ] `PlayHouseServer`에 인증 로직 추가
  - [ ] 인증 패킷 처리
  - [ ] 인증 실패 시 연결 종료
  - [ ] `SessionInfo`에 인증 상태 추가

- [ ] **H3. Reconnection 확인**
  - [ ] 현재 구현 동작 확인
  - [ ] 필요 시 Actor 유지 로직 추가
  - [ ] 테스트 작성

### Phase 2: Infrastructure

- [ ] **M1-M3. Transport 확인**
  - [ ] TLS 지원 여부 확인
  - [ ] Heartbeat 구현 여부 확인
  - [ ] Timeout 처리 확인

- [ ] **M4. Metrics**
  - [ ] `PlayHouseMetrics.cs` 생성
  - [ ] 주요 컴포넌트에 메트릭 추가
  - [ ] OpenTelemetry 연동 (선택)

### Phase 3: Testing

- [ ] **M5. E2E 테스트**
  - [ ] 테스트 프로젝트 생성
  - [ ] 서버-클라이언트 통합 테스트
  - [ ] 재연결 테스트

- [ ] **M6. HTTP API 테스트**
  - [ ] `WebApplicationFactory` 설정
  - [ ] RoomController 테스트
  - [ ] HealthController 테스트

- [ ] **M7-M8. Connector**
  - [ ] TODO 주석 구현
  - [ ] 테스트 프로젝트 생성

### Phase 4: Operations

- [ ] **L1. OpenTelemetry**
- [ ] **L2-L4. Admin API**

---

## 참고 문서

- `doc/specs/05-http-api.md` - HTTP API 스펙
- `doc/specs/06-socket-transport.md` - 소켓 전송 스펙
- `doc/specs/07-client-protocol.md` - 클라이언트 프로토콜 스펙
- `doc/specs/08-metrics-observability.md` - 메트릭 스펙
- `doc/specs/09-connector.md` - Connector 스펙
- `doc/specs/10-testing-spec.md` - 테스트 스펙

---

## 변경 이력

| 날짜 | 버전 | 변경 내용 |
|------|------|----------|
| 2025-12-09 | 2.0 | 스펙 vs 구현 리뷰 결과 반영, 기존 문서 업데이트 |
