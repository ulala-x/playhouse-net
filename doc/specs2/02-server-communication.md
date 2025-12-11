# NetMQ 서버 간 통신 스펙

## 1. 개요

### 1.1 목적
PlayHouse-NET v2에서는 서버 간 통신을 위해 **NetMQ(ZeroMQ의 .NET 구현)**를 사용합니다. REST API를 제거하고 고성능 비동기 메시징으로 전환합니다.

### 1.2 주요 특징
- **Router-Router 패턴**: 모든 서버가 RouterSocket 사용 (양방향 통신)
- **Identity 기반 라우팅**: NID로 서버 식별 및 메시지 라우팅
- **Protobuf 직렬화**: 효율적인 바이너리 프로토콜
- **Request/Reply 지원**: MsgSeq 기반 요청/응답 매칭

### 1.3 통신 유형

| 통신 유형 | 설명 | 패턴 |
|-----------|------|------|
| API → Play | Stage 생성, 메시지 전송 | Request/Reply, Send |
| Play → API | 정보 조회, 알림 | Request/Reply, Send |
| Play → Play | Cross-Stage 통신 | Request/Reply, Send |

---

## 2. 소켓 패턴

### 2.1 Router-Router 패턴

> **핵심**: **모든 서버가 RouterSocket을 사용**하여 Bind와 Connect를 동시에 수행합니다.

```
┌─────────────────┐    ┌─────────────────┐    ┌─────────────────┐
│  Play Server 1  │    │  Play Server 2  │    │   API Server    │
│  RouterSocket   │    │  RouterSocket   │    │  RouterSocket   │
│  (NID: 1:1)     │    │  (NID: 1:2)     │    │  (NID: 2:1)     │
│                 │    │                 │    │                 │
│  Bind + Connect │    │  Bind + Connect │    │  Bind + Connect │
└────────┬────────┘    └────────┬────────┘    └────────┬────────┘
         │                      │                      │
         └──────────────────────┼──────────────────────┘
                                │
                     Identity 기반 라우팅
                     (NID로 대상 서버 지정)
```

**Router-Router 패턴의 특징**:
- 모든 서버가 RouterSocket 사용 (Bind + Connect 가능)
- Identity(NID)를 메시지 첫 프레임에 지정하여 대상 서버로 라우팅
- 양방향 통신: 모든 서버가 요청/응답 가능

### 2.2 RouterSocket 설정

```csharp
public class NetMqPlaySocket : IPlaySocket
{
    private readonly RouterSocket _socket = new();

    public NetMqPlaySocket(SocketConfig config)
    {
        // Identity 설정 (NID)
        _socket.Options.Identity = Encoding.UTF8.GetBytes(config.Nid);

        // Router 옵션
        _socket.Options.RouterHandover = true;      // 동일 Identity 재연결 허용
        _socket.Options.RouterMandatory = true;     // 대상 없으면 에러 반환
        _socket.Options.DelayAttachOnConnect = true;

        // 버퍼 설정
        _socket.Options.SendBuffer = config.SendBufferSize;
        _socket.Options.ReceiveBuffer = config.ReceiveBufferSize;
        _socket.Options.SendHighWatermark = config.SendHighWatermark;
        _socket.Options.ReceiveHighWatermark = config.ReceiveHighWatermark;

        // 연결 옵션
        _socket.Options.TcpKeepalive = true;
        _socket.Options.Linger = TimeSpan.FromMilliseconds(config.Linger);
        _socket.Options.Backlog = config.BackLog;
    }
}
```

### 2.3 NID (Node ID) 구조

```
NID = "{ServiceId}:{ServerId}"

예시:
- "1:1" = ServiceId=1, ServerId=1 (Play Server #1)
- "1:2" = ServiceId=1, ServerId=2 (Play Server #2)
- "2:1" = ServiceId=2, ServerId=1 (API Server #1)
```

```csharp
public static string MakeNid(ushort serviceId, int serverId)
{
    return $"{serviceId}:{serverId}";
}
```

---

## 3. 메시지 프로토콜

### 3.1 NetMQMessage 구조

