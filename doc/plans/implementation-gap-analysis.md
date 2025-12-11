# PlayHouse-NET Implementation Gap Analysis

> **Document Purpose**: Spec vs Implementation 분석 결과 통합 및 구현 계획
> **Generated**: 2025-12-09
> **Context Reset Safe**: 이 문서만으로 작업 재개 가능

---

## Executive Summary

| Spec Document | Total Gaps | Critical | Coverage |
|--------------|-----------|----------|----------|
| 01-architecture.md | 12 | 4 | ~85% |
| 02-packet-structure.md | 7 | 2 | ~85% |
| 03-stage-actor-model.md | 8 | 2 | ~85% |
| 04-timer-system.md | 11 | 5 | ~60% |
| 05-http-api.md | 18 | 7 | ~35% |
| 06-socket-transport.md | 14 | 5 | ~65% |
| 07-client-protocol.md | 15 | 6 | ~45% |
| 08-metrics-observability.md | 15 | 8 | 0% |
| 09-connector.md | 15 | 5 | ~75% |
| 10-testing-spec.md | 45 | 15 | ~35% |
| 11-event-loop-messaging.md | 8 | 2 | ~75% |
| 12-bootstrap.md | 7 | 3 | ~65% |
| **TOTAL** | **175** | **64** | **~55%** |

---

## Phase 1: Critical Foundation (Priority: Immediate)

### 1.1 Observability Infrastructure (08-metrics)

**Gap Summary**: 전체 Observability 시스템 미구현 (0% coverage)

| Gap ID | Description | Files to Create/Modify | Complexity |
|--------|-------------|----------------------|------------|
| 08-GAP-001 | PlayHouseMetrics Core Class | `src/PlayHouse/Infrastructure/Metrics/PlayHouseMetrics.cs` | High |
| 08-GAP-002 | OpenTelemetry DI Configuration | `src/PlayHouse/Infrastructure/Http/PlayHouseServiceExtensions.cs` | Medium |
| 08-GAP-003 | Distributed Tracing ActivitySource | `src/PlayHouse/Infrastructure/Metrics/PlayHouseActivitySource.cs` | Low |
| 08-GAP-004 | Tag Cardinality Management | `src/PlayHouse/Infrastructure/Metrics/MetricTags.cs` | Low |
| 08-GAP-005 | MetricsOptions Configuration | `src/PlayHouse/Infrastructure/Metrics/MetricsOptions.cs` | Low |

**Implementation Details**:
```csharp
// 08-GAP-001: PlayHouseMetrics.cs
public sealed class PlayHouseMetrics : IDisposable
{
    private readonly Meter _meter;

    // Counters
    private readonly Counter<long> _messagesReceived;
    private readonly Counter<long> _messagesSent;
    private readonly Counter<long> _connectionsTotal;
    private readonly Counter<long> _errorsTotal;

    // Histograms
    private readonly Histogram<double> _messageProcessingDuration;
    private readonly Histogram<long> _packetSize;

    // UpDownCounters
    private readonly UpDownCounter<int> _activeConnections;
    private readonly UpDownCounter<int> _activeStages;
    private readonly UpDownCounter<int> _activeActors;

    // Methods
    public void RecordMessageReceived(string stageType, string msgId);
    public void RecordProcessingTime(string stageType, double durationMs);
    public void ConnectionOpened(string transport);
    public void ConnectionClosed(string transport, string reason);
    public void StageCreated(string stageType);
    public void StageClosed(string stageType);
    public void ActorJoined(string stageType);
    public void ActorLeft(string stageType);
    public void RecordError(string errorType, string stageType);
}
```

**Dependencies**:
- OpenTelemetry.Extensions.Hosting (v1.9 - already in project)
- OpenTelemetry.Instrumentation.AspNetCore (add)
- OpenTelemetry.Instrumentation.Runtime (add)
- OpenTelemetry.Exporter.Prometheus.AspNetCore (add)

---

### 1.2 Security Implementation (01-architecture)

**Gap Summary**: Production Security 기능 미구현

