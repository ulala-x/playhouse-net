# PlayHouse-NET 개요

## 1. 프레임워크 목표

PlayHouse-NET은 .NET 기반의 실시간 게임 서버 프레임워크로, 단일 서버 구조와 .NET 생태계에 최적화된 솔루션을 제공합니다.

### 1.1 핵심 목표

- **단순성**: 단일 Room 서버 구조
- **성능**: Lock-Free 메시지 처리를 통한 높은 처리량
- **확장성**: Stage/Actor 모델 기반의 수평 확장
- **개발 편의성**: HTTP API 내장, .NET 네이티브 라이브러리 활용
- **외부 의존성 최소화**: .NET 기본 라이브러리만으로 구현

## 2. 아키텍처 개요

### 2.1 전체 구조

```
┌─────────────────────────────────────────────────────────┐
│                    PlayHouse-NET                        │
│                     Room Server                         │
├─────────────────────────────────────────────────────────┤
│                                                         │
│  ┌──────────────┐    ┌──────────────┐                  │
│  │  HTTP API    │    │Socket Server │                  │
│  │  (REST)      │    │ TCP/WS/HTTPS │                  │
│  └──────┬───────┘    └──────┬───────┘                  │
│         │                   │                           │
│         └────────┬──────────┘                           │
│                  │                                      │
│         ┌────────▼────────┐                             │
│         │  Core Engine    │                             │
│         │  - Dispatcher   │                             │
│         │  - Stage Pool   │                             │
│         │  - Timer Mgr    │                             │
│         └────────┬────────┘                             │
│                  │                                      │
│         ┌────────▼────────┐                             │
│         │ Stage/Actor     │                             │
│         │ (User Logic)    │                             │
│         └─────────────────┘                             │
│                                                         │
└─────────────────────────────────────────────────────────┘
```

### 2.2 레이어 구조

```
┌─────────────────────────────────────┐
│   Application Layer                 │
│   (User Implementation)             │
│   - Custom Stage                    │
│   - Custom Actor                    │
│   - HTTP Controllers                │
└──────────────┬──────────────────────┘
               │ implements
┌──────────────▼──────────────────────┐
│   Abstractions Layer                │
│   (Public Interfaces)               │
│   - IStage, IActor                  │
│   - ISender                         │
│   - IPacket, IPayload               │
└──────────────┬──────────────────────┘
               │ uses
┌──────────────▼──────────────────────┐
│   Core Engine Layer                 │
│   - Message Dispatcher              │
│   - Stage/Actor Manager             │
│   - Timer System                    │
│   - Lock-Free Queue                 │
└──────────────┬──────────────────────┘
               │ uses
┌──────────────▼──────────────────────┐
│   Infrastructure Layer              │
│   - Socket Transport                │
│   - HTTP Server                     │
│   - Packet Serialization            │
│   - Compression (LZ4)               │
└─────────────────────────────────────┘
```

## 3. 핵심 철학 및 설계 원칙

### 3.1 설계 철학

1. **Single Responsibility**: 각 서버는 명확한 단일 책임
   - Room Server: 실시간 게임 로직 처리

2. **Lock-Free Architecture**:
   - Actor별 메시지 큐를 통한 동시성 제어
   - 공유 상태 최소화

3. **Simplicity over Complexity**:
   - 서버 간 통신 제거로 복잡도 감소
   - 명확한 메시지 흐름

4. **Developer Experience**:
   - 직관적인 Stage/Actor 모델
   - 풍부한 헬퍼 메서드 제공

### 3.2 핵심 설계 원칙

#### 메시지 기반 통신
- 모든 상호작용은 메시지로 처리
- 직접 메서드 호출 금지 (Actor 간)
- Immutable 메시지 권장 (`readonly record struct`)

#### Actor 격리
- 각 Actor는 독립적인 상태 보유
- Stage 내에서만 Actor 간 통신
- 외부 Stage는 `SendToStageAsync` (fire-and-forget)으로만 통신
- ~~RequestToStage~~ 제거됨 (blocking 응답 대기는 lock-free 원칙 위반)

#### 비동기 우선
- 모든 핵심 API는 `async/await` 지원
- Hot path 메서드는 `ValueTask` 사용 (allocation 최적화)
- 블로킹 작업은 `AsyncBlock`으로 처리
- Timer 기반 주기 작업

