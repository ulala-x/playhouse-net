# PlayHouse Connector 스펙

## 1. 개요

### 1.1 목적
PlayHouse Connector는 클라이언트(Unity, .NET 앱 등)가 Play Server에 TCP/WebSocket으로 연결하여 실시간 통신을 수행하기 위한 라이브러리입니다.

### 1.2 현재 구현 상태
- **경로**: `connector/PlayHouse.Connector/`
- **유지**: 연결 관리(TCP/WebSocket), 패킷 인코딩/디코딩
- **변경**: 상위 API를 참조 시스템(Connector) 인터페이스로 교체

### 1.3 참조 시스템
- **경로**: `D:\project\kairos\playhouse\playhouse-connector-net`
- **채택**: 이벤트 기반 인터페이스 (OnReceive, OnError 등)

### 1.4 참조 파일 매핑

#### 참조 시스템 (playhouse-connector-net)
| 파일 | 경로 | 용도 |
|------|------|------|
| **Connector.cs** | `D:\project\kairos\playhouse\playhouse-connector-net\PlayHouseConnector\Connector.cs` | 메인 Connector 클래스, 이벤트, 메서드 인터페이스 |
| **ConnectorConfig.cs** | `D:\project\kairos\playhouse\playhouse-connector-net\PlayHouseConnector\ConnectorConfig.cs` | 설정 클래스 |
| **Packet.cs** | `D:\project\kairos\playhouse\playhouse-connector-net\PlayHouseConnector\Protocol\Packet.cs` | IPacket 구현 |
| **Payload.cs** | `D:\project\kairos\playhouse\playhouse-connector-net\PlayHouseConnector\Protocol\Payload.cs` | IPayload, ProtoPayload 구현 |
| **AsyncManager.cs** | `D:\project\kairos\playhouse\playhouse-connector-net\PlayHouseConnector\Network\AsyncManager.cs` | 메인 스레드 콜백 처리 |
| **IConnectorCallback.cs** | `D:\project\kairos\playhouse\playhouse-connector-net\PlayHouseConnector\Callbacks\IConnectorCallback.cs` | 콜백 인터페이스 (참고용) |

#### 현재 구현 (유지할 파일)
| 파일 | 경로 | 용도 |
|------|------|------|
| **IConnection.cs** | `connector/PlayHouse.Connector/Connection/IConnection.cs` | 연결 인터페이스 |
| **TcpConnection.cs** | `connector/PlayHouse.Connector/Connection/TcpConnection.cs` | TCP 연결 구현 |
| **WebSocketConnection.cs** | `connector/PlayHouse.Connector/Connection/WebSocketConnection.cs` | WebSocket 연결 구현 |
| **PacketEncoder.cs** | `connector/PlayHouse.Connector/Protocol/PacketEncoder.cs` | 패킷 인코딩 |
| **PacketDecoder.cs** | `connector/PlayHouse.Connector/Protocol/PacketDecoder.cs` | 패킷 디코딩, LZ4 압축 해제 |
| **RequestTracker.cs** | `connector/PlayHouse.Connector/Protocol/RequestTracker.cs` | Request-Response 매칭 |

---

## 2. 변경 전략

### 2.1 유지할 코드 (현재 구현)
```
connector/PlayHouse.Connector/
├── Connection/
│   ├── IConnection.cs            # ✅ 유지
│   ├── TcpConnection.cs          # ✅ 유지
│   └── WebSocketConnection.cs    # ✅ 유지
└── Protocol/
    ├── PacketEncoder.cs          # ✅ 유지
    ├── PacketDecoder.cs          # ✅ 유지
    └── RequestTracker.cs         # ✅ 유지 (Request-Response 매칭)
```

### 2.2 교체할 코드 (참조 시스템 인터페이스 채택)
```
connector/PlayHouse.Connector/
├── IPlayHouseClient.cs           # ❌ 삭제 → Connector 클래스로 교체
├── PlayHouseClient.cs            # ❌ 삭제 → Connector 클래스로 교체
├── PlayHouseClientOptions.cs     # ❌ 삭제 → ConnectorConfig로 교체
├── Events/                       # ❌ 삭제 → Action 델리게이트로 교체
└── Packet/
    └── Packet.cs                 # ⚠️ 수정 → IPacket 인터페이스 통합
```

