# PlayHouse-NET 메트릭 및 옵저버빌리티

## 1. 개요

PlayHouse-NET은 .NET 네이티브 메트릭 API와 OpenTelemetry를 활용하여 서버 상태를 모니터링합니다.

### 1.1 핵심 기술 스택

| 구성요소 | 기술 | 설명 |
|---------|------|------|
| 메트릭 API | `System.Diagnostics.Metrics` | .NET 6+ 네이티브 고성능 메트릭 |
| 표준 | OpenTelemetry | 벤더 중립적 옵저버빌리티 표준 |
| Exporter | Prometheus, OTLP | 메트릭 수집/전송 |
| 시각화 | Grafana, Jaeger | 대시보드 및 트레이싱 |

### 1.2 설계 원칙

```
┌─────────────────────────────────────────────────────────────┐
│                    Observability Pillars                     │
├─────────────────────────────────────────────────────────────┤
│                                                              │
│   Metrics          Logging           Tracing                │
│   (수치 데이터)     (이벤트 기록)      (요청 추적)             │
│                                                              │
│   ┌──────────┐    ┌──────────┐    ┌──────────┐              │
│   │ Counter  │    │ ILogger  │    │ Activity │              │
│   │Histogram │    │Structured│    │  Span    │              │
│   │  Gauge   │    │  Logging │    │ Context  │              │
│   └──────────┘    └──────────┘    └──────────┘              │
│        │               │               │                     │
│        └───────────────┼───────────────┘                     │
│                        │                                     │
│              ┌─────────▼─────────┐                           │
│              │   OpenTelemetry   │                           │
│              │   Unified Export  │                           │
│              └─────────┬─────────┘                           │
│                        │                                     │
│         ┌──────────────┼──────────────┐                      │
│         ▼              ▼              ▼                      │
│    Prometheus      Grafana        Jaeger                    │
│                                                              │
└─────────────────────────────────────────────────────────────┘
```

## 2. System.Diagnostics.Metrics API

### 2.1 Meter 및 Instrument 타입

