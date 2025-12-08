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

## 10. 테스트 전략

### 10.1 테스트 피라미드

```
┌─────────────────────────────────────────────────────────────┐
│               Metrics 테스트 피라미드                        │
├─────────────────────────────────────────────────────────────┤
│                                                              │
│                    Unit Tests (30%)                          │
│                  ┌──────────────┐                            │
│                  │  순수 로직   │                            │
│                  │  검증 전용   │                            │
│                  └──────────────┘                            │
│                        │                                     │
│           ┌────────────┴────────────┐                        │
│           │  Integration Tests (70%) │                        │
│           │  InMemory Exporter 활용  │                        │
│           │  실제 동작 검증          │                        │
│           └─────────────────────────┘                        │
│                                                              │
└─────────────────────────────────────────────────────────────┘
```

**통합 테스트 우선 (70%)**
- InMemoryExporter를 활용한 실제 메트릭 수집 검증
- 메트릭 API와 OpenTelemetry 통합 동작 확인
- 실제 사용 시나리오에 가까운 테스트
- 태그, 단위, 메트릭 이름이 올바르게 기록되는지 검증

**유닛 테스트 보완 (30%)**
- 통합 테스트로 검증하기 어려운 순수 로직만 테스트
- 복잡한 계산 로직 (버킷 경계값, 정규화 로직)
- 입력 검증 및 예외 처리
- 통합 테스트 비용이 높은 엣지 케이스

### 10.2 Fake 구현 패턴

```csharp
/// <summary>
/// 통합 테스트용 InMemory Exporter 활용 패턴.
/// </summary>
public class MetricsIntegrationTestBase : IDisposable
{
    protected List<Metric> CollectedMetrics { get; }
    protected MeterProvider MeterProvider { get; }
    protected PlayHouseMetrics Metrics { get; }

    public MetricsIntegrationTestBase()
    {
        CollectedMetrics = new List<Metric>();

        // InMemory Exporter로 실제 메트릭 수집
        MeterProvider = Sdk.CreateMeterProviderBuilder()
            .AddMeter("PlayHouse.Server")
            .AddInMemoryExporter(CollectedMetrics)
            .Build();

        // 실제 PlayHouseMetrics 인스턴스 생성
        var meterFactory = new DefaultMeterFactory(
            new ServiceCollection()
                .AddSingleton(MeterProvider)
                .BuildServiceProvider());

        Metrics = new PlayHouseMetrics(meterFactory);
    }

    /// <summary>수집된 메트릭에서 특정 이름의 메트릭 조회</summary>
    protected Metric? FindMetric(string name)
    {
        MeterProvider.ForceFlush();
        return CollectedMetrics.FirstOrDefault(m => m.Name == name);
    }

    /// <summary>특정 태그를 가진 데이터 포인트 조회</summary>
    protected MetricPoint? FindDataPoint(
        string metricName,
        params (string key, object? value)[] tags)
    {
        var metric = FindMetric(metricName);
        if (metric == null) return null;

        foreach (ref readonly var point in metric.GetMetricPoints())
        {
            bool allTagsMatch = tags.All(tag =>
                point.Tags.Any(t => t.Key == tag.key &&
                                    Equals(t.Value, tag.value)));

            if (allTagsMatch)
                return point;
        }

        return null;
    }

    public void Dispose()
    {
        Metrics.Dispose();
        MeterProvider.Dispose();
    }
}
```

### 10.3 통합 테스트 시나리오

#### 10.3.1 기본 동작 (Basic Behavior)

메트릭이 예상대로 생성되고 기록되는지 검증합니다.

| Given | When | Then |
|-------|------|------|
| PlayHouseMetrics 인스턴스 생성 | Counter.Add(1) 호출 | 메트릭 값이 1 증가 |
| PlayHouseMetrics 인스턴스 생성 | Histogram.Record(100) 호출 | 히스토그램에 100ms 기록 |
| PlayHouseMetrics 인스턴스 생성 | UpDownCounter.Add(1) 후 Add(-1) | 현재 값이 0 |
| PlayHouseMetrics 인스턴스 생성 | ConnectionOpened("tcp") 호출 | connections.total, connections.active 모두 증가 |
| PlayHouseMetrics 인스턴스 생성 | StageCreated("BattleStage") 호출 | stages.created, stages.active 모두 증가 |