```
┌───────────────────────────────────────────────────────────┐
│ NetMQMessage                                               │
├───────────────────────────────────────────────────────────┤
│ Frame 0: Target NID                                        │
│          - UTF-8 인코딩 문자열                             │
│          - 예: "1:1"                                       │
├───────────────────────────────────────────────────────────┤
│ Frame 1: RouteHeader (Protobuf 직렬화)                     │
│          - HeaderMsg                                       │
│          - 라우팅 메타데이터                               │
├───────────────────────────────────────────────────────────┤
│ Frame 2: Payload                                           │
│          - 메시지 본문 (바이너리)                          │
│          - Protobuf 또는 커스텀 직렬화                     │
└───────────────────────────────────────────────────────────┘
```

### 3.2 Header 구조

```csharp
public class Header
{
    public ushort ServiceId { get; set; }   // 서비스 식별자
    public string MsgId { get; set; }       // 메시지 타입 식별
    public ushort MsgSeq { get; set; }      // 요청 시퀀스 (Reply 매칭용)
    public ushort ErrorCode { get; set; }   // 에러 코드 (응답 시)
    public long StageId { get; set; }       // 대상 Stage ID
}
```

### 3.3 RouteHeader 구조

```csharp
public class RouteHeader
{
    public Header Header { get; }

    // 라우팅 정보
    public long Sid { get; set; }           // Session ID
    public long AccountId { get; set; }     // 계정 ID
    public long StageId { get; set; }       // Stage ID
    public string From { get; set; }        // 발신 서버 NID

    // 메시지 플래그
    public bool IsSystem { get; set; }      // 시스템 메시지 여부
    public bool IsBase { get; set; }        // 기본 메시지 여부
    public bool IsBackend { get; set; }     // 백엔드 메시지 여부
    public bool IsReply { get; set; }       // 응답 메시지 여부
    public bool IsToClient { get; set; }    // 클라이언트 대상 여부
}
```

### 3.4 Protobuf 메시지 정의

```protobuf
syntax = "proto3";
package playhouse.protocol;

message HeaderMsg {
    int32 service_id = 1;
    string msg_id = 2;
    int32 msg_seq = 3;
    int32 error_code = 4;
    int64 stage_id = 5;
}

message RouteHeaderMsg {
    HeaderMsg header_msg = 1;
    int64 sid = 2;
    bool is_system = 3;
    bool is_base = 4;
    bool is_backend = 5;
    bool is_reply = 6;
    int64 account_id = 7;
    int64 stage_id = 8;
}
```

---

## 4. 통신 흐름

### 4.1 Request/Reply 패턴

```
Sender                              Receiver
  │                                    │
  │  1. RequestToApi/RequestToStage    │
  │  ──────────────────────────────>   │
  │     MsgSeq = N (고유 시퀀스)       │
  │                                    │
  │                                    │  2. 처리
  │                                    │
  │  3. Reply                          │
  │  <──────────────────────────────   │
  │     MsgSeq = N (동일 시퀀스)       │
  │     IsReply = true                 │
  │                                    │
```

**구현:**
```csharp
// 요청 측
public async Task<IPacket> RequestToApi(string apiNid, IPacket packet)
{
    var msgSeq = _requestCache.NextSequence();
    var tcs = new TaskCompletionSource<IPacket>();

    _requestCache.Add(msgSeq, tcs);

    var routePacket = RoutePacket.ApiOf(packet, isBase: false, isBackend: true);
    routePacket.SetMsgSeq(msgSeq);

    _communicator.Send(apiNid, routePacket);

    return await tcs.Task.WaitAsync(TimeSpan.FromSeconds(30));
}

// 응답 처리
public void OnReply(RoutePacket routePacket)
{
    if (_requestCache.TryRemove(routePacket.MsgSeq, out var tcs))
    {
        tcs.SetResult(routePacket.ToContentsPacket());
    }
}
```

### 4.2 Send 패턴 (Fire-and-Forget)

```
Sender                              Receiver
  │                                    │
  │  SendToApi/SendToStage             │
  │  ──────────────────────────────>   │
  │     MsgSeq = 0 (응답 불필요)       │
  │                                    │  처리
  │                                    │
```

**구현:**
```csharp
public void SendToApi(string apiNid, IPacket packet)
{
    var routePacket = RoutePacket.ApiOf(packet, isBase: false, isBackend: true);
    // MsgSeq = 0 (기본값)
    _communicator.Send(apiNid, routePacket);
}
```

### 4.3 타임아웃 및 에러 처리