```csharp
#nullable enable

using System.Diagnostics.Metrics;

/// <summary>
/// PlayHouse 서버 메트릭 수집기.
/// </summary>
/// <remarks>
/// IMeterFactory를 통해 DI로 주입받아 싱글톤으로 사용합니다.
/// Meter는 매번 생성하지 않고 재사용해야 합니다.
/// </remarks>
public sealed class PlayHouseMetrics : IDisposable
{
    private readonly Meter _meter;

    // ═══════════════════════════════════════════════════════════
    // Counters - 누적 값 (증가만 가능)
    // ═══════════════════════════════════════════════════════════

    /// <summary>수신된 총 메시지 수</summary>
    private readonly Counter<long> _messagesReceived;

    /// <summary>송신된 총 메시지 수</summary>
    private readonly Counter<long> _messagesSent;

    /// <summary>총 연결 수 (누적)</summary>
    private readonly Counter<long> _connectionsTotal;

    /// <summary>총 연결 해제 수</summary>
    private readonly Counter<long> _disconnectionsTotal;

    /// <summary>생성된 Stage 수 (누적)</summary>
    private readonly Counter<long> _stagesCreated;

    /// <summary>에러 발생 수</summary>
    private readonly Counter<long> _errorsTotal;

    // ═══════════════════════════════════════════════════════════
    // Histograms - 분포 측정 (latency, size 등)
    // ═══════════════════════════════════════════════════════════

    /// <summary>메시지 처리 시간 (ms)</summary>
    private readonly Histogram<double> _messageProcessingDuration;

    /// <summary>패킷 크기 (bytes)</summary>
    private readonly Histogram<int> _packetSize;

    /// <summary>Stage 참여 시간 (ms)</summary>
    private readonly Histogram<double> _joinStageDuration;

    // ═══════════════════════════════════════════════════════════
    // UpDownCounters - 증감 가능 (현재 상태)
    // ═══════════════════════════════════════════════════════════

    /// <summary>현재 활성 연결 수</summary>
    private readonly UpDownCounter<int> _activeConnections;

    /// <summary>현재 활성 Stage 수</summary>
    private readonly UpDownCounter<int> _activeStages;

    /// <summary>현재 활성 Actor 수</summary>
    private readonly UpDownCounter<int> _activeActors;

    /// <summary>현재 대기 중인 메시지 큐 깊이</summary>
    private readonly UpDownCounter<int> _pendingMessages;

    // ═══════════════════════════════════════════════════════════
    // ObservableGauges - 폴링 방식 (현재 값 조회)
    // ═══════════════════════════════════════════════════════════

    /// <summary>메모리 사용량 (bytes)</summary>
    private readonly ObservableGauge<long> _memoryUsage;

    /// <summary>GC 힙 크기 (bytes)</summary>
    private readonly ObservableGauge<long> _gcHeapSize;

    /// <summary>ThreadPool 스레드 수</summary>
    private readonly ObservableGauge<int> _threadPoolThreads;

    public PlayHouseMetrics(IMeterFactory meterFactory)
    {
        // Meter 생성 (버전 정보 포함)
        _meter = meterFactory.Create(new MeterOptions("PlayHouse.Server")
        {
            Version = "1.0.0"
        });

        // ───────────────────────────────────────────────────────
        // Counters 초기화
        // ───────────────────────────────────────────────────────
        _messagesReceived = _meter.CreateCounter<long>(
            name: "playhouse.messages.received",
            unit: "{message}",
            description: "Total number of messages received");

        _messagesSent = _meter.CreateCounter<long>(
            name: "playhouse.messages.sent",
            unit: "{message}",
            description: "Total number of messages sent");

        _connectionsTotal = _meter.CreateCounter<long>(
            name: "playhouse.connections.total",
            unit: "{connection}",
            description: "Total number of connections established");

        _disconnectionsTotal = _meter.CreateCounter<long>(
            name: "playhouse.disconnections.total",
            unit: "{connection}",
            description: "Total number of disconnections");

        _stagesCreated = _meter.CreateCounter<long>(
            name: "playhouse.stages.created",
            unit: "{stage}",
            description: "Total number of stages created");

        _errorsTotal = _meter.CreateCounter<long>(
            name: "playhouse.errors.total",
            unit: "{error}",
            description: "Total number of errors");

        // ───────────────────────────────────────────────────────
        // Histograms 초기화
        // ───────────────────────────────────────────────────────
        _messageProcessingDuration = _meter.CreateHistogram<double>(
            name: "playhouse.message.duration",
            unit: "ms",
            description: "Message processing duration in milliseconds");

        _packetSize = _meter.CreateHistogram<int>(
            name: "playhouse.packet.size",
            unit: "By",
            description: "Packet size in bytes");

        _joinStageDuration = _meter.CreateHistogram<double>(
            name: "playhouse.stage.join.duration",
            unit: "ms",
            description: "Time to join a stage in milliseconds");

        // ───────────────────────────────────────────────────────
        // UpDownCounters 초기화
        // ───────────────────────────────────────────────────────
        _activeConnections = _meter.CreateUpDownCounter<int>(
            name: "playhouse.connections.active",
            unit: "{connection}",
            description: "Current number of active connections");

        _activeStages = _meter.CreateUpDownCounter<int>(
            name: "playhouse.stages.active",
            unit: "{stage}",
            description: "Current number of active stages");

        _activeActors = _meter.CreateUpDownCounter<int>(
            name: "playhouse.actors.active",
            unit: "{actor}",
            description: "Current number of active actors");

        _pendingMessages = _meter.CreateUpDownCounter<int>(
            name: "playhouse.messages.pending",
            unit: "{message}",
            description: "Current number of pending messages in queue");

        // ───────────────────────────────────────────────────────
        // ObservableGauges 초기화 (폴링 콜백)
        // ───────────────────────────────────────────────────────
        _memoryUsage = _meter.CreateObservableGauge(
            name: "playhouse.memory.used",
            observeValue: () => GC.GetTotalMemory(forceFullCollection: false),
            unit: "By",
            description: "Total memory usage in bytes");

        _gcHeapSize = _meter.CreateObservableGauge(
            name: "playhouse.gc.heap.size",
            observeValue: () => GC.GetGCMemoryInfo().HeapSizeBytes,
            unit: "By",
            description: "GC heap size in bytes");

        _threadPoolThreads = _meter.CreateObservableGauge(
            name: "playhouse.threadpool.threads",
            observeValue: () =>
            {
                ThreadPool.GetAvailableThreads(out int workerThreads, out _);
                ThreadPool.GetMaxThreads(out int maxWorkerThreads, out _);
                return maxWorkerThreads - workerThreads;
            },
            unit: "{thread}",
            description: "Number of active ThreadPool threads");
    }

    // ═══════════════════════════════════════════════════════════
    // 메트릭 기록 메서드
    // ═══════════════════════════════════════════════════════════

    /// <summary>메시지 수신 기록</summary>
    public void RecordMessageReceived(string stageType, string msgId)
    {
        _messagesReceived.Add(1,
            new KeyValuePair<string, object?>("stage.type", stageType),
            new KeyValuePair<string, object?>("message.id", msgId));
    }

    /// <summary>메시지 송신 기록</summary>
    public void RecordMessageSent(string stageType, string msgId)
    {
        _messagesSent.Add(1,
            new KeyValuePair<string, object?>("stage.type", stageType),
            new KeyValuePair<string, object?>("message.id", msgId));
    }

    /// <summary>메시지 처리 시간 기록</summary>
    public void RecordProcessingTime(double milliseconds, string stageType, string msgId)
    {
        _messageProcessingDuration.Record(milliseconds,
            new KeyValuePair<string, object?>("stage.type", stageType),
            new KeyValuePair<string, object?>("message.id", msgId));
    }

    /// <summary>패킷 크기 기록</summary>
    public void RecordPacketSize(int bytes, string direction)
    {
        _packetSize.Record(bytes,
            new KeyValuePair<string, object?>("direction", direction));
    }

    /// <summary>연결 수립</summary>
    public void ConnectionOpened(string transport)
    {
        _connectionsTotal.Add(1,
            new KeyValuePair<string, object?>("transport", transport));
        _activeConnections.Add(1,
            new KeyValuePair<string, object?>("transport", transport));
    }

    /// <summary>연결 해제</summary>
    public void ConnectionClosed(string transport, string reason)
    {
        _disconnectionsTotal.Add(1,
            new KeyValuePair<string, object?>("transport", transport),
            new KeyValuePair<string, object?>("reason", reason));
        _activeConnections.Add(-1,
            new KeyValuePair<string, object?>("transport", transport));
    }

    /// <summary>Stage 생성</summary>
    public void StageCreated(string stageType)
    {
        _stagesCreated.Add(1,
            new KeyValuePair<string, object?>("stage.type", stageType));
        _activeStages.Add(1,
            new KeyValuePair<string, object?>("stage.type", stageType));
    }

    /// <summary>Stage 종료</summary>
    public void StageClosed(string stageType)
    {
        _activeStages.Add(-1,
            new KeyValuePair<string, object?>("stage.type", stageType));
    }

    /// <summary>Actor 참여</summary>
    public void ActorJoined(string stageType)
    {
        _activeActors.Add(1,
            new KeyValuePair<string, object?>("stage.type", stageType));
    }

    /// <summary>Actor 퇴장</summary>
    public void ActorLeft(string stageType)
    {
        _activeActors.Add(-1,
            new KeyValuePair<string, object?>("stage.type", stageType));
    }

    /// <summary>에러 기록</summary>
    public void RecordError(string errorType, string stageType)
    {
        _errorsTotal.Add(1,
            new KeyValuePair<string, object?>("error.type", errorType),
            new KeyValuePair<string, object?>("stage.type", stageType));
    }

    /// <summary>Stage 참여 시간 기록</summary>
    public void RecordJoinStageDuration(double milliseconds, string stageType)
    {
        _joinStageDuration.Record(milliseconds,
            new KeyValuePair<string, object?>("stage.type", stageType));
    }

    /// <summary>대기 메시지 수 변경</summary>
    public void UpdatePendingMessages(int delta, string stageType)
    {
        _pendingMessages.Add(delta,
            new KeyValuePair<string, object?>("stage.type", stageType));
    }

    public void Dispose()
    {
        _meter.Dispose();
    }
}
```