```csharp
public class MetricsBasicBehaviorTests : MetricsIntegrationTestBase
{
    [Fact]
    public void RecordMessageReceived_IncreasesCounter()
    {
        // Given: PlayHouseMetrics 인스턴스가 준비됨

        // When: 메시지 수신 기록
        Metrics.RecordMessageReceived("BattleStage", "PlayerMove");

        // Then: 메트릭 값이 1 증가
        var point = FindDataPoint(
            "playhouse.messages.received",
            ("stage.type", "BattleStage"),
            ("message.id", "PlayerMove"));

        Assert.NotNull(point);
        Assert.Equal(1, point.Value.GetSumLong());
    }

    [Fact]
    public void RecordProcessingTime_RecordsHistogram()
    {
        // Given: PlayHouseMetrics 인스턴스가 준비됨

        // When: 처리 시간 기록 (100ms)
        Metrics.RecordProcessingTime(100, "BattleStage", "PlayerMove");

        // Then: 히스토그램에 100ms 기록됨
        var point = FindDataPoint(
            "playhouse.message.duration",
            ("stage.type", "BattleStage"),
            ("message.id", "PlayerMove"));

        Assert.NotNull(point);
        Assert.Equal(1, point.Value.GetHistogramCount());
        Assert.Equal(100, point.Value.GetHistogramSum());
    }

    [Fact]
    public void ConnectionLifecycle_UpdatesCounters()
    {
        // Given: PlayHouseMetrics 인스턴스가 준비됨

        // When: 연결 수립 후 해제
        Metrics.ConnectionOpened("tcp");
        Metrics.ConnectionClosed("tcp", "normal");

        // Then: 누적 카운터는 증가, 활성 카운터는 0
        var totalPoint = FindDataPoint(
            "playhouse.connections.total",
            ("transport", "tcp"));
        var activePoint = FindDataPoint(
            "playhouse.connections.active",
            ("transport", "tcp"));

        Assert.Equal(1, totalPoint.Value.GetSumLong());
        Assert.Equal(0, activePoint.Value.GetSumLong());
    }
}
```

#### 10.3.2 응답 검증 (Response Validation)

메트릭이 올바른 형식과 값으로 수집되는지 검증합니다.

| Given | When | Then |
|-------|------|------|
| 메트릭 수집 완료 | Metric 조회 | Name, Unit, Description이 정확함 |
| Counter 기록 | 메트릭 조회 | MetricType이 Sum, Monotonic=true |
| Histogram 기록 | 메트릭 조회 | MetricType이 Histogram, Buckets 존재 |
| 태그와 함께 기록 | 메트릭 조회 | 태그가 정확히 저장됨 |
| ObservableGauge 콜백 등록 | ForceFlush 호출 | 콜백이 실행되어 현재 값 반환 |