```csharp
public class RequestCache
{
    private readonly ConcurrentDictionary<ushort, RequestEntry> _cache = new();
    private readonly TimeSpan _timeout = TimeSpan.FromSeconds(30);

    public void Start()
    {
        // 타임아웃 체크 타이머
        Task.Run(async () =>
        {
            while (!_disposed)
            {
                await Task.Delay(1000);
                CheckTimeout();
            }
        });
    }

    private void CheckTimeout()
    {
        var now = DateTime.UtcNow;
        foreach (var entry in _cache)
        {
            if (now - entry.Value.CreatedAt > _timeout)
            {
                if (_cache.TryRemove(entry.Key, out var removed))
                {
                    removed.Tcs.SetException(new TimeoutException());
                }
            }
        }
    }
}
```

---

## 5. ISender 인터페이스

### 5.1 기본 ISender

```csharp
public interface ISender
{
    ushort ServiceId { get; }

    // API 서버 통신
    void SendToApi(string apiNid, IPacket packet);
    void RequestToApi(string apiNid, IPacket packet, ReplyCallback replyCallback);
    Task<IPacket> RequestToApi(string apiNid, IPacket packet);

    // Stage 통신
    void SendToStage(string playNid, long stageId, IPacket packet);
    void RequestToStage(string playNid, long stageId, IPacket packet, ReplyCallback replyCallback);
    Task<IPacket> RequestToStage(string playNid, long stageId, IPacket packet);

    // 응답
    void Reply(ushort errorCode);
    void Reply(IPacket reply);
}
```

### 5.2 사용 예시

```csharp
// API 서버에서 Play 서버로 Stage 생성 요청
public async Task<CreateStageResult> CreateStage(
    string playNid, string stageType, long stageId, IPacket packet)
{
    var request = new CreateStageReq
    {
        StageType = stageType,
        StageId = stageId,
        Payload = packet.Payload.Data.ToArray()
    };

    var reply = await RequestToStage(playNid, stageId,
        new SimplePacket(CreateStageReq.Descriptor.Name, new ProtoPayload(request)));

    var response = CreateStageRes.Parser.ParseFrom(reply.Payload.Data.Span);
    return new CreateStageResult(response.ErrorCode, reply);
}
```

### 5.3 IActorSender 확장

```csharp
public interface IActorSender : ISender
{
    long Sid();                           // Stage ID
    void LeaveStage();                    // Stage 퇴장
    void SendToClient(IPacket packet);    // 클라이언트에 메시지 전송
}
```

### 5.4 IStageSender 확장

```csharp
public interface IStageSender : ISender
{
    long StageId { get; }
    string StageType { get; }

    // 타이머
    long AddRepeatTimer(TimeSpan initialDelay, TimeSpan period, Func<Task> callback);
    long AddCountTimer(TimeSpan initialDelay, TimeSpan period, int count, Func<Task> callback);
    void CancelTimer(long timerId);
    bool HasTimer(long timerId);

    // Stage 관리
    void CloseStage();
    void AsyncBlock(AsyncPreCallback preCallback, AsyncPostCallback? postCallback);
}
```

---

## 6. 서버 디스커버리

### 6.1 IServerInfo

**참조 소스**: `PlayHouse/Abstractions/Shared/IServerInfo.cs`, `PlayHouse/Runtime/XServerInfo.cs`

```csharp
public enum ServerState
{
    RUNNING,   // 정상 동작 중
    PAUSE,     // 일시 정지 (새 요청 거부)
    DISABLE    // 비활성화 (목록에서 제거 대상)
}

public interface IServerInfo
{
    string GetBindEndpoint();      // 바인드 주소 (tcp://ip:port)
    string GetNid();               // Node ID (serviceId:serverId)
    int GetServerId();             // 서버 번호
    ServiceType GetServiceType();  // 서비스 타입 (Play, Api)
    ushort GetServiceId();         // 서비스 ID
    ServerState GetState();        // 상태 (RUNNING, PAUSE, DISABLE)
    long GetLastUpdate();          // 마지막 업데이트 시간
    int GetActorCount();           // Actor 수 (로드밸런싱용)
}
```

#### 6.1.1 XServerInfo 구현체

**참조 소스**: `PlayHouse/Runtime/XServerInfo.cs`