### 2.3 최종 구조
```
connector/PlayHouse.Connector/
├── Connector.cs                  # 메인 클래스 (참조 시스템 인터페이스)
├── ConnectorConfig.cs            # 설정
├── ConnectorErrorCode.cs         # 에러 코드
├── Connection/
│   ├── IConnection.cs            # 연결 인터페이스 (유지)
│   ├── TcpConnection.cs          # TCP 연결 (유지)
│   └── WebSocketConnection.cs    # WebSocket 연결 (유지)
├── Protocol/
│   ├── IPacket.cs                # 패킷 인터페이스
│   ├── Packet.cs                 # IPacket 구현
│   ├── IPayload.cs               # 페이로드 인터페이스
│   ├── Payload.cs                # IPayload 구현들
│   ├── PacketEncoder.cs          # 패킷 인코딩 (유지)
│   ├── PacketDecoder.cs          # 패킷 디코딩 (유지)
│   └── RequestTracker.cs         # Request-Response 추적 (유지)
└── Internal/
    └── AsyncManager.cs           # 메인 스레드 콜백 처리
```

---

## 3. 목표 인터페이스

### 3.1 설계 원칙

**serviceId 제거 이유:**
- 하나의 Connector 연결은 하나의 Play Server에만 연결됨
- 클라이언트 입장에서 serviceId로 구분할 필요가 없음
- API 간소화 및 사용 편의성 향상

**인증 메시지 등록 방식:**
- Protobuf를 기본으로 사용하지 않고, 커스텀 인증 패킷 지원
- 인증 메시지 이름을 등록하면 인증 전에는 해당 메시지만 전송 가능
- 인증 성공 후에만 다른 메시지 전송 가능

### 3.2 Connector 클래스
```csharp
public class Connector
{
    // 설정
    public ConnectorConfig ConnectorConfig { get; }
    public void Init(ConnectorConfig config);

    // 연결
    public void Connect(bool debugMode = false);
    public Task<bool> ConnectAsync(bool debugMode = false);
    public void Disconnect();
    public bool IsConnected();
    public bool IsAuthenticated();

    // 인증 메시지 등록 (커스텀 인증 패킷 지원)
    // 등록된 메시지만 인증 전에 전송 가능
    public void SetAuthenticateMessageId(string msgId);

    // 인증
    public void Authenticate(IPacket request, Action<IPacket> callback);
    public Task<IPacket> AuthenticateAsync(IPacket request);

    // 메시지 전송 (Stage 없음)
    public void Send(IPacket packet);
    public void Request(IPacket request, Action<IPacket> callback);
    public Task<IPacket> RequestAsync(IPacket request);

    // 메시지 전송 (Stage 지정)
    public void Send(long stageId, IPacket packet);
    public void Request(long stageId, IPacket request, Action<IPacket> callback);
    public Task<IPacket> RequestAsync(long stageId, IPacket request);

    // Unity 지원
    public void MainThreadAction();

    // 캐시 관리
    public void ClearCache();

    // 이벤트
    public event Action<bool>? OnConnect;               // 연결 결과
    public event Action<long, IPacket>? OnReceive;      // (stageId, packet) - stageId=0이면 Stage 없음
    public event Action<long, ushort, IPacket>? OnError; // (stageId, errorCode, request)
    public event Action? OnDisconnect;                   // 연결 끊김
}
```

### 3.3 IPacket / IPayload
```csharp
public interface IPacket : IDisposable
{
    string MsgId { get; }
    IPayload Payload { get; }
}

public interface IPayload : IDisposable
{
    ReadOnlyMemory<byte> Data { get; }
    ReadOnlySpan<byte> DataSpan => Data.Span;
}

// Protobuf 메시지 래퍼
public class Packet : IPacket
{
    public Packet(string msgId, IPayload payload);
    public Packet(IMessage message);  // Protobuf 메시지로부터 생성

    public string MsgId { get; }
    public IPayload Payload { get; }
}

public class ProtoPayload : IPayload
{
    public ProtoPayload(IMessage proto);
    public ReadOnlyMemory<byte> Data => _proto.ToByteArray();
}
```