```csharp
public class MetricsResponseValidationTests : MetricsIntegrationTestBase
{
    [Fact]
    public void Metric_HasCorrectMetadata()
    {
        // Given: 메시지 수신 기록
        Metrics.RecordMessageReceived("BattleStage", "PlayerMove");

        // When: 메트릭 조회
        var metric = FindMetric("playhouse.messages.received");

        // Then: 메타데이터가 정확함
        Assert.NotNull(metric);
        Assert.Equal("playhouse.messages.received", metric.Name);
        Assert.Equal("{message}", metric.Unit);
        Assert.Equal("Total number of messages received", metric.Description);
    }

    [Fact]
    public void Counter_HasCorrectType()
    {
        // Given: Counter 기록
        Metrics.RecordMessageReceived("BattleStage", "PlayerMove");

        // When: 메트릭 타입 확인
        var metric = FindMetric("playhouse.messages.received");

        // Then: Sum 타입이고 Monotonic
        Assert.Equal(MetricType.LongSum, metric.MetricType);
        Assert.True(metric.Temporality == AggregationTemporality.Cumulative);
    }

    [Fact]
    public void Histogram_HasBuckets()
    {
        // Given: Histogram 기록
        Metrics.RecordProcessingTime(150, "BattleStage", "PlayerMove");

        // When: 메트릭 조회
        var metric = FindMetric("playhouse.message.duration");
        var point = metric.GetMetricPoints().First();

        // Then: 히스토그램 버킷이 존재
        Assert.Equal(MetricType.Histogram, metric.MetricType);
        Assert.NotEmpty(point.GetHistogramBuckets());

        // 150ms는 100-250 버킷에 포함되어야 함
        var buckets = point.GetHistogramBuckets().ToArray();
        var bucket250 = Array.Find(buckets, b =>
            b.ExplicitBound == 250);
        Assert.Equal(1, bucket250.BucketCount);
    }

    [Fact]
    public void Tags_AreStoredCorrectly()
    {
        // Given: 태그와 함께 기록
        Metrics.RecordMessageReceived("BattleStage", "PlayerMove");

        // When: 데이터 포인트 조회
        var point = FindDataPoint(
            "playhouse.messages.received",
            ("stage.type", "BattleStage"),
            ("message.id", "PlayerMove"));

        // Then: 태그가 정확히 저장됨
        Assert.NotNull(point);
        var tags = point.Value.Tags.ToArray();
        Assert.Contains(tags, t =>
            t.Key == "stage.type" && (string)t.Value == "BattleStage");
        Assert.Contains(tags, t =>
            t.Key == "message.id" && (string)t.Value == "PlayerMove");
    }
}
```

#### 10.3.3 입력 검증 (Input Validation)

잘못된 입력이나 엣지 케이스를 처리하는지 검증합니다.

| Given | When | Then |
|-------|------|------|
| PlayHouseMetrics 인스턴스 | null 태그 값 전달 | 예외 없이 기록됨 (null 허용) |
| PlayHouseMetrics 인스턴스 | 음수 값으로 Histogram 기록 | 예외 발생하지 않고 기록됨 |
| PlayHouseMetrics 인스턴스 | 매우 큰 값 (long.MaxValue) 기록 | 정상 기록됨 |
| PlayHouseMetrics 인스턴스 | 빈 문자열 태그 | 정상 기록됨 |
| PlayHouseMetrics 인스턴스 | 100개 이상의 서로 다른 태그 조합 | 모두 정상 기록됨 (카디널리티 경고는 운영 관심사) |

```csharp
public class MetricsInputValidationTests : MetricsIntegrationTestBase
{
    [Fact]
    public void RecordMessage_WithNullTag_DoesNotThrow()
    {
        // Given: PlayHouseMetrics 인스턴스

        // When/Then: null 태그 값으로 기록해도 예외 없음
        var exception = Record.Exception(() =>
            Metrics.RecordMessageReceived(null!, "PlayerMove"));

        Assert.Null(exception);
    }

    [Fact]
    public void RecordHistogram_WithNegativeValue_DoesNotThrow()
    {
        // Given: PlayHouseMetrics 인스턴스

        // When/Then: 음수 값으로 기록해도 예외 없음
        var exception = Record.Exception(() =>
            Metrics.RecordProcessingTime(-100, "BattleStage", "PlayerMove"));

        Assert.Null(exception);

        // 메트릭이 기록되었는지 확인
        var point = FindDataPoint(
            "playhouse.message.duration",
            ("stage.type", "BattleStage"),
            ("message.id", "PlayerMove"));
        Assert.NotNull(point);
    }

    [Fact]
    public void RecordCounter_WithMaxValue_RecordsSuccessfully()
    {
        // Given: PlayHouseMetrics 인스턴스

        // When: long.MaxValue 기록
        // Counter는 Add 메서드를 사용하므로 직접 테스트 불가
        // 대신 매우 큰 값을 여러 번 더함
        for (int i = 0; i < 1000; i++)
        {
            Metrics.RecordMessageReceived("BattleStage", "PlayerMove");
        }

        // Then: 정상 기록됨
        var point = FindDataPoint(
            "playhouse.messages.received",
            ("stage.type", "BattleStage"),
            ("message.id", "PlayerMove"));

        Assert.Equal(1000, point.Value.GetSumLong());
    }

    [Fact]
    public void RecordMessage_WithEmptyStringTag_RecordsSuccessfully()
    {
        // Given: PlayHouseMetrics 인스턴스

        // When: 빈 문자열 태그로 기록
        Metrics.RecordMessageReceived("", "");

        // Then: 정상 기록됨
        var point = FindDataPoint(
            "playhouse.messages.received",
            ("stage.type", ""),
            ("message.id", ""));

        Assert.NotNull(point);
    }
}
```

