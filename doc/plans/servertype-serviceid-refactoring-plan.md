# ServerType/ServiceId 분리 리팩토링 계획

## 1. 개요

### 1.1 현재 문제점

현재 `ServiceId`가 서버 타입(Play=1, Api=2)을 구분하는 용도로 사용되고 있어, 같은 타입의 서버를 여러 그룹으로 분리할 수 없습니다.

```csharp
// 현재 구조
public enum ServiceType : ushort
{
    Play = 1,  // ServiceId로 직접 사용됨
    Api = 2,
}

// PlayServerOption.cs
public ushort ServiceId => (ushort)ServiceType;  // 항상 1
```

### 1.2 변경 목표

- `ServerType` enum: 서버 종류 구분 (Play, Api)
- `ServiceId`: 같은 ServerType 내에서 서비스 그룹 구분

```
변경 후 예시:
┌─────────────┬───────────┬─────────────────────────┐
│ ServerType  │ ServiceId │ 용도                     │
├─────────────┼───────────┼─────────────────────────┤
│ Play        │ 1         │ 메인 게임 서버 군        │
│ Play        │ 2         │ PvP 전용 서버 군         │
│ Play        │ 3         │ 이벤트 서버 군           │
│ Api         │ 1         │ 일반 API 서버 군         │
│ Api         │ 2         │ 결제 전용 API 서버 군    │
└─────────────┴───────────┴─────────────────────────┘
```

### 1.3 API 변경 예시

```csharp
// Before
sender.SendToService(serviceId: 2, packet);  // 2 = API (하드코딩)

// After
sender.SendToService(ServerType.Api, serviceId: 1, packet);  // 명시적
sender.SendToService(ServerType.Play, serviceId: 2, packet); // PvP 서버 군으로 전송
```

---

## 2. 영향 분석

### 2.1 핵심 타입 정의

| 파일 | 변경 내용 |
|------|----------|
| `src/PlayHouse/Abstractions/ServiceType.cs` | `ServiceType` → `ServerType` rename |
| `src/PlayHouse/Runtime/ServerMesh/Discovery/IServerInfo.cs` | `ServerType` 속성 추가 |
| `src/PlayHouse/Runtime/ServerMesh/ServerConfig.cs` | `ServerType` 속성 추가 |

### 2.2 서버 정보 관리

| 파일 | 변경 내용 |
|------|----------|
| `src/PlayHouse/Runtime/ServerMesh/Discovery/XServerInfoCenter.cs` | `(ServerType, ServiceId)` 복합 키로 변경 |

### 2.3 통신 인터페이스

| 파일 | 변경 내용 |
|------|----------|
| `src/PlayHouse/Abstractions/ISender.cs` | `SendToService`/`RequestToService` 시그니처 변경 |
| `src/PlayHouse/Core/Shared/XSender.cs` | 새 시그니처 구현 |
| `src/PlayHouse/Core/Play/XStageSender.cs` | `ServerType` 속성 추가 |
| `src/PlayHouse/Core/Play/XActorSender.cs` | `ServerType` 속성 추가 |
| `src/PlayHouse/Core/Api/ApiSender.cs` | 동일 변경 |

### 2.4 서버 옵션/부트스트랩

| 파일 | 변경 내용 |
|------|----------|
| `src/PlayHouse/Core/Play/Bootstrap/PlayServerOption.cs` | `ServiceId` 분리 |
| `src/PlayHouse/Core/Api/Bootstrap/ApiServerOption.cs` | `ServiceId` 분리 |
| `src/PlayHouse/Core/Play/Bootstrap/PlayServer.cs` | 생성자 변경 |
| `src/PlayHouse/Core/Api/Bootstrap/ApiServer.cs` | 생성자 변경 |

### 2.5 프로토콜

| 파일 | 변경 내용 |
|------|----------|
| `src/PlayHouse/Proto/route_header.proto` | `server_type` 필드 추가 |

### 2.6 테스트

| 파일 | 변경 내용 |
|------|----------|
| `tests/unit/PlayHouse.Unit/Runtime/XServerInfoCenterTests.cs` | 새 API 테스트 |
| `tests/e2e/PlayHouse.E2E/Verifiers/ServiceRoutingVerifier.cs` | 새 API 사용 |

---

## 3. 상세 구현 계획

