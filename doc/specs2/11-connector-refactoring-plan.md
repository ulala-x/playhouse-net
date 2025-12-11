# PlayHouse.Connector 리팩토링 계획서

## 개요

### 목적
Kairos playhouse-connector-net의 좋은 구현을 PlayHouse.Connector에 포팅하여 Unity 호환 가능한 .NET Standard 2.1 클라이언트 라이브러리 구축

### 요구사항
| 항목 | 현재 상태 | 목표 |
|------|----------|------|
| 타겟 프레임워크 | net8.0;net9.0;net10.0 | **netstandard2.1** |
| 외부 의존성 | Google.Protobuf, K4os.Compression.LZ4 | **내장 API만 사용** (ArrayPool 등) |
| TCP | System.Net.Sockets | System.Net.Sockets + **SslStream** |
| WebSocket | System.Net.WebSockets | System.Net.WebSockets + **wss://** |
| 버퍼 관리 | List<byte> | **RingBuffer + ArrayPool** |
| HeartBeat | 설정만 있음 (미구현) | **완전 구현** |
| IdleTimeout | 설정만 있음 (미구현) | **완전 구현** |
| ServiceId | 없음 | 유지 (불필요 - Play 서버 직접 연결) |

---

## Kairos 소스 참조 위치

```
D:\project\kairos\playhouse\playhouse-connector-net\PlayHouseConnector\
├── Infrastructure/
│   ├── Buffers/
│   │   ├── PooledBuffer.cs        # ArrayPool으로 교체 필요
│   │   ├── PooledByteBuffer.cs    # 직접 포팅
│   │   └── RingBuffer.cs          # 직접 포팅
│   └── Threading/
│       └── AtomicShort.cs         # 직접 포팅
├── Network/
│   ├── ClientNetwork.cs           # HeartBeat, IdleTimeout 참조
│   ├── RequestCache.cs            # 직접 포팅
│   ├── PacketParser.cs            # ServiceId 제거 후 포팅
│   ├── TcpClient.cs               # NetCoreServer 사용 → 재작성
│   └── WsClient.cs                # NetCoreServer 사용 → 재작성
├── Protocol/
│   ├── PacketConst.cs             # 상수 참조
│   └── ClientPacket.cs            # 참조용
└── ConnectorConfig.cs             # 설정 참조
```

---

## 현재 PlayHouse.Connector 구조

```
D:\project\ulalax\playhouse-net\connector\PlayHouse.Connector\
├── PlayHouse.Connector.csproj     # 변경 필요
├── Connector.cs                   # API 유지
├── ConnectorConfig.cs             # 설정 추가 필요
├── ConnectorErrorCode.cs          # 유지
├── ConnectorException.cs          # 유지
├── Connection/
│   ├── IConnection.cs             # 수정 (TLS 지원)
│   ├── TcpConnection.cs           # 재작성 (SslStream)
│   └── WebSocketConnection.cs     # 재작성 (wss://)
├── Internal/
│   ├── AsyncManager.cs            # 유지
│   └── ClientNetwork.cs           # 수정 (RingBuffer, HeartBeat)
└── Protocol/
    ├── IPacket.cs                 # 유지
    ├── IPayload.cs                # 유지
    ├── Packet.cs                  # 유지
    └── Payload.cs                 # 유지
```

---

## 변경 후 디렉토리 구조 (Kairos 스타일)