```csharp
internal class XServerInfo : IServerInfo
{
    // 필드 (primary constructor로 초기화)
    private string bindEndpoint;
    private ushort serviceId;
    private int serverId;
    private string nid;
    private ServiceType serviceType;
    private ServerState serverState;
    private int actorCount;
    private long lastUpdate;

    // Factory 메서드 1: IService로부터 생성 (자기 서버 정보)
    public static XServerInfo Of(string bindEndpoint, IService service)
    {
        return new XServerInfo(
            bindEndpoint,
            service.ServiceId,
            service.ServerId,
            service.Nid,
            service.GetServiceType(),
            service.GetServerState(),
            service.GetActorCount(),
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        );
    }

    // Factory 메서드 2: Protobuf 메시지로부터 역직렬화
    public static XServerInfo Of(ServerInfoMsg infoMsg)
    {
        return new XServerInfo(
            infoMsg.Endpoint,
            (ushort)infoMsg.ServiceId,
            infoMsg.ServerId,
            infoMsg.Nid,
            Enum.Parse<ServiceType>(infoMsg.ServiceType),
            Enum.Parse<ServerState>(infoMsg.ServerState),
            infoMsg.ActorCount,
            infoMsg.Timestamp
        );
    }

    // Protobuf 메시지로 직렬화 (Heartbeat 전송용)
    public ServerInfoMsg ToMsg()
    {
        return new ServerInfoMsg
        {
            ServiceType = serviceType.ToString(),
            ServiceId = serviceId,
            Endpoint = bindEndpoint,
            ServerState = serverState.ToString(),
            Timestamp = lastUpdate,
            ActorCount = actorCount
        };
    }

    // 타임아웃 체크 (Heartbeat 미수신 서버 감지)
    public bool TimeOver()
    {
        if (ConstOption.ServerTimeLimitMs == 0) return false;
        return DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - lastUpdate
               > ConstOption.ServerTimeLimitMs;
    }

    // 상태 업데이트 (Heartbeat 수신 시)
    public void Update(XServerInfo serverInfo)
    {
        serverState = serverInfo.GetState();
        lastUpdate = serverInfo.GetLastUpdate();
        actorCount = serverInfo.GetActorCount();
    }

    // 유효성 체크 (RUNNING 상태만 유효)
    public bool IsValid() => serverState == ServerState.RUNNING;
}
```

### 6.2 IServerInfoCenter (서버 목록 관리)

**참조 소스**: `PlayHouse/Runtime/XServerInfoCenter.cs`

```csharp
internal interface IServerInfoCenter
{
    IReadOnlyList<XServerInfo> Update(IReadOnlyList<XServerInfo> serverList);
    XServerInfo FindServer(string nid);
    XServerInfo FindRoundRobinServer(ushort serviceId);
    IReadOnlyList<XServerInfo> GetServerList();
    ServiceType FindServerType(ushort serviceId);
}
```

#### 6.2.1 XServerInfoCenter 구현체

```csharp
internal class XServerInfoCenter : IServerInfoCenter
{
    private int _offset;  // Round-robin 인덱스
    private ImmutableList<XServerInfo> _serverInfoList = ImmutableList<XServerInfo>.Empty;

    // 서버 목록 업데이트 (Heartbeat 수신 시 호출)
    public IReadOnlyList<XServerInfo> Update(IReadOnlyList<XServerInfo> serverList)
    {
        var currentList = _serverInfoList.ToList();
        List<XServerInfo> updateList = [];

        foreach (var incomingServer in serverList)
        {
            var existingServer = currentList.FirstOrDefault(
                x => x.GetNid() == incomingServer.GetNid());

            if (existingServer != null)
            {
                // Endpoint 변경 감지 → 기존 연결 해제 필요
                if (existingServer.GetBindEndpoint() != incomingServer.GetBindEndpoint())
                {
                    var toDisconnectServer = XServerInfo.Of(existingServer);
                    toDisconnectServer.SetState(ServerState.DISABLE);
                    updateList.Add(toDisconnectServer);
                }
                existingServer.Update(incomingServer);
            }
            else
            {
                currentList.Add(incomingServer);  // 새 서버 추가
            }
        }

        // 타임아웃 체크 (오래된 서버 DISABLE 처리)
        foreach (var server in currentList)
        {
            server.CheckTimeout();
        }

        _serverInfoList = currentList.OrderBy(x => x.GetNid()).ToImmutableList();
        updateList.AddRange(_serverInfoList);
        return updateList;
    }

    // NID로 서버 조회
    public XServerInfo FindServer(string nid)
    {
        var serverInfo = _serverInfoList
            .FirstOrDefault(e => e.IsValid() && e.GetNid() == nid);

        if (serverInfo == null)
            throw new CommunicatorException.NotExistServerInfo(
                $"target nid:{nid}, ServerInfo is not exist");

        return serverInfo;
    }

    // Round-Robin 로드밸런싱
    public XServerInfo FindRoundRobinServer(ushort serviceId)
    {
        var list = _serverInfoList
            .Where(x => x.IsValid() && x.GetServiceId() == serviceId)
            .ToList();

        if (!list.Any())
            throw new CommunicatorException.NotExistServerInfo(
                $"serviceId:{serviceId}, ServerInfo is not exist");

        // Thread-safe 순환 인덱스
        var next = Interlocked.Increment(ref _offset);
        var index = Math.Abs(next) % list.Count;
        return list[index];
    }

}
```