### 3.1 Phase 1: Core Type 변경

#### 3.1.1 `src/PlayHouse/Abstractions/ServiceType.cs` → `ServerType.cs`

파일명을 `ServerType.cs`로 변경하고 내용 수정:

```csharp
#nullable enable

namespace PlayHouse.Abstractions;

/// <summary>
/// 서버 타입을 정의합니다.
/// </summary>
public enum ServerType : ushort
{
    /// <summary>
    /// Play Server - 게임 로직 및 실시간 통신 처리.
    /// </summary>
    Play = 1,

    /// <summary>
    /// API Server - Stateless API 요청 처리.
    /// </summary>
    Api = 2,
}
```

#### 3.1.2 `src/PlayHouse/Runtime/ServerMesh/Discovery/IServerInfo.cs`

```csharp
/// <summary>
/// 서버 정보 인터페이스.
/// </summary>
public interface IServerInfo
{
    /// <summary>
    /// 서버 타입 (Play, Api).
    /// </summary>
    ServerType ServerType { get; }

    /// <summary>
    /// 서비스 그룹 ID (같은 ServerType 내에서 서버 군 구분).
    /// </summary>
    ushort ServiceId { get; }

    /// <summary>
    /// 서버 인스턴스 ID (고유 문자열, 예: "play-1-1", "api-2-seoul").
    /// </summary>
    string ServerId { get; }

    /// <summary>
    /// ZMQ 연결 주소 (예: "tcp://192.168.1.100:5000").
    /// </summary>
    string Address { get; }

    /// <summary>
    /// 서버 상태.
    /// </summary>
    ServerState State { get; }

    /// <summary>
    /// 서버 가중치 (로드밸런싱용).
    /// </summary>
    int Weight { get; }
}

/// <summary>
/// 서버 정보 구현체.
/// </summary>
public sealed class XServerInfo : IServerInfo
{
    public ServerType ServerType { get; }
    public ushort ServiceId { get; }
    public string ServerId { get; }
    public string Address { get; }
    public ServerState State { get; }
    public int Weight { get; }

    public XServerInfo(
        ServerType serverType,
        ushort serviceId,
        string serverId,
        string address,
        ServerState state = ServerState.Running,
        int weight = 100)
    {
        ServerType = serverType;
        ServiceId = serviceId;
        ServerId = serverId;
        Address = address;
        State = state;
        Weight = weight;
    }

    public override string ToString() =>
        $"{ServerType}:{ServiceId}:{ServerId}@{Address}[{State}]";
}
```

#### 3.1.3 `src/PlayHouse/Runtime/ServerMesh/ServerConfig.cs`

```csharp
public sealed class ServerConfig
{
    public ServerType ServerType { get; }
    public ushort ServiceId { get; }
    public string ServerId { get; }
    public string BindAddress { get; }
    public int RequestTimeoutMs { get; }
    public int SendHighWatermark { get; }
    public int ReceiveHighWatermark { get; }
    public bool TcpKeepalive { get; }

    public ServerConfig(
        ServerType serverType,
        ushort serviceId,
        string serverId,
        string bindAddress,
        int requestTimeoutMs = 30000,
        int sendHighWatermark = 1000,
        int receiveHighWatermark = 1000,
        bool tcpKeepalive = true)
    {
        ServerType = serverType;
        ServiceId = serviceId;
        ServerId = serverId;
        BindAddress = bindAddress;
        RequestTimeoutMs = requestTimeoutMs;
        SendHighWatermark = sendHighWatermark;
        ReceiveHighWatermark = receiveHighWatermark;
        TcpKeepalive = tcpKeepalive;
    }

    public static ServerConfig Create(ServerType serverType, ushort serviceId, string serverId, int port)
    {
        return new ServerConfig(serverType, serviceId, serverId, $"tcp://0.0.0.0:{port}");
    }
}
```

---

### 3.2 Phase 2: Server Info Center 변경

#### 3.2.1 `src/PlayHouse/Runtime/ServerMesh/Discovery/XServerInfoCenter.cs`