| Gap ID | Description | Files to Create/Modify | Complexity |
|--------|-------------|----------------------|------------|
| 01-GAP-011 | Rate Limiter | `src/PlayHouse/Infrastructure/Security/RateLimiter.cs` | Medium |
| 01-GAP-011 | Packet Validator | `src/PlayHouse/Infrastructure/Security/PacketValidator.cs` | Low |
| 01-GAP-011 | Stage Access Control | `src/PlayHouse/Infrastructure/Security/StageAccessControl.cs` | Medium |
| 01-GAP-001 | HTTPS Transport | `src/PlayHouse/Infrastructure/Transport/Https/` | High |

**Implementation Details**:
```csharp
// RateLimiter.cs - Token Bucket Algorithm
public class RateLimiter
{
    private readonly ConcurrentDictionary<string, TokenBucket> _buckets;

    public bool Allow(string clientId, int tokens = 1);
    public void Reset(string clientId);
}

// PacketValidator.cs
public class PacketValidator
{
    public bool ValidateSize(int size, int maxSize = 2_097_152); // 2MB
    public bool ValidateMsgIdLength(int length, int maxLength = 255);
}
```

---

### 1.3 Client Protocol Implementation (07-client-protocol)

**Gap Summary**: Reconnection 및 Connection State Management 미구현

| Gap ID | Description | Files to Create/Modify | Complexity |
|--------|-------------|----------------------|------------|
| 07-GAP-007 | Reconnection Logic | `src/PlayHouse/Core/Session/RoomTokenManager.cs`, `PlayHouseServer.cs` | High |
| 07-GAP-008 | Reconnection Timeout | `src/PlayHouse/Core/Stage/ActorContext.cs`, `StageContext.cs` | Medium |
| 07-GAP-015 | OnActorConnectionChanged Integration | `src/PlayHouse/Core/Stage/StageContext.cs` | Low |
| 07-GAP-002 | JoinRoomRes Structure | `src/PlayHouse/Proto/playhouse_internal.proto` | Medium |

**Implementation Details**:
```protobuf
// playhouse_internal.proto additions
message JoinRoomRes {
  StageInfo stage_info = 1;
  repeated PlayerInfo players = 2;
  bytes game_state = 3;
  bool reconnected = 4;
}

message StageInfo {
  int32 stage_id = 1;
  int32 max_players = 2;
  int32 current_players = 3;
}

message PlayerInfo {
  int64 account_id = 1;
  string name = 2;
}
```

```csharp
// StageContext.cs - Reconnection flow
public async Task<bool> ReconnectActorAsync(long accountId, long newSessionId)
{
    if (!_actorPool.TryGetActor(accountId, out var actorContext))
        return false;

    // Update session reference without calling OnCreate
    actorContext.UpdateSession(newSessionId);
    actorContext.SetConnected(true);

    // Call OnAuthenticate (NOT OnJoinRoom)
    await actorContext.OnAuthenticateAsync(authData);

    // Notify connection change
    await _userStage.OnActorConnectionChanged(actorContext.Actor, true, null);

    return true;
}
```

---

## Phase 2: HTTP API & Backend SDK (Priority: High)

### 2.1 HTTP API Implementation (05-http-api)

**Gap Summary**: Management API 및 인증 인프라 미구현 (~35% coverage)

| Gap ID | Description | Files to Create/Modify | Complexity |
|--------|-------------|----------------------|------------|
| 05-GAP-013 | JWT Authentication | `src/PlayHouse/Infrastructure/Http/AuthController.cs` | Medium |
| 05-GAP-006 | Server Info API | `src/PlayHouse/Infrastructure/Http/ServerController.cs` | Low |
| 05-GAP-007 | Server Stats API | Same file | Medium |
| 05-GAP-008 | Stage Create API | `src/PlayHouse/Infrastructure/Http/StageController.cs` | Low |
| 05-GAP-009 | Stage List API | Same file | Low |
| 05-GAP-012 | Session Management API | `src/PlayHouse/Infrastructure/Http/SessionController.cs` | Medium |
| 05-GAP-015 | Swagger Configuration | Startup configuration | Low |
| 05-GAP-016 | CORS Configuration | Startup configuration | Low |
| 05-GAP-017 | Rate Limiting | Startup configuration | Low |
| 05-GAP-001 | Backend SDK Package | `src/PlayHouse.Backend/` (new project) | High |

