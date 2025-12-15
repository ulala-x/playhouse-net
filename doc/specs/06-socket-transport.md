# PlayHouse-NET 소켓 전송 계층

## 1. 개요

PlayHouse-NET은 **.NET 기본 소켓 라이브러리**와 **System.IO.Pipelines**를 사용하여 클라이언트와의 실시간 통신을 처리합니다.

### 1.1 기존 playhouse-net 대비 변경점

| 항목 | 기존 (playhouse-net) | 신규 (playhouse-net) |
|------|----------------------|----------------------|
| 소켓 라이브러리 | 외부 라이브러리 | **.NET 네이티브** (System.Net.Sockets) |
| 버퍼 관리 | RingBuffer (직접 구현) | **System.IO.Pipelines** (고성능 파이프라인) |
| WebSocket | 외부 라이브러리 | System.Net.WebSockets |
| TLS/SSL | 외부 라이브러리 | System.Net.Security.SslStream |
| 의존성 | 외부 의존성 필요 | 외부 의존성 없음 |
| .NET 버전 | .NET 8.0 | **.NET 8.0 / 9.0 / 10.0** 멀티 타겟 |

**변경 이유**:
- 외부 라이브러리 의존성 제거로 유지보수 용이
- **System.IO.Pipelines**로 Zero-Copy 고성능 버퍼 관리
- .NET 8.0+ 네이티브 소켓 성능이 충분히 우수
- 단일 서버 구조로 단순화

### 1.2 지원 프로토콜

- **TCP**: 고성능, 낮은 지연 (System.Net.Sockets.Socket)
- **WebSocket**: 웹 브라우저 지원 (System.Net.WebSockets)
- **HTTPS/WSS**: TLS 암호화 (System.Net.Security.SslStream)

### 1.3 핵심 특징

- **.NET 네이티브**: 외부 라이브러리 불필요 (NetCoreServer 제거)
- **System.IO.Pipelines**: Zero-Copy 고성능 버퍼 관리
- **비동기 I/O**: async/await 기반
- **멀티 타겟**: .NET 8.0 / 9.0 / 10.0 지원
- **크로스 플랫폼**: Windows, Linux, macOS

### 1.4 System.IO.Pipelines 도입 이점

```
┌─────────────────────────────────────────────────────────────┐
│              기존 방식 (RingBuffer)                          │
├─────────────────────────────────────────────────────────────┤
│  Socket.Receive() → byte[] 복사 → RingBuffer → Parse        │
│                      ↑ 메모리 복사 발생                       │
└─────────────────────────────────────────────────────────────┘

┌─────────────────────────────────────────────────────────────┐
│              신규 방식 (System.IO.Pipelines)                 │
├─────────────────────────────────────────────────────────────┤
│  Socket → PipeWriter → Pipe → PipeReader → Parse            │
│           ↑ Zero-Copy, 백프레셔 지원                         │
└─────────────────────────────────────────────────────────────┘
```

**장점**:
- **Zero-Copy**: 불필요한 메모리 복사 제거
- **백프레셔 (Backpressure)**: 소비자가 느릴 때 생산자 자동 조절
- **메모리 풀링**: ArrayPool 기반 자동 메모리 관리
- **ReadOnlySequence**: 비연속 메모리 효율적 처리

## 2. 전체 아키텍처

### 2.1 소켓 계층 구조

```
┌─────────────────────────────────────────────────────────────┐
│                     Transport Layer                          │
├─────────────────────────────────────────────────────────────┤
│                                                              │
│  ┌──────────────┐  ┌──────────────┐  ┌──────────────┐       │
│  │ TcpTransport │  │WsTransport   │  │TlsTransport  │       │
│  │              │  │              │  │              │       │
│  │ - Socket     │  │ - WebSocket  │  │ - SslStream  │       │
│  │ - Pipelines  │  │ - Handshake  │  │ - Certificate│       │
│  └──────┬───────┘  └──────┬───────┘  └──────┬───────┘       │
│         │                 │                  │               │
│         └─────────────────┼──────────────────┘               │
│                           │                                  │
│              ┌────────────▼────────────┐                     │
│              │   System.IO.Pipelines   │                     │
│              │   - PipeReader          │                     │
│              │   - PipeWriter          │                     │
│              │   - Zero-Copy Buffer    │                     │
│              └────────────┬────────────┘                     │
│                           │                                  │
│                  ┌────────▼────────┐                         │
│                  │ Session Manager │                         │
│                  │ - Connection    │                         │
│                  │ - Packet Parser │                         │
│                  └────────┬────────┘                         │
│                           │                                  │
└───────────────────────────┼──────────────────────────────────┘
                            │
                   ┌────────▼────────┐
                   │  Core Engine    │
                   │  - Dispatcher   │
                   └─────────────────┘
```

### 2.2 Pipelines 데이터 흐름