```csharp
public sealed class XServerInfoCenter : IServerInfoCenter
{
    private readonly ConcurrentDictionary<string, XServerInfo> _servers = new();
    private readonly ConcurrentDictionary<(ServerType, ushort), int> _roundRobinIndex = new();
    private readonly object _updateLock = new();

    public int Count => _servers.Count;

    /// <summary>
    /// 서버 목록을 갱신합니다.
    /// </summary>
    public List<ServerChange> Update(IEnumerable<IServerInfo> serverList)
    {
        var changes = new List<ServerChange>();
        var newServers = new HashSet<string>();

        lock (_updateLock)
        {
            foreach (var server in serverList)
            {
                var info = server as XServerInfo ?? new XServerInfo(
                    server.ServerType,
                    server.ServiceId,
                    server.ServerId,
                    server.Address,
                    server.State,
                    server.Weight);

                newServers.Add(info.ServerId);

                if (_servers.TryGetValue(info.ServerId, out var existing))
                {
                    if (existing.State != info.State || existing.Address != info.Address)
                    {
                        _servers[info.ServerId] = info;
                        changes.Add(new ServerChange(info, ChangeType.Updated));
                    }
                }
                else
                {
                    _servers[info.ServerId] = info;
                    changes.Add(new ServerChange(info, ChangeType.Added));
                }
            }

            var toRemove = _servers.Keys.Where(serverId => !newServers.Contains(serverId)).ToList();
            foreach (var serverId in toRemove)
            {
                if (_servers.TryRemove(serverId, out var removed))
                {
                    changes.Add(new ServerChange(removed, ChangeType.Removed));
                }
            }
        }

        return changes;
    }

    public XServerInfo? GetServer(string serverId)
    {
        _servers.TryGetValue(serverId, out var server);
        return server;
    }

    /// <summary>
    /// (ServerType, ServiceId)로 서버를 조회합니다 (Round-robin).
    /// </summary>
    public XServerInfo? GetServerByService(ServerType serverType, ushort serviceId)
    {
        return GetServerByService(serverType, serviceId, ServerSelectionPolicy.RoundRobin);
    }

    /// <summary>
    /// (ServerType, ServiceId)로 서버를 조회합니다.
    /// </summary>
    public XServerInfo? GetServerByService(ServerType serverType, ushort serviceId, ServerSelectionPolicy policy)
    {
        var servers = GetServerListByService(serverType, serviceId)
            .Where(s => s.State == ServerState.Running)
            .ToList();

        if (servers.Count == 0)
            return null;

        return policy switch
        {
            ServerSelectionPolicy.RoundRobin => SelectRoundRobin((serverType, serviceId), servers),
            ServerSelectionPolicy.Weighted => SelectWeighted(servers),
            _ => SelectRoundRobin((serverType, serviceId), servers)
        };
    }

    /// <summary>
    /// (ServerType, ServiceId)에 해당하는 모든 서버 목록을 조회합니다.
    /// </summary>
    public IReadOnlyList<XServerInfo> GetServerListByService(ServerType serverType, ushort serviceId)
    {
        return _servers.Values
            .Where(s => s.ServerType == serverType && s.ServiceId == serviceId)
            .ToList();
    }

    private XServerInfo SelectRoundRobin((ServerType, ushort) key, List<XServerInfo> servers)
    {
        var index = _roundRobinIndex.AddOrUpdate(key, 0, (_, i) => (i + 1) % servers.Count);
        return servers[index % servers.Count];
    }

    private XServerInfo SelectWeighted(List<XServerInfo> servers)
    {
        return servers.MaxBy(s => s.Weight) ?? servers[0];
    }

    public IReadOnlyList<XServerInfo> GetAllServers() => _servers.Values.ToList();

    public IReadOnlyList<XServerInfo> GetActiveServers() =>
        _servers.Values.Where(s => s.State == ServerState.Running).ToList();

    public XServerInfo? Remove(string serverId)
    {
        _servers.TryRemove(serverId, out var removed);
        return removed;
    }

    public void Clear()
    {
        _servers.Clear();
        _roundRobinIndex.Clear();
    }
}
```

---

### 3.3 Phase 3: ISender 인터페이스 변경

#### 3.3.1 `src/PlayHouse/Abstractions/ISender.cs`

