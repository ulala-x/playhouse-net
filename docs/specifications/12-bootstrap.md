# PlayHouse-NET Bootstrap 시스템

## 1. 개요

Bootstrap 시스템은 PlayHouse-NET 프레임워크의 초기화 및 구성을 담당하는 핵심 컴포넌트입니다. Clean Architecture 원칙에 따라 Core 레이어에 위치하며, 운영 환경과 테스트 환경 모두에서 일관된 서버 구성을 제공합니다.

### 1.1 목적

- **단일 진입점 제공**: 프레임워크 초기화를 위한 통합된 API
- **설정 간소화**: Fluent API를 통한 직관적인 서버 구성
- **테스트 지원**: 테스트 환경에서 재사용 가능한 Fixture 제공
- **타입 안전성**: 컴파일 타임 Stage/Actor 타입 검증

### 1.2 설계 원칙

```
┌─────────────────────────────────────────────────────────┐
│              Bootstrap 설계 원칙                         │
├─────────────────────────────────────────────────────────┤
│                                                         │
│  1. 의존성 역전 (Dependency Inversion)                  │
│     - Core가 Infrastructure를 참조하지 않음             │
│     - 인터페이스로 추상화                                │
│                                                         │
│  2. 단일 진입점 (Single Entry Point)                    │
│     - PlayHouseBootstrap 클래스로 모든 초기화 통합      │
│     - 일관된 설정 패턴                                   │
│                                                         │
│  3. Fluent API                                          │
│     - 메서드 체이닝으로 직관적인 설정                    │
│     - 가독성 높은 코드                                   │
│                                                         │
│  4. 테스트 친화성 (Test-Friendly)                       │
│     - TestServerFixture로 테스트 환경 캡슐화            │
│     - 자동 포트 할당, 상태 초기화                        │
│                                                         │
└─────────────────────────────────────────────────────────┘
```

---

## 2. 아키텍처

### 2.1 Clean Architecture에서의 위치

```
┌─────────────────────────────────────────────────────────┐
│                   Application Layer                      │
│                   (User Application)                     │
│                   - Program.cs                           │
│                   - Custom Stages/Actors                 │
├─────────────────────────────────────────────────────────┤
│                Infrastructure Layer                      │
│        - PlayHouseServer (IHostedService 구현)          │
│        - TcpServer, WebSocketServer                     │
│        - PlayHouseServiceExtensions (DI만)              │
├─────────────────────────────────────────────────────────┤
│                      Core Layer                          │
│   ┌─────────────────────────────────────────────────┐   │
│   │           Core/Bootstrap/                        │   │
│   │   - PlayHouseBootstrap (진입점)                  │   │
│   │   - PlayHouseBootstrapBuilder (Fluent API)      │   │
│   │   - IPlayHouseHost (호스트 인터페이스)           │   │
│   │   - PlayHouseHostImpl (호스트 구현)             │   │
│   └─────────────────────────────────────────────────┘   │
│   ┌─────────────────────────────────────────────────┐   │
│   │           Core/Stage/                            │   │
│   │   - StageTypeRegistry (타입 레지스트리)         │   │
│   │   - StageFactory (Stage 생성)                   │   │
│   └─────────────────────────────────────────────────┘   │
├─────────────────────────────────────────────────────────┤
│                 Abstractions Layer                       │
│              - IStage, IActor, IPacket                  │
└─────────────────────────────────────────────────────────┘

의존성 방향:
Application → Infrastructure → Core → Abstractions
```

**핵심 원칙:**
- Bootstrap은 **Core 레이어**에 위치 (시스템 초기화는 도메인의 핵심)
- Infrastructure는 Bootstrap이 정의한 인터페이스 구현만 제공
- **의존성 역전**: Core가 Infrastructure를 참조하지 않음

### 2.2 컴포넌트 구조

