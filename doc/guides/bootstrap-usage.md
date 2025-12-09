# PlayHouse Bootstrap System Usage Guide

> 작성일: 2025-12-09
> 버전: 1.0
> 목적: PlayHouse Bootstrap 시스템 사용법 및 예제 가이드

## 개요

PlayHouse Bootstrap 시스템은 서버 초기화 및 테스트 환경 설정을 단순화하는 Fluent API 기반 시스템입니다.

### 주요 이점

- **간편한 설정**: 50줄 이상의 DI 설정 코드를 3-5줄로 단축
- **테스트 친화적**: xUnit Fixture와 완벽 통합
- **타입 안전성**: 컴파일 타임 타입 검증
- **Clean Architecture**: Core 레이어에서 제공하는 표준 API

## 핵심 컴포넌트

### 1. PlayHouseBootstrap (진입점)

```csharp
public static class PlayHouseBootstrap
{
    public static PlayHouseBootstrapBuilder Create();
}
```

정적 진입점 클래스로 모든 부트스트랩 작업은 여기서 시작합니다.

### 2. PlayHouseBootstrapBuilder (Fluent API)

```csharp
public sealed class PlayHouseBootstrapBuilder
{
    // 옵션 설정
    public PlayHouseBootstrapBuilder WithOptions(Action<PlayHouseOptions> configure);

    // Stage 타입 등록
    public PlayHouseBootstrapBuilder WithStage<TStage>(string stageTypeName)
        where TStage : IStage;

    // Actor 타입 등록
    public PlayHouseBootstrapBuilder WithActor<TActor>(string stageTypeName)
        where TActor : IActor;

    // 로깅 설정
    public PlayHouseBootstrapBuilder WithLogging(Action<ILoggingBuilder> configure);

    // 추가 서비스 등록
    public PlayHouseBootstrapBuilder WithServices(Action<IServiceCollection> configure);

    // 빌드 및 실행
    public IHost Build();
    public Task RunAsync(CancellationToken cancellationToken = default);
    public Task<IHost> StartAsync(CancellationToken cancellationToken = default);
}
```

### 3. TestServerFixture (테스트 인프라)

```csharp
public class TestServerFixture : IAsyncDisposable
{
    public TestServerFixture RegisterStage<TStage>(string stageTypeName) where TStage : IStage;
    public TestServerFixture RegisterActor<TActor>(string stageTypeName) where TActor : IActor;
    public TestServerFixture WithServices(Action<IServiceCollection> configure);
    public Task<TestServer> StartServerAsync();
    public Task<TestServer> RestartServerAsync();
}
```

### 4. TestServer (서비스 접근자)

```csharp
public sealed class TestServer
{
    public int Port { get; }
    public string Endpoint { get; }

    public T GetService<T>() where T : notnull;

    // 주요 서비스 직접 접근
    public StageFactory StageFactory { get; }
    public StagePool StagePool { get; }
    public SessionManager SessionManager { get; }
    public PacketDispatcher PacketDispatcher { get; }
    public PlayHouseServer Server { get; }
}
```

## 사용 예제

### 운영 환경 서버 시작

```csharp
using PlayHouse.Core.Bootstrap;

await PlayHouseBootstrap.Create()
    .WithOptions(options =>
    {
        options.Ip = "0.0.0.0";
        options.Port = 5000;
    })
    .WithStage<ChatStage>("chat")
    .WithActor<ChatActor>("chat")
    .WithStage<GameStage>("game")
    .WithActor<GameActor>("game")
    .WithLogging(logging =>
    {
        logging.SetMinimumLevel(LogLevel.Information);
        logging.AddConsole();
    })
    .RunAsync();
```

### 테스트 환경 (Before & After 비교)

#### Before: 기존 방식 (50+ 줄)