```csharp
public interface ISender
{
    /// <summary>
    /// 이 Sender의 서버 타입.
    /// </summary>
    ServerType ServerType { get; }

    /// <summary>
    /// 이 Sender의 서비스 그룹 ID.
    /// </summary>
    ushort ServiceId { get; }

    #region API Server Communication

    void SendToApi(string apiServerId, IPacket packet);
    void RequestToApi(string apiServerId, IPacket packet, ReplyCallback replyCallback);
    Task<IPacket> RequestToApi(string apiServerId, IPacket packet);

    #endregion

    #region Stage Communication

    void SendToStage(string playServerId, long stageId, IPacket packet);
    void RequestToStage(string playServerId, long stageId, IPacket packet, ReplyCallback replyCallback);
    Task<IPacket> RequestToStage(string playServerId, long stageId, IPacket packet);

    #endregion

    #region Service Communication

    /// <summary>
    /// 지정된 (ServerType, ServiceId)의 서버에 패킷을 전송합니다 (RoundRobin).
    /// </summary>
    void SendToService(ServerType serverType, ushort serviceId, IPacket packet);

    /// <summary>
    /// 지정된 (ServerType, ServiceId)의 서버에 패킷을 전송합니다.
    /// </summary>
    void SendToService(ServerType serverType, ushort serviceId, IPacket packet, ServerSelectionPolicy policy);

    /// <summary>
    /// 지정된 (ServerType, ServiceId)의 서버에 요청을 전송하고 콜백으로 응답을 받습니다.
    /// </summary>
    void RequestToService(ServerType serverType, ushort serviceId, IPacket packet, ReplyCallback replyCallback);

    /// <summary>
    /// 지정된 (ServerType, ServiceId)의 서버에 요청을 전송하고 콜백으로 응답을 받습니다.
    /// </summary>
    void RequestToService(ServerType serverType, ushort serviceId, IPacket packet, ReplyCallback replyCallback, ServerSelectionPolicy policy);

    /// <summary>
    /// 지정된 (ServerType, ServiceId)의 서버에 요청을 전송하고 응답을 기다립니다.
    /// </summary>
    Task<IPacket> RequestToService(ServerType serverType, ushort serviceId, IPacket packet);

    /// <summary>
    /// 지정된 (ServerType, ServiceId)의 서버에 요청을 전송하고 응답을 기다립니다.
    /// </summary>
    Task<IPacket> RequestToService(ServerType serverType, ushort serviceId, IPacket packet, ServerSelectionPolicy policy);

    #endregion

    #region Reply

    void Reply(ushort errorCode);
    void Reply(IPacket reply);

    #endregion
}
```

---

### 3.4 Phase 4: Server Option 변경

#### 3.4.1 `src/PlayHouse/Core/Play/Bootstrap/PlayServerOption.cs`

```csharp
public sealed class PlayServerOption
{
    /// <summary>
    /// 서버 타입 (기본: Play).
    /// </summary>
    public ServerType ServerType { get; set; } = ServerType.Play;

    /// <summary>
    /// 서비스 그룹 ID (기본: 1).
    /// 같은 ServerType 내에서 서버 군을 구분합니다.
    /// </summary>
    public ushort ServiceId { get; set; } = 1;

    /// <summary>
    /// 서버 인스턴스 ID.
    /// </summary>
    public string ServerId { get; set; } = "1";

    /// <summary>
    /// ZMQ 바인드 주소.
    /// </summary>
    public string BindEndpoint { get; set; } = "tcp://0.0.0.0:5000";

    public int RequestTimeoutMs { get; set; } = 30000;

    // ... 기타 Transport 설정

    public void Validate()
    {
        if (string.IsNullOrEmpty(ServerId))
            throw new InvalidOperationException("ServerId must be set");
        if (string.IsNullOrEmpty(BindEndpoint))
            throw new InvalidOperationException("BindEndpoint must be set");
        if (ServiceId == 0)
            throw new InvalidOperationException("ServiceId must be greater than 0");
    }
}
```

#### 3.4.2 `src/PlayHouse/Core/Api/Bootstrap/ApiServerOption.cs`