### 6.3 ISystemPanel (서버 제어 패널)

**참조 소스**: `PlayHouse/Core/Shared/XSystemPanel.cs`

```csharp
public interface ISystemPanel
{
    IServerInfo GetServerInfo();                          // 현재 서버 정보
    IServerInfo GetServerInfoBy(ushort serviceId);        // Round-Robin 서버 조회
    IServerInfo GetServerInfoByNid(string nid);           // NID로 서버 조회
    IList<IServerInfo> GetServers();                      // 전체 서버 목록

    void Pause();                                         // 서버 일시정지
    void Resume();                                        // 서버 재개
    Task ShutdownASync();                                 // 서버 종료
    ServerState GetServerState();                         // 현재 상태
    long GenerateUUID();                                  // 분산 고유 ID 생성

    public static string MakeNid(ushort serviceId, int serverId)
        => $"{serviceId}:{serverId}";
}
```

#### 6.3.1 XSystemPanel 구현체

```csharp
internal class XSystemPanel : ISystemPanel
{
    private readonly IServerInfoCenter _serverInfoCenter;
    private readonly UniqueIdGenerator _uniqueIdGenerator;
    private readonly string _nid;

    public Communicator? Communicator { get; set; }

    public XSystemPanel(
        IServerInfoCenter serverInfoCenter,
        IClientCommunicator clientCommunicator,
        int serverId,
        string nid)
    {
        _serverInfoCenter = serverInfoCenter;
        _uniqueIdGenerator = new UniqueIdGenerator(serverId);
        _nid = nid;
    }

    // Round-Robin 서버 조회
    public IServerInfo GetServerInfoBy(ushort serviceId)
        => _serverInfoCenter.FindRoundRobinServer(serviceId);

    // NID로 서버 조회
    public IServerInfo GetServerInfoByNid(string nid)
        => _serverInfoCenter.FindServer(nid);

    // 분산 고유 ID 생성
    public long GenerateUUID()
        => _uniqueIdGenerator.NextId();

    // 서버 상태 제어
    public void Pause() => Communicator!.Pause();
    public void Resume() => Communicator!.Resume();
    public async Task ShutdownASync() => await Communicator!.StopAsync();
}
```

### 6.4 UniqueIdGenerator (분산 ID 생성기)

**참조 소스**: `PlayHouse/Core/Shared/UniqueIdGenerator.cs`

Snowflake 알고리즘 기반의 분산 고유 ID 생성기:

```csharp
public class UniqueIdGenerator
{
    // 비트 할당: [42bit Timestamp][12bit NodeId][10bit Sequence]
    private const long NodeIdBits = 12L;      // 최대 4096 노드
    private const long SequenceBits = 10L;    // 밀리초당 1024개
    private const long NodeIdShift = 10L;
    private const long TimestampLeftShift = NodeIdBits + SequenceBits;

    // Epoch: 2020-01-01 (ID 공간 절약)
    private static readonly long Epoch =
        new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero)
            .ToUnixTimeMilliseconds();

    private readonly long _nodeId;
    private long _lastTimestamp = -1L;
    private long _sequence;

    public UniqueIdGenerator(int nodeId)
    {
        if (nodeId < 0 || nodeId > 4095)
            throw new ArgumentOutOfRangeException(nameof(nodeId),
                "Node ID must be between 0 and 4095.");
        _nodeId = nodeId;
    }

    public long NextId()
    {
        lock (this)
        {
            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            if (timestamp < _lastTimestamp)
                throw new InvalidOperationException("Invalid system clock.");

            if (timestamp == _lastTimestamp)
            {
                _sequence = (_sequence + 1) & ((1L << (int)SequenceBits) - 1);
                if (_sequence == 0)  // Sequence 오버플로우 → 다음 밀리초 대기
                    timestamp = WaitForNextTimestamp(timestamp);
            }
            else
            {
                _sequence = 0;
            }

            _lastTimestamp = timestamp;

            return ((timestamp - Epoch) << (int)TimestampLeftShift) |
                   (_nodeId << (int)NodeIdShift) |
                   _sequence;
        }
    }
}
```

