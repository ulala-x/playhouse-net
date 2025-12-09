# PlayHouse Bootstrap 구현 계획

> 작성일: 2025-12-09
> 목적: 테스트 및 운영 환경에서 재사용 가능한 Bootstrap 시스템 구현

## 목차
1. [현재 문제점](#1-현재-문제점)
2. [Bootstrap 아키텍처 설계](#2-bootstrap-아키텍처-설계)
3. [구현 계획](#3-구현-계획)
4. [상세 구현 코드](#4-상세-구현-코드)
5. [마이그레이션 가이드](#5-마이그레이션-가이드)
6. [체크리스트](#6-체크리스트)

---

## 1. 현재 문제점

### 1.1 테스트 코드의 중복 설정

현재 E2E 및 Integration 테스트에서 **매번 50줄 이상의 중복 코드**로 서버 환경을 구성:

**문제 코드 예시 (ChatRoomE2ETests.cs:46-128)**:
```csharp
public async Task InitializeAsync()
{
    _host = Host.CreateDefaultBuilder()
        .ConfigureServices(services =>
        {
            // 매번 반복되는 20+ 줄의 설정 코드
            services.AddOptions<PlayHouseOptions>().Configure(opts => { ... });
            services.AddSingleton<PacketSerializer>();
            services.AddSingleton<SessionManager>();
            services.AddSingleton<StagePool>();
            services.AddSingleton<PacketDispatcher>();
            services.AddSingleton<TimerManager>(sp => { ... });
            services.AddSingleton<StageFactory>(sp => { ... });
            services.AddHostedService<PlayHouseServer>();
            // ...
        })
        .ConfigureLogging(logging => { ... })
        .Build();

    await _host.StartAsync();

    // 서비스 조회 코드 반복
    _stageFactory = _host.Services.GetRequiredService<StageFactory>();
    // ...
}
```

### 1.2 문제점 요약

| 문제 | 영향 | 심각도 |
|------|------|--------|
| 50줄+ 중복 설정 코드 | 유지보수 어려움, 테스트 작성 부담 | 높음 |
| StageFactory 생성 로직 노출 | 내부 구현 변경 시 모든 테스트 수정 필요 | 높음 |
| PlayHouseServiceExtensions 미사용 | DI 확장 메서드 무용지물 | 중간 |
| 테스트별 설정 불일치 가능성 | 실제 운영 환경과 다른 동작 | 높음 |
| Actor 타입 등록 불편 | Stage별로 다른 Actor 타입 사용 어려움 | 중간 |

---

## 2. Bootstrap 아키텍처 설계

### 2.1 설계 원칙

1. **단일 진입점**: `PlayHouseBootstrap` 클래스로 모든 초기화 통합
2. **Fluent API**: 체이닝 방식의 직관적인 설정 API
3. **테스트 친화적**: 테스트 전용 빌더 및 Fixture 제공
4. **확장 가능**: Stage/Actor 타입 동적 등록 지원

### 2.2 컴포넌트 구조

```
PlayHouse.Core.Bootstrap (Core 레이어 내)
├── PlayHouseBootstrap.cs          # 메인 부트스트랩 클래스
├── PlayHouseBootstrapBuilder.cs   # Fluent API 빌더
├── IPlayHouseHost.cs              # 호스트 인터페이스
└── PlayHouseHostImpl.cs           # 호스트 구현체

PlayHouse.Core.Stage (기존)
└── StageTypeRegistry.cs           # Stage/Actor 타입 레지스트리 (이미 존재)

PlayHouse.Testing (테스트 전용 - 별도 프로젝트)
├── TestServerFixture.cs           # xUnit Fixture
├── TestServerBuilder.cs           # 테스트 서버 빌더
└── InMemoryServer.cs              # 메모리 내 테스트 서버
```

**설계 원칙**:
- Bootstrap 로직은 **Core 레이어**에 위치 (Infrastructure 의존성 없음)
- Infrastructure 레이어(PlayHouseServer, TcpServer 등)는 Bootstrap에서 **인터페이스로 주입**
- 테스트 인프라는 별도 프로젝트로 분리

### 2.3 사용 예시 (목표)

**운영 환경**:
```csharp
await PlayHouseBootstrap.Create()
    .WithOptions(options => {
        options.Ip = "0.0.0.0";
        options.Port = 5000;
    })
    .WithStage<ChatStage>("chat")
    .WithActor<ChatActor>("chat")
    .WithStage<GameStage>("game")
    .WithActor<GameActor>("game")
    .WithLogging(logging => logging.AddConsole())
    .RunAsync();
```

**테스트 환경**:
```csharp
// xUnit Fixture 사용
public class ChatRoomE2ETests : IClassFixture<TestServerFixture>
{
    private readonly TestServerFixture _fixture;

    public ChatRoomE2ETests(TestServerFixture fixture)
    {
        _fixture = fixture;
        _fixture.RegisterStage<ChatStage>("chat");
        _fixture.RegisterActor<ChatActor>("chat");
    }

    [Fact]
    public async Task Test_ChatMessage()
    {
        var server = await _fixture.StartServerAsync();
        var stageFactory = server.GetService<StageFactory>();
        // 테스트 로직...
    }
}
```

---

## 3. 구현 계획

### Phase 1: Core Bootstrap (우선순위 높음)

| 순서 | 작업 | 경로 | 예상 LOC | 의존성 |
|------|------|------|----------|--------|
| 1.1 | `StageTypeRegistry.cs` 수정 | `Core/Stage/` | 80 | 없음 |
| 1.2 | `IPlayHouseHost.cs` 생성 | `Core/Bootstrap/` | 30 | 없음 |
| 1.3 | `PlayHouseBootstrapBuilder.cs` 생성 | `Core/Bootstrap/` | 150 | 1.1, 1.2 |
| 1.4 | `PlayHouseBootstrap.cs` 생성 | `Core/Bootstrap/` | 50 | 1.3 |
| 1.5 | `PlayHouseHostImpl.cs` 생성 | `Core/Bootstrap/` | 100 | 1.2 |
| 1.6 | `PlayHouseServiceExtensions.cs` 수정 | `Infrastructure/Http/` | 50 | 1.1 |

### Phase 2: Test Infrastructure (우선순위 높음)

| 순서 | 작업 | 예상 LOC | 의존성 |
|------|------|----------|--------|
| 2.1 | `TestServerBuilder.cs` 생성 | 120 | Phase 1 |
| 2.2 | `TestServerFixture.cs` 생성 | 100 | 2.1 |
| 2.3 | 기존 테스트 마이그레이션 | 수정 | 2.2 |

### Phase 3: Validation (우선순위 중간)

| 순서 | 작업 | 예상 LOC | 의존성 |
|------|------|----------|--------|
| 3.1 | Bootstrap 통합 테스트 작성 | 100 | Phase 2 |
| 3.2 | 문서화 | - | Phase 3.1 |

---

## 4. 상세 구현 코드

### 4.1 StageTypeRegistry.cs

**경로**: `src/PlayHouse/Core/Stage/StageTypeRegistry.cs`

```csharp
#nullable enable

using System;
using System.Collections.Generic;
using PlayHouse.Abstractions;

namespace PlayHouse.Core.Stage;

/// <summary>
/// Stage 및 Actor 타입 등록을 관리합니다.
/// Stage 타입과 해당 Stage에서 사용할 Actor 타입을 매핑합니다.
/// </summary>
public sealed class StageTypeRegistry
{
    private readonly Dictionary<string, Type> _stageTypes = new();
    private readonly Dictionary<string, Type> _actorTypes = new();

    /// <summary>
    /// Stage 타입을 등록합니다.
    /// </summary>
    /// <typeparam name="TStage">IStage 구현 타입</typeparam>
    /// <param name="stageTypeName">Stage 타입 식별자</param>
    public StageTypeRegistry RegisterStageType<TStage>(string stageTypeName)
        where TStage : IStage
    {
        if (string.IsNullOrWhiteSpace(stageTypeName))
            throw new ArgumentException("Stage type name cannot be empty", nameof(stageTypeName));

        _stageTypes[stageTypeName] = typeof(TStage);
        return this;
    }

    /// <summary>
    /// Actor 타입을 등록합니다.
    /// Stage 타입에 연결됩니다.
    /// </summary>
    /// <typeparam name="TActor">IActor 구현 타입</typeparam>
    /// <param name="stageTypeName">이 Actor가 사용될 Stage 타입 이름</param>
    public StageTypeRegistry RegisterActorType<TActor>(string stageTypeName)
        where TActor : IActor
    {
        if (string.IsNullOrWhiteSpace(stageTypeName))
            throw new ArgumentException("Stage type name cannot be empty", nameof(stageTypeName));

        _actorTypes[stageTypeName] = typeof(TActor);
        return this;
    }

    /// <summary>
    /// 등록된 Stage 타입을 조회합니다.
    /// </summary>
    public Type? GetStageType(string stageTypeName)
    {
        return _stageTypes.TryGetValue(stageTypeName, out var type) ? type : null;
    }

    /// <summary>
    /// 등록된 Actor 타입을 조회합니다.
    /// </summary>
    public Type? GetActorType(string stageTypeName)
    {
        return _actorTypes.TryGetValue(stageTypeName, out var type) ? type : null;
    }

    /// <summary>
    /// Stage 타입이 등록되어 있는지 확인합니다.
    /// </summary>
    public bool HasStageType(string stageTypeName) => _stageTypes.ContainsKey(stageTypeName);

    /// <summary>
    /// 등록된 모든 Stage 타입 이름을 반환합니다.
    /// </summary>
    public IReadOnlyCollection<string> GetRegisteredStageTypes() => _stageTypes.Keys;

    /// <summary>
    /// 등록된 모든 Stage 타입 정보를 반환합니다.
    /// </summary>
    public IReadOnlyDictionary<string, Type> GetAllStageTypes() => _stageTypes;

    /// <summary>
    /// 등록된 모든 Actor 타입 정보를 반환합니다.
    /// </summary>
    public IReadOnlyDictionary<string, Type> GetAllActorTypes() => _actorTypes;
}
```

### 4.2 IPlayHouseHost.cs

**경로**: `src/PlayHouse/Core/Bootstrap/IPlayHouseHost.cs`

```csharp
#nullable enable

using System;
using System.Threading;
using System.Threading.Tasks;
using PlayHouse.Core.Messaging;
using PlayHouse.Core.Session;
using PlayHouse.Core.Stage;

namespace PlayHouse.Core.Bootstrap;

/// <summary>
/// PlayHouse 호스트 인터페이스.
/// Core 레이어에서 정의하며, Infrastructure 레이어에서 구현합니다.
/// </summary>
public interface IPlayHouseHost : IAsyncDisposable
{
    /// <summary>
    /// 호스트를 시작합니다.
    /// </summary>
    Task StartAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 호스트를 중지합니다.
    /// </summary>
    Task StopAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 호스트가 실행 중인지 여부
    /// </summary>
    bool IsRunning { get; }

    /// <summary>
    /// StageFactory 인스턴스
    /// </summary>
    StageFactory StageFactory { get; }

    /// <summary>
    /// StagePool 인스턴스
    /// </summary>
    StagePool StagePool { get; }

    /// <summary>
    /// SessionManager 인스턴스
    /// </summary>
    SessionManager SessionManager { get; }

    /// <summary>
    /// PacketDispatcher 인스턴스
    /// </summary>
    PacketDispatcher PacketDispatcher { get; }

    /// <summary>
    /// 서비스를 조회합니다.
    /// </summary>
    T GetService<T>() where T : notnull;
}
```

### 4.3 PlayHouseBootstrapBuilder.cs

**경로**: `src/PlayHouse/Core/Bootstrap/PlayHouseBootstrapBuilder.cs`

```csharp
#nullable enable

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PlayHouse.Abstractions;
using PlayHouse.Core.Messaging;
using PlayHouse.Core.Session;
using PlayHouse.Core.Stage;
using PlayHouse.Core.Timer;

namespace PlayHouse.Core.Bootstrap;

/// <summary>
/// PlayHouse 서버 부트스트랩 빌더.
/// Fluent API로 서버 설정을 구성합니다.
/// </summary>
public sealed class PlayHouseBootstrapBuilder
{
    private Action<PlayHouseOptions>? _optionsAction;
    private Action<ILoggingBuilder>? _loggingAction;
    private readonly StageTypeRegistry _registry = new();
    private Action<IServiceCollection>? _additionalServices;

    /// <summary>
    /// PlayHouse 옵션을 설정합니다.
    /// </summary>
    public PlayHouseBootstrapBuilder WithOptions(Action<PlayHouseOptions> configure)
    {
        _optionsAction = configure;
        return this;
    }

    /// <summary>
    /// 로깅을 설정합니다.
    /// </summary>
    public PlayHouseBootstrapBuilder WithLogging(Action<ILoggingBuilder> configure)
    {
        _loggingAction = configure;
        return this;
    }

    /// <summary>
    /// Stage 타입을 등록합니다.
    /// </summary>
    public PlayHouseBootstrapBuilder WithStage<TStage>(string stageTypeName) where TStage : IStage
    {
        _registry.RegisterStageType<TStage>(stageTypeName);
        return this;
    }

    /// <summary>
    /// Actor 타입을 등록합니다.
    /// </summary>
    public PlayHouseBootstrapBuilder WithActor<TActor>(string stageTypeName) where TActor : IActor
    {
        _registry.RegisterActorType<TActor>(stageTypeName);
        return this;
    }

    /// <summary>
    /// 추가 서비스를 등록합니다.
    /// </summary>
    public PlayHouseBootstrapBuilder WithServices(Action<IServiceCollection> configure)
    {
        _additionalServices = configure;
        return this;
    }

    /// <summary>
    /// StageTypeRegistry를 반환합니다 (테스트용).
    /// </summary>
    public StageTypeRegistry Registry => _registry;

    /// <summary>
    /// IHost를 빌드합니다.
    /// </summary>
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

    /// <summary>
    /// 서버를 빌드하고 실행합니다.
    /// </summary>
    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        using var host = Build();
        await host.RunAsync(cancellationToken);
    }

    /// <summary>
    /// 서버를 빌드하고 시작합니다 (테스트용 - 블로킹하지 않음).
    /// </summary>
    public async Task<IHost> StartAsync(CancellationToken cancellationToken = default)
    {
        var host = Build();
        await host.StartAsync(cancellationToken);
        return host;
    }

    private void ConfigurePlayHouseServices(IServiceCollection services)
    {
        // 옵션 설정
        services.AddOptions<PlayHouseOptions>()
            .Configure(opts =>
            {
                // 기본값 설정
                typeof(PlayHouseOptions).GetProperty(nameof(PlayHouseOptions.Ip))!
                    .SetValue(opts, "127.0.0.1");
                typeof(PlayHouseOptions).GetProperty(nameof(PlayHouseOptions.Port))!
                    .SetValue(opts, 5000);
                typeof(PlayHouseOptions).GetProperty(nameof(PlayHouseOptions.EnableWebSocket))!
                    .SetValue(opts, false);

                // 사용자 설정 적용
                _optionsAction?.Invoke(opts);
            })
            .ValidateOnStart();

        // Core 서비스
        services.AddSingleton<PacketSerializer>();
        services.AddSingleton<SessionManager>();
        services.AddSingleton<StagePool>();
        services.AddSingleton<PacketDispatcher>();

        // StageTypeRegistry를 싱글톤으로 등록
        services.AddSingleton(_registry);

        // TimerManager
        services.AddSingleton<TimerManager>(sp =>
        {
            var dispatcher = sp.GetRequiredService<PacketDispatcher>();
            var logger = sp.GetRequiredService<ILoggerFactory>()
                .CreateLogger<TimerManager>();
            return new TimerManager(
                packet => dispatcher.Dispatch(packet),
                logger);
        });

        // StageFactory
        services.AddSingleton<StageFactory>(sp =>
        {
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

        // PlayHouseServer
        services.AddHostedService<PlayHouseServer>();
        services.AddSingleton<PlayHouseServer>(sp =>
            sp.GetServices<IHostedService>()
                .OfType<PlayHouseServer>()
                .First());
    }
}
```

### 4.4 PlayHouseBootstrap.cs

**경로**: `src/PlayHouse/Core/Bootstrap/PlayHouseBootstrap.cs`

```csharp
#nullable enable

namespace PlayHouse.Core.Bootstrap;

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

### 4.5 TestServerFixture.cs

**경로**: `tests/PlayHouse.Tests.Shared/TestServerFixture.cs`

```csharp
#nullable enable

using System;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PlayHouse.Abstractions;
using PlayHouse.Bootstrap;
using PlayHouse.Core.Messaging;
using PlayHouse.Core.Session;
using PlayHouse.Core.Stage;
using PlayHouse.Infrastructure.Http;

namespace PlayHouse.Testing;

/// <summary>
/// 테스트용 서버 Fixture.
/// xUnit IClassFixture 또는 IAsyncLifetime과 함께 사용합니다.
/// </summary>
public class TestServerFixture : IAsyncDisposable
{
    private readonly PlayHouseBootstrapBuilder _builder;
    private IHost? _host;
    private int _port;

    public TestServerFixture()
    {
        _port = GetAvailablePort();
        _builder = PlayHouseBootstrap.Create()
            .WithOptions(opts =>
            {
                typeof(PlayHouseOptions).GetProperty(nameof(PlayHouseOptions.Ip))!
                    .SetValue(opts, "127.0.0.1");
                typeof(PlayHouseOptions).GetProperty(nameof(PlayHouseOptions.Port))!
                    .SetValue(opts, _port);
            })
            .WithLogging(logging =>
            {
                logging.SetMinimumLevel(LogLevel.Debug);
                logging.AddConsole();
            });
    }

    /// <summary>
    /// 테스트 서버 포트
    /// </summary>
    public int Port => _port;

    /// <summary>
    /// 서버 엔드포인트
    /// </summary>
    public string Endpoint => $"tcp://127.0.0.1:{_port}";

    /// <summary>
    /// 서버가 시작되었는지 여부
    /// </summary>
    public bool IsStarted => _host != null;

    /// <summary>
    /// Stage 타입을 등록합니다.
    /// </summary>
    public TestServerFixture RegisterStage<TStage>(string stageTypeName) where TStage : IStage
    {
        _builder.WithStage<TStage>(stageTypeName);
        return this;
    }

    /// <summary>
    /// Actor 타입을 등록합니다.
    /// </summary>
    public TestServerFixture RegisterActor<TActor>(string stageTypeName) where TActor : IActor
    {
        _builder.WithActor<TActor>(stageTypeName);
        return this;
    }

    /// <summary>
    /// 추가 서비스를 등록합니다.
    /// </summary>
    public TestServerFixture WithServices(Action<IServiceCollection> configure)
    {
        _builder.WithServices(configure);
        return this;
    }

    /// <summary>
    /// 서버를 시작합니다.
    /// </summary>
    public async Task<TestServer> StartServerAsync()
    {
        if (_host != null)
        {
            throw new InvalidOperationException("Server is already started");
        }

        _host = await _builder.StartAsync();
        return new TestServer(_host, _port);
    }

    /// <summary>
    /// 새 포트로 서버를 재시작합니다.
    /// </summary>
    public async Task<TestServer> RestartServerAsync()
    {
        if (_host != null)
        {
            await _host.StopAsync();
            _host.Dispose();
            _host = null;
        }

        _port = GetAvailablePort();
        return await StartServerAsync();
    }

    /// <summary>
    /// 리소스를 정리합니다.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (_host != null)
        {
            await _host.StopAsync();
            _host.Dispose();
            _host = null;
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

/// <summary>
/// 실행 중인 테스트 서버 래퍼.
/// </summary>
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

    public T GetService<T>() where T : notnull
        => _host.Services.GetRequiredService<T>();

    public StageFactory StageFactory => GetService<StageFactory>();
    public StagePool StagePool => GetService<StagePool>();
    public SessionManager SessionManager => GetService<SessionManager>();
    public PacketDispatcher PacketDispatcher => GetService<PacketDispatcher>();
    public PlayHouseServer Server => GetService<PlayHouseServer>();
}
```

### 4.5 StageFactory 수정

**경로**: `src/PlayHouse/Core/Stage/StageFactory.cs`

StageFactory가 StageTypeRegistry를 사용하도록 수정:

```csharp
// 기존 생성자에 StageTypeRegistry 추가
public StageFactory(
    StagePool stagePool,
    PacketDispatcher dispatcher,
    TimerManager timerManager,
    SessionManager sessionManager,
    ILoggerFactory loggerFactory)
{
    _stagePool = stagePool;
    _dispatcher = dispatcher;
    _timerManager = timerManager;
    _sessionManager = sessionManager;
    _loggerFactory = loggerFactory;

    // StageTypeRegistry는 public property로 노출
    Registry = new StageTypeRegistry();
}

/// <summary>
/// Stage/Actor 타입 레지스트리
/// </summary>
public StageTypeRegistry Registry { get; }
```

---

## 5. 마이그레이션 가이드

### 5.1 기존 E2E 테스트 마이그레이션

**변경 전** (ChatRoomE2ETests.cs):
```csharp
public class ChatRoomE2ETests : IAsyncLifetime
{
    private IHost? _host;
    private StageFactory? _stageFactory;
    // ... 많은 필드들

    public async Task InitializeAsync()
    {
        ChatStage.Reset();
        ChatActor.Reset();

        _host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                // 50줄 이상의 설정 코드...
            })
            .Build();

        await _host.StartAsync();
        _stageFactory = _host.Services.GetRequiredService<StageFactory>();
        // ...
    }
}
```

**변경 후**:
```csharp
public class ChatRoomE2ETests : IAsyncLifetime
{
    private TestServerFixture _fixture = null!;
    private TestServer _server = null!;

    public async Task InitializeAsync()
    {
        ChatStage.Reset();
        ChatActor.Reset();

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
    public async Task Server_ShouldStartAndAcceptTcpConnections()
    {
        // Arrange
        var server = _server.Server;

        // Act - TCP 클라이언트로 연결
        using var client = new TcpClient();
        await client.ConnectAsync("127.0.0.1", _server.Port);

        // Assert
        client.Connected.Should().BeTrue();
        server.TcpServer.Should().NotBeNull();
    }
}
```

### 5.2 Integration 테스트 마이그레이션

**변경 전** (ActorLifecycleTests.cs):
```csharp
public class ActorLifecycleTests : IAsyncLifetime
{
    // 복잡한 설정 코드...
}
```

**변경 후**:
```csharp
public class ActorLifecycleTests : IAsyncLifetime
{
    private TestServerFixture _fixture = null!;
    private TestServer _server = null!;

    public async Task InitializeAsync()
    {
        TestStage.Reset();

        _fixture = new TestServerFixture()
            .RegisterStage<TestStage>("test-stage");

        _server = await _fixture.StartServerAsync();
    }

    public async Task DisposeAsync()
    {
        await _fixture.DisposeAsync();
    }

    [Fact]
    public async Task StageFactory_ShouldCreateStageAndCallOnCreate()
    {
        var creationPacket = CreateTestPacket("CreateStage");
        var (stageContext, errorCode, reply) = await _server.StageFactory
            .CreateStageAsync("test-stage", creationPacket);

        errorCode.Should().Be(ErrorCode.Success);
        stageContext.Should().NotBeNull();
        TestStage.OnCreateCalled.Should().BeTrue();
    }
}
```

---

## 6. 체크리스트

### Phase 1: Core Bootstrap

- [ ] `src/PlayHouse/Core/Stage/StageTypeRegistry.cs` 수정 (이미 존재하면 확장)
- [ ] `src/PlayHouse/Core/Bootstrap/IPlayHouseHost.cs` 생성
- [ ] `src/PlayHouse/Core/Bootstrap/PlayHouseBootstrapBuilder.cs` 생성
- [ ] `src/PlayHouse/Core/Bootstrap/PlayHouseBootstrap.cs` 생성
- [ ] `src/PlayHouse/Core/Bootstrap/PlayHouseHostImpl.cs` 생성
- [ ] `StageFactory.cs`에 `Registry` 프로퍼티 추가
- [ ] `PlayHouseServiceExtensions.cs` 정리 (Bootstrap과 통합)

### Phase 2: Test Infrastructure

- [ ] `tests/PlayHouse.Tests.Shared/` 프로젝트 생성 (또는 기존 프로젝트에 추가)
- [ ] `TestServerFixture.cs` 생성
- [ ] `TestServer.cs` 생성

### Phase 3: 테스트 마이그레이션

- [ ] `ChatRoomE2ETests.cs` 마이그레이션
- [ ] `ActorLifecycleTests.cs` 마이그레이션
- [ ] `TcpConnectionTests.cs` 마이그레이션
- [ ] `MessageTransmissionTests.cs` 마이그레이션

### Phase 4: 검증

- [ ] 모든 기존 테스트 통과 확인
- [ ] Bootstrap 통합 테스트 추가
- [ ] 운영 환경 부트스트랩 예제 작성

---

## 참고 자료

- `doc/plans/implementation-plan.md` - 전체 구현 계획
- `src/PlayHouse/Core/Stage/StageFactory.cs` - 현재 StageFactory 구현
- `src/PlayHouse/Infrastructure/Http/PlayHouseServiceExtensions.cs` - 기존 DI 확장

---

## 아키텍처 원칙

### Clean Architecture 준수

```
┌─────────────────────────────────────────────────────────────┐
│                    Presentation Layer                        │
│                    (Tests, Console App)                      │
├─────────────────────────────────────────────────────────────┤
│                   Infrastructure Layer                       │
│        (PlayHouseServer, TcpServer, WebSocketServer)        │
│        PlayHouseServiceExtensions (DI 등록만 담당)           │
├─────────────────────────────────────────────────────────────┤
│                      Core Layer                              │
│   ┌─────────────────────────────────────────────────────┐   │
│   │              Core/Bootstrap/                         │   │
│   │   - PlayHouseBootstrap.cs (진입점)                   │   │
│   │   - PlayHouseBootstrapBuilder.cs (Fluent API)       │   │
│   │   - IPlayHouseHost.cs (호스트 인터페이스)            │   │
│   │   - PlayHouseHostImpl.cs (호스트 구현)              │   │
│   └─────────────────────────────────────────────────────┘   │
│   ┌─────────────────────────────────────────────────────┐   │
│   │              Core/Stage/                             │   │
│   │   - StageTypeRegistry.cs (타입 레지스트리)          │   │
│   │   - StageFactory.cs (Stage 생성)                    │   │
│   │   - StageContext.cs, ActorContext.cs                │   │
│   └─────────────────────────────────────────────────────┘   │
├─────────────────────────────────────────────────────────────┤
│                   Abstractions Layer                         │
│              (IStage, IActor, IPacket, etc.)                │
└─────────────────────────────────────────────────────────────┘
```

**핵심 원칙**:
1. **Bootstrap은 Core 레이어**: 시스템 초기화 로직은 도메인의 핵심
2. **Infrastructure는 구현만 제공**: PlayHouseServer 등은 Bootstrap이 정의한 인터페이스 구현
3. **의존성 역전**: Core가 Infrastructure를 참조하지 않음 (인터페이스로 추상화)

---

## 변경 이력

| 날짜 | 버전 | 변경 내용 |
|------|------|----------|
| 2025-12-09 | 1.0 | 초기 계획 작성 |