```csharp
public sealed class ApiServerOption
{
    /// <summary>
    /// 서버 타입 (기본: Api).
    /// </summary>
    public ServerType ServerType { get; set; } = ServerType.Api;

    /// <summary>
    /// 서비스 그룹 ID (기본: 1).
    /// </summary>
    public ushort ServiceId { get; set; } = 1;

    /// <summary>
    /// 서버 인스턴스 ID.
    /// </summary>
    public string ServerId { get; set; } = "1";

    /// <summary>
    /// ZMQ 바인드 주소.
    /// </summary>
    public string BindEndpoint { get; set; } = "tcp://0.0.0.0:5100";

    public int RequestTimeoutMs { get; set; } = 30000;

    public void Validate()
    {
        if (string.IsNullOrEmpty(ServerId))
            throw new InvalidOperationException("ServerId must be set");
        if (string.IsNullOrEmpty(BindEndpoint))
            throw new InvalidOperationException("BindEndpoint must be set");
        if (ServiceId == 0)
            throw new InvalidOperationException("ServiceId must be greater than 0");
    }
}
```

---

### 3.5 Phase 5: Protocol 변경

#### 3.5.1 `src/PlayHouse/Proto/route_header.proto`

```protobuf
syntax = "proto3";

package playhouse;

message RouteHeader {
    uint32 msg_seq = 1;         // Request-reply 매칭용 시퀀스
    uint32 service_id = 2;      // 서비스 그룹 ID
    string msg_id = 3;          // 메시지 식별자
    uint32 error_code = 4;      // 에러 코드 (0 = 성공)
    string from = 5;            // 발신자 NID
    int64 stage_id = 6;         // 대상 Stage ID
    int64 account_id = 7;       // Actor 레벨 라우팅용
    int64 sid = 8;              // 클라이언트 세션 ID
    bool is_reply = 9;          // 응답 여부
    uint32 payload_size = 10;   // 페이로드 크기
    uint32 server_type = 11;    // 서버 타입 (1=Play, 2=Api)
}
```

---

## 4. 구현 순서

```
┌─────────────────────────────────────────────────────────────┐
│ Step 1: Core Types                                          │
│   ServiceType.cs → ServerType.cs rename                     │
│   IServerInfo.cs → ServerType 속성 추가                      │
│   ServerConfig.cs → ServerType 속성 추가                     │
├─────────────────────────────────────────────────────────────┤
│ Step 2: Server Info Center                                  │
│   XServerInfoCenter.cs → 복합 키 및 새 메서드                 │
├─────────────────────────────────────────────────────────────┤
│ Step 3: ISender Interface                                   │
│   ISender.cs → 시그니처 변경                                 │
├─────────────────────────────────────────────────────────────┤
│ Step 4: Sender Implementations                              │
│   XSender.cs → 구현                                          │
│   XStageSender.cs, XActorSender.cs, ApiSender.cs            │
├─────────────────────────────────────────────────────────────┤
│ Step 5: Server Options                                      │
│   PlayServerOption.cs, ApiServerOption.cs                   │
├─────────────────────────────────────────────────────────────┤
│ Step 6: Server Bootstrap                                    │
│   PlayServer.cs, ApiServer.cs                               │
├─────────────────────────────────────────────────────────────┤
│ Step 7: Protocol                                            │
│   route_header.proto → 재생성                                │
├─────────────────────────────────────────────────────────────┤
│ Step 8: Tests                                               │
│   Unit tests, E2E tests 수정                                │
└─────────────────────────────────────────────────────────────┘
```

---

## 5. 사용 예시

### 5.1 서버 설정

```csharp
// 메인 게임 서버
var mainGameOptions = new PlayServerOption
{
    ServerType = ServerType.Play,
    ServiceId = 1,
    ServerId = "play-1-1",
    BindEndpoint = "tcp://0.0.0.0:5000",
};

// PvP 전용 서버
var pvpServerOptions = new PlayServerOption
{
    ServerType = ServerType.Play,
    ServiceId = 2,
    ServerId = "play-2-1",
    BindEndpoint = "tcp://0.0.0.0:5001",
};

// 일반 API 서버
var apiOptions = new ApiServerOption
{
    ServerType = ServerType.Api,
    ServiceId = 1,
    ServerId = "api-1-1",
    BindEndpoint = "tcp://0.0.0.0:5100",
};

// 결제 전용 API 서버
var paymentApiOptions = new ApiServerOption
{
    ServerType = ServerType.Api,
    ServiceId = 2,
    ServerId = "api-2-1",
    BindEndpoint = "tcp://0.0.0.0:5101",
};
```

### 5.2 서버 간 통신