```
connector/PlayHouse.Connector/
│
├── PlayHouse.Connector.csproj          # netstandard2.1
│
├── Connector.cs                        # 공개 API (유지)
├── ConnectorConfig.cs                  # 설정 (SSL 옵션 추가)
├── ConnectorErrorCode.cs               # 에러 코드 (유지)
├── ConnectorException.cs               # 예외 (유지)
│
├── Infrastructure/                     # [신규 디렉토리]
│   │
│   ├── Buffers/                        # [신규] Kairos 포팅
│   │   ├── PooledBuffer.cs             # ArrayPool 기반 버퍼
│   │   ├── PooledByteBuffer.cs         # 바이트 버퍼 유틸리티
│   │   └── RingBuffer.cs               # 순환 버퍼
│   │
│   └── Threading/                      # [신규] Kairos 포팅
│       └── AtomicShort.cs              # 스레드 안전 시퀀스
│
├── Network/                            # [기존 Internal → Network 이동]
│   │
│   ├── IConnection.cs                  # 연결 인터페이스 (SSL 지원)
│   ├── TcpConnection.cs                # TCP + SslStream
│   ├── WebSocketConnection.cs          # WebSocket + wss://
│   │
│   ├── ClientNetwork.cs                # 클라이언트 네트워크 (HeartBeat, IdleTimeout)
│   ├── AsyncManager.cs                 # 비동기 작업 관리
│   │
│   ├── PacketParser.cs                 # [신규] 패킷 파싱
│   ├── RequestCache.cs                 # [신규] 요청 캐시 (응답 대기)
│   └── PacketConst.cs                  # [신규] 패킷 상수
│
└── Protocol/                           # 프로토콜 (유지)
    ├── IPacket.cs                      # 패킷 인터페이스
    ├── IPayload.cs                     # 페이로드 인터페이스
    ├── Packet.cs                       # 패킷 구현
    └── Payload.cs                      # 페이로드 구현
```

### 디렉토리 구조 비교

| Kairos | PlayHouse (변경 후) | 비고 |
|--------|---------------------|------|
| `Infrastructure/Buffers/` | `Infrastructure/Buffers/` | 동일 |
| `Infrastructure/Threading/` | `Infrastructure/Threading/` | 동일 |
| `Infrastructure/Compression/` | - | LZ4는 NuGet으로 대체 |
| `Infrastructure/Logging/` | - | 로깅 불필요 |
| `Network/` | `Network/` | Internal → Network 이동 |
| `Callbacks/` | - | Connector.cs에 이벤트로 통합 |
| `Protocol/` | `Protocol/` | 동일 |

### 파일 매핑 (Kairos → PlayHouse)

| Kairos 파일 | PlayHouse 파일 | 변경 사항 |
|-------------|----------------|----------|
| `Infrastructure/Buffers/PooledBuffer.cs` | `Infrastructure/Buffers/PooledBuffer.cs` | CoreWCF → ArrayPool |
| `Infrastructure/Buffers/PooledByteBuffer.cs` | `Infrastructure/Buffers/PooledByteBuffer.cs` | XBitConverter → BinaryPrimitives |
| `Infrastructure/Buffers/RingBuffer.cs` | `Infrastructure/Buffers/RingBuffer.cs` | 네임스페이스만 변경 |
| `Infrastructure/Threading/AtomicShort.cs` | `Infrastructure/Threading/AtomicShort.cs` | 네임스페이스만 변경 |
| `Network/ClientNetwork.cs` | `Network/ClientNetwork.cs` | ServiceId 제거, 기능 통합 |
| `Network/AsyncManager.cs` | `Network/AsyncManager.cs` | 유지 |
| `Network/PacketParser.cs` | `Network/PacketParser.cs` | ServiceId 제거 |
| `Network/RequestCache.cs` | `Network/RequestCache.cs` | 로깅 단순화 |
| `Network/TcpClient.cs` | `Network/TcpConnection.cs` | NetCoreServer → .NET 소켓 |
| `Network/WsClient.cs` | `Network/WebSocketConnection.cs` | NetCoreServer → ClientWebSocket |
| `Network/IClient.cs` | `Network/IConnection.cs` | 인터페이스 통합 |
| `Protocol/PacketConst.cs` | `Network/PacketConst.cs` | MinHeaderSize 변경 |
| `Callbacks/IConnectorCallback.cs` | `Connector.cs` (이벤트) | 콜백 → 이벤트 |

---

## 패킷 포맷 (변경 없음)

### Client → Server
```
[Length:4][MsgIdLen:1][MsgId:N][MsgSeq:2][StageId:8][Payload]
```

### Server → Client
```
[Length:4][MsgIdLen:1][MsgId:N][MsgSeq:2][StageId:8][ErrorCode:2][OriginalSize:4][Payload]
```