```
[수신 흐름]
Socket.ReceiveAsync()
    │
    ▼
PipeWriter.GetMemory()     ← 메모리 풀에서 버퍼 획득
    │
    ▼
PipeWriter.Advance(bytes)  ← 수신된 바이트 수 기록
    │
    ▼
PipeWriter.FlushAsync()    ← 데이터 커밋
    │
    ▼
PipeReader.ReadAsync()     ← 파서가 데이터 읽기
    │
    ▼
Parse Packet               ← ReadOnlySequence로 파싱
    │
    ▼
PipeReader.AdvanceTo()     ← 소비된 위치 기록

[송신 흐름]
Packet Serialize
    │
    ▼
PipeWriter.WriteAsync()    ← 직접 파이프에 쓰기
    │
    ▼
Socket.SendAsync()         ← 소켓으로 전송
```

### 2.3 연결 처리 흐름

```
[클라이언트 연결 흐름]

Client Connect
    │
    ▼
Accept Connection (Listener)
    │
    ▼
Create Session
    │
    ▼
Start Receive Loop (async)
    │
    ├─────▶ Receive Data
    │       │
    │       ▼
    │    Parse Packet
    │       │
    │       ▼
    │    Validate
    │       │
    │       ▼
    │    Dispatch to Core Engine
    │       │
    │       └──────┐
    │              │
    └──────────────┘ (반복)
```

## 3. TCP 소켓 구현 (System.IO.Pipelines 기반)

### 3.1 .NET Core 통합 설계 원칙

PlayHouse-NET은 .NET Core/ASP.NET Core와 최대한 통합되도록 설계됩니다:

- **Microsoft.Extensions.Hosting**: IHostedService 기반 서버 라이프사이클
- **Microsoft.Extensions.DependencyInjection**: 표준 DI 컨테이너 사용
- **Microsoft.Extensions.Logging**: ILogger 기반 로깅
- **Microsoft.Extensions.Options**: 설정 패턴 사용
- **System.IO.Pipelines**: 고성능 버퍼 관리

```csharp
// Program.cs - ASP.NET Core 통합 예시
var builder = WebApplication.CreateBuilder(args);

// PlayHouse 서비스 등록
builder.Services.AddPlayHouse(options =>
{
    options.TcpPort = 7777;
    options.WebSocketPort = 7778;
    options.MaxConnections = 10000;
});

// HTTP API 컨트롤러
builder.Services.AddControllers();

var app = builder.Build();
app.MapControllers();
app.Run();
```

### 3.2 TCP 서버 (IHostedService 기반)

```csharp
public class TcpSessionNetwork : IHostedService, ISessionNetwork
{
    private readonly Socket _listener;
    private readonly ConcurrentDictionary<long, TcpSession> _sessions = new();
    private readonly ISessionDispatcher _dispatcher;
    private readonly ILogger<TcpSessionNetwork> _logger;
    private readonly IOptions<SessionOption> _options;
    private CancellationTokenSource? _cts;

    public TcpSessionNetwork(
        ISessionDispatcher dispatcher,
        ILogger<TcpSessionNetwork> logger,
        IOptions<SessionOption> options)
    {
        _dispatcher = dispatcher;
        _logger = logger;
        _options = options;

        _listener = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        _listener.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        _listener.NoDelay = true;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        var endpoint = new IPEndPoint(IPAddress.Any, _options.Value.Port);
        _listener.Bind(endpoint);
        _listener.Listen(_options.Value.Backlog);

        _logger.LogInformation("TCP Server listening on port {Port}", _options.Value.Port);

        _ = AcceptLoopAsync(_cts.Token);
    }

    private async Task AcceptLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var clientSocket = await _listener.AcceptAsync(cancellationToken);

                var sessionId = GenerateSessionId();
                var session = new TcpSession(
                    sessionId,
                    clientSocket,
                    _dispatcher,
                    _options.Value,
                    _logger
                );

                _sessions[sessionId] = session;
                _ = session.StartAsync(cancellationToken);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to accept connection");
            }
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _cts?.Cancel();

        // 모든 세션 종료
        foreach (var session in _sessions.Values)
        {
            await session.CloseAsync();
        }

        _listener.Close();
        _logger.LogInformation("TCP Server stopped");
    }
}
```

### 3.3 TCP 세션 (System.IO.Pipelines 기반)

