# PlayHouse.Connector - .NET 클라이언트 커넥터

## 1. 개요

`PlayHouse.Connector`는 PlayHouse-NET Room Server에 연결하는 .NET 클라이언트 라이브러리입니다.
주로 **E2E 테스트**와 **통합 테스트**에서 사용됩니다.

### 1.1 용도

- **E2E 테스트**: 실제 서버에 연결하여 전체 시나리오 검증
- **통합 테스트**: 여러 컴포넌트 간 상호작용 테스트
- **부하 테스트**: 다수 클라이언트 시뮬레이션
- **개발/디버깅**: 서버 개발 중 빠른 테스트

### 1.2 범위

```
PlayHouse.Connector (이 문서)
- E2E/통합 테스트용 .NET 클라이언트
- NuGet 패키지로 제공

별도 프로젝트 (추후)
- playhouse-connector-unity (Unity용)
- playhouse-connector-unreal (Unreal용)
- playhouse-connector-ts (TypeScript/웹용)
```

### 1.3 설계 원칙

- **간결한 API**: 테스트 코드에서 쉽게 사용
- **비동기 우선**: `async/await` 기반
- **타입 안전**: Protobuf 메시지 직접 지원
- **재연결 지원**: 자동 재연결 및 상태 복구
- **테스트 친화적**: Mock/Stub 지원, 이벤트 기반

## 2. 패키지 구조

```
PlayHouse.Connector/
├── PlayHouse.Connector.csproj
├── IPlayHouseClient.cs           # 메인 클라이언트 인터페이스
├── PlayHouseClient.cs            # 클라이언트 구현
├── PlayHouseClientOptions.cs     # 클라이언트 설정
├── Connection/
│   ├── IConnection.cs            # 연결 인터페이스
│   ├── TcpConnection.cs          # TCP 연결
│   └── WebSocketConnection.cs    # WebSocket 연결
├── Protocol/
│   ├── PacketEncoder.cs          # 패킷 직렬화
│   ├── PacketDecoder.cs          # 패킷 역직렬화
│   └── RequestTracker.cs         # Request-Reply 추적
├── Events/
│   ├── ConnectionEventArgs.cs    # 연결 이벤트
│   ├── MessageEventArgs.cs       # 메시지 이벤트
│   └── ErrorEventArgs.cs         # 에러 이벤트
└── Extensions/
    └── ServiceCollectionExtensions.cs  # DI 확장
```

## 3. 핵심 인터페이스

### 3.1 IPlayHouseClient