> **Note**: ServiceId 없음 (Kairos와 다름) - Play 서버 직접 연결이므로 불필요

---

## TODO 작업 목록

### Phase 1: 프로젝트 설정 변경
- [ ] **TODO-1.1**: `PlayHouse.Connector.csproj` 타겟 변경
  - 파일: `connector/PlayHouse.Connector/PlayHouse.Connector.csproj`
  - 변경: `<TargetFrameworks>net8.0;net9.0;net10.0</TargetFrameworks>` → `<TargetFramework>netstandard2.1</TargetFramework>`
  - 추가: `<LangVersion>8.0</LangVersion>`
  - 제거: PlayHouse.csproj 참조 (Connector는 독립적)
  - 유지: Google.Protobuf, K4os.Compression.LZ4

### Phase 2: Infrastructure 포팅

#### TODO-2.1: AtomicShort 클래스 생성
- [ ] **파일 생성**: `connector/PlayHouse.Connector/Infrastructure/Threading/AtomicShort.cs`
- **Kairos 소스**: `D:\project\kairos\playhouse\playhouse-connector-net\PlayHouseConnector\Infrastructure\Threading\AtomicShort.cs`
- **변경 사항**: 네임스페이스만 변경 (`PlayHouseConnector` → `PlayHouse.Connector`)

```csharp
// 포팅할 코드 (36줄)
namespace PlayHouse.Connector.Infrastructure.Threading;

public class AtomicShort
{
    private int atomicInteger;

    public ushort Get()
    {
        return (ushort)Interlocked.CompareExchange(ref atomicInteger, 0, 0);
    }

    public ushort IncrementAndGet()
    {
        int current;
        int next;
        do
        {
            current = atomicInteger;
            next = (current + 1) & ushort.MaxValue;
            if (next == 0) next = 1;
        } while (Interlocked.CompareExchange(ref atomicInteger, next, current) != current);

        return (ushort)next;
    }

    public void Clear()
    {
        Interlocked.Exchange(ref atomicInteger, 0);
    }
}
```

#### TODO-2.2: PooledBuffer 클래스 생성 (ArrayPool 기반)
- [ ] **파일 생성**: `connector/PlayHouse.Connector/Infrastructure/Buffers/PooledBuffer.cs`
- **Kairos 소스**: `D:\project\kairos\playhouse\playhouse-connector-net\PlayHouseConnector\Infrastructure\Buffers\PooledBuffer.cs`
- **변경 사항**:
  - `CoreWCF.Channels.BufferManager` → `System.Buffers.ArrayPool<byte>`
  - 정적 Init() 제거 (ArrayPool은 초기화 불필요)

```csharp
// 변경된 구현 예시
namespace PlayHouse.Connector.Infrastructure.Buffers;

public class PooledBuffer : IDisposable
{
    private static readonly ArrayPool<byte> Pool = ArrayPool<byte>.Shared;

    private byte[]? _data;
    private readonly bool _isPooled = true;

    public PooledBuffer(int capacity)
    {
        _data = Pool.Rent(capacity);
        Size = 0;
        Offset = 0;
    }

    // ... Kairos 구현 참조하여 BufferManager → ArrayPool 교체

    public void Dispose()
    {
        if (_data != null && _isPooled)
        {
            Pool.Return(_data);
            _data = null;
        }
    }
}
```

#### TODO-2.3: PooledByteBuffer 클래스 생성
- [ ] **파일 생성**: `connector/PlayHouse.Connector/Infrastructure/Buffers/PooledByteBuffer.cs`
- **Kairos 소스**: `D:\project\kairos\playhouse\playhouse-connector-net\PlayHouseConnector\Infrastructure\Buffers\PooledByteBuffer.cs` (467줄)
- **변경 사항**:
  - 네임스페이스 변경
  - `XBitConverter` → `BinaryPrimitives` (System.Buffers.Binary)
  - Network order → Little endian (서버와 동일하게)

