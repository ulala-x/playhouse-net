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

## 5. 내부 구현

### 5.1 연결 관리

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

### 5.2 Request 추적기

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

## 6. 프로젝트 파일

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

## 7. 테스트 전략

### 7.1 테스트 피라미드

PlayHouse.Connector는 테스트 친화적인 클라이언트 라이브러리로, 다음과 같은 테스트 피라미드를 따릅니다:

```
        E2E Tests (70%)
       /               \
      /  Integration    \
     /    Tests (20%)    \
    /___________________\
    Unit Tests (10%)
```

#### 비율 설정 이유

**E2E 테스트 (70%)**
- **이유**: Connector의 핵심 가치는 실제 서버와의 통신 검증
- **목적**: 네트워크 연결, 프로토콜 호환성, 메시지 송수신, 상태 관리 검증
- **범위**: 실제 PlayHouse Room Server와 연결하여 전체 시나리오 테스트

**통합 테스트 (20%)**
- **이유**: 내부 컴포넌트 간 상호작용 검증 필요
- **목적**: Connection, PacketEncoder/Decoder, RequestTracker 간 통합 동작 확인
- **범위**: Mock 서버 또는 로컬 서버를 사용한 컴포넌트 조합 테스트

**유닛 테스트 (10%)**
- **이유**: 네트워크 없이 검증 가능한 순수 로직만 대상
- **목적**: Protobuf 직렬화, LZ4 압축, 패킷 조립 등 독립적 검증
- **범위**: 네트워크 의존성이 없는 알고리즘 및 데이터 변환 로직

## 8. 다음 단계

- 프로젝트 생성: `PlayHouse.Connector` 폴더 및 파일 생성
- 단위 테스트: `PlayHouse.Connector.Tests` 프로젝트
- E2E 테스트: `PlayHouse.E2E.Tests` 프로젝트
- 별도 클라이언트 프로젝트 (추후):
  - `playhouse-connector-unity`
  - `playhouse-connector-unreal`
  - `playhouse-connector-ts`