```csharp
#nullable enable

using Google.Protobuf;

/// <summary>
/// PlayHouse Room Server 클라이언트 인터페이스.
/// </summary>
/// <remarks>
/// E2E/통합 테스트용 .NET 클라이언트입니다.
/// 게임 클라이언트(Unity/Unreal/Web)는 별도 프로젝트에서 제공됩니다.
/// </remarks>
public interface IPlayHouseClient : IAsyncDisposable
{
    #region 연결 상태

    /// <summary>현재 연결 상태</summary>
    ConnectionState State { get; }

    /// <summary>연결된 Stage ID (0이면 미연결)</summary>
    int StageId { get; }

    /// <summary>Account ID (0이면 미인증)</summary>
    long AccountId { get; }

    /// <summary>연결 여부</summary>
    bool IsConnected { get; }

    #endregion

    #region 연결/종료

    /// <summary>
    /// Room Server에 연결하고 Stage에 입장합니다.
    /// </summary>
    /// <param name="endpoint">서버 주소 (tcp://host:port 또는 ws://host:port)</param>
    /// <param name="roomToken">HTTP API에서 발급받은 토큰</param>
    /// <param name="cancellationToken">취소 토큰</param>
    /// <returns>입장 응답</returns>
    Task<JoinRoomResult> ConnectAsync(
        string endpoint,
        string roomToken,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 연결을 종료합니다.
    /// </summary>
    /// <param name="reason">종료 사유</param>
    Task DisconnectAsync(string? reason = null);

    /// <summary>
    /// 재연결을 시도합니다.
    /// </summary>
    /// <param name="cancellationToken">취소 토큰</param>
    /// <returns>재연결 성공 여부</returns>
    Task<bool> ReconnectAsync(CancellationToken cancellationToken = default);

    #endregion

    #region 메시지 송수신

    /// <summary>
    /// Request-Reply 패턴으로 메시지를 전송하고 응답을 기다립니다.
    /// </summary>
    /// <typeparam name="TRequest">요청 메시지 타입</typeparam>
    /// <typeparam name="TResponse">응답 메시지 타입</typeparam>
    /// <param name="request">요청 메시지</param>
    /// <param name="timeout">응답 대기 시간</param>
    /// <param name="cancellationToken">취소 토큰</param>
    /// <returns>응답 결과</returns>
    Task<Response<TResponse>> RequestAsync<TRequest, TResponse>(
        TRequest request,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
        where TRequest : IMessage
        where TResponse : IMessage, new();

    /// <summary>
    /// Fire-and-forget 패턴으로 메시지를 전송합니다.
    /// </summary>
    /// <typeparam name="T">메시지 타입</typeparam>
    /// <param name="message">전송할 메시지</param>
    ValueTask SendAsync<T>(T message) where T : IMessage;

    /// <summary>
    /// 방 퇴장 요청을 전송합니다.
    /// </summary>
    /// <param name="reason">퇴장 사유</param>
    Task<LeaveRoomResult> LeaveRoomAsync(string? reason = null);

    #endregion

    #region 이벤트

    /// <summary>연결 상태 변경 이벤트</summary>
    event EventHandler<ConnectionStateChangedEventArgs>? ConnectionStateChanged;

    /// <summary>서버로부터 Push 메시지 수신 이벤트</summary>
    event EventHandler<MessageReceivedEventArgs>? MessageReceived;

    /// <summary>에러 발생 이벤트</summary>
    event EventHandler<ClientErrorEventArgs>? ErrorOccurred;

    /// <summary>연결 끊김 이벤트</summary>
    event EventHandler<DisconnectedEventArgs>? Disconnected;

    #endregion

    #region 메시지 핸들러 등록

    /// <summary>
    /// 특정 메시지 타입에 대한 핸들러를 등록합니다.
    /// </summary>
    /// <typeparam name="T">메시지 타입</typeparam>
    /// <param name="handler">메시지 핸들러</param>
    /// <returns>핸들러 해제용 IDisposable</returns>
    IDisposable On<T>(Action<T> handler) where T : IMessage, new();

    /// <summary>
    /// 특정 메시지 타입에 대한 비동기 핸들러를 등록합니다.
    /// </summary>
    /// <typeparam name="T">메시지 타입</typeparam>
    /// <param name="handler">비동기 메시지 핸들러</param>
    /// <returns>핸들러 해제용 IDisposable</returns>
    IDisposable On<T>(Func<T, Task> handler) where T : IMessage, new();

    #endregion
}

/// <summary>연결 상태</summary>
public enum ConnectionState
{
    Disconnected,
    Connecting,
    Connected,
    Reconnecting,
    Disconnecting
}
```

### 3.2 응답 타입

```csharp
/// <summary>
/// Request-Reply 응답 결과.
/// </summary>
public readonly record struct Response<T>(
    bool Success,
    ushort ErrorCode,
    T? Data,
    string? ErrorMessage = null) where T : IMessage
{
    public static Response<T> Ok(T data) => new(true, 0, data);
    public static Response<T> Fail(ushort errorCode, string? message = null)
        => new(false, errorCode, default, message);
}

/// <summary>
/// Stage 입장 결과.
/// </summary>
public record JoinRoomResult
{
    public bool Success { get; init; }
    public bool IsReconnect { get; init; }
    public int StageId { get; init; }
    public ushort ErrorCode { get; init; }
    public string? ErrorMessage { get; init; }
    public object? StageInfo { get; init; }
    public object? GameState { get; init; }
}

/// <summary>
/// 방 퇴장 결과.
/// </summary>
public record LeaveRoomResult
{
    public bool Success { get; init; }
    public ushort ErrorCode { get; init; }
    public string? Message { get; init; }
}
```

### 3.3 이벤트 인자