### 3.4 ConnectorConfig
```csharp
public class ConnectorConfig
{
    // 서버 주소
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; } = 0;

    // 프로토콜 선택
    public bool UseWebsocket { get; set; } = false;

    // 타임아웃 설정 (ms)
    public int ConnectionIdleTimeoutMs { get; set; } = 30000;
    public int HeartBeatIntervalMs { get; set; } = 10000;
    public int RequestTimeoutMs { get; set; } = 30000;

    // 디버그 옵션
    public bool EnableLoggingResponseTime { get; set; } = false;
    public bool TurnOnTrace { get; set; } = false;
}
```

### 3.5 ConnectorErrorCode
```csharp
public enum ConnectorErrorCode : ushort
{
    DISCONNECTED = 60201,
    REQUEST_TIMEOUT = 60202,
    UNAUTHENTICATED = 60203
}
```

---

## 4. 참조 시스템과의 차이점

### 4.1 serviceId 제거

**참조 시스템:**
```csharp
// 모든 메서드에 serviceId 파라미터 포함
void Send(ushort serviceId, IPacket packet);
void Request(ushort serviceId, IPacket request, Action<IPacket> callback);
void Authenticate(ushort serviceId, IPacket request, Action<IPacket> callback);
```

**변경 후:**
```csharp
// serviceId 파라미터 제거 - 하나의 연결에 하나의 서비스
void Send(IPacket packet);
void Request(IPacket request, Action<IPacket> callback);
void Authenticate(IPacket request, Action<IPacket> callback);
```

### 4.2 인증 메시지 등록 방식 추가

**참조 시스템:**
- 특정 Protobuf 메시지 타입으로 인증 (고정)
- 인증 전/후 메시지 제한 없음

**변경 후:**
```csharp
// 인증 메시지 이름 등록
connector.SetAuthenticateMessageId("CustomAuthRequest");

// 인증 전: 등록된 메시지만 전송 가능
// 인증 후: 모든 메시지 전송 가능
```

### 4.3 이벤트 시그니처 변경

**참조 시스템:**
```csharp
event Action<ushort, IPacket>? OnReceive;            // (serviceId, packet)
event Action<ushort, long, IPacket>? OnReceiveStage; // (serviceId, stageId, packet)
event Action<ushort, ushort, IPacket>? OnError;      // (serviceId, errorCode, request)
```

**변경 후:**
```csharp
event Action<long, IPacket>? OnReceive;       // (stageId, packet) - serviceId 제거
event Action<long, ushort, IPacket>? OnError; // (stageId, errorCode, request)
```

### 4.4 기타 제거 항목
```csharp
// Unity 코루틴 → 제거
IEnumerator MainCoroutineAction(); // 제거
```

---

## 5. 패킷 프로토콜 (변경됨)

**ServiceId 필드 제거**: 하나의 연결에 하나의 서비스만 사용하므로 ServiceId 필드 불필요

### 5.1 클라이언트 → 서버
```
┌─────────────────────────────────────────────────────────────┐
│                    Client → Server Packet                    │
├─────────────┬─────────────┬─────────────────────────────────┤
│ Field       │ Size        │ Description                     │
├─────────────┼─────────────┼─────────────────────────────────┤
│ Length      │ 4 bytes     │ 패킷 전체 길이 (헤더 제외)       │
│ MsgIdLen    │ 1 byte      │ MsgId 문자열 길이               │
│ MsgId       │ N bytes     │ 메시지 타입명 (UTF-8)           │
│ MsgSeq      │ 2 bytes     │ 요청 시퀀스 (Little-Endian)     │
│ StageId     │ 8 bytes     │ Stage ID (Little-Endian)       │
│ Payload     │ N bytes     │ 직렬화 데이터 (커스텀/Protobuf) │
└─────────────┴─────────────┴─────────────────────────────────┘
```