### 2.2 Instrument 타입 선택 가이드

```
┌─────────────────────────────────────────────────────────────────┐
│                    Instrument 선택 가이드                         │
├─────────────────────────────────────────────────────────────────┤
│                                                                  │
│  "얼마나 많이?" (누적)              "현재 상태는?"                  │
│       ↓                                  ↓                       │
│   Counter<T>                       UpDownCounter<T>              │
│   - 총 요청 수                      - 현재 연결 수                 │
│   - 총 에러 수                      - 현재 Stage 수               │
│   - 총 바이트 전송                   - 큐 깊이                     │
│                                                                  │
│  "분포는?" (latency, size)          "폴링으로 조회?"               │
│       ↓                                  ↓                       │
│   Histogram<T>                     ObservableGauge<T>            │
│   - 처리 시간                       - 메모리 사용량                │
│   - 패킷 크기                       - CPU 사용률                  │
│   - 응답 시간                       - ThreadPool 상태            │
│                                                                  │
└─────────────────────────────────────────────────────────────────┘
```

| Instrument | 용도 | 예시 |
|------------|------|------|
| `Counter<T>` | 단조 증가 누적 값 | 총 요청 수, 총 에러 수 |
| `UpDownCounter<T>` | 증감 가능한 현재 값 | 활성 연결 수, 큐 깊이 |
| `Histogram<T>` | 값의 분포 측정 | latency, packet size |
| `ObservableGauge<T>` | 폴링 방식 현재 값 | 메모리 사용량, CPU |
| `ObservableCounter<T>` | 폴링 방식 누적 값 | 외부 시스템 카운터 |
| `ObservableUpDownCounter<T>` | 폴링 방식 증감 값 | 외부 시스템 게이지 |