```csharp
/// <summary>연결 상태 변경 이벤트 인자</summary>
public class ConnectionStateChangedEventArgs : EventArgs
{
    public ConnectionState OldState { get; init; }
    public ConnectionState NewState { get; init; }
    public string? Reason { get; init; }
}

/// <summary>메시지 수신 이벤트 인자</summary>
public class MessageReceivedEventArgs : EventArgs
{
    public string MsgId { get; init; } = string.Empty;
    public IMessage Message { get; init; } = null!;
    public int StageId { get; init; }
}

/// <summary>클라이언트 에러 이벤트 인자</summary>
public class ClientErrorEventArgs : EventArgs
{
    public Exception Exception { get; init; } = null!;
    public string Context { get; init; } = string.Empty;
}

/// <summary>연결 끊김 이벤트 인자</summary>
public class DisconnectedEventArgs : EventArgs
{
    public DisconnectReason Reason { get; init; }
    public string? Message { get; init; }
    public bool WasConnected { get; init; }
}

/// <summary>연결 끊김 사유</summary>
public enum DisconnectReason
{
    Normal,           // 정상 종료
    NetworkError,     // 네트워크 오류
    Timeout,          // 타임아웃
    Kicked,           // 강제 킥
    ServerShutdown,   // 서버 종료
    TokenExpired      // 토큰 만료
}
```

## 4. 클라이언트 옵션

```csharp
/// <summary>
/// PlayHouse 클라이언트 설정.
/// </summary>
public class PlayHouseClientOptions
{
    /// <summary>요청 타임아웃 (기본 10초)</summary>
    public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>연결 타임아웃 (기본 5초)</summary>
    public TimeSpan ConnectTimeout { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>하트비트 간격 (기본 30초)</summary>
    public TimeSpan HeartbeatInterval { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>하트비트 타임아웃 (기본 90초)</summary>
    public TimeSpan HeartbeatTimeout { get; set; } = TimeSpan.FromSeconds(90);

    /// <summary>자동 재연결 활성화 (기본 true)</summary>
    public bool AutoReconnect { get; set; } = true;

    /// <summary>재연결 최대 시도 횟수 (기본 3)</summary>
    public int MaxReconnectAttempts { get; set; } = 3;

    /// <summary>재연결 백오프 지연 (기본 1초, 2초, 4초)</summary>
    public int[] ReconnectBackoffMs { get; set; } = { 1000, 2000, 4000 };

    /// <summary>수신 버퍼 크기 (기본 64KB)</summary>
    public int ReceiveBufferSize { get; set; } = 64 * 1024;

    /// <summary>송신 버퍼 크기 (기본 64KB)</summary>
    public int SendBufferSize { get; set; } = 64 * 1024;

    /// <summary>패킷 압축 임계값 (기본 512바이트, 0이면 비활성화)</summary>
    public int CompressionThreshold { get; set; } = 512;

    /// <summary>로깅 활성화</summary>
    public bool EnableLogging { get; set; } = true;
}
```

## 5. 사용 예시

### 5.1 기본 사용법

```csharp
using PlayHouse.Connector;
using Simple;  // Protobuf 생성 네임스페이스

// 클라이언트 생성
await using var client = new PlayHouseClient(new PlayHouseClientOptions
{
    RequestTimeout = TimeSpan.FromSeconds(5),
    AutoReconnect = true
});

// 이벤트 핸들러 등록
client.ConnectionStateChanged += (sender, e) =>
{
    Console.WriteLine($"State changed: {e.OldState} → {e.NewState}");
};

client.Disconnected += (sender, e) =>
{
    Console.WriteLine($"Disconnected: {e.Reason} - {e.Message}");
};

// 연결 및 입장
var joinResult = await client.ConnectAsync(
    endpoint: "tcp://localhost:9000",
    roomToken: "eyJhbGciOiJIUzI1NiIs...");

if (!joinResult.Success)
{
    Console.WriteLine($"Join failed: {joinResult.ErrorMessage}");
    return;
}

Console.WriteLine($"Joined stage {joinResult.StageId}");

// Request-Reply 메시지 전송
var response = await client.RequestAsync<GetRoomInfoReq, GetRoomInfoRes>(
    new GetRoomInfoReq { StageId = client.StageId });

if (response.Success)
{
    Console.WriteLine($"Room: {response.Data!.RoomName}, Players: {response.Data.CurrentPlayers}");
}

// Fire-and-forget 메시지 전송
await client.SendAsync(new ChatMsg
{
    SenderId = client.AccountId,
    SenderName = "TestPlayer",
    Message = "Hello, World!"
});

// 방 퇴장
var leaveResult = await client.LeaveRoomAsync("Test completed");
```