**Implementation Details**:
```csharp
// ServerController.cs
[ApiController]
[Route("api/server")]
public class ServerController : ControllerBase
{
    [HttpGet("info")]
    public ActionResult<ServerInfoResponse> GetInfo()
    {
        return Ok(new ServerInfoResponse
        {
            ServerName = _options.ServerName,
            Version = Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "1.0.0",
            StartTime = _startTime,
            Uptime = (int)(DateTime.UtcNow - _startTime).TotalSeconds,
            Platform = Environment.OSVersion.Platform.ToString(),
            DotnetVersion = Environment.Version.ToString(),
            Endpoints = new Dictionary<string, string>
            {
                ["http"] = $"http://localhost:{_options.HttpPort}",
                ["tcp"] = $"tcp://localhost:{_options.TcpPort}",
                ["websocket"] = $"ws://localhost:{_options.WebSocketPort}"
            }
        });
    }

    [HttpGet("stats")]
    [Authorize]
    public ActionResult<ServerStatsResponse> GetStats()
    {
        return Ok(new ServerStatsResponse
        {
            Timestamp = DateTime.UtcNow,
            Stages = new StageStats { Active = _stagePool.Count, Total = _stagePool.TotalCreated },
            Actors = new ActorStats { Active = _actorCount, Total = _totalActorsJoined },
            Sessions = new SessionStats { Active = _sessionManager.ActiveCount },
            Messages = new MessageStats { Received = _metrics.MessagesReceived, Sent = _metrics.MessagesSent },
            Performance = new PerformanceStats { AverageProcessingTimeMs = _metrics.AverageProcessingTime }
        });
    }
}
```

---

### 2.2 Connector Enhancements (09-connector)

**Gap Summary**: Heartbeat, Compression, Test Utilities 미구현

| Gap ID | Description | Files to Create/Modify | Complexity |
|--------|-------------|----------------------|------------|
| 09-GAP-002 | Heartbeat Implementation | `connector/PlayHouse.Connector/PlayHouseClient.cs` | Medium |
| 09-GAP-003 | Compression Support | `connector/PlayHouse.Connector/Protocol/PacketEncoder.cs` | Medium |
| 09-GAP-001 | Test Helper Utilities | `connector/PlayHouse.Connector/TestHelpers/PlayHouseTestHelper.cs` | Low |
| 09-GAP-015 | Message ID Mapping | `connector/PlayHouse.Connector/Protocol/MessageRegistry.cs` | Medium |

**Implementation Details**:
```csharp
// PlayHouseClient.cs - Heartbeat
private Timer? _heartbeatTimer;

private void StartHeartbeat()
{
    _heartbeatTimer = new Timer(
        async _ => await SendHeartbeatAsync(),
        null,
        _options.HeartbeatInterval,
        _options.HeartbeatInterval
    );
}

private async Task SendHeartbeatAsync()
{
    if (_state != ConnectionState.Connected) return;

    try
    {
        await SendAsync(new HeartbeatRequest());
        _lastHeartbeatSent = DateTime.UtcNow;
    }
    catch (Exception ex)
    {
        _logger?.LogWarning(ex, "Heartbeat failed");
    }
}
```

---

## Phase 3: Testing Infrastructure (Priority: High)

### 3.1 Test Coverage Gaps (10-testing-spec)

**Gap Summary**: Comprehensive Test Suite 미구현 (~35% coverage)

| Category | Gaps | Test Files to Create |
|----------|------|---------------------|
| Stage Lifecycle | 10-GAP-001 | `tests/PlayHouse.Tests.Integration/Core/StageLifecycleTests.cs` |
| Connection State | 10-GAP-004 | `tests/PlayHouse.Tests.Integration/Core/ConnectionStateTests.cs` |
| Message Routing | 10-GAP-003 | `tests/PlayHouse.Tests.Integration/Core/MessageRoutingTests.cs` |
| Timer System | 10-GAP-005, 04-GAP-001 | `tests/PlayHouse.Tests.Integration/Core/TimerSystemTests.cs` |
| HTTP API | 10-GAP-006 | `tests/PlayHouse.Tests.Integration/Http/HttpApiTests.cs` |
| Metrics | 08-GAP-008~014 | `tests/PlayHouse.Tests.Integration/Metrics/` |
| Event Loop | 11-GAP-001 | `tests/PlayHouse.Tests.Integration/Core/EventLoopTests.cs` |