```
src/PlayHouse/Core/Bootstrap/
├── PlayHouseBootstrap.cs          # 정적 진입점
├── PlayHouseBootstrapBuilder.cs   # Fluent API 빌더
├── IPlayHouseHost.cs              # 호스트 추상화
└── PlayHouseHostImpl.cs           # 호스트 구현체

src/PlayHouse/Core/Stage/
└── StageTypeRegistry.cs           # Stage/Actor 타입 레지스트리

tests/PlayHouse.Tests.Shared/      # 테스트 전용
├── Fixtures/
│   ├── TestServerFixture.cs       # xUnit Fixture
│   └── TestServer.cs              # 서버 래퍼
└── TestImplementations/
    ├── TestStage.cs               # 공용 테스트 Stage
    └── TestActor.cs               # 공용 테스트 Actor
```

---

## 3. 핵심 컴포넌트

### 3.1 PlayHouseBootstrap (진입점)

**역할**: 프레임워크 초기화를 위한 정적 진입점

```csharp
/// <summary>
/// PlayHouse 서버 부트스트랩 진입점.
/// Core 레이어에서 제공하는 시스템 초기화 API입니다.
/// </summary>
public static class PlayHouseBootstrap
{
    /// <summary>
    /// 새 부트스트랩 빌더를 생성합니다.
    /// </summary>
    public static PlayHouseBootstrapBuilder Create() => new();
}
```

### 3.2 PlayHouseBootstrapBuilder (Fluent API)

**역할**: 체이닝 방식의 서버 설정 API

**주요 메서드:**

| 메서드 | 설명 | 반환 |
|--------|------|------|
| `WithOptions(Action<PlayHouseOptions>)` | PlayHouse 옵션 설정 | Builder |
| `WithLogging(Action<ILoggingBuilder>)` | 로깅 설정 | Builder |
| `WithStage<TStage>(string)` | Stage 타입 등록 | Builder |
| `WithActor<TActor>(string)` | Actor 타입 등록 | Builder |
| `WithServices(Action<IServiceCollection>)` | 추가 서비스 등록 | Builder |
| `Build()` | IHost 빌드 | IHost |
| `RunAsync(CancellationToken)` | 서버 빌드 및 실행 | Task |
| `StartAsync(CancellationToken)` | 서버 시작 (테스트용) | Task<IHost> |

**내부 동작:**
```csharp
public PlayHouseBootstrapBuilder WithStage<TStage>(string stageTypeName)
    where TStage : IStage
{
    _registry.RegisterStageType<TStage>(stageTypeName);
    return this;
}

public IHost Build()
{
    return Host.CreateDefaultBuilder()
        .ConfigureServices(services =>
        {
            ConfigurePlayHouseServices(services);
            _additionalServices?.Invoke(services);
        })
        .ConfigureLogging(logging =>
        {
            _loggingAction?.Invoke(logging);
        })
        .Build();
}
```

### 3.3 IPlayHouseHost (호스트 인터페이스)

**역할**: 호스트 추상화 (Core 레이어 정의, Infrastructure 레이어 구현)

```csharp
/// <summary>
/// PlayHouse 호스트 인터페이스.
/// Core 레이어에서 정의하며, Infrastructure 레이어에서 구현합니다.
/// </summary>
public interface IPlayHouseHost : IAsyncDisposable
{
    /// <summary>호스트를 시작합니다.</summary>
    Task StartAsync(CancellationToken cancellationToken = default);

    /// <summary>호스트를 중지합니다.</summary>
    Task StopAsync(CancellationToken cancellationToken = default);

    /// <summary>호스트 실행 여부</summary>
    bool IsRunning { get; }

    /// <summary>Core 서비스 접근</summary>
    StageFactory StageFactory { get; }
    StagePool StagePool { get; }
    SessionManager SessionManager { get; }
    PacketDispatcher PacketDispatcher { get; }

    /// <summary>서비스 조회</summary>
    T GetService<T>() where T : notnull;
}
```

### 3.4 StageTypeRegistry (타입 레지스트리)

**역할**: Stage/Actor 타입 등록 및 조회

**주요 메서드:**