### 5.2 서버 → 클라이언트
```
┌─────────────────────────────────────────────────────────────┐
│                    Server → Client Packet                    │
├─────────────┬─────────────┬─────────────────────────────────┤
│ Field       │ Size        │ Description                     │
├─────────────┼─────────────┼─────────────────────────────────┤
│ Length      │ 4 bytes     │ 패킷 전체 길이 (헤더 제외)       │
│ MsgIdLen    │ 1 byte      │ MsgId 문자열 길이               │
│ MsgId       │ N bytes     │ 메시지 타입명 (UTF-8)           │
│ MsgSeq      │ 2 bytes     │ 응답 시퀀스 (Little-Endian)     │
│ StageId     │ 8 bytes     │ Stage ID (Little-Endian)       │
│ ErrorCode   │ 2 bytes     │ 에러 코드 (0이면 성공)          │
│ OriginalSize│ 4 bytes     │ 압축 전 크기 (0이면 미압축)     │
│ Payload     │ N bytes     │ 직렬화 데이터 (LZ4 압축 가능)   │
└─────────────┴─────────────┴─────────────────────────────────┘
```

### 5.3 패킷 인코딩 (PacketEncoder.cs - 수정)
```csharp
// ServiceId 파라미터 제거
public byte[] EncodeWithLengthPrefix(IPacket packet, ushort msgSeq = 0, long stageId = 0)
{
    // 1. MsgId 추출
    var msgIdBytes = Encoding.UTF8.GetBytes(packet.MsgId);

    // 2. Payload 직렬화
    var payloadBytes = packet.Payload.Data.ToArray();

    // 3. 패킷 조립 (ServiceId 제거)
    // MsgIdLen(1) + MsgId(N) + MsgSeq(2) + StageId(8) + Payload
    var totalSize = 1 + msgIdBytes.Length + 2 + 8 + payloadBytes.Length;
    var buffer = new byte[4 + totalSize];

    // Length prefix
    BinaryPrimitives.WriteInt32LittleEndian(buffer, totalSize);
    // ... 나머지 필드
}
```

### 5.4 패킷 디코딩 (PacketDecoder.cs - 수정)
```csharp
// ServiceId 파싱 제거
private ServerPacket ParseServerPacket(byte[] data)
{
    int offset = 0;

    // MsgIdLen + MsgId (ServiceId 제거됨)
    var msgIdLen = data[offset++];
    var msgId = Encoding.UTF8.GetString(data, offset, msgIdLen);
    offset += msgIdLen;

    // MsgSeq (2) + StageId (8) + ErrorCode (2) + OriginalSize (4)
    var msgSeq = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(offset));
    offset += 2;
    var stageId = BinaryPrimitives.ReadInt64LittleEndian(data.AsSpan(offset));
    offset += 8;
    var errorCode = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(offset));
    offset += 2;
    var originalSize = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(offset));
    offset += 4;

    // Payload (LZ4 압축 해제)
    byte[] bodyData = originalSize > 0
        ? LZ4Pickler.Unpickle(data.AsSpan(offset).ToArray())
        : data.AsSpan(offset).ToArray();

    return new ServerPacket { ... };
}
```

---

## 6. 구현 체크리스트

### Phase 1: 기존 코드 정리
- [ ] `IPlayHouseClient.cs` 삭제
- [ ] `PlayHouseClient.cs` 삭제
- [ ] `PlayHouseClientOptions.cs` 삭제
- [ ] `Events/` 폴더 삭제
- [ ] `MockPlayHouseClient.cs` 삭제
- [ ] `Extensions/ServiceCollectionExtensions.cs` 삭제

### Phase 2: 새 인터페이스 구현
- [ ] `Connector.cs` 생성 (참조 시스템 인터페이스)
- [ ] `ConnectorConfig.cs` 생성
- [ ] `ConnectorErrorCode.cs` 생성
- [ ] `Internal/AsyncManager.cs` 생성 (메인 스레드 콜백)

### Phase 3: 프로토콜 레이어 수정
- [ ] `Protocol/IPacket.cs` 생성
- [ ] `Protocol/IPayload.cs` 생성
- [ ] `Protocol/Payload.cs` 생성 (ProtoPayload, EmptyPayload)
- [ ] `Packet/Packet.cs` 수정 → `Protocol/Packet.cs`로 이동, IPacket 구현
- [ ] `PacketEncoder.cs` 수정 (IPacket 지원)
- [ ] `PacketDecoder.cs` 수정 (IPacket 반환)