### 3.3 .NET Core 프레임워크 통합

PlayHouse-NET은 .NET Core 프레임워크와 깊이 통합되어 설계되었습니다.

#### Microsoft.Extensions.Options 패턴

```csharp
#nullable enable

/// <summary>
/// PlayHouse 서버 설정.
/// </summary>
public sealed class PlayHouseOptions : IValidateOptions<PlayHouseOptions>
{
    public const string SectionName = "PlayHouse";

    [Required]
    public required string Ip { get; init; }

    [Range(1, 65535)]
    public int Port { get; init; } = 7777;

    [Range(1000, 300000)]
    public int RequestTimeoutMs { get; init; } = 30000;

    public int MaxConnections { get; init; } = 10000;

    public ValidateOptionsResult Validate(string? name, PlayHouseOptions options)
    {
        var failures = new List<string>();

        if (string.IsNullOrEmpty(options.Ip))
            failures.Add("Ip is required");

        if (options.Port <= 0 || options.Port > 65535)
            failures.Add("Port must be between 1 and 65535");

        return failures.Count > 0
            ? ValidateOptionsResult.Fail(failures)
            : ValidateOptionsResult.Success;
    }
}

/// <summary>
/// 세션 옵션 (중첩 옵션).
/// </summary>
public sealed class SessionOptions
{
    public int Backlog { get; init; } = 1000;
    public int MaxPacketSize { get; init; } = 2 * 1024 * 1024; // 2MB

    // System.IO.Pipelines 백프레셔 설정
    public int PauseWriterThreshold { get; init; } = 64 * 1024;  // 64KB
    public int ResumeWriterThreshold { get; init; } = 32 * 1024; // 32KB

    // Heartbeat
    public int HeartbeatIntervalSeconds { get; init; } = 30;
    public int HeartbeatTimeoutSeconds { get; init; } = 90;
}
```

#### Microsoft.Extensions.DependencyInjection 통합

```csharp
// Program.cs
var builder = WebApplication.CreateBuilder(args);

// PlayHouse 서비스 등록
builder.Services.AddPlayHouse(builder.Configuration);

var app = builder.Build();
app.Run();

// 확장 메서드 구현
public static class PlayHouseServiceExtensions
{
    public static IServiceCollection AddPlayHouse(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Options 바인딩 + 유효성 검사
        services.AddOptions<PlayHouseOptions>()
            .Bind(configuration.GetSection(PlayHouseOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<SessionOptions>()
            .Bind(configuration.GetSection("PlayHouse:Session"));

        // 핵심 서비스 등록
        services.AddSingleton<IStageManager, StageManager>();
        services.AddSingleton<IPacketDispatcher, PacketDispatcher>();

        // IHostedService로 서버 수명주기 관리
        services.AddHostedService<PlayHouseServer>();

        return services;
    }
}
```

#### Microsoft.Extensions.Logging 통합

```csharp
public class StageManager : IStageManager
{
    private readonly ILogger<StageManager> _logger;
    private readonly IOptions<PlayHouseOptions> _options;

    public StageManager(
        ILogger<StageManager> logger,
        IOptions<PlayHouseOptions> options)
    {
        _logger = logger;
        _options = options;
    }

    public async ValueTask<IStage> CreateStageAsync(string stageType, IPacket packet)
    {
        _logger.LogInformation("Creating stage of type {StageType}", stageType);
        // ...
    }
}
```

#### appsettings.json 예시

```json
{
  "PlayHouse": {
    "Ip": "0.0.0.0",
    "Port": 7777,
    "RequestTimeoutMs": 30000,
    "MaxConnections": 10000,
    "Session": {
      "Backlog": 1000,
      "MaxPacketSize": 2097152,
      "PauseWriterThreshold": 65536,
      "ResumeWriterThreshold": 32768,
      "HeartbeatIntervalSeconds": 30,
      "HeartbeatTimeoutSeconds": 90
    }
  }
}
```

## 4. 지원 플랫폼 및 프로토콜

### 4.1 플랫폼

- **.NET 8.0 / 9.0 / 10.0** 멀티 타겟 지원
  - .NET 8.0 (LTS - 2026년 11월까지)
  - .NET 9.0 (STS - 2025년 5월까지)
  - .NET 10.0 (LTS 예정 - 2025년 11월 출시)