### 5.2 메시지 핸들러 등록

```csharp
// 특정 메시지 타입에 대한 핸들러 등록
using var chatHandler = client.On<ChatMsg>(msg =>
{
    Console.WriteLine($"[{msg.SenderName}]: {msg.Message}");
});

using var playerJoinHandler = client.On<PlayerJoinedNotify>(msg =>
{
    Console.WriteLine($"Player joined: {msg.Player.PlayerName}");
});

// 비동기 핸들러도 지원
using var stateHandler = client.On<RoomStateMsg>(async msg =>
{
    Console.WriteLine($"State update: {msg.StateType}");
    await ProcessStateAsync(msg);
});

// 핸들러 해제 (using 블록 종료 시 자동 해제)
```

### 5.3 E2E 테스트 예시

```csharp
[TestClass]
public class GameRoomE2ETests
{
    private IPlayHouseClient _client1 = null!;
    private IPlayHouseClient _client2 = null!;

    [TestInitialize]
    public async Task Setup()
    {
        // HTTP API로 토큰 발급 (PlayHouse.Backend 사용)
        var roomClient = _serviceProvider.GetRequiredService<IRoomServerClient>();

        var response1 = await roomClient.GetOrCreateRoomAsync(new GetOrCreateRoomRequest
        {
            RoomType = "BattleStage",
            AccountId = 1001,
            UserInfo = new { Nickname = "Player1", Level = 10 }
        });

        var response2 = await roomClient.JoinRoomAsync(new JoinRoomRequest
        {
            StageId = response1.StageId,
            AccountId = 1002,
            UserInfo = new { Nickname = "Player2", Level = 15 }
        });

        // 클라이언트 생성 및 연결
        _client1 = new PlayHouseClient();
        _client2 = new PlayHouseClient();

        await _client1.ConnectAsync(response1.Endpoint!, response1.RoomToken!);
        await _client2.ConnectAsync(response2.Endpoint!, response2.RoomToken!);
    }

    [TestCleanup]
    public async Task Cleanup()
    {
        await _client1.DisposeAsync();
        await _client2.DisposeAsync();
    }

    [TestMethod]
    public async Task Chat_ShouldBroadcastToAllPlayers()
    {
        // Arrange
        var receivedMessages = new List<ChatMsg>();
        using var handler = _client2.On<ChatMsg>(msg => receivedMessages.Add(msg));

        // Act
        await _client1.SendAsync(new ChatMsg
        {
            SenderId = 1001,
            SenderName = "Player1",
            Message = "Hello from Player1!"
        });

        // Assert - 잠시 대기 후 검증
        await Task.Delay(100);
        Assert.AreEqual(1, receivedMessages.Count);
        Assert.AreEqual("Hello from Player1!", receivedMessages[0].Message);
    }

    [TestMethod]
    public async Task GetRoomInfo_ShouldReturnCurrentState()
    {
        // Act
        var response = await _client1.RequestAsync<GetRoomInfoReq, GetRoomInfoRes>(
            new GetRoomInfoReq { StageId = _client1.StageId });

        // Assert
        Assert.IsTrue(response.Success);
        Assert.AreEqual(2, response.Data!.CurrentPlayers);
    }

    [TestMethod]
    public async Task Reconnect_ShouldRestoreSession()
    {
        // Arrange
        var originalStageId = _client1.StageId;

        // Act - 강제 연결 끊기 후 재연결
        await _client1.DisconnectAsync("Test disconnect");
        var reconnected = await _client1.ReconnectAsync();

        // Assert
        Assert.IsTrue(reconnected);
        Assert.AreEqual(originalStageId, _client1.StageId);
    }
}
```