```csharp
public class ChatRoomE2ETests : IAsyncLifetime
{
    private IHost? _host;
    private StageFactory? _stageFactory;
    private StagePool? _stagePool;
    private SessionManager? _sessionManager;
    private PacketDispatcher? _dispatcher;

    public async Task InitializeAsync()
    {
        ChatStage.Reset();
        ChatActor.Reset();

        _host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                // 옵션 설정
                services.AddOptions<PlayHouseOptions>()
                    .Configure(opts =>
                    {
                        typeof(PlayHouseOptions).GetProperty(nameof(PlayHouseOptions.Ip))!
                            .SetValue(opts, "127.0.0.1");
                        typeof(PlayHouseOptions).GetProperty(nameof(PlayHouseOptions.Port))!
                            .SetValue(opts, 5000);
                        // ...
                    });

                // 핵심 서비스 등록
                services.AddSingleton<PacketSerializer>();
                services.AddSingleton<SessionManager>();
                services.AddSingleton<StagePool>();
                services.AddSingleton<PacketDispatcher>();

                // TimerManager 등록
                services.AddSingleton<TimerManager>(sp =>
                {
                    // 복잡한 의존성 설정...
                });

                // StageFactory 등록 - ChatStage 타입 등록
                services.AddSingleton<StageFactory>(sp =>
                {
                    var factory = new StageFactory(/* 많은 파라미터 */);
                    factory.Registry.RegisterStageType<ChatStage>("chat-stage");
                    factory.Registry.RegisterActorType<ChatActor>("chat-stage");
                    return factory;
                });

                // PlayHouseServer 등록
                services.AddHostedService<PlayHouseServer>();
                // ...
            })
            .ConfigureLogging(logging =>
            {
                logging.SetMinimumLevel(LogLevel.Debug);
                logging.AddConsole();
            })
            .Build();

        await _host.StartAsync();

        _stageFactory = _host.Services.GetRequiredService<StageFactory>();
        _stagePool = _host.Services.GetRequiredService<StagePool>();
        _sessionManager = _host.Services.GetRequiredService<SessionManager>();
        _dispatcher = _host.Services.GetRequiredService<PacketDispatcher>();
    }

    public async Task DisposeAsync()
    {
        if (_host != null)
        {
            await _host.StopAsync();
            _host.Dispose();
        }
    }
}
```

#### After: Bootstrap 사용 (5줄!)

```csharp
using PlayHouse.Tests.Shared;

public class ChatRoomE2ETests : IAsyncLifetime
{
    private TestServerFixture _fixture = null!;
    private TestServer _server = null!;

    public async Task InitializeAsync()
    {
        ChatStage.Reset();
        ChatActor.Reset();

        // Bootstrap을 사용한 간단한 설정
        _fixture = new TestServerFixture()
            .RegisterStage<ChatStage>("chat-stage")
            .RegisterActor<ChatActor>("chat-stage");

        _server = await _fixture.StartServerAsync();
    }

    public async Task DisposeAsync()
    {
        await _fixture.DisposeAsync();
    }

    [Fact]
    public async Task Test_ChatMessage()
    {
        // _server를 통해 모든 서비스에 직접 접근 가능
        var stageFactory = _server.StageFactory;
        var sessionManager = _server.SessionManager;

        // 테스트 로직...
    }
}
```

### 고급 사용 예제

#### 1. 커스텀 서비스 등록

```csharp
_fixture = new TestServerFixture()
    .RegisterStage<ChatStage>("chat-stage")
    .RegisterActor<ChatActor>("chat-stage")
    .WithServices(services =>
    {
        // 테스트용 Mock 서비스 등록
        services.AddSingleton<IExternalService, MockExternalService>();

        // 테스트용 설정 추가
        services.Configure<CustomOptions>(opts =>
        {
            opts.EnableFeatureX = true;
        });
    });
```

#### 2. 여러 Stage 타입 등록

```csharp
_fixture = new TestServerFixture()
    .RegisterStage<ChatStage>("chat")
    .RegisterActor<ChatActor>("chat")
    .RegisterStage<GameStage>("game")
    .RegisterActor<GameActor>("game")
    .RegisterStage<LobbyStage>("lobby");  // Actor 없는 Stage
```

#### 3. 서버 재시작

```csharp
// 초기 서버 시작
_server = await _fixture.StartServerAsync();

// 테스트 수행...

// 새 포트로 재시작 (이전 서버는 자동 종료)
_server = await _fixture.RestartServerAsync();
```

## 프로젝트 구조

```
PlayHouse-NET/
├── src/PlayHouse/
│   └── Core/Bootstrap/
│       ├── IPlayHouseHost.cs              # 호스트 인터페이스
│       ├── PlayHouseBootstrap.cs          # 정적 진입점
│       └── PlayHouseBootstrapBuilder.cs   # Fluent API 빌더
│
└── tests/PlayHouse.Tests.Shared/
    ├── PlayHouse.Tests.Shared.csproj      # 공유 테스트 인프라
    └── TestServerFixture.cs               # xUnit Fixture
```

## Clean Architecture 준수

Bootstrap 시스템은 Clean Architecture 원칙을 준수합니다:

1. **Core 레이어**: Bootstrap 로직이 Core/Bootstrap에 위치
2. **Infrastructure 레이어**: PlayHouseServer 등은 Bootstrap이 정의한 인터페이스 구현
3. **의존성 역전**: Core가 Infrastructure를 참조하지 않음