#### TODO-2.4: RingBuffer 클래스 생성
- [ ] **파일 생성**: `connector/PlayHouse.Connector/Infrastructure/Buffers/RingBuffer.cs`
- **Kairos 소스**: `D:\project\kairos\playhouse\playhouse-connector-net\PlayHouseConnector\Infrastructure\Buffers\RingBuffer.cs` (87줄)
- **변경 사항**: 네임스페이스만 변경

```csharp
namespace PlayHouse.Connector.Infrastructure.Buffers;

public class RingBuffer : PooledByteBuffer
{
    public RingBuffer(int capacity) : base(capacity) { }
    public RingBuffer(int capacity, int maxCapacity) : base(capacity, maxCapacity) { }

    public int PeekNextIndex(int offSet)
    {
        var readerIndex = ReaderIndex;
        for (var i = 0; i < offSet; i++)
        {
            readerIndex = NextIndex(readerIndex);
        }
        return readerIndex;
    }

    protected internal override int NextIndex(int index)
    {
        return (index + 1) % Capacity;
    }

    public bool ReadBool()
    {
        var data = ReadByte();
        return data != 0;
    }
}
```

### Phase 3: Network Infrastructure 포팅

#### TODO-3.1: RequestCache 클래스 생성
- [ ] **파일 생성**: `connector/PlayHouse.Connector/Network/RequestCache.cs`
- **Kairos 소스**: `D:\project\kairos\playhouse\playhouse-connector-net\PlayHouseConnector\Network\RequestCache.cs` (149줄)
- **변경 사항**:
  - 네임스페이스 변경
  - `LOG<T>` → `ILogger?` 또는 Console.WriteLine (단순화)
  - `AtomicShort` 사용 (TODO-2.1에서 생성)
  - Response time 로깅 기능 유지

```csharp
namespace PlayHouse.Connector.Network;

public class ReplyObject
{
    private readonly Action<ushort, IPacket>? _replyCallback;
    private readonly DateTime _requestTime = DateTime.UtcNow;
    private DateTime _responseTime = DateTime.MinValue;

    public ReplyObject(int msgSeq, Action<ushort, IPacket>? callback = null)
    {
        MsgSeq = msgSeq;
        _replyCallback = callback;
    }

    public int MsgSeq { get; set; }

    public void OnReceive(ushort errorCode, IPacket packet)
    {
        _replyCallback?.Invoke(errorCode, packet);
    }

    public bool IsExpired(int timeoutMs)
    {
        if (_responseTime != DateTime.MinValue) return false;
        var difference = DateTime.UtcNow - _requestTime;
        return difference.TotalMilliseconds > timeoutMs;
    }

    public void TouchReceive()
    {
        _responseTime = DateTime.UtcNow;
    }

    public double GetElapsedTime()
    {
        if (_responseTime == DateTime.MinValue) return 0;
        return (_responseTime - _requestTime).TotalMilliseconds;
    }
}

public class RequestCache
{
    private readonly ConcurrentDictionary<int, ReplyObject> _cache = new();
    private readonly AtomicShort _sequence = new();
    private readonly int _timeoutMs;
    private readonly bool _enableLoggingResponseTime;

    // ... Kairos 구현 참조
}
```

#### TODO-3.2: PacketParser 클래스 생성
- [ ] **파일 생성**: `connector/PlayHouse.Connector/Network/PacketParser.cs`
- **Kairos 소스**: `D:\project\kairos\playhouse\playhouse-connector-net\PlayHouseConnector\Network\PacketParser.cs` (90줄)
- **변경 사항**:
  - **ServiceId 제거** (2바이트 제거)
  - MinHeaderSize: 23 → 21 (ServiceId 2바이트 제거)
  - 파싱 로직에서 serviceId 읽기 제거

```csharp
namespace PlayHouse.Connector.Network;

/*
 * PlayHouse 패킷 포맷 (ServiceId 없음):
 *  4byte  body size
 *  1byte  msgId size
 *  n byte msgId string
 *  2byte  msgSeq
 *  8byte  stageId
 *  2byte  errorCode
 *  4byte  original body size (if 0 not compressed)
 *  Header Size = 4+1+2+8+2+4+N = 21 + n
 */
public sealed class PacketParser
{
    public const int MinHeaderSize = 21; // ServiceId 제거로 23 → 21
    public const int MaxBodySize = 1024 * 1024 * 2;

    public List<ParsedPacket> Parse(RingBuffer buffer)
    {
        // Kairos 구현 참조, serviceId 읽기 제거
    }
}
```