### Phase 4: 연결 레이어 유지
- [ ] `Connection/IConnection.cs` 유지 (변경 없음)
- [ ] `Connection/TcpConnection.cs` 유지 (변경 없음)
- [ ] `Connection/WebSocketConnection.cs` 유지 (변경 없음)
- [ ] `Protocol/RequestTracker.cs` 유지 (IPacket 지원으로 수정)

### Phase 5: 테스트
- [ ] 기존 테스트 삭제/수정
- [ ] 새 Connector 단위 테스트 작성
- [ ] TCP 연결 테스트
- [ ] WebSocket 연결 테스트
- [ ] Request-Response 테스트
- [ ] Unity 통합 테스트

---

## 7. 사용 예제

### 7.1 기본 연결 및 인증 (커스텀 인증 패킷)
```csharp
var connector = new Connector();

// 설정
connector.Init(new ConnectorConfig
{
    Host = "127.0.0.1",
    Port = 6000,
    UseWebsocket = false,
    RequestTimeoutMs = 30000
});

// 인증 메시지 등록 (커스텀 인증 패킷 지원)
// 인증 전에는 이 메시지만 전송 가능
connector.SetAuthenticateMessageId("MyAuthRequest");

// 이벤트 등록
connector.OnConnect += (success) =>
{
    if (success)
    {
        Console.WriteLine("Connected!");
        // 인증 요청 (커스텀 패킷 사용)
        var authPacket = new Packet("MyAuthRequest", new BytePayload(myAuthData));
        connector.Authenticate(authPacket, (response) =>
        {
            Console.WriteLine("Authenticated!");
        });
    }
    else
    {
        Console.WriteLine("Connection failed");
    }
};

connector.OnReceive += (stageId, packet) =>
{
    Console.WriteLine($"Received: {packet.MsgId}, StageId: {stageId}");
};

connector.OnError += (stageId, errorCode, request) =>
{
    Console.WriteLine($"Error: {errorCode} for {request.MsgId}");
};

connector.OnDisconnect += () =>
{
    Console.WriteLine("Disconnected");
};

// 연결
connector.Connect();
```

### 7.2 async/await 패턴
```csharp
var connector = new Connector();
connector.Init(new ConnectorConfig { Host = "127.0.0.1", Port = 6000 });

// 인증 메시지 등록
connector.SetAuthenticateMessageId("AuthRequest");

// 비동기 연결
bool connected = await connector.ConnectAsync();
if (!connected) return;

// 비동기 인증 (serviceId 파라미터 제거됨)
var authRequest = new Packet(new AuthRequest { UserId = "player1" });
var authResponse = await connector.AuthenticateAsync(authRequest);

// Stage로 메시지 전송 (serviceId 파라미터 제거됨)
var gamePacket = new Packet(new MoveRequest { X = 10, Y = 20 });
connector.Send(stageId: 12345, gamePacket);

// Request-Response (serviceId 파라미터 제거됨)
var attackPacket = new Packet(new AttackRequest { TargetId = 123 });
var response = await connector.RequestAsync(stageId: 12345, attackPacket);
```

### 7.3 Unity 통합
```csharp
public class NetworkManager : MonoBehaviour
{
    private Connector _connector;

    void Start()
    {
        _connector = new Connector();
        _connector.Init(new ConnectorConfig
        {
            Host = "game.server.com",
            Port = 6000,
            UseWebsocket = true,  // WebGL은 WebSocket 필수
            HeartBeatIntervalMs = 10000
        });

        // 커스텀 인증 메시지 등록
        _connector.SetAuthenticateMessageId("GameAuthRequest");

        _connector.OnConnect += OnConnected;
        _connector.OnReceive += OnMessageReceived;
        _connector.OnError += OnErrorReceived;
        _connector.OnDisconnect += OnDisconnected;

        _connector.Connect();
    }

    void Update()
    {
        // 메인 스레드에서 콜백 처리 (필수!)
        _connector?.MainThreadAction();
    }

    void OnDestroy()
    {
        _connector?.Disconnect();
    }

    private void OnConnected(bool success)
    {
        if (success)
        {
            Debug.Log("Connected to server");
        }
    }

    // 이벤트 시그니처에서 serviceId 제거됨
    private void OnMessageReceived(long stageId, IPacket packet)
    {
        // stageId == 0 이면 Stage 없는 메시지
        Debug.Log($"Received: {packet.MsgId}");
    }

    private void OnErrorReceived(long stageId, ushort errorCode, IPacket request)
    {
        Debug.LogError($"Request failed: {errorCode}");
    }

    private void OnDisconnected()
    {
        Debug.Log("Disconnected from server");
    }
}
```