| 메서드 | 설명 | 반환 |
|--------|------|------|
| `RegisterStageType<TStage>(string)` | Stage 타입 등록 | Registry |
| `RegisterActorType<TActor>(string)` | Actor 타입 등록 | Registry |
| `GetStageType(string)` | 등록된 Stage 타입 조회 | Type? |
| `GetActorType(string)` | 등록된 Actor 타입 조회 | Type? |
| `HasStageType(string)` | Stage 타입 등록 여부 | bool |
| `GetAllStageTypes()` | 모든 Stage 타입 반환 | Dictionary |

**설계 특징:**
- Fluent API 지원 (메서드 체이닝)
- 타입 안전성 (제네릭 제약)
- 런타임 타입 조회 지원

```csharp
public sealed class StageTypeRegistry
{
    private readonly Dictionary<string, Type> _stageTypes = new();
    private readonly Dictionary<string, Type> _actorTypes = new();

    public StageTypeRegistry RegisterStageType<TStage>(string stageTypeName)
        where TStage : IStage
    {
        _stageTypes[stageTypeName] = typeof(TStage);
        return this;
    }

    public Type? GetStageType(string stageTypeName)
    {
        return _stageTypes.TryGetValue(stageTypeName, out var type)
            ? type : null;
    }
}
```

---

## 4. 사용 패턴

### 4.1 운영 환경 Bootstrap

**간단한 서버 시작:**

```csharp
// Program.cs
await PlayHouseBootstrap.Create()
    .WithOptions(options => {
        options.Ip = "0.0.0.0";
        options.Port = 5000;
        options.EnableWebSocket = true;
    })
    .WithStage<LobbyStage>("lobby")
    .WithActor<PlayerActor>("lobby")
    .WithStage<GameRoomStage>("game")
    .WithActor<GamePlayerActor>("game")
    .WithLogging(logging => {
        logging.AddConsole();
        logging.SetMinimumLevel(LogLevel.Information);
    })
    .RunAsync();
```

**고급 설정:**

```csharp
await PlayHouseBootstrap.Create()
    .WithOptions(options => {
        options.Ip = "0.0.0.0";
        options.Port = 5000;
        options.EnableWebSocket = true;
        options.MaxSessions = 10000;
        options.MaxPacketSize = 2 * 1024 * 1024; // 2MB
    })
    .WithStage<LobbyStage>("lobby")
    .WithActor<PlayerActor>("lobby")
    .WithStage<GameRoomStage>("game")
    .WithActor<GamePlayerActor>("game")
    .WithStage<ChatStage>("chat")
    .WithActor<ChatActor>("chat")
    .WithServices(services => {
        // 커스텀 서비스 추가
        services.AddSingleton<ILeaderboard, RedisLeaderboard>();
        services.AddSingleton<IMatchmaker, SkillBasedMatchmaker>();
    })
    .WithLogging(logging => {
        logging.AddConsole();
        logging.AddFile("logs/playhouse.log");
        logging.SetMinimumLevel(LogLevel.Debug);
    })
    .RunAsync();
```

### 4.2 테스트 환경 Bootstrap

**TestServerFixture 사용 (권장):**

```csharp
using PlayHouse.Testing;
using PlayHouse.Testing.TestImplementations;

public class GameRoomTests : IAsyncLifetime
{
    private TestServerFixture _fixture = null!;
    private TestServer _server = null!;

    public async Task InitializeAsync()
    {
        // 테스트 상태 초기화
        TestStage.Reset();
        TestActor.Reset();

        // Fixture 설정 - 3줄로 서버 구성 완료
        _fixture = new TestServerFixture()
            .RegisterStage<GameRoomStage>("game")
            .RegisterActor<GamePlayerActor>("game");

        _server = await _fixture.StartServerAsync();
    }

    public async Task DisposeAsync()
    {
        await _fixture.DisposeAsync();
    }

    [Fact]
    public async Task GameRoom_ShouldAcceptPlayers()
    {
        // Arrange
        var stageFactory = _server.StageFactory;
        var createPacket = new TestPacket("CreateRoom");

        // Act
        var (stageContext, errorCode, _) =
            await stageFactory.CreateStageAsync("game", createPacket);

        // Assert
        errorCode.Should().Be(ErrorCode.Success);
        stageContext.Should().NotBeNull();
    }
}
```