```csharp
// Stage에서 일반 API 서버로 요청
var response = await stageSender.RequestToService(
    ServerType.Api,
    serviceId: 1,
    new GetUserInfoRequest { UserId = 123 });

// Stage에서 결제 API 서버로 요청
var paymentResponse = await stageSender.RequestToService(
    ServerType.Api,
    serviceId: 2,
    new ProcessPaymentRequest { Amount = 1000 });

// API에서 PvP Play 서버로 전송
apiSender.SendToService(
    ServerType.Play,
    serviceId: 2,
    new PvpMatchRequest { PlayerId = 456 });
```

---

## 6. 검증 계획

### 6.1 빌드 검증

```bash
dotnet build src/PlayHouse/PlayHouse.csproj
```

### 6.2 단위 테스트

```bash
dotnet test tests/unit/PlayHouse.Unit/PlayHouse.Unit.csproj
```

주요 검증 항목:
- `XServerInfoCenter.GetServerByService(ServerType, ushort)` 동작
- Round-robin이 `(ServerType, ServiceId)` 단위로 분리되는지

### 6.3 E2E 테스트

```bash
dotnet test tests/e2e/PlayHouse.E2E/PlayHouse.E2E.csproj
```

주요 검증 항목:
- PlayServer ↔ ApiServer 간 통신
- 여러 ServiceId 그룹 간 라우팅 분리

### 6.4 수동 검증 체크리스트

- [ ] 새 API로 서버 간 통신이 정상 동작하는가
- [ ] Round-robin이 `(ServerType, ServiceId)` 조합별로 분리되는가
- [ ] 여러 ServiceId 그룹이 독립적으로 동작하는가

---

## 7. 수정 파일 전체 목록

**프로젝트 루트**: `/home/ulalax/project/ulalax/playhouse/playhouse-net`

### 7.1 Core Types (Phase 1)

| # | 파일 | 변경 내용 |
|---|------|----------|
| 1 | `ServiceType.cs` | `ServerType.cs`로 rename, enum 변경 |
| 2 | `IServerInfo.cs` | `ServerType` 추가, `XServerInfo` 생성자 변경 |
| 3 | `ServerConfig.cs` | `ServerType` 추가, 생성자 변경 |

**파일 경로:**
```
src/PlayHouse/Abstractions/ServiceType.cs
src/PlayHouse/Runtime/ServerMesh/Discovery/IServerInfo.cs
src/PlayHouse/Runtime/ServerMesh/ServerConfig.cs
```

### 7.2 Server Info Center (Phase 2)

| # | 파일 | 변경 내용 |
|---|------|----------|
| 4 | `IServerInfoCenter.cs` | 새 메서드 시그니처 추가 |
| 5 | `XServerInfoCenter.cs` | 복합 키, 메서드 시그니처 변경 |

**파일 경로:**
```
src/PlayHouse/Abstractions/IServerInfoCenter.cs
src/PlayHouse/Runtime/ServerMesh/Discovery/XServerInfoCenter.cs
```

### 7.3 ISender Interface (Phase 3)

| # | 파일 | 변경 내용 |
|---|------|----------|
| 6 | `ISender.cs` | `ServerType` 속성, 메서드 시그니처 변경 |

**파일 경로:**
```
src/PlayHouse/Abstractions/ISender.cs
```

### 7.4 Sender Implementations (Phase 4)

| # | 파일 | 변경 내용 |
|---|------|----------|
| 7 | `XSender.cs` | 구현 변경 |
| 8 | `XStageSender.cs` | `ServerType` 추가 |
| 9 | `XActorSender.cs` | `ServerType` 추가 |
| 10 | `ApiSender.cs` | 동일 변경 |

**파일 경로:**
```
src/PlayHouse/Core/Shared/XSender.cs
src/PlayHouse/Core/Play/XStageSender.cs
src/PlayHouse/Core/Play/XActorSender.cs
src/PlayHouse/Core/Api/ApiSender.cs
```

### 7.5 Server Options (Phase 5)

| # | 파일 | 변경 내용 |
|---|------|----------|
| 11 | `PlayServerOption.cs` | `ServiceId` 분리 |
| 12 | `ApiServerOption.cs` | `ServiceId` 분리 |

**파일 경로:**
```
src/PlayHouse/Core/Play/Bootstrap/PlayServerOption.cs
src/PlayHouse/Core/Api/Bootstrap/ApiServerOption.cs
```

### 7.6 Server Bootstrap & Dispatcher (Phase 6)