## 3. OpenTelemetry 통합

### 3.1 DI 서비스 등록

```csharp
// Program.cs
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using OpenTelemetry.Logs;

var builder = WebApplication.CreateBuilder(args);

// ═══════════════════════════════════════════════════════════
// OpenTelemetry 설정
// ═══════════════════════════════════════════════════════════
builder.Services.AddOpenTelemetry()
    // ───────────────────────────────────────────────────────
    // Metrics 설정
    // ───────────────────────────────────────────────────────
    .WithMetrics(metrics =>
    {
        metrics
            // .NET 런타임 메트릭 (GC, ThreadPool 등)
            .AddRuntimeInstrumentation()

            // ASP.NET Core 메트릭 (HTTP 요청/응답)
            .AddAspNetCoreInstrumentation()

            // PlayHouse 커스텀 메트릭
            .AddMeter("PlayHouse.Server")

            // Prometheus Exporter (Pull 방식)
            .AddPrometheusExporter()

            // OTLP Exporter (Push 방식 - Grafana, Jaeger 등)
            .AddOtlpExporter(options =>
            {
                options.Endpoint = new Uri("http://localhost:4317");
            });
    })
    // ───────────────────────────────────────────────────────
    // Tracing 설정 (분산 추적)
    // ───────────────────────────────────────────────────────
    .WithTracing(tracing =>
    {
        tracing
            // ASP.NET Core 요청 추적
            .AddAspNetCoreInstrumentation()

            // HTTP 클라이언트 요청 추적
            .AddHttpClientInstrumentation()

            // PlayHouse 커스텀 Activity Source
            .AddSource("PlayHouse.Server")

            // OTLP Exporter
            .AddOtlpExporter(options =>
            {
                options.Endpoint = new Uri("http://localhost:4317");
            });
    });

// ───────────────────────────────────────────────────────────
// Logging 설정 (OpenTelemetry 연동)
// ───────────────────────────────────────────────────────────
builder.Logging.AddOpenTelemetry(logging =>
{
    logging.IncludeScopes = true;
    logging.IncludeFormattedMessage = true;
    logging.AddOtlpExporter(options =>
    {
        options.Endpoint = new Uri("http://localhost:4317");
    });
});

// ───────────────────────────────────────────────────────────
// PlayHouse 서비스 등록
// ───────────────────────────────────────────────────────────
builder.Services.AddSingleton<PlayHouseMetrics>();
builder.Services.AddPlayHouse(builder.Configuration);

var app = builder.Build();

// Prometheus 스크래핑 엔드포인트
app.MapPrometheusScrapingEndpoint();

app.Run();
```