**수동 설정 (고급):**

```csharp
public async Task CustomBootstrapTest()
{
    var builder = PlayHouseBootstrap.Create()
        .WithOptions(opts => {
            opts.Ip = "127.0.0.1";
            opts.Port = 20000;
        })
        .WithStage<TestStage>("test")
        .WithLogging(logging => {
            logging.SetMinimumLevel(LogLevel.Debug);
        });

    using var host = await builder.StartAsync();

    var stageFactory = host.Services.GetRequiredService<StageFactory>();

    // 테스트 로직...

    await host.StopAsync();
}
```

### 4.3 Stage/Actor 타입 등록

**타입 등록 방식:**

```csharp
// 1. 간단한 등록
builder.WithStage<ChatStage>("chat")
       .WithActor<ChatActor>("chat");

// 2. 여러 Stage 타입
builder.WithStage<LobbyStage>("lobby")
       .WithActor<PlayerActor>("lobby")
       .WithStage<GameStage>("game")
       .WithActor<GamePlayerActor>("game")
       .WithStage<ChatStage>("chat")
       .WithActor<ChatActor>("chat");

// 3. 동일 Actor 타입, 다른 Stage
builder.WithStage<PvPStage>("pvp")
       .WithActor<CombatActor>("pvp")
       .WithStage<PvEStage>("pve")
       .WithActor<CombatActor>("pve"); // 재사용 가능
```

**런타임 타입 조회:**

```csharp
var stageFactory = host.Services.GetRequiredService<StageFactory>();

// Registry를 통한 타입 조회
var registry = stageFactory.Registry;
var stageType = registry.GetStageType("game");
var actorType = registry.GetActorType("game");

if (stageType != null)
{
    // 리플렉션을 통한 Stage 생성
    var stage = (IStage)Activator.CreateInstance(stageType)!;
}
```

---

## 5. 테스트 지원

### 5.1 TestServerFixture

**역할**: 테스트 환경에서 서버 구성 캡슐화

**주요 기능:**
- 자동 포트 할당 (포트 충돌 방지)
- 서버 라이프사이클 관리
- Core 서비스 접근 제공
- xUnit IAsyncLifetime 통합

**클래스 구조:**

```csharp
public class TestServerFixture : IAsyncDisposable
{
    private readonly PlayHouseBootstrapBuilder _builder;
    private IHost? _host;
    private int _port;

    public TestServerFixture()
    {
        _port = GetAvailablePort(); // 자동 포트 할당
        _builder = PlayHouseBootstrap.Create()
            .WithOptions(opts => {
                opts.Ip = "127.0.0.1";
                opts.Port = _port;
            })
            .WithLogging(logging => {
                logging.SetMinimumLevel(LogLevel.Debug);
                logging.AddConsole();
            });
    }

    public int Port => _port;
    public string Endpoint => $"tcp://127.0.0.1:{_port}";
    public bool IsStarted => _host != null;

    public TestServerFixture RegisterStage<TStage>(string stageTypeName)
        where TStage : IStage
    {
        _builder.WithStage<TStage>(stageTypeName);
        return this;
    }

    public TestServerFixture RegisterActor<TActor>(string stageTypeName)
        where TActor : IActor
    {
        _builder.WithActor<TActor>(stageTypeName);
        return this;
    }

    public async Task<TestServer> StartServerAsync()
    {
        _host = await _builder.StartAsync();
        return new TestServer(_host, _port);
    }

    public async ValueTask DisposeAsync()
    {
        if (_host != null)
        {
            await _host.StopAsync();
            _host.Dispose();
        }
    }

    private static int GetAvailablePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}
```

### 5.2 TestServer (래퍼)

**역할**: 실행 중인 테스트 서버 접근 제공