**ID 구조**:
```
┌───────────────────────────────────────────────────────────────┐
│ 63                41                 29                      0 │
│ [     Timestamp (42bit)   ][NodeId(12bit)][  Sequence(10bit) ]│
│ (69년 사용 가능)            (4096 서버)     (ms당 1024개)       │
└───────────────────────────────────────────────────────────────┘
```

### 6.5 Heartbeat 메커니즘

```
┌─────────────┐                    ┌─────────────┐
│   Server A  │                    │   Server B  │
└──────┬──────┘                    └──────┬──────┘
       │                                  │
       │  UpdateServerInfo (주기적)       │
       │ ────────────────────────────>    │
       │   - NID, State, ActorCount       │
       │   - LastUpdate timestamp         │
       │                                  │
       │  ServerInfoList (응답)           │
       │ <────────────────────────────    │
       │   - 전체 서버 목록               │
       │                                  │
```

---

## 7. 메시지 전송 예시

### 7.1 API → Stage (Stage 생성)

```csharp
// API Controller
public async Task HandleCreateRoom(IPacket packet, IApiSender sender)
{
    var req = packet.Parse<CreateRoomReq>();

    // Play 서버 선택 (로드밸런싱)
    var playServer = _systemPanel.GetServerInfoBy(ServiceType.Play);

    // Stage 생성 요청
    var result = await sender.CreateStage(
        playServer.GetNid(),
        "BattleRoom",
        req.RoomId,
        new SimplePacket("RoomConfig", new ProtoPayload(req.Config))
    );

    if (result.ErrorCode == 0)
    {
        sender.Reply(new CreateRoomRes { Success = true });
    }
    else
    {
        sender.Reply(result.ErrorCode);
    }
}
```

### 7.2 Stage → API (정보 조회)

```csharp
// Stage에서 API 서버로 유저 정보 요청
public async Task OnPostJoinStage(IActor actor)
{
    var apiServer = _systemPanel.GetServerInfoBy(ServiceType.Api);

    var userInfo = await StageSender.RequestToApi(
        apiServer.GetNid(),
        new SimplePacket("GetUserInfo", new ProtoPayload(new GetUserInfoReq
        {
            AccountId = actor.ActorSender.AccountId
        }))
    );

    var response = userInfo.Parse<GetUserInfoRes>();
    // 유저 정보 저장
}
```

### 7.3 Stage → Stage (Cross-Stage 통신)

```csharp
// 다른 Stage로 메시지 전송
public async Task NotifyOtherStage(long targetStageId, IPacket message)
{
    // 대상 Stage가 있는 Play 서버 찾기
    var targetServer = FindServerByStageId(targetStageId);

    await StageSender.RequestToStage(
        targetServer.GetNid(),
        targetStageId,
        message
    );
}
```

---

## 8. 성능 최적화

### 8.1 메시지 풀링

```csharp
public class PooledByteBuffer
{
    private static readonly ArrayPool<byte> Pool = ArrayPool<byte>.Shared;
    private byte[] _buffer;

    public PooledByteBuffer(int size)
    {
        _buffer = Pool.Rent(size);
    }

    public void Dispose()
    {
        Pool.Return(_buffer);
    }
}
```

### 8.2 Zero-Copy 전송

```csharp
// Frame 직접 전달 (복사 최소화)
if (payload is FramePayload framePayload)
{
    frame = framePayload.Frame;
}
else
{
    _buffer.Write(payload.DataSpan);
    frame = new NetMQFrame(_buffer.Buffer(), _buffer.Count);
}
```

### 8.3 배치 처리

```csharp
// 여러 메시지를 한 번에 전송
public void SendBatch(string nid, IEnumerable<RoutePacket> packets)
{
    foreach (var packet in packets)
    {
        _socket.TrySendMultipartMessage(CreateMessage(nid, packet));
    }
}
```

---

## 9. 에러 처리 및 복구