| # | 파일 | 변경 내용 |
|---|------|----------|
| 13 | `PlayServer.cs` | 생성자 호출 변경 |
| 14 | `ApiServer.cs` | 생성자 호출 변경 |
| 15 | `PlayDispatcher.cs` | `ServerType` 매개변수 추가 |
| 16 | `ApiDispatcher.cs` | `ServerType` 매개변수 추가 |

**파일 경로:**
```
src/PlayHouse/Core/Play/Bootstrap/PlayServer.cs
src/PlayHouse/Core/Api/Bootstrap/ApiServer.cs
src/PlayHouse/Core/Play/PlayDispatcher.cs
src/PlayHouse/Core/Api/ApiDispatcher.cs
```

### 7.7 Communicator (Phase 6)

| # | 파일 | 변경 내용 |
|---|------|----------|
| 17 | `CommunicatorOption.cs` | `ServerType` 추가 |

**파일 경로:**
```
src/PlayHouse/Runtime/ServerMesh/Communicator/CommunicatorOption.cs
```

### 7.8 Protocol (Phase 7)

| # | 파일 | 변경 내용 |
|---|------|----------|
| 18 | `route_header.proto` | `server_type` 필드 추가 |

**파일 경로:**
```
src/PlayHouse/Proto/route_header.proto
```

### 7.9 System Controllers (Phase 7)

| # | 파일 | 변경 내용 |
|---|------|----------|
| 19 | `StaticSystemController.cs` | ServerType 파싱 |

**파일 경로:**
```
src/PlayHouse/Abstractions/System/StaticSystemController.cs
```

### 7.10 Tests (Phase 8)

| # | 파일 | 변경 내용 |
|---|------|----------|
| 20 | `XServerInfoCenterTests.cs` | 테스트 수정 |
| 21 | `ServiceRoutingVerifier.cs` | 테스트 수정 |
| 22 | `ServerContext.cs` | 테스트 수정 |

**파일 경로:**
```
tests/unit/PlayHouse.Unit/Runtime/XServerInfoCenterTests.cs
tests/e2e/PlayHouse.E2E/Verifiers/ServiceRoutingVerifier.cs
tests/e2e/PlayHouse.E2E/ServerContext.cs
```

---

## 8. 의존성 체인

```
┌─────────────────────────────────────────────────────────────────┐
│ PlayServerOption / ApiServerOption                              │
│   - ServerType (enum)                                           │
│   - ServiceId (ushort)                                          │
└───────────────────────────┬─────────────────────────────────────┘
                            ↓
┌─────────────────────────────────────────────────────────────────┐
│ PlayServer / ApiServer (Bootstrap)                              │
│   - ServerConfig 생성                                           │
│   - XServerInfo 생성                                            │
│   - Dispatcher 생성                                             │
└───────────────────────────┬─────────────────────────────────────┘
                            ↓
┌─────────────────────────────────────────────────────────────────┐
│ PlayDispatcher / ApiDispatcher                                  │
│   - ServerType, ServiceId 저장                                   │
│   - Sender 생성 시 전달                                          │
└───────────────────────────┬─────────────────────────────────────┘
                            ↓
┌─────────────────────────────────────────────────────────────────┐
│ XSender (abstract)                                              │
│   - ServerType, ServiceId 속성                                   │
│   - SendToService/RequestToService 구현                          │
└───────────────────────────┬─────────────────────────────────────┘
                            ↓
┌─────────────────────────────────────────────────────────────────┐
│ XStageSender / ApiSender (XSender 상속)                         │
│ XActorSender (ISender 위임)                                     │
└─────────────────────────────────────────────────────────────────┘
```

---

## 9. 인터페이스 상속 구조

```
ISender (기본 인터페이스)
├── ServerType { get; }          // 추가
├── ServiceId { get; }           // 유지
├── SendToService(ServerType, ushort, IPacket)      // 변경
├── RequestToService(ServerType, ushort, IPacket)   // 변경
│
├─ IApiSender (extends ISender)
│   └─ ApiSender (XSender 상속)
│
├─ IStageSender (extends ISender)
│   └─ XStageSender (XSender 상속)
│
└─ IActorSender (extends ISender)
    └─ XActorSender (ISender 위임)
```

**핵심**: ISender만 변경하면 하위 인터페이스들은 자동 적용됨