```csharp
public sealed class TestServer
{
    private readonly IHost _host;

    public TestServer(IHost host, int port)
    {
        _host = host;
        Port = port;
    }

    public int Port { get; }
    public string Endpoint => $"tcp://127.0.0.1:{Port}";

    // 서비스 접근
    public T GetService<T>() where T : notnull
        => _host.Services.GetRequiredService<T>();

    // Core 서비스 단축 접근
    public StageFactory StageFactory => GetService<StageFactory>();
    public StagePool StagePool => GetService<StagePool>();
    public SessionManager SessionManager => GetService<SessionManager>();
    public PacketDispatcher PacketDispatcher => GetService<PacketDispatcher>();
    public PlayHouseServer Server => GetService<PlayHouseServer>();
}
```

### 5.3 테스트 격리 및 상태 초기화

**테스트 격리 전략:**

```csharp
// 각 테스트마다 독립적인 서버 인스턴스
public class ChatTests : IAsyncLifetime
{
    private TestServerFixture _fixture = null!;
    private TestServer _server = null!;

    public async Task InitializeAsync()
    {
        // 1. 정적 상태 초기화
        ChatStage.Reset();
        ChatActor.Reset();

        // 2. 새 Fixture 생성 (독립 서버)
        _fixture = new TestServerFixture()
            .RegisterStage<ChatStage>("chat")
            .RegisterActor<ChatActor>("chat");

        // 3. 서버 시작
        _server = await _fixture.StartServerAsync();
    }

    public async Task DisposeAsync()
    {
        // 서버 종료 및 리소스 정리
        await _fixture.DisposeAsync();
    }
}
```

**병렬 테스트 실행:**

```csharp
// xUnit Collection으로 테스트 격리
[Collection("ChatTests")]
public class ChatRoomTests : IAsyncLifetime { }

[Collection("GameTests")]
public class GameRoomTests : IAsyncLifetime { }

// 각 Collection은 독립적인 서버 인스턴스 사용
// 자동 포트 할당으로 포트 충돌 방지
```

---

## 6. DI 통합

### 6.1 서비스 등록 구조

**Bootstrap에서 자동 등록되는 서비스:**

```csharp
private void ConfigurePlayHouseServices(IServiceCollection services)
{
    // 1. 옵션 설정
    services.AddOptions<PlayHouseOptions>()
        .Configure(opts => { /* ... */ })
        .ValidateOnStart();

    // 2. Core 서비스
    services.AddSingleton<PacketSerializer>();
    services.AddSingleton<SessionManager>();
    services.AddSingleton<StagePool>();
    services.AddSingleton<PacketDispatcher>();

    // 3. StageTypeRegistry
    services.AddSingleton(_registry);

    // 4. TimerManager (팩토리 패턴)
    services.AddSingleton<TimerManager>(sp => {
        var dispatcher = sp.GetRequiredService<PacketDispatcher>();
        var logger = sp.GetRequiredService<ILoggerFactory>()
            .CreateLogger<TimerManager>();
        return new TimerManager(
            packet => dispatcher.Dispatch(packet),
            logger);
    });

    // 5. StageFactory (팩토리 패턴)
    services.AddSingleton<StageFactory>(sp => {
        var stagePool = sp.GetRequiredService<StagePool>();
        var dispatcher = sp.GetRequiredService<PacketDispatcher>();
        var timerManager = sp.GetRequiredService<TimerManager>();
        var sessionManager = sp.GetRequiredService<SessionManager>();
        var loggerFactory = sp.GetRequiredService<ILoggerFactory>();

        return new StageFactory(
            stagePool,
            dispatcher,
            timerManager,
            sessionManager,
            loggerFactory);
    });

    // 6. PlayHouseServer (IHostedService)
    services.AddHostedService<PlayHouseServer>();
}
```

### 6.2 커스텀 서비스 추가