### 3.2 분산 추적 (Distributed Tracing)

```csharp
using System.Diagnostics;

/// <summary>
/// PlayHouse 분산 추적 지원.
/// </summary>
public static class PlayHouseActivitySource
{
    public static readonly ActivitySource Source = new("PlayHouse.Server", "1.0.0");

    /// <summary>메시지 처리 Span 시작</summary>
    public static Activity? StartMessageProcessing(string msgId, string stageType)
    {
        return Source.StartActivity(
            name: "ProcessMessage",
            kind: ActivityKind.Server,
            tags: new ActivityTagsCollection
            {
                { "message.id", msgId },
                { "stage.type", stageType }
            });
    }

    /// <summary>Stage 생성 Span 시작</summary>
    public static Activity? StartStageCreation(string stageType, int stageId)
    {
        return Source.StartActivity(
            name: "CreateStage",
            kind: ActivityKind.Internal,
            tags: new ActivityTagsCollection
            {
                { "stage.type", stageType },
                { "stage.id", stageId }
            });
    }

    /// <summary>Actor 참여 Span 시작</summary>
    public static Activity? StartActorJoin(long accountId, int stageId)
    {
        return Source.StartActivity(
            name: "JoinStage",
            kind: ActivityKind.Internal,
            tags: new ActivityTagsCollection
            {
                { "account.id", accountId },
                { "stage.id", stageId }
            });
    }
}

// 사용 예시
public class GameStage : IStage
{
    private readonly PlayHouseMetrics _metrics;

    public async ValueTask OnDispatch(IActor actor, IPacket packet)
    {
        // 분산 추적 Span 시작
        using var activity = PlayHouseActivitySource.StartMessageProcessing(
            packet.MsgId,
            StageSender.StageType);

        var sw = Stopwatch.StartNew();

        try
        {
            // 메시지 처리 로직
            await ProcessMessage(actor, packet);

            activity?.SetStatus(ActivityStatusCode.Ok);
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            activity?.RecordException(ex);

            _metrics.RecordError(ex.GetType().Name, StageSender.StageType);
            throw;
        }
        finally
        {
            sw.Stop();
            _metrics.RecordProcessingTime(
                sw.Elapsed.TotalMilliseconds,
                StageSender.StageType,
                packet.MsgId);
        }
    }
}
```

## 4. 권장 메트릭 목록

### 4.1 연결 메트릭

```yaml
# 연결 관련 메트릭
playhouse.connections.total:
  type: Counter
  unit: "{connection}"
  tags: [transport]
  description: "총 연결 수 (누적)"

playhouse.connections.active:
  type: UpDownCounter
  unit: "{connection}"
  tags: [transport]
  description: "현재 활성 연결 수"

playhouse.disconnections.total:
  type: Counter
  unit: "{connection}"
  tags: [transport, reason]
  description: "총 연결 해제 수"

playhouse.connection.duration:
  type: Histogram
  unit: "s"
  tags: [transport]
  description: "연결 유지 시간"
```

### 4.2 Stage 메트릭

```yaml
# Stage 관련 메트릭
playhouse.stages.created:
  type: Counter
  unit: "{stage}"
  tags: [stage.type]
  description: "생성된 Stage 수 (누적)"

playhouse.stages.active:
  type: UpDownCounter
  unit: "{stage}"
  tags: [stage.type]
  description: "현재 활성 Stage 수"

playhouse.stage.actors.count:
  type: ObservableGauge
  unit: "{actor}"
  tags: [stage.type, stage.id]
  description: "Stage당 Actor 수"

playhouse.stage.join.duration:
  type: Histogram
  unit: "ms"
  tags: [stage.type]
  description: "Stage 참여 소요 시간"

playhouse.stage.lifetime:
  type: Histogram
  unit: "s"
  tags: [stage.type]
  description: "Stage 생존 시간"
```

### 4.3 메시지 메트릭