**Test Helper Infrastructure**:
```csharp
// tests/PlayHouse.Tests.Shared/Helpers/EventLoopTestHelper.cs
public static class EventLoopTestHelper
{
    public static async Task WaitUntilAsync(
        Func<bool> condition,
        int timeoutMs = 5000,
        int checkIntervalMs = 10)
    {
        var sw = Stopwatch.StartNew();
        while (!condition())
        {
            if (sw.ElapsedMilliseconds > timeoutMs)
                throw new TimeoutException($"Condition not met within {timeoutMs}ms");
            await Task.Delay(checkIntervalMs);
        }
    }

    public static async Task WaitForEventLoopIdleAsync(BaseStage stage)
    {
        await WaitUntilAsync(() => !stage.IsProcessing && stage.QueueDepth == 0);
    }
}

// FakeTestStage for unit testing
public class FakeTestStage : BaseStage
{
    public List<RoutePacket> DispatchedMessages { get; } = new();
    public int DispatchCallCount { get; private set; }

    protected override async ValueTask DispatchAsync(RoutePacket routePacket)
    {
        DispatchedMessages.Add(routePacket);
        DispatchCallCount++;
        await Task.CompletedTask;
    }
}
```

---

## Phase 4: Protocol & Transport (Priority: Medium)

### 4.1 Packet Structure Fixes (02-packet-structure)

| Gap ID | Description | Files to Modify | Complexity |
|--------|-------------|-----------------|------------|
| 02-GAP-002 | StageId Type (int → long) | Multiple files | High (Breaking) |
| 02-GAP-001 | ServiceId Field | `PacketHeader.cs`, `IPacket.cs` | Low |
| 02-GAP-004 | Packet Size Validation | `PacketSerializer.cs`, new `PacketException.cs` | Low |
| 02-GAP-005 | PacketCache for Broadcast | `src/PlayHouse/Core/Messaging/PacketCache.cs` | Medium |

**StageId Migration Plan**:
```csharp
// BREAKING CHANGE: int → long
// Files requiring update:
// 1. src/PlayHouse/Abstractions/PacketHeader.cs (StageId: int → long)
// 2. src/PlayHouse/Abstractions/IPacket.cs (StageId: int → long)
// 3. src/PlayHouse/Abstractions/RoutePacket.cs (StageId: int → long)
// 4. src/PlayHouse/Infrastructure/Serialization/SimplePacket.cs
// 5. All Stage-related classes (BaseStage, StageContext, StagePool, etc.)

// Migration steps:
// 1. Create feature branch
// 2. Update all type definitions
// 3. Update serialization (already writes long)
// 4. Update all usages
// 5. Run full test suite
// 6. Update client connector
```

### 4.2 Socket Transport Enhancements (06-socket-transport)

| Gap ID | Description | Files to Create/Modify | Complexity |
|--------|-------------|----------------------|------------|
| 06-GAP-003 | Kestrel ConnectionHandler | `src/PlayHouse/Infrastructure/Transport/Tcp/TcpConnectionHandler.cs` | Medium |
| 06-GAP-007 | Heartbeat Service | `src/PlayHouse/Core/Session/SessionService.cs` | Medium |
| 06-GAP-008 | Reconnect Token System | `src/PlayHouse/Core/Session/ReconnectTokenService.cs` | Medium |
| 06-GAP-010 | DDoS Protection | `src/PlayHouse/Infrastructure/Transport/Tcp/TcpServer.cs` | Low |
| 06-GAP-011 | Rate Limiter | `src/PlayHouse/Core/Security/RateLimiter.cs` | Medium |

---

## Phase 5: Documentation & Examples (Priority: Low)

### 5.1 Documentation Gaps

| Gap ID | Description | Files to Create |
|--------|-------------|-----------------|
| 03-GAP-002 | Handler Separation Pattern Examples | `examples/Stages/RoomMessageHandler.cs` |
| 04-GAP-002 | Timer System E2E Examples | `examples/GameTickExample/`, etc. |
| 07-GAP-012 | Client SDK Reference | `examples/Clients/Unity/`, `examples/Clients/TypeScript/` |
| 11-GAP-008 | Event Loop Code Examples | `samples/EventLoop/` |
| 08-GAP-015 | Prometheus/Grafana Config | `deploy/prometheus/`, `deploy/grafana/` |