```csharp
await PlayHouseBootstrap.Create()
    .WithServices(services => {
        // 게임 로직 서비스
        services.AddSingleton<ILeaderboard, RedisLeaderboard>();
        services.AddSingleton<IMatchmaker, SkillBasedMatchmaker>();
        services.AddSingleton<IInventorySystem, DatabaseInventory>();

        // 외부 연동 서비스
        services.AddHttpClient<IAchievementService, AchievementService>();
        services.AddSingleton<INotificationService, FirebaseNotifications>();

        // 설정
        services.AddOptions<GameConfig>()
            .Configure(config => {
                config.MaxPlayersPerRoom = 100;
                config.MatchmakingTimeout = TimeSpan.FromSeconds(30);
            });
    })
    .RunAsync();
```

---

## 7. 마이그레이션 가이드

### 7.1 기존 코드에서 Bootstrap으로 전환

**변경 전 (수동 DI 설정):**

```csharp
// 기존 테스트 코드 (83줄)
public async Task InitializeAsync()
{
    _host = Host.CreateDefaultBuilder()
        .ConfigureServices(services =>
        {
            services.AddOptions<PlayHouseOptions>().Configure(opts => {
                opts.Ip = "127.0.0.1";
                opts.Port = _testPort;
            });
            services.AddSingleton<PacketSerializer>();
            services.AddSingleton<SessionManager>();
            services.AddSingleton<StagePool>();
            services.AddSingleton<PacketDispatcher>();
            services.AddSingleton<TimerManager>(sp => { /* ... */ });
            services.AddSingleton<StageFactory>(sp => { /* ... */ });
            services.AddHostedService<PlayHouseServer>();
            // ... 30+ 줄 더
        })
        .ConfigureLogging(logging => { /* ... */ })
        .Build();

    await _host.StartAsync();

    _stageFactory = _host.Services.GetRequiredService<StageFactory>();
    _stagePool = _host.Services.GetRequiredService<StagePool>();
    // ... 서비스 조회 코드
}
```

**변경 후 (Bootstrap 사용):**

```csharp
// Bootstrap 사용 (10줄)
public async Task InitializeAsync()
{
    TestStage.Reset();

    _fixture = new TestServerFixture()
        .RegisterStage<TestStage>("test");

    _server = await _fixture.StartServerAsync();

    // _server.StageFactory, _server.StagePool 등 직접 접근 가능
}
```

**코드 감소율: 88% (83줄 → 10줄)**

### 7.2 운영 환경 전환

**변경 전:**

```csharp
var builder = WebApplication.CreateBuilder(args);

// 수동 서비스 등록 (50+ 줄)
builder.Services.AddOptions<PlayHouseOptions>()...
builder.Services.AddSingleton<PacketSerializer>();
// ...

var app = builder.Build();
app.Run();
```

**변경 후:**

```csharp
await PlayHouseBootstrap.Create()
    .WithOptions(options => {
        options.Ip = "0.0.0.0";
        options.Port = 5000;
    })
    .WithStage<LobbyStage>("lobby")
    .WithActor<PlayerActor>("lobby")
    .WithLogging(logging => logging.AddConsole())
    .RunAsync();
```

---

## 8. 성능 고려사항

### 8.1 초기화 성능

- **타입 등록**: 컴파일 타임 제네릭 → 런타임 오버헤드 최소
- **DI 컨테이너**: .NET Built-in DI 사용 (고성능)
- **Stage/Actor 인스턴스**: 지연 생성 (Lazy Initialization)

### 8.2 메모리 효율

```
StageTypeRegistry 메모리 사용량:
- Stage 타입 1개: ~120 bytes (Dictionary Entry + Type Reference)
- Actor 타입 1개: ~120 bytes
- 총 100 Stage 타입: ~24KB (무시 가능)
```

### 8.3 스레드 안전성

- **StageTypeRegistry**: 초기화 이후 Read-Only → 스레드 안전
- **IHost**: Microsoft.Extensions.Hosting → 스레드 안전 보장
- **StageFactory**: 내부적으로 동기화 처리

---

## 9. 모범 사례

### 9.1 Stage/Actor 타입 명명 규칙

```csharp
// 권장: 명확하고 일관된 이름
.WithStage<LobbyStage>("lobby")
.WithActor<PlayerActor>("lobby")
.WithStage<GameRoomStage>("game")
.WithActor<GamePlayerActor>("game")

// 비권장: 모호한 이름
.WithStage<LobbyStage>("stage1")
.WithActor<PlayerActor>("actor1")
```