### 5.4 부하 테스트 예시

```csharp
[TestClass]
public class LoadTests
{
    [TestMethod]
    public async Task ConcurrentClients_ShouldHandleLoad()
    {
        const int clientCount = 100;
        var clients = new List<IPlayHouseClient>();
        var tasks = new List<Task>();

        try
        {
            // 100개 클라이언트 동시 연결
            for (int i = 0; i < clientCount; i++)
            {
                var accountId = 1000 + i;
                var token = await GetRoomTokenAsync(accountId);

                var client = new PlayHouseClient(new PlayHouseClientOptions
                {
                    AutoReconnect = false  // 부하 테스트에서는 자동 재연결 비활성화
                });

                clients.Add(client);
                tasks.Add(client.ConnectAsync("tcp://localhost:9000", token));
            }

            await Task.WhenAll(tasks);

            // 연결 검증
            var connectedCount = clients.Count(c => c.IsConnected);
            Assert.AreEqual(clientCount, connectedCount);

            // 동시 메시지 전송
            var sendTasks = clients.Select(c => c.SendAsync(new ChatMsg
            {
                Message = $"Hello from {c.AccountId}"
            }));

            await Task.WhenAll(sendTasks.Select(t => t.AsTask()));
        }
        finally
        {
            // 정리
            foreach (var client in clients)
            {
                await client.DisposeAsync();
            }
        }
    }
}
```

### 5.5 DI 통합

```csharp
// Program.cs (테스트 프로젝트)
var services = new ServiceCollection();

// PlayHouse Connector 등록
services.AddPlayHouseConnector(options =>
{
    options.RequestTimeout = TimeSpan.FromSeconds(10);
    options.AutoReconnect = true;
});

var serviceProvider = services.BuildServiceProvider();

// 사용
var client = serviceProvider.GetRequiredService<IPlayHouseClient>();
```

```csharp
// ServiceCollectionExtensions.cs
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddPlayHouseConnector(
        this IServiceCollection services,
        Action<PlayHouseClientOptions>? configure = null)
    {
        var options = new PlayHouseClientOptions();
        configure?.Invoke(options);

        services.AddSingleton(options);
        services.AddTransient<IPlayHouseClient, PlayHouseClient>();

        return services;
    }
}
```

## 6. 내부 구현

### 6.1 연결 관리