---

## Implementation Order (TODO Format)

### Immediate (Week 1-2)
```
[ ] 08-GAP-001: Implement PlayHouseMetrics core class
[ ] 08-GAP-003: Implement PlayHouseActivitySource
[ ] 08-GAP-004: Implement MetricTags normalization
[ ] 08-GAP-005: Implement MetricsOptions
[ ] 08-GAP-002: Configure OpenTelemetry DI
[ ] 08-GAP-006: Integrate metrics into StageContext
[ ] 08-GAP-007: Integrate metrics into Sessions
```

### Week 3-4
```
[ ] 07-GAP-015: Integrate OnActorConnectionChanged calls
[ ] 07-GAP-008: Implement reconnection timeout
[ ] 07-GAP-007: Implement reconnection logic
[ ] 07-GAP-002: Update JoinRoomRes protobuf
[ ] 01-GAP-011: Implement RateLimiter
[ ] 01-GAP-011: Implement PacketValidator
```

### Week 5-6
```
[ ] 05-GAP-013: Implement JWT Authentication
[ ] 05-GAP-015: Configure Swagger
[ ] 05-GAP-006: Implement Server Info API
[ ] 05-GAP-007: Implement Server Stats API
[ ] 05-GAP-008~011: Implement Stage Management APIs
[ ] 05-GAP-012: Implement Session Management API
```

### Week 7-8
```
[ ] 09-GAP-002: Implement Connector Heartbeat
[ ] 09-GAP-003: Implement Compression support
[ ] 09-GAP-001: Create Test Helper utilities
[ ] 04-GAP-001: Implement Timer Integration Tests
[ ] 10-GAP-001~006: Implement Integration Tests
```

### Week 9-10
```
[ ] 02-GAP-002: StageId type migration (int → long)
[ ] 06-GAP-003: Implement Kestrel ConnectionHandler
[ ] 06-GAP-007: Implement Heartbeat Service
[ ] 06-GAP-008: Implement Reconnect Token System
```

### Week 11-12
```
[ ] 05-GAP-001: Create PlayHouse.Backend SDK package
[ ] 01-GAP-001: Implement HTTPS Transport
[ ] 12-GAP-001: Implement PlayHouseHostImpl
[ ] Documentation and Examples
```

---

## Risk Assessment

### High Risk Items
1. **02-GAP-002 (StageId int→long)**: Breaking protocol change affecting all components
2. **08-* (Observability)**: Complete new system, high complexity
3. **07-GAP-007 (Reconnection)**: Complex state management across multiple systems

### Medium Risk Items
1. **05-* (HTTP API)**: Many endpoints, but straightforward implementation
2. **06-* (Transport)**: Security-critical components
3. **10-* (Testing)**: Large volume, but no architectural risk

### Low Risk Items
1. Documentation and examples
2. Configuration options alignment
3. API naming consistency fixes

---

## Dependencies Graph

```
08-metrics (foundation)
    └── All other components (metrics integration)

07-client-protocol
    └── 06-socket-transport (transport layer)
    └── 03-stage-actor-model (actor lifecycle)

05-http-api
    └── 08-metrics (stats API)
    └── 07-client-protocol (token generation)

09-connector
    └── 02-packet-structure (protocol)
    └── 07-client-protocol (reconnection)

10-testing
    └── All components (test coverage)
```

---

## Notes for Context Reset

이 문서를 참조하여 작업을 재개할 때:

1. **Current Phase 확인**: TODO Format 섹션에서 현재 진행 상태 확인
2. **Gap ID로 검색**: 각 Gap ID는 해당 spec 문서의 섹션과 연결됨
3. **파일 경로**: 모든 파일 경로는 프로젝트 루트 기준
4. **Implementation Details**: 코드 예시는 spec 기반으로 작성됨, 실제 구현 시 기존 코드 패턴 따를 것
5. **Dependencies**: 구현 순서는 dependencies를 고려하여 설계됨

---

*Generated by parallel arch agent analysis on 2025-12-09*