### 9.2 테스트 조직화

```csharp
// 권장: 테스트 클래스별 독립 Fixture
public class GameRoomTests : IAsyncLifetime
{
    private TestServerFixture _fixture = null!;

    public async Task InitializeAsync()
    {
        _fixture = new TestServerFixture()
            .RegisterStage<GameRoomStage>("game");
    }
}

// 비권장: 여러 테스트에서 Fixture 공유
// → 테스트 간 간섭 발생 가능
```

### 9.3 설정 관리

```csharp
// 권장: 환경별 설정 분리
var config = builder.Configuration;

await PlayHouseBootstrap.Create()
    .WithOptions(options => {
        options.Ip = config["PlayHouse:Ip"] ?? "0.0.0.0";
        options.Port = config.GetValue<int>("PlayHouse:Port", 5000);
    })
    .RunAsync();
```

---

## 10. 문제 해결

### 10.1 일반적인 오류

**오류 1: Stage 타입을 찾을 수 없음**

```
System.InvalidOperationException: Stage type 'game' not found
```

**해결:**
```csharp
// Stage 타입을 등록했는지 확인
.WithStage<GameRoomStage>("game")
```

**오류 2: 포트 충돌**

```
System.Net.Sockets.SocketException: Address already in use
```

**해결:**
```csharp
// TestServerFixture는 자동 포트 할당 사용
// 수동 설정 시 포트 번호 변경
.WithOptions(opts => opts.Port = 5001)
```

**오류 3: DI 순환 참조**

```
System.InvalidOperationException: Circular dependency detected
```

**해결:**
```csharp
// WithServices에서 순환 참조 제거
// 팩토리 패턴 사용
services.AddSingleton<IService>(sp =>
    new ServiceImpl(sp.GetRequiredService<Dependency>()));
```

### 10.2 디버깅 팁

```csharp
// 1. 로깅 레벨 상승
.WithLogging(logging => {
    logging.SetMinimumLevel(LogLevel.Trace);
    logging.AddConsole();
})

// 2. Registry 내용 확인
var registry = builder.Registry;
var stageTypes = registry.GetAllStageTypes();
Console.WriteLine($"Registered stages: {string.Join(", ", stageTypes.Keys)}");

// 3. 서비스 등록 확인
var services = host.Services;
var stageFactory = services.GetService<StageFactory>();
if (stageFactory == null)
{
    throw new Exception("StageFactory not registered");
}
```

---

## 11. 향후 확장

### 11.1 계획된 기능

- **Configuration Validation**: 설정 검증 강화
- **Hot Reload**: Stage/Actor 타입 런타임 재등록
- **Metrics Integration**: Bootstrap 단계 메트릭 수집
- **Health Checks**: 초기화 상태 헬스 체크

### 11.2 확장 포인트

```csharp
// 커스텀 Bootstrap 확장
public static class CustomBootstrapExtensions
{
    public static PlayHouseBootstrapBuilder WithDatabase(
        this PlayHouseBootstrapBuilder builder,
        string connectionString)
    {
        return builder.WithServices(services => {
            services.AddDbContext<GameDbContext>(options =>
                options.UseSqlServer(connectionString));
        });
    }
}

// 사용
await PlayHouseBootstrap.Create()
    .WithDatabase("Server=localhost;Database=Game")
    .RunAsync();
```

---

## 12. 참고 자료

- `01-architecture.md` - 전체 아키텍처 개요
- `03-stage-actor-model.md` - Stage/Actor 모델 상세
- `10-testing-spec.md` - 테스트 전략
- `doc/plans/bootstrap-implementation-plan.md` - Bootstrap 구현 계획
- `doc/plans/test-modification-plan.md` - 테스트 마이그레이션 계획

---

## 변경 이력

| 날짜 | 버전 | 변경 내용 |
|------|------|----------|
| 2025-12-09 | 1.0 | 초기 스펙 작성 |
