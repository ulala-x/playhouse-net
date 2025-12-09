# PlayHouse-NET 미구현 사항 구현 계획

> 이 문서는 context가 초기화되더라도 개발을 진행할 수 있도록 필요한 모든 정보를 포함합니다.

## 목차
1. [현재 구현 상태 분석](#1-현재-구현-상태-분석)
2. [미구현 사항 목록](#2-미구현-사항-목록)
3. [구현 우선순위](#3-구현-우선순위)
4. [Phase별 상세 구현 계획](#4-phase별-상세-구현-계획)
5. [테스트 전략](#5-테스트-전략)
6. [개발 체크리스트](#6-개발-체크리스트)

---

## 1. 현재 구현 상태 분석

### 1.1 완성된 컴포넌트 (약 70%)

| 컴포넌트 | 파일 경로 | 완성도 | 설명 |
|---------|----------|--------|------|
| Event Loop (CAS) | `Core/Stage/BaseStage.cs`, `AtomicBoolean.cs` | 100% | Lock-Free CAS 패턴 완성 |
| StageContext | `Core/Stage/StageContext.cs` | 100% | Stage 래퍼, 메시지 디스패치 |
| ActorContext | `Core/Stage/ActorContext.cs` | 100% | Actor 래퍼, 연결 상태 관리 |
| StagePool | `Core/Stage/StagePool.cs` | 100% | Stage 관리 (ConcurrentDictionary) |
| ActorPool | `Core/Stage/ActorPool.cs` | 100% | Actor 관리 (Stage 내) |
| PacketDispatcher | `Core/Messaging/PacketDispatcher.cs` | 100% | Stage 라우팅 |
| TimerManager | `Core/Timer/TimerManager.cs` | 100% | Repeat/Count 타이머 |
| SessionManager | `Core/Session/SessionManager.cs` | 100% | 세션-계정 매핑 |
| TcpSession/Server | `Infrastructure/Transport/Tcp/` | 100% | System.IO.Pipelines 기반 |
| WebSocketSession | `Infrastructure/Transport/WebSocket/` | 90% | 기본 세션 관리 |
| PacketSerializer | `Infrastructure/Serialization/PacketSerializer.cs` | 100% | LZ4 압축 지원 |
| Abstractions | `Abstractions/` | 100% | IStage, IActor, ISender 등 |

### 1.2 핵심 구현 코드 요약

#### BaseStage Event Loop (Lock-Free CAS 패턴)
```csharp
// src/PlayHouse/Core/Stage/BaseStage.cs:52-72
public void Post(RoutePacket routePacket)
{
    _msgQueue.Enqueue(routePacket);
    if (_isProcessing.CompareAndSet(false, true))
    {
        _ = Task.Run(async () =>
        {
            try { await ProcessMessageLoopAsync(); }
            catch (Exception ex) { _logger.LogError(ex, "..."); }
        });
    }
}

private async Task ProcessMessageLoopAsync()
{
    do
    {
        while (_msgQueue.TryDequeue(out var packet))
        {
            using (packet) { await DispatchAsync(packet); }
        }
        _isProcessing.Set(false);
    } while (!_msgQueue.IsEmpty && _isProcessing.CompareAndSet(false, true));
}
```

#### PacketDispatcher (Stage 라우팅)
```csharp
// src/PlayHouse/Core/Messaging/PacketDispatcher.cs:40-63
public bool Dispatch(RoutePacket packet)
{
    var stage = _stagePool.GetStage(packet.StageId);
    if (stage == null)
    {
        _logger.LogWarning("Stage {StageId} not found", packet.StageId);
        return false;
    }
    stage.Post(packet);
    return true;
}
```

#### StageContext (메시지 처리)
```csharp
// src/PlayHouse/Core/Stage/StageContext.cs:62-101
protected override async Task DispatchAsync(RoutePacket packet)
{
    switch (packet.PacketType)
    {
        case RoutePacketType.ClientPacket:
            await DispatchClientPacketAsync(packet);
            break;
        case RoutePacketType.StagePacket:
            await DispatchStagePacketAsync(packet);
            break;
        case RoutePacketType.Timer:
            await packet.TimerCallback();
            break;
        case RoutePacketType.AsyncBlockResult:
            await DispatchAsyncBlockResultAsync(packet);
            break;
    }
}
```

---

## 2. 미구현 사항 목록

### 2.1 Critical (핵심 기능) - 서비스 운영 필수

| ID | 기능 | 현재 상태 | 위치 |
|----|------|----------|------|
| C1 | PlayHouseServer 메시지 라우팅 통합 | TODO만 있음 | `Infrastructure/Http/PlayHouseServer.cs:168-173` |
| C2 | Stage 생성/등록 API (StageFactory) | 미구현 | 신규 생성 필요 |
| C3 | Actor Join/Leave 처리 | Stage 콜백만 정의됨 | `StageContext.cs` 확장 필요 |
| C4 | IStageSender/IActorSender 구현체 | 미구현 | 신규 생성 필요 |

### 2.2 High Priority - 기본 운영 기능

| ID | 기능 | 현재 상태 | 위치 |
|----|------|----------|------|
| H1 | RoomController HTTP API 완성 | TODO만 있음 | `Infrastructure/Http/RoomController.cs` |
| H2 | Health Check 엔드포인트 | 미구현 | 신규 생성 필요 |
| H3 | Stage 타입 등록/팩토리 DI | 미구현 | `PlayHouseServiceExtensions.cs` 확장 |

### 2.3 Medium Priority - 확장 기능

| ID | 기능 | 현재 상태 | 위치 |
|----|------|----------|------|
| M1 | WebSocket ASP.NET Core 통합 | 부분 구현 | `WebSocketServer.cs` 확장 |
| M2 | Backend SDK (IRoomServerClient) | 미구현 | 신규 프로젝트 |

> **참고**: JWT 인증은 내부망 배포 환경에서 불필요하므로 제외됨. 외부 노출이 필요한 경우 별도 검토.

### 2.4 Low Priority - 운영 편의

| ID | 기능 | 현재 상태 | 위치 |
|----|------|----------|------|
| L1 | Metrics/Observability | 미구현 | 신규 생성 필요 |
| L2 | Swagger/OpenAPI 문서화 | 미구현 | 설정 추가 |

---

## 3. 구현 우선순위

```
Phase 1: Core Integration (Critical) ─────────────────────────────────┐
│  C1. PlayHouseServer 메시지 라우팅 통합                              │
│  C2. Stage 생성/등록 API (StageFactory)                             │
│  C3. Actor Join/Leave 처리 완성                                     │
│  C4. IStageSender/IActorSender 구현체                               │
└─────────────────────────────────────────────────────────────────────┘
                              │
                              ▼
Phase 2: HTTP API (High Priority) ────────────────────────────────────┐
│  H1. RoomController 완성                                             │
│  H2. Health Check 엔드포인트                                         │
│  H3. Stage 타입 등록/팩토리 DI                                       │
└─────────────────────────────────────────────────────────────────────┘
                              │
                              ▼
Phase 3: Integration (Medium) ────────────────────────────────────────┐
│  M1. WebSocket Middleware 통합                                       │
│  M2. Backend SDK                                                     │
└─────────────────────────────────────────────────────────────────────┘
                              │
                              ▼
Phase 4: Operations (Low Priority) ───────────────────────────────────┐
│  L1. Metrics/Observability                                           │
│  L2. Swagger/OpenAPI                                                 │
└─────────────────────────────────────────────────────────────────────┘
```

---

## 4. Phase별 상세 구현 계획

### Phase 1: Core Integration (Critical)

---

#### C1. PlayHouseServer 메시지 라우팅 통합

**목표**: TCP/WebSocket에서 수신한 바이트 데이터를 Stage로 라우팅

**수정 파일**: `src/PlayHouse/Infrastructure/Http/PlayHouseServer.cs`

**현재 코드 (라인 168-173)**:
```csharp
private void OnTcpMessageReceived(long sessionId, ReadOnlyMemory<byte> data)
{
    _logger.LogDebug("TCP message received from session {SessionId}: {Size} bytes", sessionId, data.Length);
    // TODO: Process message (deserialize packet, route to appropriate handler)
}
```

**수정 사항**:

1. **의존성 추가** (생성자):
```csharp
private readonly PacketSerializer _packetSerializer;
private readonly PacketDispatcher _packetDispatcher;
private readonly SessionManager _sessionManager;

public PlayHouseServer(
    IOptions<PlayHouseOptions> options,
    ILoggerFactory loggerFactory,
    PacketSerializer packetSerializer,
    PacketDispatcher packetDispatcher,
    SessionManager sessionManager)
{
    _options = options.Value;
    _loggerFactory = loggerFactory;
    _logger = loggerFactory.CreateLogger<PlayHouseServer>();
    _packetSerializer = packetSerializer;
    _packetDispatcher = packetDispatcher;
    _sessionManager = sessionManager;
}
```

2. **메시지 처리 구현**:
```csharp
private void OnTcpMessageReceived(long sessionId, ReadOnlyMemory<byte> data)
{
    try
    {
        // 1. 패킷 역직렬화
        var packet = _packetSerializer.Deserialize(data.Span);

        // 2. 세션에서 AccountId 조회
        var sessionInfo = _sessionManager.GetSession(sessionId);
        if (sessionInfo == null)
        {
            _logger.LogWarning("Session {SessionId} not found", sessionId);
            return;
        }

        // 3. RoutePacket 생성 및 디스패치
        if (sessionInfo.AccountId.HasValue)
        {
            var routePacket = RoutePacket.ClientPacketOf(
                packet.StageId,
                sessionInfo.AccountId.Value,
                packet);

            if (!_packetDispatcher.Dispatch(routePacket))
            {
                _logger.LogWarning("Failed to dispatch packet to stage {StageId}", packet.StageId);
                // TODO: 에러 응답 전송
            }
        }
        else
        {
            _logger.LogWarning("Session {SessionId} not authenticated", sessionId);
            // TODO: 인증 요청 처리
        }
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Error processing message from session {SessionId}", sessionId);
    }
}

private void OnWebSocketMessageReceived(long sessionId, ReadOnlyMemory<byte> data)
{
    // TCP와 동일한 로직
    OnTcpMessageReceived(sessionId, data);
}
```

---

#### C2. Stage 생성/등록 API (StageFactory)

**신규 파일**: `src/PlayHouse/Core/Stage/StageFactory.cs`

```csharp
#nullable enable

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PlayHouse.Abstractions;
using PlayHouse.Core.Messaging;
using PlayHouse.Core.Session;
using PlayHouse.Core.Timer;

namespace PlayHouse.Core.Stage;

/// <summary>
/// Stage 타입 등록 및 인스턴스 생성을 담당합니다.
/// </summary>
public sealed class StageFactory
{
    private readonly Dictionary<string, Type> _stageTypes = new();
    private readonly IServiceProvider _serviceProvider;
    private readonly ILoggerFactory _loggerFactory;
    private readonly StagePool _stagePool;
    private readonly TimerManager _timerManager;
    private readonly PacketDispatcher _packetDispatcher;
    private readonly SessionManager _sessionManager;

    public StageFactory(
        IServiceProvider serviceProvider,
        ILoggerFactory loggerFactory,
        StagePool stagePool,
        TimerManager timerManager,
        PacketDispatcher packetDispatcher,
        SessionManager sessionManager,
        IOptions<StageTypeRegistration> stageTypeOptions)
    {
        _serviceProvider = serviceProvider;
        _loggerFactory = loggerFactory;
        _stagePool = stagePool;
        _timerManager = timerManager;
        _packetDispatcher = packetDispatcher;
        _sessionManager = sessionManager;

        // 등록된 Stage 타입 로드
        foreach (var (name, type) in stageTypeOptions.Value.StageTypes)
        {
            _stageTypes[name] = type;
        }
    }

    /// <summary>
    /// Stage 타입을 런타임에 등록합니다.
    /// </summary>
    public void RegisterStageType<TStage>(string stageTypeName) where TStage : IStage
    {
        _stageTypes[stageTypeName] = typeof(TStage);
    }

    /// <summary>
    /// Stage를 생성하고 StagePool에 등록합니다.
    /// </summary>
    public async Task<(StageContext? context, ushort errorCode, IPacket? reply)> CreateStageAsync(
        string stageTypeName,
        IPacket initPacket)
    {
        if (!_stageTypes.TryGetValue(stageTypeName, out var stageType))
        {
            return (null, ErrorCode.StageTypeNotFound, null);
        }

        // 1. Stage ID 생성
        var stageId = _stagePool.GenerateStageId();

        // 2. IStageSender 구현체 생성
        var stageSender = new StageSenderImpl(
            stageId,
            stageTypeName,
            _timerManager,
            _packetDispatcher,
            _sessionManager,
            _stagePool);

        // 3. IStage 인스턴스 생성 (DI 활용)
        IStage userStage;
        try
        {
            userStage = (IStage)ActivatorUtilities.CreateInstance(_serviceProvider, stageType);
        }
        catch (Exception ex)
        {
            _loggerFactory.CreateLogger<StageFactory>()
                .LogError(ex, "Failed to create stage instance of type {StageType}", stageTypeName);
            return (null, ErrorCode.StageCreationFailed, null);
        }

        // 4. StageContext 래핑
        var context = new StageContext(
            userStage,
            stageSender,
            _loggerFactory.CreateLogger<StageContext>());

        // 5. OnCreate 호출
        var (errorCode, reply) = await context.OnCreateAsync(initPacket);
        if (errorCode != 0)
        {
            await context.DisposeAsync();
            return (null, errorCode, reply);
        }

        // 6. StagePool에 등록
        if (!_stagePool.AddStage(context))
        {
            await context.DisposeAsync();
            return (null, ErrorCode.StageAlreadyExists, null);
        }

        // 7. OnPostCreate 호출
        await context.OnPostCreateAsync();

        return (context, 0, reply);
    }

    /// <summary>
    /// Stage를 삭제합니다.
    /// </summary>
    public async Task<bool> DestroyStageAsync(int stageId)
    {
        var context = _stagePool.RemoveStage(stageId);
        if (context == null) return false;

        // 타이머 정리
        _timerManager.CancelAllTimersForStage(stageId);

        await context.DisposeAsync();
        return true;
    }

    /// <summary>
    /// 등록된 Stage 타입 목록을 반환합니다.
    /// </summary>
    public IEnumerable<string> GetRegisteredStageTypes()
    {
        return _stageTypes.Keys;
    }
}

/// <summary>
/// Stage 타입 등록 정보
/// </summary>
public class StageTypeRegistration
{
    public Dictionary<string, Type> StageTypes { get; } = new();
}
```

---

#### C3. IStageSender/IActorSender 구현체

**신규 파일**: `src/PlayHouse/Core/Stage/StageSenderImpl.cs`

```csharp
#nullable enable

using System;
using System.Threading.Tasks;
using PlayHouse.Abstractions;
using PlayHouse.Core.Messaging;
using PlayHouse.Core.Session;
using PlayHouse.Core.Timer;

namespace PlayHouse.Core.Stage;

/// <summary>
/// IStageSender 구현체 - Stage에서 사용하는 메시지 전송 및 타이머 관리
/// </summary>
internal sealed class StageSenderImpl : IStageSender
{
    private readonly TimerManager _timerManager;
    private readonly PacketDispatcher _packetDispatcher;
    private readonly SessionManager _sessionManager;
    private readonly StagePool _stagePool;

    // 현재 처리 중인 요청 컨텍스트 (Reply용)
    [ThreadStatic]
    private static RequestContext? _currentRequest;

    public int StageId { get; }
    public string StageType { get; }

    public StageSenderImpl(
        int stageId,
        string stageType,
        TimerManager timerManager,
        PacketDispatcher packetDispatcher,
        SessionManager sessionManager,
        StagePool stagePool)
    {
        StageId = stageId;
        StageType = stageType;
        _timerManager = timerManager;
        _packetDispatcher = packetDispatcher;
        _sessionManager = sessionManager;
        _stagePool = stagePool;
    }

    /// <summary>
    /// 현재 요청 컨텍스트를 설정합니다 (메시지 처리 시 호출)
    /// </summary>
    internal static void SetCurrentRequest(RequestContext context)
    {
        _currentRequest = context;
    }

    /// <summary>
    /// 현재 요청 컨텍스트를 초기화합니다.
    /// </summary>
    internal static void ClearCurrentRequest()
    {
        _currentRequest = null;
    }

    public void Reply(ushort errorCode)
    {
        if (_currentRequest == null)
        {
            throw new InvalidOperationException("No active request to reply to");
        }

        // 세션을 통해 에러 코드만 전송
        var session = _sessionManager.GetSession(_currentRequest.SessionId);
        if (session != null)
        {
            // TODO: 에러 응답 패킷 생성 및 전송
        }
    }

    public void Reply(IPacket packet)
    {
        if (_currentRequest == null)
        {
            throw new InvalidOperationException("No active request to reply to");
        }

        // 세션을 통해 응답 패킷 전송
        var session = _sessionManager.GetSession(_currentRequest.SessionId);
        if (session != null)
        {
            // TODO: 응답 패킷 직렬화 및 전송
        }
    }

    public ValueTask SendAsync(IPacket packet)
    {
        // 현재 요청의 세션에 패킷 전송
        if (_currentRequest != null)
        {
            var session = _sessionManager.GetSession(_currentRequest.SessionId);
            if (session != null)
            {
                // TODO: 패킷 직렬화 및 전송
            }
        }
        return ValueTask.CompletedTask;
    }

    public ValueTask SendToStageAsync(int targetStageId, IPacket packet)
    {
        _packetDispatcher.DispatchToStage(targetStageId, packet);
        return ValueTask.CompletedTask;
    }

    public ValueTask BroadcastAsync(IPacket packet)
    {
        var stage = _stagePool.GetStage(StageId);
        if (stage == null) return ValueTask.CompletedTask;

        foreach (var actorContext in stage.ActorPool.GetConnectedActors())
        {
            var session = _sessionManager.GetSessionByAccount(actorContext.AccountId);
            if (session != null)
            {
                // TODO: 패킷 직렬화 및 전송
            }
        }
        return ValueTask.CompletedTask;
    }

    public ValueTask BroadcastAsync(IPacket packet, Func<IActor, bool> filter)
    {
        var stage = _stagePool.GetStage(StageId);
        if (stage == null) return ValueTask.CompletedTask;

        foreach (var actorContext in stage.ActorPool.GetAllActors())
        {
            if (filter(actorContext.UserActor))
            {
                var session = _sessionManager.GetSessionByAccount(actorContext.AccountId);
                if (session != null)
                {
                    // TODO: 패킷 직렬화 및 전송
                }
            }
        }
        return ValueTask.CompletedTask;
    }

    public long AddRepeatTimer(TimeSpan initialDelay, TimeSpan period, Func<Task> callback)
    {
        return _timerManager.AddRepeatTimer(StageId, initialDelay, period, callback);
    }

    public long AddCountTimer(TimeSpan initialDelay, TimeSpan period, int count, Func<Task> callback)
    {
        return _timerManager.AddCountTimer(StageId, initialDelay, period, count, callback);
    }

    public void CancelTimer(long timerId)
    {
        _timerManager.CancelTimer(timerId);
    }

    public bool HasTimer(long timerId)
    {
        return _timerManager.HasTimer(timerId);
    }

    public void CloseStage()
    {
        // Stage 종료 요청 - StagePool에서 제거 및 정리 예약
        // 이 메서드는 Stage 내에서 호출되므로 즉시 삭제하지 않고 예약
        Task.Run(async () =>
        {
            var context = _stagePool.RemoveStage(StageId);
            if (context != null)
            {
                _timerManager.CancelAllTimersForStage(StageId);
                await context.DisposeAsync();
            }
        });
    }

    public void AsyncBlock(Func<Task<object?>> preCallback, Func<object?, Task>? postCallback = null)
    {
        Task.Run(async () =>
        {
            try
            {
                var result = await preCallback();
                if (postCallback != null)
                {
                    _packetDispatcher.DispatchAsyncBlockResult(StageId, postCallback, result);
                }
            }
            catch (Exception)
            {
                // 에러 처리
            }
        });
    }
}

/// <summary>
/// 요청 컨텍스트 - Reply에 필요한 정보
/// </summary>
internal sealed class RequestContext
{
    public long SessionId { get; init; }
    public long AccountId { get; init; }
    public ushort MsgSeq { get; init; }
}
```

**신규 파일**: `src/PlayHouse/Core/Stage/ActorSenderImpl.cs`

```csharp
#nullable enable

using System.Threading.Tasks;
using PlayHouse.Abstractions;
using PlayHouse.Core.Session;

namespace PlayHouse.Core.Stage;

/// <summary>
/// IActorSender 구현체 - Actor에서 사용하는 메시지 전송
/// </summary>
internal sealed class ActorSenderImpl : IActorSender
{
    private readonly SessionManager _sessionManager;
    private readonly StageSenderImpl _stageSender;

    public long AccountId { get; }
    public long SessionId { get; }

    public ActorSenderImpl(
        long accountId,
        long sessionId,
        SessionManager sessionManager,
        StageSenderImpl stageSender)
    {
        AccountId = accountId;
        SessionId = sessionId;
        _sessionManager = sessionManager;
        _stageSender = stageSender;
    }

    public void Reply(ushort errorCode)
    {
        _stageSender.Reply(errorCode);
    }

    public void Reply(IPacket packet)
    {
        _stageSender.Reply(packet);
    }

    public ValueTask SendAsync(IPacket packet)
    {
        var session = _sessionManager.GetSession(SessionId);
        if (session != null)
        {
            // TODO: 패킷 직렬화 및 전송
        }
        return ValueTask.CompletedTask;
    }
}
```

---

#### C4. Actor Join/Leave 처리 완성

**수정 파일**: `src/PlayHouse/Core/Stage/StageContext.cs`

**추가할 메서드** (클래스 끝 부분에 추가):

```csharp
/// <summary>
/// Actor를 Stage에 참가시킵니다.
/// </summary>
/// <param name="accountId">계정 ID</param>
/// <param name="sessionId">세션 ID</param>
/// <param name="userActor">사용자 정의 Actor</param>
/// <param name="userInfo">사용자 정보 패킷</param>
/// <returns>에러 코드와 응답 패킷</returns>
public async Task<(ushort errorCode, IPacket? reply)> JoinActorAsync(
    long accountId,
    long sessionId,
    IActor userActor,
    IPacket userInfo)
{
    // 1. Actor 중복 체크
    if (_actorPool.HasActor(accountId))
    {
        _logger.LogWarning("Actor {AccountId} already exists in stage {StageId}", accountId, StageId);
        return (ErrorCode.ActorAlreadyExists, null);
    }

    // 2. ActorContext 생성
    var actorContext = new ActorContext(
        accountId,
        sessionId,
        userActor,
        _logger);

    try
    {
        // 3. Actor.OnCreate 호출
        await actorContext.OnCreateAsync();

        // 4. Stage.OnJoinRoom 호출
        var (errorCode, reply) = await _userStage.OnJoinRoom(userActor, userInfo);
        if (errorCode != 0)
        {
            _logger.LogInformation("Actor {AccountId} join rejected with error {ErrorCode}", accountId, errorCode);
            await actorContext.DisposeAsync();
            return (errorCode, reply);
        }

        // 5. ActorPool에 등록
        if (!_actorPool.AddActor(actorContext))
        {
            _logger.LogWarning("Failed to add actor {AccountId} to pool", accountId);
            await actorContext.DisposeAsync();
            return (ErrorCode.ActorAlreadyExists, null);
        }

        // 6. Stage.OnPostJoinRoom 호출
        await _userStage.OnPostJoinRoom(userActor);

        _logger.LogInformation("Actor {AccountId} joined stage {StageId}", accountId, StageId);
        return (0, reply);
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Error joining actor {AccountId} to stage {StageId}", accountId, StageId);
        await actorContext.DisposeAsync();
        return (ErrorCode.InternalError, null);
    }
}

/// <summary>
/// Actor를 Stage에서 퇴장시킵니다.
/// </summary>
/// <param name="accountId">계정 ID</param>
/// <param name="reason">퇴장 사유</param>
public async Task LeaveActorAsync(long accountId, LeaveReason reason)
{
    var actorContext = _actorPool.RemoveActor(accountId);
    if (actorContext == null)
    {
        _logger.LogWarning("Actor {AccountId} not found in stage {StageId} for leave", accountId, StageId);
        return;
    }

    try
    {
        await _userStage.OnLeaveRoom(actorContext.UserActor, reason);
        _logger.LogInformation("Actor {AccountId} left stage {StageId} (reason: {Reason})",
            accountId, StageId, reason);
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Error in OnLeaveRoom for actor {AccountId}", accountId);
    }
    finally
    {
        await actorContext.DisposeAsync();
    }
}

/// <summary>
/// Actor 연결 상태를 업데이트합니다.
/// </summary>
/// <param name="accountId">계정 ID</param>
/// <param name="isConnected">연결 상태</param>
/// <param name="reason">연결 해제 사유 (연결 해제 시)</param>
public async Task UpdateActorConnectionAsync(
    long accountId,
    bool isConnected,
    DisconnectReason? reason)
{
    var actorContext = _actorPool.GetActor(accountId);
    if (actorContext == null)
    {
        _logger.LogWarning("Actor {AccountId} not found in stage {StageId} for connection update",
            accountId, StageId);
        return;
    }

    actorContext.SetConnectionState(isConnected);

    try
    {
        await _userStage.OnActorConnectionChanged(actorContext.UserActor, isConnected, reason);
        _logger.LogInformation("Actor {AccountId} connection state changed to {State} in stage {StageId}",
            accountId, isConnected ? "connected" : "disconnected", StageId);
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Error in OnActorConnectionChanged for actor {AccountId}", accountId);
    }
}

/// <summary>
/// 특정 Actor를 조회합니다.
/// </summary>
public ActorContext? GetActor(long accountId)
{
    return _actorPool.GetActor(accountId);
}

/// <summary>
/// Actor 수를 반환합니다.
/// </summary>
public int ActorCount => _actorPool.Count;
```

---

### Phase 2: HTTP API (High Priority)

---

#### H1. RoomController 완성

**수정 파일**: `src/PlayHouse/Infrastructure/Http/RoomController.cs`

전체 교체:

```csharp
#nullable enable

using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using PlayHouse.Abstractions;
using PlayHouse.Core.Stage;
using PlayHouse.Infrastructure.Serialization;

namespace PlayHouse.Infrastructure.Http;

/// <summary>
/// HTTP API controller for room management operations.
/// </summary>
[ApiController]
[Route("api/rooms")]
public sealed class RoomController : ControllerBase
{
    private readonly StageFactory _stageFactory;
    private readonly StagePool _stagePool;
    private readonly ILogger<RoomController> _logger;

    public RoomController(
        StageFactory stageFactory,
        StagePool stagePool,
        ILogger<RoomController> logger)
    {
        _stageFactory = stageFactory;
        _stagePool = stagePool;
        _logger = logger;
    }

    /// <summary>
    /// 서버 상태를 조회합니다.
    /// </summary>
    [HttpGet("status")]
    [ProducesResponseType(typeof(ServerStatusResponse), 200)]
    public IActionResult GetStatus()
    {
        var stats = _stagePool.GetStatistics();

        return Ok(new ServerStatusResponse
        {
            TotalStages = (int)stats["total_stages"],
            TotalActors = (int)stats["total_actors"],
            StagesByType = (Dictionary<string, int>)stats["stages_by_type"],
            Timestamp = DateTime.UtcNow
        });
    }

    /// <summary>
    /// 방을 생성하거나 기존 방을 반환합니다.
    /// </summary>
    [HttpPost("get-or-create")]
    [ProducesResponseType(typeof(GetOrCreateRoomResponse), 200)]
    [ProducesResponseType(400)]
    public async Task<IActionResult> GetOrCreateRoom([FromBody] GetOrCreateRoomRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.StageType))
        {
            return BadRequest(new { message = "StageType is required" });
        }

        _logger.LogInformation("GetOrCreateRoom: StageType={StageType}", request.StageType);

        // 1. 기존 방 검색 (RoomId로 매칭)
        if (!string.IsNullOrEmpty(request.RoomId))
        {
            var existingStage = _stagePool.GetStagesByType(request.StageType)
                .FirstOrDefault(s => s.StageId.ToString() == request.RoomId);

            if (existingStage != null)
            {
                return Ok(new GetOrCreateRoomResponse
                {
                    StageId = existingStage.StageId,
                    StageType = request.StageType,
                    Created = false
                });
            }
        }

        // 2. 새 방 생성
        var initPacket = CreateInitPacket(request);
        var (context, errorCode, reply) = await _stageFactory.CreateStageAsync(
            request.StageType,
            initPacket);

        if (context == null)
        {
            _logger.LogWarning("Failed to create stage: errorCode={ErrorCode}", errorCode);
            return BadRequest(new ErrorResponse
            {
                ErrorCode = errorCode,
                Message = GetErrorMessage(errorCode)
            });
        }

        _logger.LogInformation("Created stage {StageId} of type {StageType}", context.StageId, request.StageType);

        return Ok(new GetOrCreateRoomResponse
        {
            StageId = context.StageId,
            StageType = request.StageType,
            Created = true
        });
    }

    /// <summary>
    /// 방 정보를 조회합니다.
    /// </summary>
    [HttpGet("{stageId:int}")]
    [ProducesResponseType(typeof(RoomInfoResponse), 200)]
    [ProducesResponseType(404)]
    public IActionResult GetRoom(int stageId)
    {
        var stage = _stagePool.GetStage(stageId);
        if (stage == null)
        {
            return NotFound(new { message = "Stage not found" });
        }

        return Ok(new RoomInfoResponse
        {
            StageId = stage.StageId,
            StageType = stage.StageType,
            ActorCount = stage.ActorCount,
            QueueDepth = stage.QueueDepth,
            IsProcessing = stage.IsProcessing
        });
    }

    /// <summary>
    /// 방에 참가합니다. (HTTP를 통한 Join - 주로 테스트/관리용)
    /// </summary>
    [HttpPost("{stageId:int}/join")]
    [ProducesResponseType(typeof(JoinRoomResponse), 200)]
    [ProducesResponseType(404)]
    [ProducesResponseType(400)]
    public async Task<IActionResult> JoinRoom(int stageId, [FromBody] JoinRoomRequest request)
    {
        var stage = _stagePool.GetStage(stageId);
        if (stage == null)
        {
            return NotFound(new { message = "Stage not found" });
        }

        _logger.LogInformation("JoinRoom: StageId={StageId}, AccountId={AccountId}", stageId, request.AccountId);

        // 주의: 실제 운영에서는 Actor 인스턴스 생성 방식이 다를 수 있음
        // 여기서는 테스트/관리 목적으로 간단히 처리
        // TODO: Actor 팩토리 연동 필요

        return Ok(new JoinRoomResponse
        {
            StageId = stageId,
            AccountId = request.AccountId,
            JoinedAt = DateTime.UtcNow
        });
    }

    /// <summary>
    /// 방에서 퇴장합니다.
    /// </summary>
    [HttpPost("{stageId:int}/leave")]
    [ProducesResponseType(204)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> LeaveRoom(int stageId, [FromQuery] long accountId)
    {
        var stage = _stagePool.GetStage(stageId);
        if (stage == null)
        {
            return NotFound(new { message = "Stage not found" });
        }

        _logger.LogInformation("LeaveRoom: StageId={StageId}, AccountId={AccountId}", stageId, accountId);

        await stage.LeaveActorAsync(accountId, LeaveReason.Normal);
        return NoContent();
    }

    /// <summary>
    /// 방을 삭제합니다.
    /// </summary>
    [HttpDelete("{stageId:int}")]
    [ProducesResponseType(204)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> DeleteRoom(int stageId)
    {
        _logger.LogInformation("DeleteRoom: StageId={StageId}", stageId);

        var success = await _stageFactory.DestroyStageAsync(stageId);
        if (!success)
        {
            return NotFound(new { message = "Stage not found" });
        }

        return NoContent();
    }

    /// <summary>
    /// 모든 방 목록을 조회합니다.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(RoomListResponse), 200)]
    public IActionResult GetRooms([FromQuery] string? stageType = null)
    {
        var stages = string.IsNullOrEmpty(stageType)
            ? _stagePool.GetAllStages()
            : _stagePool.GetStagesByType(stageType);

        var rooms = stages.Select(s => new RoomInfoResponse
        {
            StageId = s.StageId,
            StageType = s.StageType,
            ActorCount = s.ActorCount,
            QueueDepth = s.QueueDepth,
            IsProcessing = s.IsProcessing
        }).ToList();

        return Ok(new RoomListResponse { Rooms = rooms });
    }

    private IPacket CreateInitPacket(GetOrCreateRoomRequest request)
    {
        // 초기화 데이터를 패킷으로 변환
        var data = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(new
        {
            request.RoomId,
            request.MaxPlayers,
            request.Metadata
        });

        return new SimplePacket("RoomInit", new BinaryPayload(data));
    }

    private string GetErrorMessage(ushort errorCode)
    {
        return errorCode switch
        {
            ErrorCode.StageTypeNotFound => "Stage type not found",
            ErrorCode.StageAlreadyExists => "Stage already exists",
            ErrorCode.StageCreationFailed => "Stage creation failed",
            _ => "Unknown error"
        };
    }
}

#region DTOs

public sealed class ServerStatusResponse
{
    public int TotalStages { get; init; }
    public int TotalActors { get; init; }
    public Dictionary<string, int> StagesByType { get; init; } = new();
    public DateTime Timestamp { get; init; }
}

public sealed class GetOrCreateRoomRequest
{
    public required string StageType { get; init; }
    public string? RoomId { get; init; }
    public int MaxPlayers { get; init; } = 4;
    public Dictionary<string, string>? Metadata { get; init; }
}

public sealed class GetOrCreateRoomResponse
{
    public int StageId { get; init; }
    public string StageType { get; init; } = "";
    public bool Created { get; init; }
}

public sealed class RoomInfoResponse
{
    public int StageId { get; init; }
    public string StageType { get; init; } = "";
    public int ActorCount { get; init; }
    public int QueueDepth { get; init; }
    public bool IsProcessing { get; init; }
}

public sealed class RoomListResponse
{
    public List<RoomInfoResponse> Rooms { get; init; } = new();
}

public sealed class JoinRoomRequest
{
    public required long AccountId { get; init; }
    public long SessionId { get; init; }
    public string? UserData { get; init; }
}

public sealed class JoinRoomResponse
{
    public int StageId { get; init; }
    public long AccountId { get; init; }
    public DateTime JoinedAt { get; init; }
}

public sealed class ErrorResponse
{
    public ushort ErrorCode { get; init; }
    public string Message { get; init; } = "";
}

#endregion
```

---

#### H2. Health Check 엔드포인트

**신규 파일**: `src/PlayHouse/Infrastructure/Http/HealthController.cs`

```csharp
#nullable enable

using Microsoft.AspNetCore.Mvc;
using PlayHouse.Core.Messaging;
using PlayHouse.Core.Session;
using PlayHouse.Core.Stage;

namespace PlayHouse.Infrastructure.Http;

/// <summary>
/// Health check endpoints for Kubernetes/Docker health probes.
/// </summary>
[ApiController]
[Route("health")]
public sealed class HealthController : ControllerBase
{
    private readonly StagePool _stagePool;
    private readonly SessionManager _sessionManager;
    private readonly PacketDispatcher _packetDispatcher;
    private static readonly DateTime StartTime = DateTime.UtcNow;

    public HealthController(
        StagePool stagePool,
        SessionManager sessionManager,
        PacketDispatcher packetDispatcher)
    {
        _stagePool = stagePool;
        _sessionManager = sessionManager;
        _packetDispatcher = packetDispatcher;
    }

    /// <summary>
    /// 전체 서버 상태를 반환합니다.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(HealthResponse), 200)]
    public IActionResult GetHealth()
    {
        var stageStats = _stagePool.GetStatistics();
        var sessionStats = _sessionManager.GetStatistics();
        var dispatcherStats = _packetDispatcher.GetStatistics();

        return Ok(new HealthResponse
        {
            Status = "healthy",
            Uptime = (long)(DateTime.UtcNow - StartTime).TotalSeconds,
            Stages = (int)stageStats["total_stages"],
            Actors = (int)stageStats["total_actors"],
            Sessions = sessionStats.TotalSessions,
            ConnectedSessions = sessionStats.ConnectedSessions,
            DisconnectedSessions = sessionStats.DisconnectedSessions,
            QueueDepth = dispatcherStats.TotalQueueDepth,
            StagesProcessing = dispatcherStats.StagesProcessing,
            Timestamp = DateTime.UtcNow
        });
    }

    /// <summary>
    /// Readiness probe - 트래픽 수신 준비 완료 여부
    /// </summary>
    [HttpGet("ready")]
    [ProducesResponseType(200)]
    [ProducesResponseType(503)]
    public IActionResult GetReady()
    {
        // 서버가 준비되었는지 확인하는 로직
        // 예: 필수 서비스가 초기화되었는지 확인
        return Ok(new { status = "ready", timestamp = DateTime.UtcNow });
    }

    /// <summary>
    /// Liveness probe - 서버가 살아있는지 여부
    /// </summary>
    [HttpGet("live")]
    [ProducesResponseType(200)]
    public IActionResult GetLive()
    {
        return Ok(new { status = "alive", timestamp = DateTime.UtcNow });
    }
}

public sealed class HealthResponse
{
    public string Status { get; init; } = "";
    public long Uptime { get; init; }
    public int Stages { get; init; }
    public int Actors { get; init; }
    public int Sessions { get; init; }
    public int ConnectedSessions { get; init; }
    public int DisconnectedSessions { get; init; }
    public int QueueDepth { get; init; }
    public int StagesProcessing { get; init; }
    public DateTime Timestamp { get; init; }
}
```

---

#### H3. DI 확장 메서드 업데이트

**수정 파일**: `src/PlayHouse/Infrastructure/Http/PlayHouseServiceExtensions.cs`

```csharp
#nullable enable

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PlayHouse.Abstractions;
using PlayHouse.Core.Messaging;
using PlayHouse.Core.Session;
using PlayHouse.Core.Stage;
using PlayHouse.Core.Timer;
using PlayHouse.Infrastructure.Serialization;

namespace PlayHouse.Infrastructure.Http;

/// <summary>
/// PlayHouse 서비스 등록 확장 메서드
/// </summary>
public static class PlayHouseServiceExtensions
{
    /// <summary>
    /// PlayHouse 프레임워크 서비스를 등록합니다.
    /// </summary>
    public static IServiceCollection AddPlayHouse(
        this IServiceCollection services,
        Action<PlayHouseOptions> configureOptions)
    {
        services.Configure(configureOptions);
        services.Configure<StageTypeRegistration>(_ => { }); // 빈 등록

        // Core Services - Singleton
        services.AddSingleton<StagePool>();
        services.AddSingleton<SessionManager>();
        services.AddSingleton<PacketSerializer>();
        services.AddSingleton<PacketDispatcher>();

        // TimerManager - PacketDispatcher 의존
        services.AddSingleton<TimerManager>(sp =>
        {
            var dispatcher = sp.GetRequiredService<PacketDispatcher>();
            var logger = sp.GetRequiredService<ILogger<TimerManager>>();
            return new TimerManager(
                packet => dispatcher.Dispatch(packet),
                logger);
        });

        // StageFactory
        services.AddSingleton<StageFactory>();

        // PlayHouseServer - IHostedService
        services.AddSingleton<PlayHouseServer>();
        services.AddHostedService(sp => sp.GetRequiredService<PlayHouseServer>());

        // Controllers
        services.AddControllers()
            .AddApplicationPart(typeof(RoomController).Assembly);

        return services;
    }

    /// <summary>
    /// Stage 타입을 등록합니다.
    /// </summary>
    /// <typeparam name="TStage">IStage 구현 타입</typeparam>
    /// <param name="services">서비스 컬렉션</param>
    /// <param name="stageTypeName">Stage 타입 식별자</param>
    public static IServiceCollection AddStageType<TStage>(
        this IServiceCollection services,
        string stageTypeName) where TStage : class, IStage
    {
        // Stage 타입을 Transient로 등록 (매번 새 인스턴스)
        services.AddTransient<TStage>();

        // StageTypeRegistration에 타입 등록
        services.Configure<StageTypeRegistration>(options =>
        {
            options.StageTypes[stageTypeName] = typeof(TStage);
        });

        return services;
    }

    /// <summary>
    /// Actor 타입을 등록합니다.
    /// </summary>
    /// <typeparam name="TActor">IActor 구현 타입</typeparam>
    /// <param name="services">서비스 컬렉션</param>
    public static IServiceCollection AddActorType<TActor>(
        this IServiceCollection services) where TActor : class, IActor
    {
        services.AddTransient<TActor>();
        return services;
    }
}
```

---

### ErrorCode 클래스 추가

**수정 파일**: `src/PlayHouse/Abstractions/ErrorCode.cs`

```csharp
#nullable enable

namespace PlayHouse.Abstractions;

/// <summary>
/// 프레임워크 에러 코드 정의
/// </summary>
public static class ErrorCode
{
    /// <summary>성공</summary>
    public const ushort Success = 0;

    // Stage 관련 (1000~1999)
    /// <summary>Stage를 찾을 수 없음</summary>
    public const ushort StageNotFound = 1001;
    /// <summary>등록되지 않은 Stage 타입</summary>
    public const ushort StageTypeNotFound = 1002;
    /// <summary>이미 존재하는 Stage</summary>
    public const ushort StageAlreadyExists = 1003;
    /// <summary>Stage 생성 실패</summary>
    public const ushort StageCreationFailed = 1004;
    /// <summary>Stage가 닫힘</summary>
    public const ushort StageClosed = 1005;
    /// <summary>Stage가 가득 참</summary>
    public const ushort StageFull = 1006;

    // Actor 관련 (2000~2999)
    /// <summary>Actor를 찾을 수 없음</summary>
    public const ushort ActorNotFound = 2001;
    /// <summary>이미 존재하는 Actor</summary>
    public const ushort ActorAlreadyExists = 2002;
    /// <summary>인증되지 않은 Actor</summary>
    public const ushort ActorNotAuthenticated = 2003;
    /// <summary>Actor 참가 실패</summary>
    public const ushort ActorJoinFailed = 2004;
    /// <summary>Actor 인증 실패</summary>
    public const ushort ActorAuthFailed = 2005;

    // Session 관련 (3000~3999)
    /// <summary>세션을 찾을 수 없음</summary>
    public const ushort SessionNotFound = 3001;
    /// <summary>세션 만료</summary>
    public const ushort SessionExpired = 3002;
    /// <summary>세션 인증 필요</summary>
    public const ushort SessionNotAuthenticated = 3003;

    // 패킷 관련 (4500~4999)
    /// <summary>잘못된 패킷 형식</summary>
    public const ushort InvalidPacket = 4501;
    /// <summary>알 수 없는 메시지 ID</summary>
    public const ushort UnknownMessageId = 4502;

    // 일반 (5000~5999)
    /// <summary>내부 서버 오류</summary>
    public const ushort InternalError = 5001;
    /// <summary>잘못된 요청</summary>
    public const ushort InvalidRequest = 5002;
    /// <summary>서비스 이용 불가</summary>
    public const ushort ServiceUnavailable = 5003;
    /// <summary>요청 시간 초과</summary>
    public const ushort Timeout = 5004;
}
```

---

## 5. 테스트 전략

### 5.1 테스트 구조

```
tests/
├── PlayHouse.Tests.Integration/       # 통합 테스트 (80%)
│   ├── Stage/
│   │   ├── StageCreationTests.cs
│   │   ├── ActorJoinLeaveTests.cs
│   │   └── StageMessageRoutingTests.cs
│   ├── Transport/
│   │   ├── TcpServerTests.cs
│   │   └── WebSocketServerTests.cs
│   ├── Http/
│   │   ├── RoomControllerTests.cs
│   │   └── HealthControllerTests.cs
│   └── EndToEnd/
│       └── FullFlowTests.cs
│
├── PlayHouse.Tests.Unit/             # 유닛 테스트 (20%)
│   ├── Serialization/
│   │   └── PacketSerializerTests.cs
│   ├── Stage/
│   │   └── AtomicBooleanTests.cs
│   └── Timer/
│       └── TimerManagerTests.cs
│
└── PlayHouse.Tests.E2E/              # E2E 테스트 (Connector 사용)
    └── ClientServerTests.cs
```

### 5.2 핵심 테스트 시나리오

```csharp
// Stage 생성 → Actor Join → 메시지 전송 → Actor Leave → Stage 삭제
[Fact]
public async Task FullRoomLifecycleTest()
{
    // Arrange
    var stageFactory = _serviceProvider.GetRequiredService<StageFactory>();
    var stagePool = _serviceProvider.GetRequiredService<StagePool>();
    var dispatcher = _serviceProvider.GetRequiredService<PacketDispatcher>();

    // 1. Stage 생성
    var initPacket = CreateTestPacket("Init");
    var (stage, errorCode, _) = await stageFactory.CreateStageAsync("TestRoom", initPacket);
    Assert.Equal(0, errorCode);
    Assert.NotNull(stage);

    // 2. Actor Join
    var userActor = new TestActor();
    var userInfo = CreateTestPacket("UserInfo");
    var (joinError, _) = await stage.JoinActorAsync(accountId: 1001, sessionId: 1, userActor, userInfo);
    Assert.Equal(0, joinError);
    Assert.Equal(1, stage.ActorCount);

    // 3. 메시지 전송
    var packet = CreateTestPacket("GameAction");
    var dispatched = dispatcher.DispatchToActor(stage.StageId, accountId: 1001, packet);
    Assert.True(dispatched);

    // 4. Actor Leave
    await stage.LeaveActorAsync(accountId: 1001, LeaveReason.Normal);
    Assert.Equal(0, stage.ActorCount);

    // 5. Stage 삭제
    await stageFactory.DestroyStageAsync(stage.StageId);
    Assert.Null(stagePool.GetStage(stage.StageId));
}
```

---

## 6. 개발 체크리스트

### Phase 1: Core Integration (Critical)

- [ ] **C1. PlayHouseServer 메시지 라우팅**
  - [ ] `PlayHouseServer.cs`에 의존성 주입 추가
    - [ ] PacketSerializer
    - [ ] PacketDispatcher
    - [ ] SessionManager
  - [ ] `OnTcpMessageReceived` 구현
  - [ ] `OnWebSocketMessageReceived` 구현
  - [ ] 에러 처리 및 로깅 추가
  - [ ] 통합 테스트 작성

- [ ] **C2. Stage 생성/등록 API**
  - [ ] `StageFactory.cs` 신규 생성
  - [ ] `StageTypeRegistration.cs` 추가
  - [ ] StageFactory DI 등록
  - [ ] 통합 테스트 작성

- [ ] **C3. IStageSender/IActorSender 구현체**
  - [ ] `StageSenderImpl.cs` 신규 생성
  - [ ] `ActorSenderImpl.cs` 신규 생성
  - [ ] `RequestContext.cs` 추가
  - [ ] Reply 메커니즘 구현

- [ ] **C4. Actor Join/Leave 처리**
  - [ ] `StageContext.JoinActorAsync` 추가
  - [ ] `StageContext.LeaveActorAsync` 추가
  - [ ] `StageContext.UpdateActorConnectionAsync` 추가
  - [ ] 통합 테스트 작성

### Phase 2: HTTP API (High Priority)

- [ ] **H1. RoomController 완성**
  - [ ] `GET /api/rooms/status` 구현
  - [ ] `POST /api/rooms/get-or-create` 구현
  - [ ] `GET /api/rooms/{stageId}` 구현
  - [ ] `POST /api/rooms/{stageId}/join` 구현
  - [ ] `POST /api/rooms/{stageId}/leave` 구현
  - [ ] `DELETE /api/rooms/{stageId}` 구현
  - [ ] `GET /api/rooms` 구현
  - [ ] DTO 클래스 정리

- [ ] **H2. Health Check**
  - [ ] `HealthController.cs` 신규 생성
  - [ ] `GET /health` 구현
  - [ ] `GET /health/ready` 구현
  - [ ] `GET /health/live` 구현

- [ ] **H3. DI 확장**
  - [ ] `PlayHouseServiceExtensions.cs` 업데이트
  - [ ] `AddStageType<T>` 확장 메서드
  - [ ] `AddActorType<T>` 확장 메서드

- [ ] **H4. ErrorCode 정의**
  - [ ] `ErrorCode.cs` 업데이트
  - [ ] 에러 메시지 매핑

### Phase 3: Integration (Medium)

- [ ] **M1. WebSocket Middleware**
  - [ ] `UseWebSockets()` 미들웨어 추가
  - [ ] WebSocket 핸들러 구현
  - [ ] WebSocketServer 통합

- [ ] **M2. Backend SDK**
  - [ ] `PlayHouse.Client` 프로젝트 생성
  - [ ] `IRoomServerClient` 인터페이스
  - [ ] HTTP 클라이언트 구현
  - [ ] NuGet 패키지 설정

### Phase 4: Operations (Low)

- [ ] **L1. Metrics/Observability**
  - [ ] OpenTelemetry 설정
  - [ ] Prometheus 메트릭
  - [ ] 커스텀 메트릭 추가

- [ ] **L2. Swagger/OpenAPI**
  - [ ] Swashbuckle 패키지 추가
  - [ ] XML 문서 활성화
  - [ ] API 버전 관리

---

## 참고 문서

- `doc/specs/01-architecture.md` - 전체 아키텍처
- `doc/specs/05-http-api.md` - HTTP API 스펙
- `doc/specs/06-socket-transport.md` - 소켓 전송 스펙
- `doc/specs/11-event-loop-messaging.md` - 이벤트 루프 스펙

---

## 변경 이력

| 날짜 | 버전 | 변경 내용 |
|------|------|----------|
| 2025-12-09 | 1.0 | 초기 계획 작성 |