```csharp
public class TcpSession : ISession
{
    private readonly long _sessionId;
    private readonly Socket _socket;
    private readonly ISessionDispatcher _dispatcher;
    private readonly ILogger _logger;
    private readonly SessionOption _option;

    // System.IO.Pipelines
    private readonly Pipe _receivePipe;
    private readonly Pipe _sendPipe;

    public TcpSession(
        long sessionId,
        Socket socket,
        ISessionDispatcher dispatcher,
        SessionOption option,
        ILogger logger)
    {
        _sessionId = sessionId;
        _socket = socket;
        _dispatcher = dispatcher;
        _option = option;
        _logger = logger;

        // Pipe 설정 (백프레셔 및 메모리 풀링)
        var pipeOptions = new PipeOptions(
            pool: MemoryPool<byte>.Shared,
            readerScheduler: PipeScheduler.ThreadPool,
            writerScheduler: PipeScheduler.ThreadPool,
            pauseWriterThreshold: option.PauseWriterThreshold,    // 64KB
            resumeWriterThreshold: option.ResumeWriterThreshold,  // 32KB
            useSynchronizationContext: false
        );

        _receivePipe = new Pipe(pipeOptions);
        _sendPipe = new Pipe(pipeOptions);
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _dispatcher.OnConnect(_sessionId);

            // 수신/파싱/송신을 병렬로 실행
            var receiveTask = ReceiveLoopAsync(cancellationToken);
            var parseTask = ParseLoopAsync(cancellationToken);
            var sendTask = SendLoopAsync(cancellationToken);

            await Task.WhenAll(receiveTask, parseTask, sendTask);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Session {SessionId} error", _sessionId);
        }
        finally
        {
            await _dispatcher.OnDisconnect(_sessionId);
            await CloseAsync();
        }
    }

    // 소켓에서 데이터 수신 → PipeWriter에 쓰기
    private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
    {
        var writer = _receivePipe.Writer;

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                // 메모리 풀에서 버퍼 획득 (Zero-Copy)
                var memory = writer.GetMemory(_option.MinBufferSize);

                var bytesRead = await _socket.ReceiveAsync(memory, SocketFlags.None, cancellationToken);

                if (bytesRead == 0) break; // 연결 종료

                // 수신된 바이트 수만큼 전진
                writer.Advance(bytesRead);

                // 데이터 커밋 (파서에게 알림)
                var result = await writer.FlushAsync(cancellationToken);

                if (result.IsCompleted) break;
            }
        }
        catch (SocketException) { }
        finally
        {
            await writer.CompleteAsync();
        }
    }

    // PipeReader에서 데이터 읽기 → 패킷 파싱
    private async Task ParseLoopAsync(CancellationToken cancellationToken)
    {
        var reader = _receivePipe.Reader;

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var result = await reader.ReadAsync(cancellationToken);
                var buffer = result.Buffer;

                // 패킷 파싱 (ReadOnlySequence 사용)
                while (TryParsePacket(ref buffer, out var packet))
                {
                    await _dispatcher.OnReceive(_sessionId, packet);
                }

                // 소비된 위치 기록
                reader.AdvanceTo(buffer.Start, buffer.End);

                if (result.IsCompleted) break;
            }
        }
        finally
        {
            await reader.CompleteAsync();
        }
    }

    // 송신 파이프에서 데이터 읽기 → 소켓으로 전송
    private async Task SendLoopAsync(CancellationToken cancellationToken)
    {
        var reader = _sendPipe.Reader;

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var result = await reader.ReadAsync(cancellationToken);
                var buffer = result.Buffer;

                foreach (var segment in buffer)
                {
                    await _socket.SendAsync(segment, SocketFlags.None, cancellationToken);
                }

                reader.AdvanceTo(buffer.End);

                if (result.IsCompleted) break;
            }
        }
        finally
        {
            await reader.CompleteAsync();
        }
    }

    // ReadOnlySequence 기반 패킷 파싱
    private bool TryParsePacket(ref ReadOnlySequence<byte> buffer, out IPacket? packet)
    {
        packet = null;

        // 최소 헤더 크기 확인 (4 bytes: packet length)
        if (buffer.Length < 4) return false;

        // 패킷 길이 읽기
        Span<byte> lengthSpan = stackalloc byte[4];
        buffer.Slice(0, 4).CopyTo(lengthSpan);
        var packetLength = BinaryPrimitives.ReadInt32LittleEndian(lengthSpan);

        // 크기 검증
        if (packetLength > _option.MaxPacketSize)
        {
            throw new PacketException($"Packet size exceeds limit: {packetLength}");
        }

        // 전체 패킷 도착 확인
        if (buffer.Length < 4 + packetLength) return false;

        // 패킷 데이터 추출
        var packetData = buffer.Slice(4, packetLength);
        packet = DeserializePacket(packetData);

        // 버퍼에서 소비된 부분 제거
        buffer = buffer.Slice(4 + packetLength);

        return true;
    }

    // ReadOnlySequence에서 직접 역직렬화 (Zero-Copy)
    private IPacket DeserializePacket(ReadOnlySequence<byte> data)
    {
        var reader = new SequenceReader<byte>(data);

        // MsgId
        reader.TryRead(out byte msgIdLen);
        Span<byte> msgIdBytes = stackalloc byte[msgIdLen];
        reader.TryCopyTo(msgIdBytes);
        reader.Advance(msgIdLen);
        var msgId = Encoding.UTF8.GetString(msgIdBytes);

        // MsgSeq
        reader.TryReadLittleEndian(out short msgSeq);

        // StageId
        reader.TryReadLittleEndian(out long stageId);

        // ErrorCode
        reader.TryReadLittleEndian(out short errorCode);

        // OriginalSize
        reader.TryReadLittleEndian(out int originalSize);

        // Body
        var remaining = data.Slice(reader.Consumed);
        byte[] bodyData;

        if (originalSize > 0)
        {
            // 압축된 데이터 해제
            bodyData = LZ4.Decompress(remaining.ToArray(), originalSize);
        }
        else
        {
            bodyData = remaining.ToArray();
        }

        return new Packet(msgId, (ushort)msgSeq, stageId, (ushort)errorCode, new BinaryPayload(bodyData));
    }

    public async ValueTask SendAsync(IPacket packet)
    {
        var writer = _sendPipe.Writer;

        // 패킷 직렬화 후 파이프에 쓰기
        var serialized = SerializePacket(packet);
        await writer.WriteAsync(serialized);
    }

    public async Task CloseAsync()
    {
        try
        {
            _receivePipe.Writer.Complete();
            _receivePipe.Reader.Complete();
            _sendPipe.Writer.Complete();
            _sendPipe.Reader.Complete();

            _socket.Shutdown(SocketShutdown.Both);
            _socket.Close();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error closing session {SessionId}", _sessionId);
        }
    }

    public string GetRemoteIp()
    {
        var endpoint = _socket.RemoteEndPoint as IPEndPoint;
        return endpoint?.Address.ToString() ?? "unknown";
    }
}
```