### 7.4 메시지 파싱 (Protobuf 및 커스텀)
```csharp
// serviceId 파라미터 제거됨
connector.OnReceive += (stageId, packet) =>
{
    // packet.MsgId로 메시지 타입 확인
    switch (packet.MsgId)
    {
        case "ChatMessage":
            // Protobuf 메시지 파싱
            var chat = ChatMessage.Parser.ParseFrom(packet.Payload.Data.Span);
            Console.WriteLine($"[{chat.Sender}]: {chat.Text}");
            break;

        case "GameEvent":
            var evt = GameEvent.Parser.ParseFrom(packet.Payload.Data.Span);
            ProcessGameEvent(evt);
            break;

        case "CustomBinaryData":
            // 커스텀 바이너리 데이터 처리
            var data = packet.Payload.Data.ToArray();
            ProcessBinaryData(data);
            break;
    }
};
```

---

## 8. 에러 처리

### 8.1 에러 코드
```csharp
// 클라이언트 에러 (60xxx)
public enum ConnectorErrorCode : ushort
{
    DISCONNECTED = 60201,      // 연결 끊김 상태에서 요청
    REQUEST_TIMEOUT = 60202,   // 요청 타임아웃
    UNAUTHENTICATED = 60203    // 미인증 상태에서 요청
}

// 서버 에러 (응답의 ErrorCode 필드)
// 0 = 성공
// 그 외 = 서버 정의 에러 코드
```

### 8.2 에러 이벤트 처리
```csharp
// serviceId 파라미터 제거됨
connector.OnError += (stageId, errorCode, request) =>
{
    switch ((ConnectorErrorCode)errorCode)
    {
        case ConnectorErrorCode.DISCONNECTED:
            // 연결 끊김 상태에서 요청
            Console.WriteLine("Not connected!");
            break;

        case ConnectorErrorCode.REQUEST_TIMEOUT:
            // 요청 타임아웃 - 재시도 로직
            RetryRequest(request);
            break;

        case ConnectorErrorCode.UNAUTHENTICATED:
            // 미인증 상태에서 요청 (등록된 인증 메시지가 아닌 경우)
            Console.WriteLine("Please authenticate first");
            break;

        default:
            // 서버 에러 코드
            Console.WriteLine($"Server error: {errorCode}");
            break;
    }
};
```

### 8.3 비동기 예외 처리
```csharp
try
{
    // serviceId 파라미터 제거됨
    var response = await connector.RequestAsync(stageId, request);
    // 응답 처리
}
catch (ConnectorException ex)
{
    Console.WriteLine($"StageId: {ex.StageId}");
    Console.WriteLine($"ErrorCode: {ex.ErrorCode}");
    Console.WriteLine($"Request: {ex.Request.MsgId}");
}
```

---

## 9. 코드 재사용 요약

| 항목 | 현재 구현 | 조치 |
|------|----------|------|
| **Connection/** | TcpConnection, WebSocketConnection | ✅ 유지 |
| **Protocol/PacketEncoder.cs** | 패킷 인코딩 | ✅ 유지 (IPacket 지원 추가) |
| **Protocol/PacketDecoder.cs** | 패킷 디코딩 | ✅ 유지 (IPacket 반환으로 수정) |
| **Protocol/RequestTracker.cs** | Request-Response 매칭 | ✅ 유지 |
| **IPlayHouseClient** | 인터페이스 | ❌ 삭제 |
| **PlayHouseClient** | 구현체 | ❌ 삭제 → Connector로 대체 |
| **PlayHouseClientOptions** | 설정 | ❌ 삭제 → ConnectorConfig로 대체 |
| **Events/** | EventArgs 클래스들 | ❌ 삭제 → Action 델리게이트로 대체 |
| LZ4 압축 | K4os.Compression.LZ4 | ✅ 동일 라이브러리 유지 |