```csharp
internal class PlayHouseClient : IPlayHouseClient
{
    private readonly PlayHouseClientOptions _options;
    private readonly RequestTracker _requestTracker;
    private readonly Dictionary<string, List<Delegate>> _handlers = new();

    private IConnection? _connection;
    private string? _roomToken;
    private string? _endpoint;
    private CancellationTokenSource? _heartbeatCts;

    public ConnectionState State { get; private set; } = ConnectionState.Disconnected;
    public int StageId { get; private set; }
    public long AccountId { get; private set; }
    public bool IsConnected => State == ConnectionState.Connected;

    public PlayHouseClient(PlayHouseClientOptions? options = null)
    {
        _options = options ?? new PlayHouseClientOptions();
        _requestTracker = new RequestTracker(_options.RequestTimeout);
    }

    public async Task<JoinRoomResult> ConnectAsync(
        string endpoint,
        string roomToken,
        CancellationToken cancellationToken = default)
    {
        if (State != ConnectionState.Disconnected)
        {
            throw new InvalidOperationException($"Cannot connect in state: {State}");
        }

        _endpoint = endpoint;
        _roomToken = roomToken;

        try
        {
            SetState(ConnectionState.Connecting);

            // 연결 생성
            _connection = CreateConnection(endpoint);
            await _connection.ConnectAsync(cancellationToken);

            // 토큰으로 입장 요청
            var joinResult = await SendJoinRequestAsync(roomToken, cancellationToken);

            if (joinResult.Success)
            {
                StageId = joinResult.StageId;
                AccountId = ExtractAccountIdFromToken(roomToken);
                SetState(ConnectionState.Connected);

                // 하트비트 시작
                StartHeartbeat();

                // 메시지 수신 시작
                _ = StartReceiveLoopAsync();
            }
            else
            {
                SetState(ConnectionState.Disconnected);
                await _connection.DisconnectAsync();
            }

            return joinResult;
        }
        catch (Exception ex)
        {
            SetState(ConnectionState.Disconnected);
            OnError(ex, "Connect");
            throw;
        }
    }

    public async Task<Response<TResponse>> RequestAsync<TRequest, TResponse>(
        TRequest request,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
        where TRequest : IMessage
        where TResponse : IMessage, new()
    {
        EnsureConnected();

        var msgSeq = _requestTracker.NextSequence();
        var packet = new SimplePacket(request) { MsgSeq = msgSeq, StageId = StageId };

        // 응답 대기 등록
        var completionSource = _requestTracker.Register<TResponse>(msgSeq);

        try
        {
            // 패킷 전송
            await _connection!.SendAsync(PacketEncoder.Encode(packet), cancellationToken);

            // 응답 대기
            var effectiveTimeout = timeout ?? _options.RequestTimeout;
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(effectiveTimeout);

            var response = await completionSource.Task.WaitAsync(cts.Token);
            return Response<TResponse>.Ok(response);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            _requestTracker.Cancel(msgSeq);
            return Response<TResponse>.Fail(3, "Request timeout");
        }
        catch (Exception ex)
        {
            _requestTracker.Cancel(msgSeq);
            return Response<TResponse>.Fail(1, ex.Message);
        }
    }

    public async ValueTask SendAsync<T>(T message) where T : IMessage
    {
        EnsureConnected();

        var packet = new SimplePacket(message) { MsgSeq = 0, StageId = StageId };
        await _connection!.SendAsync(PacketEncoder.Encode(packet));
    }

    public IDisposable On<T>(Action<T> handler) where T : IMessage, new()
    {
        var msgId = new T().Descriptor.Name;

        if (!_handlers.TryGetValue(msgId, out var list))
        {
            list = new List<Delegate>();
            _handlers[msgId] = list;
        }

        list.Add(handler);

        return new HandlerDisposable(() => list.Remove(handler));
    }

    private async Task StartReceiveLoopAsync()
    {
        try
        {
            while (IsConnected && _connection != null)
            {
                var data = await _connection.ReceiveAsync();
                if (data == null) break;

                var packet = PacketDecoder.Decode(data);
                await HandlePacketAsync(packet);
            }
        }
        catch (Exception ex) when (IsConnected)
        {
            OnError(ex, "Receive");
            await HandleDisconnectAsync(DisconnectReason.NetworkError);
        }
    }

    private async Task HandlePacketAsync(IPacket packet)
    {
        // Request-Reply 응답 처리
        if (packet.MsgSeq > 0 && _requestTracker.TryComplete(packet))
        {
            return;
        }

        // Push 메시지 핸들러 호출
        if (_handlers.TryGetValue(packet.MsgId, out var handlers))
        {
            foreach (var handler in handlers.ToList())
            {
                try
                {
                    // 메시지 파싱 및 핸들러 호출
                    var message = ParseMessage(packet);
                    if (handler is Action<IMessage> syncHandler)
                    {
                        syncHandler(message);
                    }
                    else if (handler is Func<IMessage, Task> asyncHandler)
                    {
                        await asyncHandler(message);
                    }
                }
                catch (Exception ex)
                {
                    OnError(ex, $"Handler:{packet.MsgId}");
                }
            }
        }

        // 일반 이벤트 발생
        var message = ParseMessage(packet);
        MessageReceived?.Invoke(this, new MessageReceivedEventArgs
        {
            MsgId = packet.MsgId,
            Message = message,
            StageId = packet.StageId
        });
    }

    // ... 기타 구현
}
```

### 6.2 Request 추적기