### 3.4 DI 서비스 등록

```csharp
public static class PlayHouseServiceExtensions
{
    public static IServiceCollection AddPlayHouse(
        this IServiceCollection services,
        Action<SessionOption> configure)
    {
        // 설정
        services.Configure(configure);

        // 핵심 서비스
        services.AddSingleton<ISessionDispatcher, SessionDispatcher>();
        services.AddSingleton<SessionManager>();
        services.AddSingleton<StageManager>();
        services.AddSingleton<TimerManager>();

        // 호스티드 서비스 (백그라운드 실행)
        services.AddHostedService<TcpSessionNetwork>();
        services.AddHostedService<WebSocketSessionNetwork>();

        // 로깅
        services.AddLogging();

        return services;
    }
}
```

### 3.5 설정 옵션

```csharp
public class SessionOption
{
    public int Port { get; set; } = 7777;
    public int Backlog { get; set; } = 1000;
    public int MaxConnections { get; set; } = 10000;
    public int MaxPacketSize { get; set; } = 2 * 1024 * 1024; // 2MB
    public int MinBufferSize { get; set; } = 4096;

    // Pipelines 백프레셔 설정
    public int PauseWriterThreshold { get; set; } = 64 * 1024;  // 64KB
    public int ResumeWriterThreshold { get; set; } = 32 * 1024; // 32KB

    // Heartbeat
    public int HeartbeatIntervalSeconds { get; set; } = 30;
    public int HeartbeatTimeoutSeconds { get; set; } = 90;
}
```

## 4. WebSocket 구현 (ASP.NET Core 통합)

### 4.1 WebSocket 미들웨어 설정

ASP.NET Core의 네이티브 WebSocket 미들웨어를 사용합니다:

```csharp
// Program.cs - WebSocket 미들웨어 등록
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddPlayHouse(options => { /* ... */ });

var app = builder.Build();

// WebSocket 미들웨어 활성화
app.UseWebSockets(new WebSocketOptions
{
    KeepAliveInterval = TimeSpan.FromSeconds(30),
    ReceiveBufferSize = 4096
});

// WebSocket 엔드포인트
app.Map("/ws", async context =>
{
    if (context.WebSockets.IsWebSocketRequest)
    {
        var webSocket = await context.WebSockets.AcceptWebSocketAsync();
        var handler = context.RequestServices.GetRequiredService<WebSocketHandler>();
        await handler.HandleAsync(webSocket, context.RequestAborted);
    }
    else
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
    }
});

app.Run();
```

### 4.2 WebSocket 핸들러 (Pipelines 기반)