### 9.1 재연결 전략

```csharp
public class ReconnectStrategy
{
    private int _retryCount = 0;
    private readonly int _maxRetries = 5;
    private readonly TimeSpan _baseDelay = TimeSpan.FromSeconds(1);

    public async Task<bool> TryReconnect(string endpoint)
    {
        while (_retryCount < _maxRetries)
        {
            try
            {
                _socket.Connect(endpoint);
                _retryCount = 0;
                return true;
            }
            catch
            {
                _retryCount++;
                var delay = _baseDelay * Math.Pow(2, _retryCount);
                await Task.Delay(delay);
            }
        }
        return false;
    }
}
```

---

## 10. 클라이언트 인증 프로토콜

### 10.1 인증 흐름 개요

**참조 소스**: `PlayHouse/Core/Session/SessionActor.cs` (기존 Session 서버 인증 로직 참고)

PlayHouse-NET v2에서는 Session 서버가 제거되고 Play 서버가 직접 클라이언트 인증을 처리합니다.

```
┌────────────┐                      ┌─────────────┐                    ┌─────────────┐
│   Client   │                      │ Play Server │                    │  API Server │
└─────┬──────┘                      └──────┬──────┘                    └──────┬──────┘
      │                                    │                                  │
      │  1. TCP/WebSocket Connect          │                                  │
      │ ─────────────────────────────────> │                                  │
      │                                    │                                  │
      │  2. AuthenticateReq                │                                  │
      │     { payload_id, payload }        │                                  │
      │ ─────────────────────────────────> │                                  │
      │                                    │                                  │
      │                                    │  3. IActor.OnAuthenticate(IPacket)
      │                                    │     호출 (payload → IPacket)     │
      │                                    │                                  │
      │                                    │  4. (선택) API 서버에 유저 정보 요청
      │                                    │ ────────────────────────────────>│
      │                                    │                                  │
      │                                    │  5. 유저 정보 응답               │
      │                                    │ <────────────────────────────────│
      │                                    │                                  │
      │  6. AuthenticateRes                │                                  │
      │     { success, error_code }        │                                  │
      │ <───────────────────────────────── │                                  │
      │                                    │                                  │
      │  [인증 성공 시]                    │                                  │
      │                                    │  7. IActor.OnPostAuthenticate()  │
      │                                    │                                  │
      │                                    │  8. IStage.OnJoinStage(actor)    │
      │                                    │                                  │
```

### 10.2 인증 프로토콜 메시지

**Protobuf 정의** (server.proto에 추가):

```protobuf
// 클라이언트 → Play 서버
message AuthenticateReq {
  string payload_id = 1;    // 컨텐츠 패킷 식별자 (예: "LoginReq")
  bytes payload = 2;        // 컨텐츠 인증 데이터 (토큰, 유저 정보 등)
}

// Play 서버 → 클라이언트
message AuthenticateRes {
  bool success = 1;         // 인증 성공 여부
  int32 error_code = 2;     // 실패 시 에러 코드
  string payload_id = 3;    // 컨텐츠 응답 패킷 식별자
  bytes payload = 4;        // 컨텐츠 응답 데이터
}
```

### 10.3 Play 서버 인증 처리 구현

**참조 소스**: `SessionActor.cs:128-173` (Dispatch 메서드 참고)