```yaml
# 메시지 관련 메트릭
playhouse.messages.received:
  type: Counter
  unit: "{message}"
  tags: [stage.type, message.id]
  description: "수신 메시지 수"

playhouse.messages.sent:
  type: Counter
  unit: "{message}"
  tags: [stage.type, message.id]
  description: "송신 메시지 수"

playhouse.message.duration:
  type: Histogram
  unit: "ms"
  tags: [stage.type, message.id]
  description: "메시지 처리 시간"
  buckets: [1, 5, 10, 25, 50, 100, 250, 500, 1000]

playhouse.packet.size:
  type: Histogram
  unit: "By"
  tags: [direction]
  description: "패킷 크기"
  buckets: [64, 256, 1024, 4096, 16384, 65536, 262144]

playhouse.messages.pending:
  type: UpDownCounter
  unit: "{message}"
  tags: [stage.type]
  description: "대기 중인 메시지 수"
```

### 4.4 시스템 메트릭

```yaml
# 시스템 관련 메트릭
playhouse.memory.used:
  type: ObservableGauge
  unit: "By"
  description: "메모리 사용량"

playhouse.gc.heap.size:
  type: ObservableGauge
  unit: "By"
  description: "GC 힙 크기"

playhouse.gc.collections:
  type: ObservableCounter
  unit: "{collection}"
  tags: [generation]
  description: "GC 수집 횟수"

playhouse.threadpool.threads:
  type: ObservableGauge
  unit: "{thread}"
  description: "활성 ThreadPool 스레드 수"

playhouse.errors.total:
  type: Counter
  unit: "{error}"
  tags: [error.type, stage.type]
  description: "에러 발생 수"
```

## 5. Tag 카디널리티 관리

### 5.1 베스트 프랙티스

```csharp
// ❌ 잘못된 예 - 고유 값 태그 (카디널리티 폭발)
_messagesReceived.Add(1,
    new("account.id", actor.AccountId),    // ❌ 수백만 개 고유값
    new("session.id", actor.SessionId));   // ❌ 수백만 개 고유값

// ✅ 올바른 예 - 제한된 카디널리티 태그
_messagesReceived.Add(1,
    new("stage.type", "BattleStage"),      // ✅ 10개 미만
    new("message.id", "PlayerMove"));       // ✅ 100개 미만
```

### 5.2 카디널리티 가이드라인

| 태그 | 권장 카디널리티 | 예시 |
|------|----------------|------|
| `stage.type` | < 10 | "BattleStage", "ChatStage" |
| `message.id` | < 100 | "PlayerMove", "ChatMessage" |
| `transport` | < 5 | "tcp", "websocket", "tls" |
| `error.type` | < 50 | "ValidationError", "Timeout" |
| `direction` | 2 | "inbound", "outbound" |
| `reason` | < 10 | "normal", "timeout", "error" |

```csharp
/// <summary>
/// 태그 값 정규화 (카디널리티 제어).
/// </summary>
public static class MetricTags
{
    // Stage 타입 화이트리스트
    private static readonly HashSet<string> ValidStageTypes = new()
    {
        "BattleStage", "ChatStage", "LobbyStage", "MatchStage"
    };

    public static string NormalizeStageType(string stageType)
    {
        return ValidStageTypes.Contains(stageType) ? stageType : "unknown";
    }

    // 에러 타입 정규화
    public static string NormalizeErrorType(Exception ex)
    {
        return ex switch
        {
            ValidationException => "validation",
            TimeoutException => "timeout",
            OperationCanceledException => "cancelled",
            SocketException => "network",
            _ => "unknown"
        };
    }
}
```

## 6. Stage/Actor 통합 예시

### 6.1 메트릭 수집 Stage