#### 10.3.4 엣지 케이스 (Edge Cases)

동시성, 대량 데이터, 경계값 등의 엣지 케이스를 검증합니다.

| Given | When | Then |
|-------|------|------|
| PlayHouseMetrics 인스턴스 | 10개 스레드에서 동시에 기록 | 모든 값이 정확히 누적됨 |
| PlayHouseMetrics 인스턴스 | 1만 번 연속 기록 | 성능 저하 없이 모두 기록됨 |
| PlayHouseMetrics 인스턴스 | Dispose 후 메트릭 기록 | ObjectDisposedException 발생 |
| 여러 Meter 인스턴스 | 같은 이름의 메트릭 생성 | 각각 독립적으로 동작 |
| Histogram | 모든 버킷 경계값에 정확히 일치하는 값 기록 | 올바른 버킷에 카운트됨 |

```csharp
public class MetricsEdgeCaseTests : MetricsIntegrationTestBase
{
    [Fact]
    public void ConcurrentRecording_AccumulatesCorrectly()
    {
        // Given: PlayHouseMetrics 인스턴스
        const int ThreadCount = 10;
        const int RecordsPerThread = 100;

        // When: 10개 스레드에서 동시 기록
        Parallel.For(0, ThreadCount, _ =>
        {
            for (int i = 0; i < RecordsPerThread; i++)
            {
                Metrics.RecordMessageReceived("BattleStage", "PlayerMove");
            }
        });

        // Then: 모든 값이 정확히 누적됨
        var point = FindDataPoint(
            "playhouse.messages.received",
            ("stage.type", "BattleStage"),
            ("message.id", "PlayerMove"));

        Assert.Equal(ThreadCount * RecordsPerThread,
                     point.Value.GetSumLong());
    }

    [Fact]
    public void BulkRecording_PerformsWell()
    {
        // Given: PlayHouseMetrics 인스턴스
        const int RecordCount = 10000;

        // When: 1만 번 연속 기록
        var sw = Stopwatch.StartNew();
        for (int i = 0; i < RecordCount; i++)
        {
            Metrics.RecordMessageReceived("BattleStage", "PlayerMove");
        }
        sw.Stop();

        // Then: 성능 저하 없이 모두 기록됨 (100ms 이내)
        Assert.True(sw.ElapsedMilliseconds < 100);

        var point = FindDataPoint(
            "playhouse.messages.received",
            ("stage.type", "BattleStage"),
            ("message.id", "PlayerMove"));

        Assert.Equal(RecordCount, point.Value.GetSumLong());
    }

    [Fact]
    public void Dispose_ThrowsOnSubsequentUse()
    {
        // Given: Disposed PlayHouseMetrics
        Metrics.Dispose();

        // When/Then: 메트릭 기록 시 예외 발생
        Assert.Throws<ObjectDisposedException>(() =>
            Metrics.RecordMessageReceived("BattleStage", "PlayerMove"));
    }

    [Fact]
    public void Histogram_BucketBoundaries_WorkCorrectly()
    {
        // Given: PlayHouseMetrics 인스턴스
        // 버킷: [1, 5, 10, 25, 50, 100, 250, 500, 1000]

        // When: 각 버킷 경계값에 정확히 일치하는 값 기록
        double[] values = { 1, 5, 10, 25, 50, 100, 250, 500, 1000 };
        foreach (var value in values)
        {
            Metrics.RecordProcessingTime(value, "BattleStage", "PlayerMove");
        }

        // Then: 올바른 버킷에 카운트됨
        var metric = FindMetric("playhouse.message.duration");
        var point = metric.GetMetricPoints().First();

        Assert.Equal(values.Length, point.GetHistogramCount());

        // 각 버킷 확인
        var buckets = point.GetHistogramBuckets().ToArray();
        foreach (var expectedBound in values)
        {
            var bucket = Array.Find(buckets, b =>
                b.ExplicitBound >= expectedBound);
            Assert.NotNull(bucket);
        }
    }
}
```