```csharp
public class WebSocketHandler
{
    private readonly ISessionDispatcher _dispatcher;
    private readonly SessionManager _sessionManager;
    private readonly ILogger<WebSocketHandler> _logger;
    private readonly IOptions<SessionOption> _options;

    public WebSocketHandler(
        ISessionDispatcher dispatcher,
        SessionManager sessionManager,
        ILogger<WebSocketHandler> logger,
        IOptions<SessionOption> options)
    {
        _dispatcher = dispatcher;
        _sessionManager = sessionManager;
        _logger = logger;
        _options = options;
    }

    public async Task HandleAsync(WebSocket webSocket, CancellationToken cancellationToken)
    {
        var sessionId = _sessionManager.GenerateSessionId();
        var session = new WebSocketSession(
            sessionId,
            webSocket,
            _dispatcher,
            _options.Value,
            _logger
        );

        _sessionManager.AddSession(sessionId, session);

        try
        {
            await session.StartAsync(cancellationToken);
        }
        finally
        {
            _sessionManager.RemoveSession(sessionId);
        }
    }
}
```

### 4.3 WebSocket 세션 (Pipelines 기반)

```csharp
public class WebSocketSession : ISession
{
    private readonly long _sessionId;
    private readonly WebSocket _webSocket;
    private readonly ISessionDispatcher _dispatcher;
    private readonly ILogger _logger;
    private readonly SessionOption _option;

    // System.IO.Pipelines
    private readonly Pipe _receivePipe;

    public WebSocketSession(
        long sessionId,
        WebSocket webSocket,
        ISessionDispatcher dispatcher,
        SessionOption option,
        ILogger logger)
    {
        _sessionId = sessionId;
        _webSocket = webSocket;
        _dispatcher = dispatcher;
        _option = option;
        _logger = logger;

        var pipeOptions = new PipeOptions(
            pool: MemoryPool<byte>.Shared,
            pauseWriterThreshold: option.PauseWriterThreshold,
            resumeWriterThreshold: option.ResumeWriterThreshold
        );

        _receivePipe = new Pipe(pipeOptions);
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _dispatcher.OnConnect(_sessionId);

            var receiveTask = ReceiveLoopAsync(cancellationToken);
            var parseTask = ParseLoopAsync(cancellationToken);

            await Task.WhenAll(receiveTask, parseTask);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "WebSocket session {SessionId} error", _sessionId);
        }
        finally
        {
            await _dispatcher.OnDisconnect(_sessionId);
            await CloseAsync();
        }
    }

    private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
    {
        var writer = _receivePipe.Writer;

        try
        {
            while (_webSocket.State == WebSocketState.Open &&
                   !cancellationToken.IsCancellationRequested)
            {
                var memory = writer.GetMemory(_option.MinBufferSize);

                var result = await _webSocket.ReceiveAsync(memory, cancellationToken);

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    break;
                }

                if (result.MessageType == WebSocketMessageType.Binary)
                {
                    writer.Advance(result.Count);
                    await writer.FlushAsync(cancellationToken);
                }
            }
        }
        finally
        {
            await writer.CompleteAsync();
        }
    }

    private async Task ParseLoopAsync(CancellationToken cancellationToken)
    {
        var reader = _receivePipe.Reader;

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var result = await reader.ReadAsync(cancellationToken);
                var buffer = result.Buffer;

                while (TryParsePacket(ref buffer, out var packet))
                {
                    await _dispatcher.OnReceive(_sessionId, packet);
                }

                reader.AdvanceTo(buffer.Start, buffer.End);

                if (result.IsCompleted) break;
            }
        }
        finally
        {
            await reader.CompleteAsync();
        }
    }

    private bool TryParsePacket(ref ReadOnlySequence<byte> buffer, out IPacket? packet)
    {
        // TCP 세션과 동일한 파싱 로직
        // ... (3.3절 참조)
    }

    public async ValueTask SendAsync(ReadOnlyMemory<byte> data)
    {
        try
        {
            await _webSocket.SendAsync(
                data,
                WebSocketMessageType.Binary,
                endOfMessage: true,
                CancellationToken.None
            );
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send WebSocket message");
        }
    }

    public async Task CloseAsync()
    {
        try
        {
            _receivePipe.Writer.Complete();
            _receivePipe.Reader.Complete();

            if (_webSocket.State == WebSocketState.Open)
            {
                await _webSocket.CloseAsync(
                    WebSocketCloseStatus.NormalClosure,
                    "Server closed",
                    CancellationToken.None
                );
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error closing WebSocket session {SessionId}", _sessionId);
        }
    }
}
```

## 5. TLS/SSL 지원 (Kestrel 통합)

### 5.1 Kestrel 기반 TLS 설정

ASP.NET Core의 Kestrel을 사용하여 TLS를 처리합니다. 별도 구현 없이 설정만으로 가능합니다:

```csharp
// Program.cs - Kestrel TLS 설정
var builder = WebApplication.CreateBuilder(args);

builder.WebHost.ConfigureKestrel(options =>
{
    // HTTP API (평문)
    options.ListenAnyIP(5000);

    // HTTPS API (TLS)
    options.ListenAnyIP(5001, listenOptions =>
    {
        listenOptions.UseHttps("certificate.pfx", "password");
    });

    // TCP 게임 서버 (평문)
    options.ListenAnyIP(7777, listenOptions =>
    {
        listenOptions.UseConnectionHandler<TcpConnectionHandler>();
    });

    // TCP 게임 서버 (TLS)
    options.ListenAnyIP(7778, listenOptions =>
    {
        listenOptions.UseHttps("certificate.pfx", "password");
        listenOptions.UseConnectionHandler<TcpConnectionHandler>();
    });
});
```