```
┌─────────────────────────────────────────────────────────────┐
│                    Presentation Layer                        │
│                    (Tests, Console App)                      │
├─────────────────────────────────────────────────────────────┤
│                   Infrastructure Layer                       │
│        (PlayHouseServer, TcpServer, WebSocketServer)        │
├─────────────────────────────────────────────────────────────┤
│                      Core Layer                              │
│   ┌─────────────────────────────────────────────────────┐   │
│   │              Core/Bootstrap/                         │   │
│   │   - PlayHouseBootstrap.cs (진입점)                   │   │
│   │   - PlayHouseBootstrapBuilder.cs (Fluent API)       │   │
│   │   - IPlayHouseHost.cs (호스트 인터페이스)            │   │
│   └─────────────────────────────────────────────────────┘   │
├─────────────────────────────────────────────────────────────┤
│                   Abstractions Layer                         │
│              (IStage, IActor, IPacket, etc.)                │
└─────────────────────────────────────────────────────────────┘
```

## API 참조

### PlayHouseOptions 설정

```csharp
.WithOptions(options =>
{
    options.Ip = "127.0.0.1";           // 서버 IP
    options.Port = 5000;                 // 서버 포트
    options.EnableWebSocket = false;     // WebSocket 활성화
})
```

### 로깅 설정

```csharp
.WithLogging(logging =>
{
    logging.SetMinimumLevel(LogLevel.Debug);
    logging.AddConsole();
    logging.AddFile("logs/playhouse.log");
})
```

### TestServerFixture 속성

- `Port`: 할당된 테스트 서버 포트 (자동 할당)
- `Endpoint`: TCP 엔드포인트 문자열 (예: `tcp://127.0.0.1:52731`)
- `IsStarted`: 서버 시작 여부

## 마이그레이션 가이드

기존 테스트를 Bootstrap 시스템으로 마이그레이션하는 단계:

1. **프로젝트 참조 추가**
   ```xml
   <ProjectReference Include="..\PlayHouse.Tests.Shared\PlayHouse.Tests.Shared.csproj" />
   ```

2. **using 문 추가**
   ```csharp
   using PlayHouse.Tests.Shared;
   ```

3. **필드 단순화**
   ```csharp
   // Before
   private IHost? _host;
   private StageFactory? _stageFactory;
   // ...

   // After
   private TestServerFixture _fixture = null!;
   private TestServer _server = null!;
   ```

4. **InitializeAsync 간소화**
   ```csharp
   public async Task InitializeAsync()
   {
       _fixture = new TestServerFixture()
           .RegisterStage<YourStage>("stage-type")
           .RegisterActor<YourActor>("stage-type");

       _server = await _fixture.StartServerAsync();
   }
   ```

5. **서비스 접근 업데이트**
   ```csharp
   // Before
   var factory = _host.Services.GetRequiredService<StageFactory>();

   // After
   var factory = _server.StageFactory;
   ```

## 실제 예제

전체 작동 예제는 다음 파일들을 참조하세요:

- `tests/PlayHouse.Tests.E2E/BootstrapExampleTests.cs` - Bootstrap 사용 예제
- `tests/PlayHouse.Tests.Shared/TestServerFixture.cs` - TestServerFixture 구현
- `src/PlayHouse/Core/Bootstrap/PlayHouseBootstrapBuilder.cs` - 빌더 구현

## 문제 해결

### Q: "Stage type not registered" 오류가 발생합니다

A: `RegisterStage<T>()` 호출 시 타입 이름이 일치하는지 확인하세요:

```csharp
_fixture.RegisterStage<ChatStage>("chat-stage");  // 정확한 타입 이름 사용
await _server.StageFactory.CreateStageAsync("chat-stage", packet);  // 동일한 이름 사용
```

### Q: 서비스를 찾을 수 없습니다

A: `TestServer.GetService<T>()` 또는 직접 속성을 사용하세요:

```csharp
var factory = _server.StageFactory;  // 권장
var factory = _server.GetService<StageFactory>();  // 대안
```

### Q: 테스트 간 상태가 공유됩니다

A: 각 테스트마다 `Reset()` 메서드를 호출하거나 새 Fixture를 생성하세요:

```csharp
public async Task InitializeAsync()
{
    ChatStage.Reset();  // 정적 상태 초기화
    ChatActor.Reset();

    _fixture = new TestServerFixture()  // 새 인스턴스 생성
        .RegisterStage<ChatStage>("chat-stage");
}
```

## 다음 단계

- [Bootstrap 구현 계획](../plans/bootstrap-implementation-plan.md) - 상세 구현 계획
- [아키텍처 가이드](../architecture-guide.md) - 전체 시스템 아키텍처
- [테스트 전략](../testing-strategy.md) - 테스트 작성 가이드