#### 10.3.5 활용 예제 (Usage Examples)

실제 사용 시나리오를 검증합니다.

| Given | When | Then |
|-------|------|------|
| MetricsAwareStage 인스턴스 | OnCreate 호출 | stages.created, stages.active 증가 |
| MetricsAwareStage 인스턴스 | OnJoinStage 호출 | actors.active 증가, join.duration 기록 |
| MetricsAwareStage 인스턴스 | OnDispatch 호출 | messages.received, message.duration 기록 |
| MetricsAwareStage 인스턴스 | OnDispatch 예외 발생 | errors.total 증가, Activity Error 상태 |
| MetricsAwareStage 인스턴스 | DisposeAsync 호출 | stages.active 감소 |

```csharp
public class MetricsUsageExampleTests : MetricsIntegrationTestBase
{
    [Fact]
    public async Task Stage_OnCreate_RecordsMetrics()
    {
        // Given: MetricsAwareStage 인스턴스
        var logger = new Mock<ILogger<MetricsAwareStage>>();
        var stage = new MetricsAwareStage(Metrics, logger.Object)
        {
            StageSender = CreateMockStageSender("BattleStage", 1)
        };

        // When: OnCreate 호출
        await stage.OnCreate(CreateMockPacket());

        // Then: stages.created, stages.active 증가
        var createdPoint = FindDataPoint(
            "playhouse.stages.created",
            ("stage.type", "BattleStage"));
        var activePoint = FindDataPoint(
            "playhouse.stages.active",
            ("stage.type", "BattleStage"));

        Assert.Equal(1, createdPoint.Value.GetSumLong());
        Assert.Equal(1, activePoint.Value.GetSumLong());
    }

    [Fact]
    public async Task Stage_OnJoinStage_RecordsMetrics()
    {
        // Given: MetricsAwareStage 인스턴스
        var logger = new Mock<ILogger<MetricsAwareStage>>();
        var stage = new MetricsAwareStage(Metrics, logger.Object)
        {
            StageSender = CreateMockStageSender("BattleStage", 1)
        };

        // When: OnJoinStage 호출
        await stage.OnJoinStage(CreateMockActor(), CreateMockPacket());

        // Then: actors.active 증가, join.duration 기록
        var actorsPoint = FindDataPoint(
            "playhouse.actors.active",
            ("stage.type", "BattleStage"));
        var durationMetric = FindMetric("playhouse.stage.join.duration");

        Assert.Equal(1, actorsPoint.Value.GetSumLong());
        Assert.NotNull(durationMetric);
    }

    [Fact]
    public async Task Stage_OnDispatch_WithError_RecordsError()
    {
        // Given: 예외를 발생시키는 Stage
        var logger = new Mock<ILogger<MetricsAwareStage>>();
        var stage = new ThrowingStage(Metrics, logger.Object)
        {
            StageSender = CreateMockStageSender("BattleStage", 1)
        };

        // When: OnDispatch 호출 시 예외 발생
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            stage.OnDispatch(CreateMockActor(), CreateMockPacket()));

        // Then: errors.total 증가
        var errorPoint = FindDataPoint(
            "playhouse.errors.total",
            ("error.type", "InvalidOperationException"),
            ("stage.type", "BattleStage"));

        Assert.NotNull(errorPoint);
        Assert.Equal(1, errorPoint.Value.GetSumLong());
    }

    [Fact]
    public async Task Stage_Lifecycle_RecordsCompleteMetrics()
    {
        // Given: MetricsAwareStage 인스턴스
        var logger = new Mock<ILogger<MetricsAwareStage>>();
        var stage = new MetricsAwareStage(Metrics, logger.Object)
        {
            StageSender = CreateMockStageSender("BattleStage", 1)
        };

        // When: 전체 생명주기 실행
        await stage.OnCreate(CreateMockPacket());
        var actor = CreateMockActor();
        await stage.OnJoinStage(actor, CreateMockPacket());
        await stage.OnDispatch(actor, CreateMockPacket());
        await stage.OnDisconnect(actor);
        await stage.DisposeAsync();

        // Then: 모든 메트릭이 기록됨
        var createdPoint = FindDataPoint(
            "playhouse.stages.created",
            ("stage.type", "BattleStage"));
        var activePoint = FindDataPoint(
            "playhouse.stages.active",
            ("stage.type", "BattleStage"));
        var messagesPoint = FindDataPoint(
            "playhouse.messages.received",
            ("stage.type", "BattleStage"));

        Assert.Equal(1, createdPoint.Value.GetSumLong());
        Assert.Equal(0, activePoint.Value.GetSumLong()); // Disposed
        Assert.Equal(1, messagesPoint.Value.GetSumLong());
    }
}
```