#### TODO-3.3: PacketConst 클래스 생성
- [ ] **파일 생성**: `connector/PlayHouse.Connector/Network/PacketConst.cs`
- **내용**:
```csharp
namespace PlayHouse.Connector.Network;

internal static class PacketConst
{
    public const int MsgIdLimit = 256;
    public const int MaxBodySize = 1024 * 1024 * 2;
    public const int MinHeaderSize = 21; // ServiceId 없음

    public const string HeartBeat = "@Heart@Beat@";
    public const string Debug = "@Debug@";
    public const string Timeout = "@Timeout@";
}
```

### Phase 4: Connection 재작성

#### TODO-4.1: IConnection 인터페이스 수정
- [ ] **파일 이동 및 수정**: `connector/PlayHouse.Connector/Network/IConnection.cs`
- **변경 사항**: TLS 지원을 위한 ConnectAsync 시그니처 변경
```csharp
Task ConnectAsync(string host, int port, bool useSsl = false, CancellationToken cancellationToken = default);
```

#### TODO-4.2: TcpConnection 재작성 (SslStream 지원)
- [ ] **파일 이동 및 재작성**: `connector/PlayHouse.Connector/Network/TcpConnection.cs`
- **변경 사항**:
  - SslStream 래핑 추가
  - List<byte> → RingBuffer 사용
  - SSL 인증서 검증 옵션

```csharp
namespace PlayHouse.Connector.Network;

internal sealed class TcpConnection : IConnection
{
    private readonly ConnectorConfig _config;
    private TcpClient? _client;
    private Stream? _stream; // NetworkStream 또는 SslStream

    public async Task ConnectAsync(string host, int port, bool useSsl = false, CancellationToken ct = default)
    {
        _client = new TcpClient { NoDelay = true };
        await _client.ConnectAsync(host, port, ct);

        var networkStream = _client.GetStream();

        if (useSsl || _config.EnableSsl)
        {
            var sslStream = new SslStream(
                networkStream,
                false,
                _config.SkipCertValidation ? (_, _, _, _) => true : null);

            await sslStream.AuthenticateAsClientAsync(host);
            _stream = sslStream;
        }
        else
        {
            _stream = networkStream;
        }

        _isConnected = true;
        _receiveTask = ReceiveLoopAsync(_receiveCts.Token);
    }

    // RingBuffer 기반 수신 루프
}
```

#### TODO-4.3: WebSocketConnection 재작성 (WSS 지원)
- [ ] **파일 이동 및 재작성**: `connector/PlayHouse.Connector/Network/WebSocketConnection.cs`
- **변경 사항**:
  - ws:// / wss:// 자동 선택
  - List<byte> → 직접 처리 (WebSocket은 메시지 단위)

```csharp
namespace PlayHouse.Connector.Network;

internal sealed class WebSocketConnection : IConnection
{
    public async Task ConnectAsync(string host, int port, bool useSsl = false, CancellationToken ct = default)
    {
        var scheme = (useSsl || _config.EnableSsl) ? "wss" : "ws";
        var uri = new Uri($"{scheme}://{host}:{port}");

        _webSocket = new ClientWebSocket();
        _webSocket.Options.KeepAliveInterval = TimeSpan.FromMilliseconds(_config.HeartBeatIntervalMs);

        if (_config.SkipCertValidation)
        {
            // .NET Standard 2.1에서는 RemoteCertificateValidationCallback 설정 불가
            // Unity에서는 별도 처리 필요
        }

        await _webSocket.ConnectAsync(uri, ct);
        // ...
    }
}
```

### Phase 5: Core 업데이트