```csharp
#nullable enable

public class MetricsAwareStage : IStage
{
    private readonly PlayHouseMetrics _metrics;
    private readonly ILogger<MetricsAwareStage> _logger;
    private readonly Stopwatch _lifetimeWatch = Stopwatch.StartNew();

    public required IStageSender StageSender { get; init; }

    public MetricsAwareStage(
        PlayHouseMetrics metrics,
        ILogger<MetricsAwareStage> logger)
    {
        _metrics = metrics;
        _logger = logger;
    }

    public async Task<(ushort, IPacket?)> OnCreate(IPacket packet)
    {
        _metrics.StageCreated(StageSender.StageType);

        _logger.LogInformation(
            "Stage created: {StageType} {StageId}",
            StageSender.StageType,
            StageSender.StageId);

        return (0, null);
    }

    public async Task<(ushort, IPacket?)> OnJoinStage(IActor actor, IPacket packet)
    {
        var sw = Stopwatch.StartNew();

        try
        {
            // 참여 로직
            _metrics.ActorJoined(StageSender.StageType);

            return (0, CreateReply("Joined"));
        }
        finally
        {
            sw.Stop();
            _metrics.RecordJoinStageDuration(
                sw.Elapsed.TotalMilliseconds,
                StageSender.StageType);
        }
    }

    public async ValueTask OnDispatch(IActor actor, IPacket packet)
    {
        using var activity = PlayHouseActivitySource.StartMessageProcessing(
            packet.MsgId,
            StageSender.StageType);

        var sw = Stopwatch.StartNew();

        try
        {
            _metrics.RecordMessageReceived(StageSender.StageType, packet.MsgId);
            _metrics.RecordPacketSize(packet.Payload.Length, "inbound");

            // 메시지 처리
            await ProcessMessage(actor, packet);

            activity?.SetStatus(ActivityStatusCode.Ok);
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            activity?.RecordException(ex);

            _metrics.RecordError(
                MetricTags.NormalizeErrorType(ex),
                StageSender.StageType);

            _logger.LogError(ex,
                "Error processing message {MsgId} in stage {StageId}",
                packet.MsgId,
                StageSender.StageId);

            throw;
        }
        finally
        {
            sw.Stop();
            _metrics.RecordProcessingTime(
                sw.Elapsed.TotalMilliseconds,
                StageSender.StageType,
                packet.MsgId);
        }
    }

    public async ValueTask OnDisconnect(IActor actor)
    {
        _metrics.ActorLeft(StageSender.StageType);

        _logger.LogInformation(
            "Actor left: {AccountId} from stage {StageId}",
            actor.ActorSender.AccountId,
            StageSender.StageId);
    }

    public async ValueTask DisposeAsync()
    {
        _lifetimeWatch.Stop();
        _metrics.StageClosed(StageSender.StageType);

        _logger.LogInformation(
            "Stage closed: {StageType} {StageId}, lifetime: {Lifetime}s",
            StageSender.StageType,
            StageSender.StageId,
            _lifetimeWatch.Elapsed.TotalSeconds);
    }

    // Helper methods
    public async Task OnPostCreate() { }
    public async Task OnPostJoinStage(IActor actor) { }
    private async Task ProcessMessage(IActor actor, IPacket packet) { }
    private IPacket CreateReply(string message) => throw new NotImplementedException();
}
```

## 7. Prometheus & Grafana 설정

### 7.1 Prometheus 설정

```yaml
# prometheus.yml
global:
  scrape_interval: 15s
  evaluation_interval: 15s

scrape_configs:
  - job_name: 'playhouse'
    static_configs:
      - targets: ['localhost:5000']
    metrics_path: '/metrics'
    scheme: 'http'
```

### 7.2 Grafana 대시보드 쿼리 예시

```promql
# 초당 메시지 처리량 (QPS)
rate(playhouse_messages_received_total[1m])

# 평균 처리 시간 (ms)
histogram_quantile(0.95,
  rate(playhouse_message_duration_bucket[5m])
)

# 활성 연결 수
playhouse_connections_active

# 활성 Stage 수 (타입별)
playhouse_stages_active{stage_type="BattleStage"}

# 에러율
rate(playhouse_errors_total[5m])
  / rate(playhouse_messages_received_total[5m]) * 100

# 메모리 사용량
playhouse_memory_used / 1024 / 1024  # MB

# P99 처리 시간
histogram_quantile(0.99,
  rate(playhouse_message_duration_bucket[5m])
)
```

### 7.3 알림 규칙 예시