### 10.4 유닛 테스트 시나리오

통합 테스트로 검증하기 어려운 순수 로직만 유닛 테스트로 작성합니다.

#### 10.4.1 태그 정규화 로직 (MetricTags 클래스)

**통합 테스트로 커버 불가한 이유**:
- 순수 함수 로직으로 외부 의존성 없음
- 다양한 입력 조합을 빠르게 검증해야 함
- 통합 테스트는 메트릭 수집 전체 플로우를 검증하므로 이런 세부 로직 테스트에 오버헤드

```csharp
public class MetricTagsTests
{
    [Theory]
    [InlineData("BattleStage", "BattleStage")]
    [InlineData("ChatStage", "ChatStage")]
    [InlineData("UnknownStage", "unknown")]
    [InlineData(null, "unknown")]
    [InlineData("", "unknown")]
    public void NormalizeStageType_ReturnsExpectedValue(
        string input,
        string expected)
    {
        // When: Stage 타입 정규화
        var result = MetricTags.NormalizeStageType(input);

        // Then: 예상 값 반환
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData(typeof(ValidationException), "validation")]
    [InlineData(typeof(TimeoutException), "timeout")]
    [InlineData(typeof(OperationCanceledException), "cancelled")]
    [InlineData(typeof(SocketException), "network")]
    [InlineData(typeof(Exception), "unknown")]
    public void NormalizeErrorType_ReturnsExpectedValue(
        Type exceptionType,
        string expected)
    {
        // Given: 예외 인스턴스
        var exception = (Exception)Activator.CreateInstance(exceptionType);

        // When: 에러 타입 정규화
        var result = MetricTags.NormalizeErrorType(exception);

        // Then: 예상 값 반환
        Assert.Equal(expected, result);
    }
}
```

#### 10.4.2 Histogram 버킷 경계값 계산 (커스텀 Histogram View)

**통합 테스트로 커버 불가한 이유**:
- 통합 테스트는 실제 버킷 할당을 검증하지만, 경계값 계산 로직 자체는 검증 어려움
- 수학적 정확성을 요구하는 순수 계산 로직
- 다양한 버킷 설정 조합을 빠르게 검증해야 함

```csharp
/// <summary>
/// 커스텀 Histogram 버킷 경계값 계산 로직.
/// </summary>
public static class HistogramBucketCalculator
{
    /// <summary>값이 어느 버킷에 속하는지 계산</summary>
    public static int FindBucketIndex(double value, double[] boundaries)
    {
        for (int i = 0; i < boundaries.Length; i++)
        {
            if (value <= boundaries[i])
                return i;
        }
        return boundaries.Length; // Overflow bucket
    }

    /// <summary>버킷 경계값이 유효한지 검증</summary>
    public static bool ValidateBuckets(double[] boundaries)
    {
        if (boundaries.Length == 0)
            return false;

        for (int i = 1; i < boundaries.Length; i++)
        {
            if (boundaries[i] <= boundaries[i - 1])
                return false; // 증가하지 않음
        }

        return true;
    }
}

public class HistogramBucketCalculatorTests
{
    [Theory]
    [InlineData(0.5, new[] { 1.0, 5.0, 10.0 }, 0)]
    [InlineData(1.0, new[] { 1.0, 5.0, 10.0 }, 0)]
    [InlineData(3.0, new[] { 1.0, 5.0, 10.0 }, 1)]
    [InlineData(5.0, new[] { 1.0, 5.0, 10.0 }, 1)]
    [InlineData(10.0, new[] { 1.0, 5.0, 10.0 }, 2)]
    [InlineData(15.0, new[] { 1.0, 5.0, 10.0 }, 3)] // Overflow
    public void FindBucketIndex_ReturnsCorrectIndex(
        double value,
        double[] boundaries,
        int expectedIndex)
    {
        // When: 버킷 인덱스 계산
        var index = HistogramBucketCalculator.FindBucketIndex(
            value,
            boundaries);

        // Then: 올바른 인덱스 반환
        Assert.Equal(expectedIndex, index);
    }

    [Theory]
    [InlineData(new[] { 1.0, 5.0, 10.0 }, true)]
    [InlineData(new[] { 1.0 }, true)]
    [InlineData(new double[] { }, false)]
    [InlineData(new[] { 5.0, 1.0, 10.0 }, false)] // 감소
    [InlineData(new[] { 1.0, 1.0, 10.0 }, false)] // 동일
    public void ValidateBuckets_ReturnsExpectedResult(
        double[] boundaries,
        bool expected)
    {
        // When: 버킷 유효성 검증
        var isValid = HistogramBucketCalculator.ValidateBuckets(boundaries);

        // Then: 예상 결과 반환
        Assert.Equal(expected, isValid);
    }
}
```

