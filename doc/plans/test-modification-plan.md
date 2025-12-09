# PlayHouse 테스트 수정 계획

> 작성일: 2025-12-09
> 목적: Bootstrap 시스템 도입 후 기존 테스트 코드 마이그레이션 상세 계획

## 목차
1. [현재 테스트 구조 분석](#1-현재-테스트-구조-분석)
2. [문제점 및 개선 방향](#2-문제점-및-개선-방향)
3. [테스트 수정 계획](#3-테스트-수정-계획)
4. [상세 마이그레이션 코드](#4-상세-마이그레이션-코드)
5. [공유 테스트 인프라](#5-공유-테스트-인프라)
6. [체크리스트](#6-체크리스트)

---

## 1. 현재 테스트 구조 분석

### 1.1 테스트 프로젝트 현황

```
tests/
├── PlayHouse.Tests.E2E/
│   ├── ChatRoomE2ETests.cs          # 535줄 - E2E 테스트 (Stage/Actor 생명주기)
│   ├── TestFixtures/
│   │   ├── ChatStage.cs             # 113줄 - 테스트용 Stage 구현
│   │   ├── ChatActor.cs             # 61줄 - 테스트용 Actor 구현
│   │   └── SimplePacket.cs          # 패킷 헬퍼
│   └── Proto/                       # Protobuf 메시지 정의
│
├── PlayHouse.Tests.Integration/
│   └── Core/
│       ├── ActorLifecycleTests.cs   # 464줄 - Actor 생명주기 통합 테스트
│       ├── TcpConnectionTests.cs    # 407줄 - TCP 연결 테스트
│       └── MessageTransmissionTests.cs # 527줄 - 메시지 전송 테스트
│
└── PlayHouse.Tests.Unit/
    └── Core/
        ├── Session/SessionIdGeneratorTests.cs
        ├── Stage/AtomicBooleanTests.cs
        └── Timer/TimerIdGeneratorTests.cs
```

### 1.2 현재 테스트별 설정 코드 분석

| 테스트 파일 | 설정 코드 줄 수 | 문제점 |
|------------|----------------|--------|
| `ChatRoomE2ETests.cs` | 83줄 (46-128) | 전체 DI 수동 설정 |
| `ActorLifecycleTests.cs` | 78줄 (42-119) | 동일한 DI 설정 중복 |
| `TcpConnectionTests.cs` | 54줄 (24-77) | TcpServer 직접 생성 |
| `MessageTransmissionTests.cs` | 49줄 (29-77) | TcpServer 직접 생성 |

### 1.3 중복 코드 패턴

**패턴 1: Host 빌더 설정 (모든 통합 테스트에서 반복)**
```csharp
_host = Host.CreateDefaultBuilder()
    .ConfigureServices(services =>
    {
        services.AddOptions<PlayHouseOptions>().Configure(opts => { ... });
        services.AddSingleton<PacketSerializer>();
        services.AddSingleton<SessionManager>();
        services.AddSingleton<StagePool>();
        services.AddSingleton<PacketDispatcher>();
        services.AddSingleton<TimerManager>(sp => { ... });
        services.AddSingleton<StageFactory>(sp => { ... });
        services.AddHostedService<PlayHouseServer>();
    })
    .ConfigureLogging(logging => { ... })
    .Build();
```

**패턴 2: 서비스 조회 (모든 테스트에서 반복)**
```csharp
_stageFactory = _host.Services.GetRequiredService<StageFactory>();
_stagePool = _host.Services.GetRequiredService<StagePool>();
_sessionManager = _host.Services.GetRequiredService<SessionManager>();
_dispatcher = _host.Services.GetRequiredService<PacketDispatcher>();
```

**패턴 3: 포트 할당 (각 테스트 클래스에서 중복)**
```csharp
private static int _portCounter = 20000;
private readonly int _testPort;

public TestClass()
{
    _testPort = Interlocked.Increment(ref _portCounter);
}
```

---

## 2. 문제점 및 개선 방향

### 2.1 현재 문제점

| 문제 | 영향 | 심각도 |
|------|------|--------|
| **50+ 줄 중복 설정** | 새 테스트 작성 시 복사/붙여넣기 필요 | 높음 |
| **StageFactory 내부 생성 로직 노출** | 구현 변경 시 모든 테스트 수정 필요 | 높음 |
| **Stage/Actor 타입 등록 방식 불일치** | E2E vs Integration 테스트 간 차이 | 중간 |
| **포트 충돌 가능성** | 병렬 테스트 실행 시 충돌 | 중간 |
| **테스트 상태 초기화 누락** | 정적 필드 오염으로 테스트 간섭 | 높음 |
| **TestStage/ChatStage 중복** | 유사한 테스트 Stage 구현 중복 | 낮음 |

### 2.2 개선 방향

1. **TestServerFixture 도입**: 모든 서버 설정을 캡슐화
2. **공유 테스트 인프라**: `PlayHouse.Tests.Shared` 프로젝트 생성
3. **표준화된 테스트 Stage/Actor**: 재사용 가능한 테스트 구현체
4. **자동 포트 할당**: 시스템에서 사용 가능한 포트 자동 탐색
5. **테스트 격리 강화**: 각 테스트 전후 상태 초기화 보장

---

## 3. 테스트 수정 계획

### 3.1 Phase 1: 공유 테스트 인프라 구축

**새 프로젝트**: `tests/PlayHouse.Tests.Shared/`

```
PlayHouse.Tests.Shared/
├── PlayHouse.Tests.Shared.csproj
├── Fixtures/
│   ├── TestServerFixture.cs        # 서버 Fixture
│   └── TestServer.cs               # 서버 래퍼
├── TestImplementations/
│   ├── TestStage.cs                # 공용 테스트 Stage
│   ├── TestActor.cs                # 공용 테스트 Actor
│   └── TestPacket.cs               # 공용 테스트 패킷
└── Helpers/
    └── PortHelper.cs               # 포트 할당 헬퍼
```

### 3.2 Phase 2: 테스트 마이그레이션 순서

| 순서 | 테스트 파일 | 변경 규모 | 우선순위 |
|------|------------|----------|----------|
| 1 | `ActorLifecycleTests.cs` | 중간 | 높음 |
| 2 | `ChatRoomE2ETests.cs` | 큰 | 높음 |
| 3 | `TcpConnectionTests.cs` | 작음 | 중간 |
| 4 | `MessageTransmissionTests.cs` | 작음 | 중간 |

### 3.3 마이그레이션 전략

**전략 1: 점진적 마이그레이션**
- 기존 테스트를 유지하면서 새 Fixture 기반 테스트 추가
- 모든 테스트 통과 확인 후 기존 코드 제거

**전략 2: 테스트별 격리 보장**
- `IAsyncLifetime` 활용하여 각 테스트 전후 초기화
- 정적 상태 초기화 메서드 표준화

---

## 4. 상세 마이그레이션 코드

### 4.1 PlayHouse.Tests.Shared 프로젝트

**PlayHouse.Tests.Shared.csproj**:
```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <RootNamespace>PlayHouse.Testing</RootNamespace>
    <AssemblyName>PlayHouse.Tests.Shared</AssemblyName>
    <IsPackable>false</IsPackable>
    <Nullable>enable</Nullable>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="FluentAssertions" Version="6.12.0" />
    <PackageReference Include="Microsoft.Extensions.Hosting" Version="8.0.0" />
    <PackageReference Include="xunit" Version="2.6.1" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\src\PlayHouse\PlayHouse.csproj" />
    <ProjectReference Include="..\..\connector\PlayHouse.Connector\PlayHouse.Connector.csproj" />
  </ItemGroup>

</Project>
```

### 4.2 공용 테스트 구현체

**TestStage.cs** (공용):
```csharp
#nullable enable

using System.Collections.Concurrent;
using PlayHouse.Abstractions;

namespace PlayHouse.Testing.TestImplementations;

/// <summary>
/// 통합 테스트용 범용 Stage 구현.
/// 모든 콜백을 추적하여 테스트 검증에 사용합니다.
/// </summary>
public class TestStage : IStage
{
    // 정적 추적 - 테스트 검증용
    private static readonly ConcurrentDictionary<string, bool> _callbackTracking = new();
    private static readonly ConcurrentBag<(long accountId, string msgId, string? content)> _receivedMessages = new();
    private static readonly ConcurrentBag<long> _joinedActors = new();
    private static readonly ConcurrentBag<long> _leftActors = new();
    private static long _lastJoinedAccountId;
    private static string? _lastDispatchedMsgId;
    private static LeaveReason? _lastLeaveReason;

    // 콜백 추적 프로퍼티
    public static bool OnCreateCalled => _callbackTracking.GetValueOrDefault("OnCreate");
    public static bool OnPostCreateCalled => _callbackTracking.GetValueOrDefault("OnPostCreate");
    public static bool OnJoinRoomCalled => _callbackTracking.GetValueOrDefault("OnJoinRoom");
    public static bool OnPostJoinRoomCalled => _callbackTracking.GetValueOrDefault("OnPostJoinRoom");
    public static bool OnLeaveRoomCalled => _callbackTracking.GetValueOrDefault("OnLeaveRoom");
    public static bool OnDispatchCalled => _callbackTracking.GetValueOrDefault("OnDispatch");
    public static bool DisposeCalled => _callbackTracking.GetValueOrDefault("Dispose");

    // 데이터 추적 프로퍼티
    public static long LastJoinedAccountId => _lastJoinedAccountId;
    public static string? LastDispatchedMsgId => _lastDispatchedMsgId;
    public static LeaveReason? LastLeaveReason => _lastLeaveReason;
    public static IReadOnlyCollection<(long accountId, string msgId, string? content)> ReceivedMessages
        => _receivedMessages.ToArray();
    public static IReadOnlyCollection<long> JoinedActors => _joinedActors.ToArray();
    public static IReadOnlyCollection<long> LeftActors => _leftActors.ToArray();

    /// <summary>
    /// 모든 정적 상태를 초기화합니다.
    /// 각 테스트 시작 전 호출해야 합니다.
    /// </summary>
    public static void Reset()
    {
        _callbackTracking.Clear();
        _receivedMessages.Clear();
        _joinedActors.Clear();
        _leftActors.Clear();
        _lastJoinedAccountId = 0;
        _lastDispatchedMsgId = null;
        _lastLeaveReason = null;
    }

    public IStageSender StageSender { get; init; } = null!;

    public Task<(ushort errorCode, IPacket? reply)> OnCreate(IPacket packet)
    {
        _callbackTracking["OnCreate"] = true;
        return Task.FromResult<(ushort, IPacket?)>((ErrorCode.Success, null));
    }

    public Task OnPostCreate()
    {
        _callbackTracking["OnPostCreate"] = true;
        return Task.CompletedTask;
    }

    public Task<(ushort errorCode, IPacket? reply)> OnJoinRoom(IActor actor, IPacket userInfo)
    {
        _callbackTracking["OnJoinRoom"] = true;
        _lastJoinedAccountId = actor.ActorSender.AccountId;
        _joinedActors.Add(actor.ActorSender.AccountId);
        return Task.FromResult<(ushort, IPacket?)>((ErrorCode.Success, null));
    }

    public Task OnPostJoinRoom(IActor actor)
    {
        _callbackTracking["OnPostJoinRoom"] = true;
        return Task.CompletedTask;
    }

    public ValueTask OnLeaveRoom(IActor actor, LeaveReason reason)
    {
        _callbackTracking["OnLeaveRoom"] = true;
        _lastLeaveReason = reason;
        _leftActors.Add(actor.ActorSender.AccountId);
        return ValueTask.CompletedTask;
    }

    public ValueTask OnActorConnectionChanged(IActor actor, bool isConnected, DisconnectReason? reason)
    {
        _callbackTracking["OnActorConnectionChanged"] = true;
        return ValueTask.CompletedTask;
    }

    public ValueTask OnDispatch(IActor actor, IPacket packet)
    {
        _callbackTracking["OnDispatch"] = true;
        _lastDispatchedMsgId = packet.MsgId;
        _receivedMessages.Add((actor.ActorSender.AccountId, packet.MsgId, null));
        return ValueTask.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        _callbackTracking["Dispose"] = true;
        return ValueTask.CompletedTask;
    }
}
```

**TestActor.cs** (공용):
```csharp
#nullable enable

using System.Collections.Concurrent;
using PlayHouse.Abstractions;

namespace PlayHouse.Testing.TestImplementations;

/// <summary>
/// 통합 테스트용 범용 Actor 구현.
/// </summary>
public class TestActor : IActor
{
    private static readonly ConcurrentDictionary<long, bool> _createdActors = new();
    private static readonly ConcurrentDictionary<long, bool> _authenticatedActors = new();
    private static readonly ConcurrentDictionary<long, bool> _destroyedActors = new();

    public static bool IsActorCreated(long accountId) => _createdActors.GetValueOrDefault(accountId);
    public static bool IsActorAuthenticated(long accountId) => _authenticatedActors.GetValueOrDefault(accountId);
    public static bool IsActorDestroyed(long accountId) => _destroyedActors.GetValueOrDefault(accountId);
    public static int CreatedCount => _createdActors.Count;
    public static int AuthenticatedCount => _authenticatedActors.Count;

    public static void Reset()
    {
        _createdActors.Clear();
        _authenticatedActors.Clear();
        _destroyedActors.Clear();
    }

    public IActorSender ActorSender { get; set; } = null!;
    public bool IsConnected { get; private set; }

    public Task OnCreate()
    {
        _createdActors[ActorSender.AccountId] = true;
        return Task.CompletedTask;
    }

    public Task OnDestroy()
    {
        _destroyedActors[ActorSender.AccountId] = true;
        return Task.CompletedTask;
    }

    public Task OnAuthenticate(IPacket? authData)
    {
        IsConnected = true;
        _authenticatedActors[ActorSender.AccountId] = true;
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        return ValueTask.CompletedTask;
    }
}
```

**TestPacket.cs** (공용):
```csharp
#nullable enable

using PlayHouse.Abstractions;

namespace PlayHouse.Testing.TestImplementations;

/// <summary>
/// 테스트용 간단한 패킷 구현.
/// </summary>
public sealed class TestPacket : IPacket
{
    public string MsgId { get; }
    public ushort MsgSeq { get; }
    public int StageId { get; }
    public ushort ErrorCode { get; }
    public IPayload Payload { get; }

    public TestPacket(string msgId, int stageId = 0, ushort msgSeq = 0, ushort errorCode = 0)
    {
        MsgId = msgId;
        StageId = stageId;
        MsgSeq = msgSeq;
        ErrorCode = errorCode;
        Payload = EmptyPayload.Instance;
    }

    public void Dispose() { }
}
```

### 4.3 ActorLifecycleTests.cs 마이그레이션

**변경 전** (현재 코드 78줄 설정):
```csharp
public class ActorLifecycleTests : IAsyncLifetime
{
    private static int _portCounter = 19500;
    private readonly int _testPort;
    private IHost? _host;
    private StageFactory? _stageFactory;
    // ... 많은 필드

    public async Task InitializeAsync()
    {
        TestStage.Reset();
        _testPort = Interlocked.Increment(ref _portCounter);

        _host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                // 78줄의 설정 코드...
            })
            .Build();

        await _host.StartAsync();
        _stageFactory = _host.Services.GetRequiredService<StageFactory>();
        // ...
    }
}
```

**변경 후** (간소화된 코드):
```csharp
using PlayHouse.Testing;
using PlayHouse.Testing.TestImplementations;

namespace PlayHouse.Tests.Integration.Core;

[Collection("ActorLifecycle")]
public class ActorLifecycleTests : IAsyncLifetime
{
    private TestServerFixture _fixture = null!;
    private TestServer _server = null!;

    public async Task InitializeAsync()
    {
        // 테스트 상태 초기화
        TestStage.Reset();

        // Fixture 설정 - 단 3줄로 서버 구성
        _fixture = new TestServerFixture()
            .RegisterStage<TestStage>("test-stage");

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

        // Act
        using var client = new TcpClient();
        await client.ConnectAsync("127.0.0.1", _server.Port);

        // Assert
        client.Connected.Should().BeTrue();
        server.TcpServer.Should().NotBeNull();
    }

    [Fact]
    public async Task StageFactory_ShouldCreateStageAndCallOnCreate()
    {
        // Arrange
        var creationPacket = new TestPacket("CreateStage");

        // Act
        var (stageContext, errorCode, reply) = await _server.StageFactory
            .CreateStageAsync("test-stage", creationPacket);

        // Assert
        errorCode.Should().Be(ErrorCode.Success);
        stageContext.Should().NotBeNull();
        stageContext!.StageId.Should().BeGreaterThan(0);
        stageContext.StageType.Should().Be("test-stage");

        TestStage.OnCreateCalled.Should().BeTrue();
        TestStage.OnPostCreateCalled.Should().BeTrue();
    }

    [Fact]
    public async Task StageContext_ShouldJoinActorAndCallOnJoinRoom()
    {
        // Arrange
        var creationPacket = new TestPacket("CreateStage");
        var (stageContext, _, _) = await _server.StageFactory
            .CreateStageAsync("test-stage", creationPacket);
        stageContext.Should().NotBeNull();

        var session = _server.SessionManager.CreateSession(1);
        _server.SessionManager.MapAccountId(1, 100);

        var joinPacket = new TestPacket("JoinRoom");

        // Act
        var (joinError, joinReply, actorContext) = await stageContext!.JoinActorAsync(
            accountId: 100,
            sessionId: 1,
            userInfo: joinPacket);

        // Assert
        joinError.Should().Be(ErrorCode.Success);
        actorContext.Should().NotBeNull();
        TestStage.OnJoinRoomCalled.Should().BeTrue();
        TestStage.OnPostJoinRoomCalled.Should().BeTrue();
        TestStage.LastJoinedAccountId.Should().Be(100);
    }

    // ... 나머지 테스트 메서드들
}
```

### 4.4 ChatRoomE2ETests.cs 마이그레이션

**변경 후**:
```csharp
using PlayHouse.Testing;
using PlayHouse.Tests.E2E.TestFixtures;

namespace PlayHouse.Tests.E2E;

[Collection("ChatRoomE2E")]
public class ChatRoomE2ETests : IAsyncLifetime
{
    private TestServerFixture _fixture = null!;
    private TestServer _server = null!;

    public async Task InitializeAsync()
    {
        // 테스트 상태 초기화
        ChatStage.Reset();
        ChatActor.Reset();

        // Fixture 설정
        _fixture = new TestServerFixture()
            .RegisterStage<ChatStage>("chat-stage")
            .RegisterActor<ChatActor>("chat-stage");

        _server = await _fixture.StartServerAsync();
    }

    public async Task DisposeAsync()
    {
        await _fixture.DisposeAsync();
    }

    [Fact(DisplayName = "서버가 정상적으로 시작되고 TCP 연결을 수락함")]
    public async Task Server_ShouldStartAndAcceptTcpConnections()
    {
        // Arrange
        var server = _server.Server;

        // Act
        using var client = new TcpClient();
        await client.ConnectAsync("127.0.0.1", _server.Port);

        // Assert
        client.Connected.Should().BeTrue();
        server.TcpServer.Should().NotBeNull();
    }

    [Fact(DisplayName = "PlayHouseClient로 서버에 연결할 수 있음")]
    public async Task PlayHouseClient_CanConnectToServer()
    {
        // Arrange
        var options = new PlayHouseClientOptions
        {
            ConnectionTimeout = TimeSpan.FromSeconds(5),
            TcpNoDelay = true
        };

        await using var client = new PlayHouseClient(options, null);

        // Act
        var result = await client.ConnectAsync(_server.Endpoint, "test-room-token");

        // Assert
        result.Success.Should().BeTrue();
        client.IsConnected.Should().BeTrue();
        client.State.Should().Be(ConnectionState.Connected);
    }

    [Fact(DisplayName = "ChatStage를 생성하면 OnCreate가 호출됨")]
    public async Task CreateChatStage_ShouldCallOnCreate()
    {
        // Arrange
        var createRequest = new CreateStageRequest
        {
            StageType = "chat-stage",
            StageName = "TestChatRoom",
            MaxActors = 100
        };
        var createPacket = new SimplePacket(createRequest);

        // Act
        var (stageContext, errorCode, _) = await _server.StageFactory
            .CreateStageAsync("chat-stage", createPacket);

        // Assert
        errorCode.Should().Be(ErrorCode.Success);
        stageContext.Should().NotBeNull();
        stageContext!.StageId.Should().BeGreaterThan(0);

        ChatStage.OnCreateCalled.Should().BeTrue();
        ChatStage.OnPostCreateCalled.Should().BeTrue();
    }

    // ... 나머지 테스트 메서드들
}
```

### 4.5 TcpConnectionTests.cs 마이그레이션

TCP 연결 테스트는 `PlayHouseServer`가 아닌 `TcpServer`를 직접 테스트하므로,
별도의 경량 Fixture를 사용합니다.

**TcpServerFixture.cs**:
```csharp
#nullable enable

using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging.Abstractions;
using PlayHouse.Infrastructure.Transport.Tcp;

namespace PlayHouse.Testing.Fixtures;

/// <summary>
/// TCP 서버 전용 테스트 Fixture.
/// PlayHouseServer 전체 스택 없이 TCP 레이어만 테스트합니다.
/// </summary>
public class TcpServerFixture : IAsyncDisposable
{
    private TcpServer? _server;
    private readonly List<(long sessionId, byte[] data)> _receivedMessages = new();
    private readonly List<long> _disconnectedSessions = new();
    private readonly Dictionary<long, TcpSession> _sessions = new();

    public int Port { get; private set; }
    public string Endpoint => $"tcp://127.0.0.1:{Port}";
    public TcpServer? Server => _server;
    public int SessionCount => _server?.SessionCount ?? 0;
    public IReadOnlyList<(long sessionId, byte[] data)> ReceivedMessages => _receivedMessages;
    public IReadOnlyList<long> DisconnectedSessions => _disconnectedSessions;

    public async Task StartAsync()
    {
        Port = GetAvailablePort();

        var options = new TcpSessionOptions
        {
            ReceiveBufferSize = 8192,
            SendBufferSize = 8192,
            MaxPacketSize = 1024 * 1024,
            HeartbeatTimeout = TimeSpan.FromSeconds(60)
        };

        _server = new TcpServer(
            options,
            CreateSessionAsync,
            NullLogger<TcpServer>.Instance);

        var endpoint = new IPEndPoint(IPAddress.Loopback, Port);
        await _server.StartAsync(endpoint);
    }

    public async Task StopAsync()
    {
        if (_server != null)
        {
            await _server.StopAsync();
        }
    }

    public void Reset()
    {
        lock (_receivedMessages) _receivedMessages.Clear();
        lock (_disconnectedSessions) _disconnectedSessions.Clear();
    }

    public async ValueTask DisposeAsync()
    {
        if (_server != null)
        {
            await _server.DisposeAsync();
        }
    }

    private Task<TcpSession> CreateSessionAsync(long sessionId, Socket socket)
    {
        var options = new TcpSessionOptions
        {
            ReceiveBufferSize = 8192,
            SendBufferSize = 8192,
            MaxPacketSize = 1024 * 1024,
            HeartbeatTimeout = TimeSpan.FromSeconds(60)
        };

        var session = new TcpSession(
            sessionId,
            socket,
            options,
            OnMessageReceived,
            OnDisconnected,
            NullLogger<TcpSession>.Instance);

        lock (_sessions)
        {
            _sessions[sessionId] = session;
        }

        return Task.FromResult(session);
    }

    private void OnMessageReceived(long sessionId, ReadOnlyMemory<byte> data)
    {
        lock (_receivedMessages)
        {
            _receivedMessages.Add((sessionId, data.ToArray()));
        }
    }

    private void OnDisconnected(long sessionId, Exception? exception)
    {
        lock (_disconnectedSessions)
        {
            _disconnectedSessions.Add(sessionId);
        }

        lock (_sessions)
        {
            _sessions.Remove(sessionId);
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

**TcpConnectionTests.cs 마이그레이션**:
```csharp
using PlayHouse.Testing.Fixtures;

namespace PlayHouse.Tests.Integration.Core;

public class TcpConnectionTests : IAsyncLifetime
{
    private TcpServerFixture _fixture = null!;

    public async Task InitializeAsync()
    {
        _fixture = new TcpServerFixture();
        await _fixture.StartAsync();
    }

    public async Task DisposeAsync()
    {
        await _fixture.DisposeAsync();
    }

    [Fact(DisplayName = "TCP 서버가 정상적으로 시작됨")]
    public void TcpServer_StartsSuccessfully()
    {
        _fixture.Server.Should().NotBeNull();
        _fixture.SessionCount.Should().Be(0);
    }

    [Fact(DisplayName = "PlayHouseClient가 TCP 서버에 연결할 수 있음")]
    public async Task PlayHouseClient_CanConnectToTcpServer()
    {
        // Given
        var options = new PlayHouseClientOptions
        {
            ConnectionTimeout = TimeSpan.FromSeconds(5),
            TcpNoDelay = true,
            TcpKeepAlive = true
        };

        await using var client = new PlayHouseClient(options, null);

        // When
        var result = await client.ConnectAsync(_fixture.Endpoint, "test-token");

        // Then
        result.Success.Should().BeTrue();
        client.IsConnected.Should().BeTrue();
        client.State.Should().Be(ConnectionState.Connected);

        await Task.Delay(100);
        _fixture.SessionCount.Should().Be(1);
    }

    // ... 나머지 테스트들
}
```

---

## 5. 공유 테스트 인프라

### 5.1 프로젝트 참조 구조

```
PlayHouse.Tests.Shared
    ├── PlayHouse.csproj 참조
    └── PlayHouse.Connector.csproj 참조

PlayHouse.Tests.E2E
    ├── PlayHouse.csproj 참조
    ├── PlayHouse.Connector.csproj 참조
    └── PlayHouse.Tests.Shared.csproj 참조  (추가)

PlayHouse.Tests.Integration
    ├── PlayHouse.csproj 참조
    ├── PlayHouse.Connector.csproj 참조
    └── PlayHouse.Tests.Shared.csproj 참조  (추가)
```

### 5.2 기존 TestFixtures 처리

E2E 테스트의 `TestFixtures/` 폴더(ChatStage, ChatActor)는 유지합니다.
이들은 E2E 특화 테스트에서 Protobuf 메시지와 함께 사용됩니다.

공용 `TestStage`, `TestActor`는 간단한 통합 테스트에 사용합니다.

---

## 6. 체크리스트

### Phase 1: 공유 테스트 인프라 구축

- [ ] `PlayHouse.Tests.Shared.csproj` 생성
- [ ] `Fixtures/TestServerFixture.cs` 생성
- [ ] `Fixtures/TestServer.cs` 생성
- [ ] `Fixtures/TcpServerFixture.cs` 생성
- [ ] `TestImplementations/TestStage.cs` 생성
- [ ] `TestImplementations/TestActor.cs` 생성
- [ ] `TestImplementations/TestPacket.cs` 생성
- [ ] `Helpers/PortHelper.cs` 생성

### Phase 2: E2E 프로젝트 업데이트

- [ ] `PlayHouse.Tests.E2E.csproj`에 Shared 참조 추가
- [ ] `ChatRoomE2ETests.cs` 마이그레이션
  - [ ] TestServerFixture 사용하도록 변경
  - [ ] 기존 InitializeAsync 코드 제거
  - [ ] 모든 테스트 통과 확인

### Phase 3: Integration 프로젝트 업데이트

- [ ] `PlayHouse.Tests.Integration.csproj`에 Shared 참조 추가
- [ ] `ActorLifecycleTests.cs` 마이그레이션
  - [ ] TestServerFixture 사용하도록 변경
  - [ ] 기존 TestStage/TestPacket 클래스 제거 (Shared로 이동)
  - [ ] 모든 테스트 통과 확인
- [ ] `TcpConnectionTests.cs` 마이그레이션
  - [ ] TcpServerFixture 사용하도록 변경
  - [ ] 모든 테스트 통과 확인
- [ ] `MessageTransmissionTests.cs` 마이그레이션
  - [ ] TcpServerFixture 사용하도록 변경
  - [ ] 모든 테스트 통과 확인

### Phase 4: 검증 및 정리

- [ ] 모든 테스트 스위트 실행 및 통과 확인
- [ ] 기존 중복 코드 완전 제거
- [ ] 테스트 코드 라인 수 감소 확인 (목표: 40% 이상 감소)
- [ ] 병렬 테스트 실행 검증

---

## 예상 결과

### 코드 감소 예상치

| 테스트 파일 | 변경 전 | 변경 후 | 감소율 |
|------------|--------|--------|--------|
| `ChatRoomE2ETests.cs` | 535줄 | ~400줄 | ~25% |
| `ActorLifecycleTests.cs` | 464줄 | ~200줄 | ~57% |
| `TcpConnectionTests.cs` | 407줄 | ~300줄 | ~26% |
| `MessageTransmissionTests.cs` | 527줄 | ~400줄 | ~24% |

### 장점

1. **유지보수성 향상**: 서버 설정 변경 시 Fixture만 수정
2. **테스트 작성 용이**: 새 테스트 작성 시 3-5줄로 서버 구성 완료
3. **일관성 보장**: 모든 테스트가 동일한 방식으로 서버 구성
4. **병렬 테스트 안전**: 자동 포트 할당으로 충돌 방지

---

## 변경 이력

| 날짜 | 버전 | 변경 내용 |
|------|------|----------|
| 2025-12-09 | 1.0 | 초기 계획 작성 |