#### TODO-5.1: ConnectorConfig 설정 추가
- [ ] **파일 수정**: `connector/PlayHouse.Connector/ConnectorConfig.cs`
- **추가 설정**:
```csharp
namespace PlayHouse.Connector;

public sealed class ConnectorConfig
{
    // 기존 설정
    public bool UseWebsocket { get; set; }
    public int ConnectionIdleTimeoutMs { get; set; } = 30000;
    public int HeartBeatIntervalMs { get; set; } = 10000;
    public int RequestTimeoutMs { get; set; } = 30000;
    public bool EnableLoggingResponseTime { get; set; }
    public bool TurnOnTrace { get; set; }

    // 추가 설정
    public bool EnableSsl { get; set; } = false;          // TLS/SSL 사용
    public bool SkipCertValidation { get; set; } = false; // 개발용 인증서 검증 스킵
}
```

#### TODO-5.2: ClientNetwork 업데이트
- [ ] **파일 이동 및 수정**: `connector/PlayHouse.Connector/Network/ClientNetwork.cs`
- **변경 사항**:
  - `List<byte> _receiveBuffer` → `RingBuffer _receiveBuffer`
  - HeartBeat 전송 로직 추가 (Kairos ClientNetwork.cs:212-224 참조)
  - IdleTimeout 검사 로직 추가 (Kairos ClientNetwork.cs:184-207 참조)
  - RequestCache 사용 (TODO-3.1에서 생성)
  - PacketParser 사용 (TODO-3.2에서 생성)

```csharp
// HeartBeat 구현 (Kairos 참조)
private readonly Stopwatch _lastReceivedTime = new();
private readonly Stopwatch _lastSendHeartBeatTime = new();

private void SendHeartBeat()
{
    if (_config.HeartBeatIntervalMs == 0) return;

    if (_lastSendHeartBeatTime.ElapsedMilliseconds > _config.HeartBeatIntervalMs)
    {
        var packet = new Packet(PacketConst.HeartBeat);
        Send(packet, 0);
        _lastSendHeartBeatTime.Restart();
    }
}

private bool IsIdleState()
{
    if (!_isAuthenticated || _config.ConnectionIdleTimeoutMs == 0) return false;
    return _lastReceivedTime.ElapsedMilliseconds > _config.ConnectionIdleTimeoutMs;
}

private void UpdateClientConnection()
{
    if (IsConnect())
    {
        _requestCache.CheckExpire();
        SendHeartBeat();

        if (IsIdleState())
        {
            _ = DisconnectAsync();
        }
    }
}

public void MainThreadAction()
{
    UpdateClientConnection();
    _asyncManager.MainThreadAction();
}
```

### Phase 6: 서버 측 HeartBeat 핸들러 추가

> **Note**: 클라이언트가 `@Heart@Beat@` 메시지를 보내면 서버가 응답해야 연결이 유지됩니다.

#### Kairos 참조 (HeartBeat 처리 방식)

**Kairos 소스 위치**:
- `D:\project\kairos\playhouse\playhouse-net\PlayHouse\PlayHouse\Core\Session\SessionDispatcher.cs:157-162`
- `D:\project\kairos\playhouse\playhouse-net\PlayHouse\PlayHouse\Core\Session\SessionActor.cs:404-407`
- `D:\project\kairos\playhouse\playhouse-net\PlayHouse\PlayHouse\Core\Session\Network\PacketConst.cs`

```csharp
// Kairos SessionDispatcher.cs:157-162
if (msgId == PacketConst.HeartBeat) //heartbeat
{
    sessionClient.SendHeartBeat(clientPacket);
    return;
}

// Kairos SessionActor.cs:404-407
public void SendHeartBeat(ClientPacket packet)
{
    _sessionSender.SendToClient(packet);  // 단순 에코 (패킷 그대로 반환)
}

// Kairos PacketConst.cs
public static readonly string HeartBeat = "@Heart@Beat@";
public static readonly string Debug = "@Debug@";
public static readonly string Timeout = "@Timeout@";
```

**핵심 로직**: 클라이언트가 보낸 HeartBeat 패킷을 그대로 돌려보냄 (Ping-Pong)