```csharp
internal class RequestTracker
{
    private readonly ConcurrentDictionary<ushort, TaskCompletionSource<IPacket>> _pending = new();
    private readonly TimeSpan _defaultTimeout;
    private ushort _sequence;

    public RequestTracker(TimeSpan defaultTimeout)
    {
        _defaultTimeout = defaultTimeout;
    }

    public ushort NextSequence()
    {
        return Interlocked.Increment(ref _sequence) == 0
            ? Interlocked.Increment(ref _sequence)  // 0 스킵
            : _sequence;
    }

    public TaskCompletionSource<T> Register<T>(ushort msgSeq) where T : IMessage, new()
    {
        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var wrapper = new TaskCompletionSource<IPacket>();

        // 래퍼에서 실제 타입으로 변환
        wrapper.Task.ContinueWith(t =>
        {
            if (t.IsCompletedSuccessfully)
            {
                var packet = t.Result;
                var message = packet.Parse<T>();
                tcs.SetResult(message);
            }
            else if (t.IsFaulted)
            {
                tcs.SetException(t.Exception!.InnerExceptions);
            }
            else
            {
                tcs.SetCanceled();
            }
        });

        _pending[msgSeq] = wrapper;
        return tcs;
    }

    public bool TryComplete(IPacket packet)
    {
        if (_pending.TryRemove(packet.MsgSeq, out var tcs))
        {
            tcs.SetResult(packet);
            return true;
        }
        return false;
    }

    public void Cancel(ushort msgSeq)
    {
        if (_pending.TryRemove(msgSeq, out var tcs))
        {
            tcs.TrySetCanceled();
        }
    }
}
```

## 7. 테스트 유틸리티

### 7.1 테스트 헬퍼

```csharp
/// <summary>
/// E2E 테스트용 헬퍼 클래스.
/// </summary>
public static class PlayHouseTestHelper
{
    /// <summary>
    /// 지정된 메시지를 수신할 때까지 대기합니다.
    /// </summary>
    public static async Task<T> WaitForMessageAsync<T>(
        this IPlayHouseClient client,
        TimeSpan timeout) where T : IMessage, new()
    {
        var tcs = new TaskCompletionSource<T>();

        using var handler = client.On<T>(msg => tcs.TrySetResult(msg));

        using var cts = new CancellationTokenSource(timeout);
        cts.Token.Register(() => tcs.TrySetCanceled());

        return await tcs.Task;
    }

    /// <summary>
    /// 여러 클라이언트를 동시에 연결합니다.
    /// </summary>
    public static async Task<IPlayHouseClient[]> ConnectClientsAsync(
        IRoomServerClient roomClient,
        string roomType,
        int count,
        long startAccountId = 1000)
    {
        var clients = new IPlayHouseClient[count];
        var tasks = new List<Task>();

        // 첫 번째 클라이언트로 방 생성
        var firstResponse = await roomClient.GetOrCreateRoomAsync(new GetOrCreateRoomRequest
        {
            RoomType = roomType,
            AccountId = startAccountId,
            UserInfo = new { Nickname = $"Player{startAccountId}" }
        });

        clients[0] = new PlayHouseClient();
        await clients[0].ConnectAsync(firstResponse.Endpoint!, firstResponse.RoomToken!);

        // 나머지 클라이언트 연결
        for (int i = 1; i < count; i++)
        {
            var accountId = startAccountId + i;
            var response = await roomClient.JoinRoomAsync(new JoinRoomRequest
            {
                StageId = firstResponse.StageId,
                AccountId = accountId,
                UserInfo = new { Nickname = $"Player{accountId}" }
            });

            clients[i] = new PlayHouseClient();
            tasks.Add(clients[i].ConnectAsync(response.Endpoint!, response.RoomToken!));
        }

        await Task.WhenAll(tasks);
        return clients;
    }

    /// <summary>
    /// 모든 클라이언트를 정리합니다.
    /// </summary>
    public static async Task DisposeAllAsync(params IPlayHouseClient[] clients)
    {
        var tasks = clients.Select(c => c.DisposeAsync().AsTask());
        await Task.WhenAll(tasks);
    }
}
```

### 7.2 Mock 클라이언트