- **크로스 플랫폼**: Windows, Linux, macOS
- **컨테이너**: Docker 지원

### 4.2 지원 프로토콜

#### 클라이언트 통신
- **TCP**: 고성능, 낮은 지연
- **WebSocket**: 웹 브라우저 지원
- **HTTPS**: TLS 암호화 지원

#### HTTP API
- **REST API**: 서버 관리 및 모니터링
- **JSON**: 요청/응답 포맷
- **Swagger**: API 문서 자동 생성

### 4.3 직렬화

- **기본**: Binary (Custom Protocol)
- **HTTP**: JSON
- **압축**: LZ4 (옵션)

## 5. 주요 컴포넌트

### 5.1 Stage (방/룸)

게임 로직이 실행되는 논리적 컨테이너

- 독립적인 메시지 큐
- 여러 Actor 포함 가능
- 타이머 기능 내장
- 라이프사이클 관리

### 5.2 Actor (플레이어)

Stage 내의 개별 참가자

- 세션과 1:1 매핑
- Stage 내에서만 존재
- 독립적인 상태 관리
- 생성/파괴 이벤트

### 5.3 Sender

메시지 전송 인터페이스 (Lock-free 원칙 적용)

- `Reply()` - 클라이언트 요청에 응답
- `SendAsync()` - 클라이언트에게 푸시 메시지
- `SendToStageAsync()` - 다른 Stage로 fire-and-forget 전송
- `BroadcastAsync()` - Stage 내 모든 Actor에게 브로드캐스트
- ~~RequestToStage~~ - 제거됨 (blocking 응답 대기 금지)

### 5.4 Timer

Stage 내 시간 기반 작업

- RepeatTimer: 주기적 실행
- CountTimer: 제한된 횟수 실행
- 자동 정리

## 6. 사용 시나리오

- **실시간 멀티플레이어 게임**
  - 배틀로얄, FPS, 레이싱, MOBA
  - 카드 게임, 보드 게임, 턴제 게임
  - MMO (Stage 기반 영역 분할)

- **게임 서비스 인프라**
  - 로비, 매칭, 파티 시스템
  - 게임 내 채팅
  - 랭킹, 리더보드

## 7. 성능 특성

### 7.1 성능 지표

- **메시지 처리량**: 400K+ TPS (10K CCU, 10 inflight, 1KB 메시지 기준)
- **지연 시간**: <10ms (평균 메시지 처리)
- **동시 연결**: 10K+ (하드웨어 의존)
- **Stage 수**: 1K+ (메모리 의존)

### 7.2 확장 전략

#### 수평 확장 (Horizontal Scaling)
- 여러 Room 서버 인스턴스 실행
- 로드 밸런서를 통한 연결 분산
- Stage ID 기반 샤딩

#### 수직 확장 (Vertical Scaling)
- CPU 코어 수에 따라 처리량 증가
- 메모리 증설로 Stage 수 증가

## 8. 개발 로드맵

### Phase 1: Core Engine (현재)
- Stage/Actor 모델 구현
- 메시지 디스패처
- 타이머 시스템
- TCP/WebSocket 지원

### Phase 2: HTTP API
- REST API 엔드포인트
- Swagger 통합
- 모니터링 API

### Phase 3: Advanced Features
- 통계 및 모니터링
- 퍼시스턴스 연동 헬퍼
- 클러스터링 지원 (선택)

### Phase 4: Ecosystem
- 클라이언트 SDK (Unity, Unreal)
- 샘플 프로젝트
- 문서 및 튜토리얼

## 9. 라이센스 및 지원

- **라이센스**: MIT (예정)
- **언어**: C# (.NET 8.0 / 9.0 / 10.0)
- **문서**: 한국어, 영어 지원
- **커뮤니티**: GitHub Issues, Discord

## 10. 다음 단계

이 문서를 읽은 후, 다음 문서를 순서대로 참고하세요:

1. `01-architecture.md` - 상세 아키텍처
2. `02-packet-structure.md` - 패킷 구조
3. `03-stage-actor-model.md` - Stage/Actor 모델
4. `04-timer-system.md` - 타이머 시스템
5. `05-http-api.md` - HTTP API
6. `06-socket-transport.md` - 소켓 전송
7. `07-client-protocol.md` - 클라이언트 프로토콜
8. `08-metrics-observability.md` - 메트릭 및 옵저버빌리티