#### TODO-6.1: PlayServer HeartBeat 핸들러 추가
- [ ] **파일 수정**: `src/PlayHouse/Bootstrap/PlayServer.cs`
- **위치**: `HandleDefaultMessageAsync` 메서드 (라인 253-303)
- **변경 사항**: `@Heart@Beat@` 메시지 처리 추가

```csharp
// HandleDefaultMessageAsync 메서드 내부에 추가 (인증 처리 후)

// HeartBeat 요청 처리 (클라이언트에서 주기적으로 전송)
// Kairos와 동일하게 패킷을 에코하여 연결 유지 확인
if (msgId == "@Heart@Beat@")
{
    var response = TcpTransportSession.CreateResponsePacket(
        msgId, msgSeq, stageId, 0, ReadOnlySpan<byte>.Empty);
    await session.SendAsync(response);
    return;
}
```

- **코드 위치**: 인증 처리 블록과 에코 처리 블록 사이에 삽입
- **현재 코드 위치 참조**:
  ```csharp
  // 인증 요청 처리 (라인 261-269)
  if (msgId == _options.AuthenticateMessageId) { ... }

  // [여기에 HeartBeat 핸들러 추가]

  // 에코 요청 처리 (라인 271-279)
  if (msgId.Contains("Echo")) { ... }
  ```

#### TODO-6.2: HeartBeat 상수 정의 (선택)
- [ ] **파일 생성** (선택): `src/PlayHouse/Runtime/Constants/PacketConst.cs`
- 또는 PlayServer 클래스 내부에 상수로 정의

```csharp
internal static class PacketConst
{
    public const string HeartBeat = "@Heart@Beat@";
    public const string Debug = "@Debug@";
}
```

### Phase 7: 빌드 및 테스트

#### TODO-7.1: .NET Standard 2.1 빌드 확인
- [ ] 빌드 명령: `dotnet build connector/PlayHouse.Connector/PlayHouse.Connector.csproj`
- [ ] 경고/에러 해결

#### TODO-7.2: E2E 테스트 프로젝트 설정 업데이트
- [ ] **파일 수정**: E2E 테스트가 새 Connector를 참조하도록
- [ ] 테스트 실행: `dotnet test tests/PlayHouse.Tests.E2E/`

#### TODO-7.3: 기존 테스트 통과 확인
- [ ] 연결 테스트 통과
- [ ] 메시징 테스트 통과
- [ ] 인증 테스트 통과
- [ ] **HeartBeat 테스트 추가 (새로 작성)**

---

## .NET Standard 2.1 호환성 체크리스트

| API | 사용 가능 | 비고 |
|-----|----------|------|
| ArrayPool<T> | ✅ | System.Buffers (NuGet) |
| Memory<T>/Span<T> | ✅ | System.Memory (NuGet) |
| BinaryPrimitives | ✅ | System.Memory |
| ConcurrentDictionary | ✅ | System.Collections.Concurrent |
| Interlocked | ✅ | System.Threading |
| TcpClient | ✅ | System.Net.Sockets |
| SslStream | ✅ | System.Net.Security |
| ClientWebSocket | ✅ | System.Net.WebSockets |
| ValueTask | ✅ | System.Threading.Tasks.Extensions |
| IAsyncDisposable | ⚠️ | 폴리필 필요 또는 IDisposable로 대체 |

### 필요한 NuGet 패키지 (netstandard2.1)
```xml
<ItemGroup>
  <PackageReference Include="System.Buffers" Version="4.5.1" />
  <PackageReference Include="System.Memory" Version="4.5.5" />
  <PackageReference Include="System.Threading.Tasks.Extensions" Version="4.5.4" />
  <PackageReference Include="Google.Protobuf" Version="3.28.*" />
  <PackageReference Include="K4os.Compression.LZ4" Version="1.3.8" />
</ItemGroup>
```

---

## 파일 변경 요약