#### 10.4.3 메트릭 이름 컨벤션 검증

**통합 테스트로 커버 불가한 이유**:
- OpenTelemetry 네이밍 규칙 준수 여부는 문자열 검증 로직
- 통합 테스트는 실제 메트릭 수집을 검증하지만, 이름 규칙 자체는 별도 검증 필요
- 정규식 기반 검증으로 빠른 피드백 필요

```csharp
/// <summary>
/// 메트릭 이름 컨벤션 검증기.
/// </summary>
public static class MetricNameValidator
{
    // OpenTelemetry 메트릭 이름 규칙:
    // - 소문자, 숫자, '.', '_' 만 허용
    // - 알파벳으로 시작
    // - 연속된 '.' 금지
    private static readonly Regex NamePattern = new(
        @"^[a-z][a-z0-9._]*[a-z0-9]$",
        RegexOptions.Compiled);

    public static bool IsValid(string name)
    {
        if (string.IsNullOrEmpty(name))
            return false;

        if (!NamePattern.IsMatch(name))
            return false;

        // 연속된 '.' 체크
        if (name.Contains(".."))
            return false;

        return true;
    }
}

public class MetricNameValidatorTests
{
    [Theory]
    [InlineData("playhouse.messages.received", true)]
    [InlineData("playhouse.message.duration", true)]
    [InlineData("playhouse.connections.active", true)]
    [InlineData("PlayHouse.Messages", false)] // 대문자
    [InlineData("playhouse..messages", false)] // 연속 '.'
    [InlineData(".playhouse.messages", false)] // '.'로 시작
    [InlineData("playhouse.messages.", false)] // '.'로 끝
    [InlineData("1playhouse", false)] // 숫자로 시작
    [InlineData("playhouse-messages", false)] // '-' 포함
    [InlineData("", false)] // 빈 문자열
    [InlineData(null, false)] // null
    public void IsValid_ReturnsExpectedResult(string name, bool expected)
    {
        // When: 이름 유효성 검증
        var isValid = MetricNameValidator.IsValid(name);

        // Then: 예상 결과 반환
        Assert.Equal(expected, isValid);
    }
}
```

### 10.5 테스트 조직화

```
PlayHouse.Tests/
├── Metrics/
│   ├── Integration/                  # 통합 테스트 (70%)
│   │   ├── MetricsIntegrationTestBase.cs
│   │   ├── BasicBehaviorTests.cs
│   │   ├── ResponseValidationTests.cs
│   │   ├── InputValidationTests.cs
│   │   ├── EdgeCaseTests.cs
│   │   └── UsageExampleTests.cs
│   └── Unit/                         # 유닛 테스트 (30%)
│       ├── MetricTagsTests.cs
│       ├── HistogramBucketCalculatorTests.cs
│       └── MetricNameValidatorTests.cs
```

## 11. 다음 단계

- `00-overview.md`: 프레임워크 전체 개요
- `03-stage-actor-model.md`: Stage/Actor 메트릭 수집 적용
- `06-socket-transport.md`: 연결 메트릭 수집 적용
- `10-testing-spec.md`: 전체 테스트 전략 및 가이드라인