### 5.2 Kestrel ConnectionHandler (TCP over TLS)

```csharp
public class TcpConnectionHandler : ConnectionHandler
{
    private readonly ISessionDispatcher _dispatcher;
    private readonly SessionManager _sessionManager;
    private readonly ILogger<TcpConnectionHandler> _logger;
    private readonly IOptions<SessionOption> _options;

    public TcpConnectionHandler(
        ISessionDispatcher dispatcher,
        SessionManager sessionManager,
        ILogger<TcpConnectionHandler> logger,
        IOptions<SessionOption> options)
    {
        _dispatcher = dispatcher;
        _sessionManager = sessionManager;
        _logger = logger;
        _options = options;
    }

    public override async Task OnConnectedAsync(ConnectionContext connection)
    {
        var sessionId = _sessionManager.GenerateSessionId();

        _logger.LogInformation(
            "Client connected: {SessionId}, {RemoteEndPoint}",
            sessionId,
            connection.RemoteEndPoint
        );

        try
        {
            await _dispatcher.OnConnect(sessionId);

            // Kestrel의 Pipe 직접 사용 (Zero-Copy)
            var reader = connection.Transport.Input;
            var writer = connection.Transport.Output;

            await ProcessAsync(sessionId, reader, writer, connection.ConnectionClosed);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Connection {SessionId} error", sessionId);
        }
        finally
        {
            await _dispatcher.OnDisconnect(sessionId);
            _logger.LogInformation("Client disconnected: {SessionId}", sessionId);
        }
    }

    private async Task ProcessAsync(
        long sessionId,
        PipeReader reader,
        PipeWriter writer,
        CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var result = await reader.ReadAsync(cancellationToken);
                var buffer = result.Buffer;

                while (TryParsePacket(ref buffer, out var packet))
                {
                    await _dispatcher.OnReceive(sessionId, packet);
                }

                reader.AdvanceTo(buffer.Start, buffer.End);

                if (result.IsCompleted) break;
            }
        }
        finally
        {
            await reader.CompleteAsync();
            await writer.CompleteAsync();
        }
    }

    private bool TryParsePacket(ref ReadOnlySequence<byte> buffer, out IPacket? packet)
    {
        // 3.3절과 동일한 파싱 로직
        // ...
    }
}
```

### 5.3 인증서 관리

```csharp
// appsettings.json
{
    "Kestrel": {
        "Endpoints": {
            "Https": {
                "Url": "https://*:5001",
                "Certificate": {
                    "Path": "certificate.pfx",
                    "Password": "password"
                }
            },
            "TcpTls": {
                "Url": "https://*:7778",
                "Certificate": {
                    "Path": "certificate.pfx",
                    "Password": "password"
                }
            }
        }
    }
}
```

**인증서 옵션**:
- **개발**: `dotnet dev-certs https --trust`
- **프로덕션**: Let's Encrypt 또는 상용 인증서
- **자동 갱신**: Azure Key Vault, AWS Certificate Manager 연동

## 6. 세션 관리

### 6.1 세션 매니저

```csharp
public class SessionManager
{
    private readonly ConcurrentDictionary<long, SessionInfo> _sessions = new();
    private readonly ConcurrentDictionary<long, long> _accountToSession = new();

    public void AddSession(long sessionId, ISession session)
    {
        var info = new SessionInfo
        {
            SessionId = sessionId,
            Session = session,
            ConnectedAt = DateTime.UtcNow,
            LastActivity = DateTime.UtcNow,
            IsAuthenticated = false
        };

        _sessions[sessionId] = info;
    }

    public void RemoveSession(long sessionId)
    {
        if (_sessions.TryRemove(sessionId, out var info))
        {
            // AccountId 매핑 제거
            if (info.AccountId.HasValue)
            {
                _accountToSession.TryRemove(info.AccountId.Value, out _);
            }
        }
    }

    public void Authenticate(long sessionId, long accountId)
    {
        if (_sessions.TryGetValue(sessionId, out var info))
        {
            info.AccountId = accountId;
            info.IsAuthenticated = true;
            _accountToSession[accountId] = sessionId;
        }
    }

    public SessionInfo? GetSession(long sessionId)
    {
        return _sessions.TryGetValue(sessionId, out var info) ? info : null;
    }

    public SessionInfo? GetSessionByAccountId(long accountId)
    {
        if (_accountToSession.TryGetValue(accountId, out var sessionId))
        {
            return GetSession(sessionId);
        }
        return null;
    }

    public void UpdateActivity(long sessionId)
    {
        if (_sessions.TryGetValue(sessionId, out var info))
        {
            info.LastActivity = DateTime.UtcNow;
        }
    }

    public int GetSessionCount() => _sessions.Count;

    public IEnumerable<SessionInfo> GetAllSessions() => _sessions.Values;
}

public class SessionInfo
{
    public long SessionId { get; set; }
    public ISession Session { get; set; }
    public long? AccountId { get; set; }
    public bool IsAuthenticated { get; set; }
    public DateTime ConnectedAt { get; set; }
    public DateTime LastActivity { get; set; }
}
```