```csharp
internal class PlayActor
{
    private bool _isAuthenticated = false;
    private readonly IActor _actor;

    public async Task Dispatch(ClientPacket clientPacket)
    {
        var msgId = clientPacket.MsgId;

        if (!_isAuthenticated)
        {
            // 인증 전: AuthenticateReq만 허용
            if (msgId == AuthenticateReq.Descriptor.Name)
            {
                await HandleAuthenticate(clientPacket);
            }
            else
            {
                _log.Warn(() => $"client is not authenticated: {msgId}");
                DisconnectClient();
            }
        }
        else
        {
            // 인증 후: 일반 메시지 처리
            await _actor.OnDispatch(clientPacket.ToPacket());
        }
    }

    private async Task HandleAuthenticate(ClientPacket clientPacket)
    {
        var req = AuthenticateReq.Parser.ParseFrom(clientPacket.Span);

        // payload를 IPacket으로 변환하여 콜백 호출
        var authPacket = new SimplePacket(req.PayloadId, new BytesPayload(req.Payload));

        try
        {
            // IActor.OnAuthenticate 호출
            var success = await _actor.OnAuthenticate(authPacket);

            if (success)
            {
                _isAuthenticated = true;

                // OnPostAuthenticate 호출 (API 서버에서 유저 정보 조회 등)
                await _actor.OnPostAuthenticate();

                // Stage 입장
                var joinResult = await _stage.OnJoinStage(_actor);

                if (joinResult)
                {
                    await _stage.OnPostJoinStage(_actor);
                    SendAuthenticateRes(true, 0, null);
                }
                else
                {
                    SendAuthenticateRes(false, ErrorCode.JoinStageFailed, null);
                    DisconnectClient();
                }
            }
            else
            {
                SendAuthenticateRes(false, ErrorCode.AuthFailed, null);
                DisconnectClient();
            }
        }
        catch (Exception ex)
        {
            _log.Error(() => $"Authentication failed: {ex.Message}");
            SendAuthenticateRes(false, ErrorCode.SystemError, null);
            DisconnectClient();
        }
    }

    private void SendAuthenticateRes(bool success, int errorCode, IPacket? reply)
    {
        var res = new AuthenticateRes
        {
            Success = success,
            ErrorCode = errorCode
        };

        if (reply != null)
        {
            res.PayloadId = reply.MsgId;
            res.Payload = ByteString.CopyFrom(reply.Payload.DataSpan);
        }

        SendToClient(res);
    }
}
```

### 10.4 클라이언트 SDK 사용 예시

```csharp
// 클라이언트 측 코드
public class GameClient
{
    private PlayConnection _connection;

    public async Task<bool> ConnectAndAuthenticate(string host, int port, string token)
    {
        // 1. TCP 연결
        await _connection.ConnectAsync(host, port);

        // 2. 인증 요청
        var loginReq = new LoginReq { Token = token, DeviceId = GetDeviceId() };
        var authPacket = new SimplePacket("LoginReq", new ProtoPayload(loginReq));

        var result = await _connection.AuthenticateAsync(authPacket);

        if (result.Success)
        {
            Console.WriteLine("인증 성공!");
            return true;
        }
        else
        {
            Console.WriteLine($"인증 실패: {result.ErrorCode}");
            return false;
        }
    }
}
```

### 10.5 IActor 인증 구현 예시

```csharp
public class GameActor : IActor
{
    public IActorSender ActorSender { get; set; }

    private string _userId;
    private UserInfo _userInfo;

    public async Task<bool> OnAuthenticate(IPacket authPacket)
    {
        // 1. 클라이언트가 보낸 인증 데이터 파싱
        var loginReq = authPacket.Parse<LoginReq>();

        // 2. 토큰 검증 (자체 검증 또는 외부 서비스 호출)
        var isValid = await ValidateToken(loginReq.Token);

        if (!isValid)
        {
            return false;
        }

        _userId = ExtractUserId(loginReq.Token);
        return true;
    }

    public async Task OnPostAuthenticate()
    {
        // 3. API 서버에서 유저 정보 조회
        var apiServer = _systemPanel.GetServerInfoBy(ServiceId.Api);

        var userInfoReq = new GetUserInfoReq { UserId = _userId };
        var response = await ActorSender.RequestToApi(
            apiServer.GetNid(),
            new SimplePacket("GetUserInfo", new ProtoPayload(userInfoReq))
        );

        _userInfo = response.Parse<GetUserInfoRes>().UserInfo;
    }

    public Task OnCreate() => Task.CompletedTask;
    public Task OnDestroy() => Task.CompletedTask;
}
```

---

## 11. 설정

### 11.1 소켓 설정

```csharp
public class PlaySocketConfig
{
    public int BackLog { get; set; } = 100;
    public int Linger { get; set; } = 0;
    public int SendBufferSize { get; set; } = 65536;
    public int ReceiveBufferSize { get; set; } = 65536;
    public int SendHighWatermark { get; set; } = 1000;
    public int ReceiveHighWatermark { get; set; } = 1000;
}
```

### 11.2 초기화 예시

```csharp
var socketConfig = new PlaySocketConfig
{
    BackLog = 100,
    SendBufferSize = 65536,
    ReceiveBufferSize = 65536
};

var socket = new NetMqPlaySocket(new SocketConfig(
    nid: "1:1",
    bindEndpoint: "tcp://0.0.0.0:11100",
    config: socketConfig
));

socket.Bind();
```