```csharp
/// <summary>
/// 단위 테스트용 Mock 클라이언트.
/// </summary>
public class MockPlayHouseClient : IPlayHouseClient
{
    public ConnectionState State { get; set; } = ConnectionState.Disconnected;
    public int StageId { get; set; }
    public long AccountId { get; set; }
    public bool IsConnected => State == ConnectionState.Connected;

    public List<IMessage> SentMessages { get; } = new();
    public Queue<object> ResponseQueue { get; } = new();

    public event EventHandler<ConnectionStateChangedEventArgs>? ConnectionStateChanged;
    public event EventHandler<MessageReceivedEventArgs>? MessageReceived;
    public event EventHandler<ClientErrorEventArgs>? ErrorOccurred;
    public event EventHandler<DisconnectedEventArgs>? Disconnected;

    public Task<JoinRoomResult> ConnectAsync(
        string endpoint, string roomToken, CancellationToken cancellationToken = default)
    {
        State = ConnectionState.Connected;
        StageId = 12345;
        AccountId = 1001;
        return Task.FromResult(new JoinRoomResult { Success = true, StageId = StageId });
    }

    public Task DisconnectAsync(string? reason = null)
    {
        State = ConnectionState.Disconnected;
        return Task.CompletedTask;
    }

    public Task<bool> ReconnectAsync(CancellationToken cancellationToken = default)
    {
        State = ConnectionState.Connected;
        return Task.FromResult(true);
    }

    public Task<Response<TResponse>> RequestAsync<TRequest, TResponse>(
        TRequest request, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
        where TRequest : IMessage
        where TResponse : IMessage, new()
    {
        SentMessages.Add(request);

        if (ResponseQueue.Count > 0 && ResponseQueue.Dequeue() is TResponse response)
        {
            return Task.FromResult(Response<TResponse>.Ok(response));
        }

        return Task.FromResult(Response<TResponse>.Fail(1, "No response queued"));
    }

    public ValueTask SendAsync<T>(T message) where T : IMessage
    {
        SentMessages.Add(message);
        return ValueTask.CompletedTask;
    }

    public Task<LeaveRoomResult> LeaveRoomAsync(string? reason = null)
    {
        State = ConnectionState.Disconnected;
        return Task.FromResult(new LeaveRoomResult { Success = true });
    }

    public IDisposable On<T>(Action<T> handler) where T : IMessage, new()
        => new HandlerDisposable(() => { });

    public IDisposable On<T>(Func<T, Task> handler) where T : IMessage, new()
        => new HandlerDisposable(() => { });

    public ValueTask DisposeAsync()
    {
        State = ConnectionState.Disconnected;
        return ValueTask.CompletedTask;
    }

    // 테스트용 메서드
    public void SimulateMessageReceived(IMessage message)
    {
        MessageReceived?.Invoke(this, new MessageReceivedEventArgs
        {
            MsgId = message.Descriptor.Name,
            Message = message,
            StageId = StageId
        });
    }
}
```

## 8. 프로젝트 파일

```xml
<!-- PlayHouse.Connector.csproj -->
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFrameworks>net8.0;net9.0</TargetFrameworks>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <LangVersion>latest</LangVersion>

    <PackageId>PlayHouse.Connector</PackageId>
    <Version>1.0.0</Version>
    <Authors>PlayHouse Team</Authors>
    <Description>PlayHouse-NET client connector for E2E and integration testing</Description>
    <PackageTags>playhouse;game-server;client;testing;e2e</PackageTags>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Google.Protobuf" Version="3.25.1" />
    <PackageReference Include="Microsoft.Extensions.DependencyInjection.Abstractions" Version="8.0.0" />
    <PackageReference Include="Microsoft.Extensions.Logging.Abstractions" Version="8.0.0" />
    <PackageReference Include="System.IO.Pipelines" Version="8.0.0" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\PlayHouse.Abstractions\PlayHouse.Abstractions.csproj" />
  </ItemGroup>

</Project>
```

## 9. 다음 단계

- 프로젝트 생성: `PlayHouse.Connector` 폴더 및 파일 생성
- 단위 테스트: `PlayHouse.Connector.Tests` 프로젝트
- E2E 테스트: `PlayHouse.E2E.Tests` 프로젝트
- 별도 클라이언트 프로젝트 (추후):
  - `playhouse-connector-unity`
  - `playhouse-connector-unreal`
  - `playhouse-connector-ts`