## 7. Heartbeat 처리

### 7.1 서버측 Heartbeat 타이머

```csharp
public class SessionService
{
    private readonly SessionManager _sessionManager;
    private readonly Timer _heartbeatTimer;
    private const int HeartbeatInterval = 30; // 30초
    private const int HeartbeatTimeout = 90; // 90초

    public SessionService(SessionManager sessionManager)
    {
        _sessionManager = sessionManager;

        // Heartbeat 타이머 시작
        _heartbeatTimer = new Timer(CheckHeartbeats, null,
            TimeSpan.FromSeconds(HeartbeatInterval),
            TimeSpan.FromSeconds(HeartbeatInterval));
    }

    private void CheckHeartbeats(object? state)
    {
        var now = DateTime.UtcNow;
        var timeoutThreshold = now.AddSeconds(-HeartbeatTimeout);

        foreach (var session in _sessionManager.GetAllSessions())
        {
            if (session.LastActivity < timeoutThreshold)
            {
                LOG.Warn($"Session timeout: {session.SessionId}");
                session.Session.Close();
            }
        }
    }

    public void OnHeartbeat(long sessionId)
    {
        _sessionManager.UpdateActivity(sessionId);
    }
}
```

### 7.2 클라이언트측 Heartbeat

클라이언트는 30초마다 Heartbeat 패킷 전송:

```json
{
  "MsgId": "Heartbeat",
  "MsgSeq": 0,
  "StageId": 0,
  "ErrorCode": 0,
  "Body": {}
}
```

## 8. 성능 최적화

### 8.1 System.IO.Pipelines 활용 (권장)

System.IO.Pipelines가 SocketAsyncEventArgs를 대체하며, 더 간단하고 효율적입니다:

```csharp
// Pipelines 기반 성능 최적화 포인트
public class OptimizedSessionOption
{
    // 메모리 풀 설정
    public MemoryPool<byte> MemoryPool { get; set; } = MemoryPool<byte>.Shared;

    // 백프레셔 임계값 (클라이언트가 느릴 때)
    public int PauseWriterThreshold { get; set; } = 64 * 1024;  // 64KB에서 일시 중지
    public int ResumeWriterThreshold { get; set; } = 32 * 1024; // 32KB에서 재개

    // 최소 버퍼 크기 (GetMemory 호출 시)
    public int MinimumSegmentSize { get; set; } = 4096;

    // 스케줄러 설정
    public PipeScheduler ReaderScheduler { get; set; } = PipeScheduler.ThreadPool;
    public PipeScheduler WriterScheduler { get; set; } = PipeScheduler.ThreadPool;
}

// 최적화된 Pipe 생성
var pipeOptions = new PipeOptions(
    pool: option.MemoryPool,
    readerScheduler: option.ReaderScheduler,
    writerScheduler: option.WriterScheduler,
    pauseWriterThreshold: option.PauseWriterThreshold,
    resumeWriterThreshold: option.ResumeWriterThreshold,
    minimumSegmentSize: option.MinimumSegmentSize,
    useSynchronizationContext: false
);
```

### 8.2 Zero-Copy 패턴

```csharp
// ❌ 기존 방식 - 메모리 복사 발생
byte[] buffer = new byte[4096];
int bytesRead = await socket.ReceiveAsync(buffer, SocketFlags.None);
ringBuffer.Write(buffer, 0, bytesRead); // 복사 발생!

// ✅ Pipelines 방식 - Zero-Copy
var writer = pipe.Writer;
Memory<byte> memory = writer.GetMemory(4096); // 풀에서 직접 획득
int bytesRead = await socket.ReceiveAsync(memory, SocketFlags.None);
writer.Advance(bytesRead); // 복사 없이 커밋
await writer.FlushAsync();
```

### 8.3 ReadOnlySequence 활용

```csharp
// ReadOnlySequence로 비연속 메모리 효율적 처리
private bool TryParsePacket(ref ReadOnlySequence<byte> buffer, out IPacket? packet)
{
    packet = null;
    if (buffer.Length < 4) return false;

    // stackalloc으로 힙 할당 방지
    Span<byte> lengthSpan = stackalloc byte[4];
    buffer.Slice(0, 4).CopyTo(lengthSpan);

    var packetLength = BinaryPrimitives.ReadInt32LittleEndian(lengthSpan);

    // ...
}
```