| 파일 | 작업 | 소스 |
|------|------|------|
| `PlayHouse.Connector.csproj` | 수정 | - |
| `Infrastructure/Threading/AtomicShort.cs` | **신규** | Kairos 포팅 |
| `Infrastructure/Buffers/PooledBuffer.cs` | **신규** | Kairos 수정 |
| `Infrastructure/Buffers/PooledByteBuffer.cs` | **신규** | Kairos 포팅 |
| `Infrastructure/Buffers/RingBuffer.cs` | **신규** | Kairos 포팅 |
| `Internal/RequestCache.cs` | **신규** | Kairos 포팅 |
| `Internal/PacketParser.cs` | **신규** | Kairos 수정 |
| `Internal/PacketConst.cs` | **신규** | Kairos 참조 |
| `Connection/IConnection.cs` | 수정 | - |
| `Connection/TcpConnection.cs` | 재작성 | - |
| `Connection/WebSocketConnection.cs` | 재작성 | - |
| `Internal/ClientNetwork.cs` | 대폭 수정 | Kairos 참조 |
| `ConnectorConfig.cs` | 수정 | - |

---

## 작업 순서

```
Phase 1 → Phase 2 (순서대로) → Phase 3 (순서대로) → Phase 4 → Phase 5 → Phase 6 (서버) → Phase 7 (테스트)
```

### Connector 작업 (클라이언트)
1. **TODO-1.1**: csproj 변경
2. **TODO-2.1**: AtomicShort
3. **TODO-2.2**: PooledBuffer
4. **TODO-2.3**: PooledByteBuffer
5. **TODO-2.4**: RingBuffer
6. **TODO-3.1**: RequestCache
7. **TODO-3.2**: PacketParser
8. **TODO-3.3**: PacketConst
9. **TODO-4.1**: IConnection 수정
10. **TODO-4.2**: TcpConnection 재작성
11. **TODO-4.3**: WebSocketConnection 재작성
12. **TODO-5.1**: ConnectorConfig 추가
13. **TODO-5.2**: ClientNetwork 업데이트

### PlayServer 작업 (서버)
14. **TODO-6.1**: PlayServer HeartBeat 핸들러 추가
15. **TODO-6.2**: HeartBeat 상수 정의 (선택)

### 빌드 및 테스트
16. **TODO-7.1~7.3**: 빌드 및 테스트

---

## 참고: Kairos vs PlayHouse 패킷 포맷 차이

### Kairos (ServiceId 포함)
```
[Length:4][ServiceId:2][MsgIdLen:1][MsgId:N][MsgSeq:2][StageId:8][ErrorCode:2][OriginalSize:4][Payload]
```

### PlayHouse (ServiceId 없음)
```
[Length:4][MsgIdLen:1][MsgId:N][MsgSeq:2][StageId:8][ErrorCode:2][OriginalSize:4][Payload]
```

**차이점**: PlayHouse는 Session 서버 없이 Play 서버에 직접 연결하므로 ServiceId가 불필요함

---

## 검증 체크리스트

작업 완료 후 다음 항목 확인:

### Connector 빌드
- [ ] `dotnet build connector/PlayHouse.Connector/` 성공 (경고 0개)
- [ ] netstandard2.1 타겟 확인

### PlayServer 빌드
- [ ] `dotnet build src/PlayHouse/` 성공 (HeartBeat 핸들러 추가 후)

### 기본 테스트
- [ ] 기존 E2E 테스트 19개 통과
- [ ] TCP 연결 테스트 통과
- [ ] WebSocket 연결 테스트 통과

### HeartBeat 테스트 (새로 추가)
- [ ] 클라이언트 → 서버 `@Heart@Beat@` 전송 확인
- [ ] 서버 → 클라이언트 HeartBeat 응답 수신 확인
- [ ] HeartBeatIntervalMs 설정에 따른 주기적 전송 확인
- [ ] HeartBeat 응답 없을 시 IdleTimeout 동작 확인

### IdleTimeout 테스트
- [ ] ConnectionIdleTimeoutMs 설정 동작 확인
- [ ] 타임아웃 시 연결 해제 확인

### TLS/SSL 테스트 (선택)
- [ ] TCP + SslStream 연결
- [ ] WebSocket + wss:// 연결
- [ ] SkipCertValidation 옵션 동작 확인