```yaml
# alerting_rules.yml
groups:
  - name: playhouse
    rules:
      # 높은 에러율
      - alert: HighErrorRate
        expr: |
          rate(playhouse_errors_total[5m])
          / rate(playhouse_messages_received_total[5m]) > 0.01
        for: 5m
        labels:
          severity: warning
        annotations:
          summary: "High error rate detected"
          description: "Error rate is {{ $value | humanizePercentage }}"

      # 높은 처리 시간
      - alert: HighLatency
        expr: |
          histogram_quantile(0.95,
            rate(playhouse_message_duration_bucket[5m])
          ) > 100
        for: 5m
        labels:
          severity: warning
        annotations:
          summary: "High message processing latency"
          description: "P95 latency is {{ $value }}ms"

      # 메모리 사용량
      - alert: HighMemoryUsage
        expr: playhouse_memory_used > 1073741824  # 1GB
        for: 10m
        labels:
          severity: warning
        annotations:
          summary: "High memory usage"
          description: "Memory usage is {{ $value | humanize1024 }}B"
```

## 8. 설정 옵션

### 8.1 메트릭 옵션

```csharp
/// <summary>
/// 메트릭 설정 옵션.
/// </summary>
public sealed class MetricsOptions
{
    public const string SectionName = "PlayHouse:Metrics";

    /// <summary>메트릭 활성화 여부</summary>
    public bool Enabled { get; init; } = true;

    /// <summary>Prometheus 엔드포인트 경로</summary>
    public string PrometheusEndpoint { get; init; } = "/metrics";

    /// <summary>OTLP Exporter 엔드포인트</summary>
    public string? OtlpEndpoint { get; init; }

    /// <summary>Histogram 버킷 (처리 시간용, ms)</summary>
    public double[] DurationBuckets { get; init; } =
        { 1, 5, 10, 25, 50, 100, 250, 500, 1000 };

    /// <summary>Histogram 버킷 (패킷 크기용, bytes)</summary>
    public int[] SizeBuckets { get; init; } =
        { 64, 256, 1024, 4096, 16384, 65536, 262144 };

    /// <summary>런타임 메트릭 포함 여부</summary>
    public bool IncludeRuntimeMetrics { get; init; } = true;

    /// <summary>ASP.NET Core 메트릭 포함 여부</summary>
    public bool IncludeAspNetCoreMetrics { get; init; } = true;
}
```

### 8.2 appsettings.json 예시

```json
{
  "PlayHouse": {
    "Metrics": {
      "Enabled": true,
      "PrometheusEndpoint": "/metrics",
      "OtlpEndpoint": "http://localhost:4317",
      "DurationBuckets": [1, 5, 10, 25, 50, 100, 250, 500, 1000],
      "SizeBuckets": [64, 256, 1024, 4096, 16384, 65536, 262144],
      "IncludeRuntimeMetrics": true,
      "IncludeAspNetCoreMetrics": true
    }
  }
}
```

## 9. 베스트 프랙티스

### 9.1 Do (권장)

```
1. Meter 재사용
   - IMeterFactory로 DI 주입
   - 싱글톤으로 관리
   - 매번 생성 금지

2. 태그 카디널리티 제한
   - 고유 값 (userId, sessionId) 태그 금지
   - 태그 조합 수 제한 (< 1000)
   - 화이트리스트 기반 정규화

3. 단위 명시
   - ms, s (시간)
   - By (바이트)
   - {message}, {connection} (개수)

4. 네이밍 규칙
   - namespace.component.metric 형식
   - 소문자 + 언더스코어
   - 단위는 별도 파라미터로

5. OpenTelemetry 활용
   - Metrics + Tracing + Logging 통합
   - 벤더 중립적 Exporter 사용
   - 분산 추적 컨텍스트 전파
```

### 9.2 Don't (금지)

```
1. 고카디널리티 태그
   - account.id, session.id 태그 금지
   - 동적 문자열 태그 금지
   - 무제한 열거형 태그 금지

2. 과도한 메트릭
   - 모든 필드를 메트릭화하지 않음
   - 의미 없는 메트릭 생성 금지
   - 중복 메트릭 금지

3. 동기 메트릭 수집
   - 메트릭 수집이 요청 처리 블로킹 금지
   - 무거운 계산은 ObservableGauge 사용

4. 메트릭 이름 변경
   - 프로덕션 메트릭 이름 변경 주의
   - 대시보드/알림 깨질 수 있음
```

## 10. 다음 단계

- `00-overview.md`: 프레임워크 전체 개요
- `03-stage-actor-model.md`: Stage/Actor 메트릭 수집 적용
- `06-socket-transport.md`: 연결 메트릭 수집 적용