### 8.4 Kestrel ConnectionHandler (최적 성능)

Kestrel의 ConnectionHandler를 사용하면 Pipelines가 기본 제공됩니다:

```csharp
public class GameConnectionHandler : ConnectionHandler
{
    public override async Task OnConnectedAsync(ConnectionContext connection)
    {
        // Kestrel이 이미 최적화된 Pipe를 제공
        var input = connection.Transport.Input;   // PipeReader
        var output = connection.Transport.Output; // PipeWriter

        // TLS도 자동 처리됨 (설정에 따라)
        // 백프레셔도 자동 처리됨

        await ProcessAsync(input, output, connection.ConnectionClosed);
    }
}
```

### 8.5 성능 비교

| 방식 | 메모리 복사 | GC 압력 | 백프레셔 | 복잡도 |
|------|------------|---------|---------|-------|
| byte[] + RingBuffer | 2회 | 높음 | 수동 | 높음 |
| SocketAsyncEventArgs | 1회 | 중간 | 수동 | 중간 |
| **System.IO.Pipelines** | **0회** | **낮음** | **자동** | **낮음** |
| **Kestrel ConnectionHandler** | **0회** | **최저** | **자동** | **최저** |

## 9. 에러 처리 및 재연결

### 9.1 연결 종료 처리

```csharp
public async Task OnDisconnect(long sessionId)
{
    // 세션 제거
    var session = _sessionManager.GetSession(sessionId);
    if (session == null) return;

    // Stage에 알림
    if (session.IsAuthenticated && session.AccountId.HasValue)
    {
        await NotifyStageDisconnect(session.AccountId.Value);
    }

    // 정리
    _sessionManager.RemoveSession(sessionId);
    LOG.Info($"Session disconnected: {sessionId}");
}
```

### 9.2 재연결 토큰 (클라이언트용)

```csharp
public string GenerateReconnectToken(long sessionId, long accountId)
{
    var payload = new
    {
        SessionId = sessionId,
        AccountId = accountId,
        Timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
        ExpiresIn = 300 // 5분
    };

    return JWT.Encode(payload, _secretKey);
}

public (bool valid, long accountId) ValidateReconnectToken(string token)
{
    try
    {
        var payload = JWT.Decode<ReconnectPayload>(token, _secretKey);

        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        if (now > payload.Timestamp + payload.ExpiresIn)
        {
            return (false, 0); // 만료됨
        }

        return (true, payload.AccountId);
    }
    catch
    {
        return (false, 0);
    }
}
```

## 10. 모니터링 및 로깅

### 10.1 연결 메트릭

```csharp
public class ConnectionMetrics
{
    public int TotalConnections { get; set; }
    public int ActiveConnections { get; set; }
    public long BytesReceived { get; set; }
    public long BytesSent { get; set; }
    public int PacketsReceived { get; set; }
    public int PacketsSent { get; set; }
    public Dictionary<string, int> ErrorCounts { get; set; }
}
```

### 10.2 로깅

```csharp
// 연결 로그
LOG.Info($"New connection: SessionId={sessionId}, IP={remoteIp}");

// 에러 로그
LOG.Error(ex, $"Socket error: SessionId={sessionId}");

// 성능 로그
LOG.Debug($"Packet received: SessionId={sessionId}, MsgId={packet.MsgId}, Size={packet.Size}");
```

## 11. 보안 고려사항

### 11.1 DDoS 방지

```csharp
// IP별 연결 수 제한
private readonly ConcurrentDictionary<string, int> _ipConnectionCounts = new();
private const int MaxConnectionsPerIp = 10;

public bool CanAcceptConnection(string ipAddress)
{
    var count = _ipConnectionCounts.GetOrAdd(ipAddress, 0);
    if (count >= MaxConnectionsPerIp)
    {
        LOG.Warn($"Connection limit exceeded for IP: {ipAddress}");
        return false;
    }

    _ipConnectionCounts[ipAddress] = count + 1;
    return true;
}
```

### 11.2 데이터 검증

```csharp
// 패킷 크기 검증
if (packetLength > _maxPacketSize)
{
    LOG.Warn($"Packet too large: {packetLength} bytes");
    throw new PacketException("Packet size exceeds limit");
}

// 속도 제한
private readonly RateLimiter _rateLimiter = new(100, TimeSpan.FromSeconds(1));

if (!_rateLimiter.Allow(sessionId))
{
    LOG.Warn($"Rate limit exceeded: SessionId={sessionId}");
    return;
}
```

## 12. 다음 단계

- `07-client-protocol.md`: 클라이언트 연결 및 프로토콜 가이드
- `02-packet-structure.md`: 패킷 구조 상세
