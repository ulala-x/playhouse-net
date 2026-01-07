# PlayHouse-NET 클라이언트 프로토콜

## 1. 개요

이 문서는 클라이언트가 PlayHouse-NET 서버와 통신하는 방법을 정의합니다. 연결 수립부터 인증, Stage 입장, 메시지 송수신, 재연결까지 전체 프로토콜을 다룹니다.

### 1.1 프로토콜 특징

- **바이너리 프로토콜**: 효율적인 데이터 전송
- **Request-Reply**: MsgSeq 기반 응답 매칭
- **Push 지원**: 서버에서 클라이언트로 일방향 전송
- **Heartbeat**: 연결 유지 확인

### 1.2 지원 전송 방식

- **TCP**: 고성능, 낮은 지연
- **WebSocket**: 웹 브라우저 호환
- **TLS/SSL**: 암호화 통신

## 2. 연결 플로우

새 설계에서는 HTTP API로 토큰을 발급받고, 클라이언트가 토큰으로 Room 서버에 직접 연결합니다.
인증(Login)은 소켓 연결이 아닌 HTTP API 단계에서 처리됩니다.

### 2.1 전체 연결 시퀀스

```
[클라이언트 연결 및 게임 입장 시퀀스]

Client                    Web Server              Room Server
  │                           │                       │
  │  1. HTTP: 로그인          │                       │
  │     (credentials)         │                       │
  ├──────────────────────────▶│                       │
  │                           │                       │
  │  2. HTTP: 로그인 응답     │                       │
  │     (accountId, token)    │                       │
  │◀──────────────────────────┤                       │
  │                           │                       │
  │  3. HTTP: GetOrCreateRoom │                       │
  │     (roomType, userInfo)  │                       │
  ├──────────────────────────▶│                       │
  │                           │                       │
  │                           │  4. CreateStage       │
  │                           │     (if needed)       │
  │                           ├──────────────────────▶│
  │                           │                       │
  │                           │     OnCreate()        │
  │                           │     OnPostCreate()    │
  │                           │                       │
  │                           │  5. Stage 정보 응답    │
  │                           │◀──────────────────────┤
  │                           │                       │
  │  6. HTTP: GetOrCreateRoom 응답                     │
  │     (roomToken, endpoint) │                       │
  │◀──────────────────────────┤                       │
  │                           │                       │
  │  7. TCP/WebSocket Connect (with roomToken)        │
  ├───────────────────────────────────────────────────▶│
  │                                                    │
  │                           8. ValidateToken         │
  │                              → JoinRoom            │
  │                              → OnJoinRoom(actor)   │
  │                              → actor.OnCreate()    │
  │                              → actor.OnAuthenticate│
  │                              → OnPostJoinRoom()    │
  │                              → OnActorConnection   │
  │                                  Changed(true)     │
  │                                                    │
  │  9. JoinRoomRes (stageInfo, currentState)          │
  │◀───────────────────────────────────────────────────┤
  │                                                    │
  │  10. GameMsg (player actions)                      │
  ├───────────────────────────────────────────────────▶│
  │                                                    │
  │  11. GameMsg (state updates, Push)                 │
  │◀───────────────────────────────────────────────────┤
  │                                                    │
  │  12. Heartbeat (주기적)                             │
  ├───────────────────────────────────────────────────▶│
  │                                                    │
  │  13. HeartbeatRes                                  │
  │◀───────────────────────────────────────────────────┤
  │                                                    │
  │  14. Disconnect / Close                            │
  ├───────────────────────────────────────────────────▶│
  │                                                    │
  │                           15. OnActorConnection    │
  │                               Changed(false)       │
  │                                                    │
  │                           (Actor는 Stage에 유지,    │
  │                            재연결 대기)             │
  │                                                    │
```

**핵심 변경사항:**
- **HTTP API 기반 토큰 발급**: 소켓 연결 전에 HTTP로 roomToken 발급
- **인증 분리**: 로그인은 Web Server에서, 토큰 검증은 Room Server에서
- **Actor 유지**: 연결 끊김 시 Actor는 Stage에 유지되어 재연결 지원
- **OnActorConnectionChanged**: 연결/끊김 시마다 Stage에 알림

### 2.2 단계별 상세 설명

#### 1-2. HTTP 로그인 (Web Server)

클라이언트는 Web Server에 HTTP로 로그인합니다. 이는 PlayHouse-NET의 범위 밖이며, 각 게임에서 자유롭게 구현합니다.

```http
POST /api/auth/login
Content-Type: application/json

{
  "username": "player1",
  "password": "secret"
}

Response:
{
  "success": true,
  "accountId": 1001,
  "accessToken": "jwt-access-token",
  "displayName": "Player One"
}
```

#### 3-6. HTTP 방 입장 토큰 발급 (Web Server → Room Server)

로그인 후, Web Server의 HTTP API로 방 입장 토큰을 발급받습니다.

```http
POST /api/rooms/join
Authorization: Bearer jwt-access-token
Content-Type: application/json

{
  "roomType": "BattleStage",
  "roomId": null,                    // null이면 새 방 생성 또는 적합한 방 찾기
  "userInfo": {                      // 게임 컨텐츠 패킷 - Stage.OnJoinRoom에 전달
    "characterId": 123,
    "nickname": "Player One",
    "level": 10
  }
}

Response:
{
  "success": true,
  "roomToken": "eyJhbGciOiJIUzI1NiIs...",  // Room Server 연결용 토큰
  "endpoint": "tcp://room-server:9000",     // Room Server 주소
  "stageId": 12345,
  "roomInfo": {
    "maxPlayers": 4,
    "currentPlayers": 0
  }
}
```

**roomToken 내용 (서버에서 검증):**
```json
{
  "accountId": 1001,
  "stageId": 12345,
  "userInfo": { "characterId": 123, "nickname": "Player One", "level": 10 },
  "exp": 1701234567  // 토큰 만료 시간
}
```

#### 7-9. 소켓 연결 및 입장 (Client → Room Server)

roomToken을 가지고 Room Server에 직접 연결합니다.

```
TCP 연결:
- 서버 주소: tcp://room-server:9000
- 연결 시 roomToken 전송 (첫 패킷)

WebSocket 연결:
- 서버 주소: ws://room-server:9001
- HTTP Upgrade 핸드셰이크
- 쿼리 스트링 또는 첫 패킷으로 roomToken 전송
```

연결 후 첫 패킷으로 roomToken 전송:
```
Request: ConnectWithToken
{
  "MsgId": "ConnectWithToken",
  "MsgSeq": 1,
  "StageId": 0,
  "ErrorCode": 0,
  "Body": {
    "roomToken": "eyJhbGciOiJIUzI1NiIs..."
  }
}
```

Room Server는 토큰을 검증하고, 내부적으로 다음 순서로 처리합니다:
1. `ValidateToken` - 토큰 유효성 검증, AccountId/StageId 추출
2. `OnJoinRoom(actor, userInfo)` - Stage 콜백 (최초 입장 시)
3. `actor.OnCreate()` - Actor 초기화 (최초 입장 시)
4. `actor.OnAuthenticate(authData)` - Actor 인증 완료 (매 연결 시)
5. `OnPostJoinRoom(actor)` - Stage 후처리 (최초 입장 시)
6. `OnActorConnectionChanged(actor, isConnected=true)` - 연결 알림

입장 성공 응답:
```
Response: JoinRoomRes
{
  "MsgId": "JoinRoomRes",
  "MsgSeq": 1,
  "StageId": 12345,
  "ErrorCode": 0,
  "Body": {
    "stageInfo": {
      "stageId": 12345,
      "maxPlayers": 4,
      "currentPlayers": 1
    },
    "players": [
      { "accountId": 1001, "name": "Player One" }
    ],
    "gameState": { ... }  // 현재 게임 상태 (옵션)
  }
}
```

입장 실패 시:
```
Response: JoinRoomRes
{
  "MsgId": "JoinRoomRes",
  "MsgSeq": 1,
  "StageId": 0,
  "ErrorCode": 1000,  // StageFull
  "Body": {
    "message": "Stage is full"
  }
}
```

#### 14-15. 게임 메시지 송수신

클라이언트 → 서버 (Player Action):
```
{
  "MsgId": "PlayerMove",
  "MsgSeq": 3,
  "StageId": 12345,
  "ErrorCode": 0,
  "Body": {
    "position": { "x": 100, "y": 200, "z": 0 },
    "velocity": { "x": 10, "y": 0, "z": 0 }
  }
}
```

서버 → 클라이언트 (State Update - Push):
```
{
  "MsgId": "GameStateUpdate",
  "MsgSeq": 0,  // Push는 MsgSeq 불필요
  "StageId": 12345,
  "ErrorCode": 0,
  "Body": {
    "players": [
      {
        "accountId": 1001,
        "position": { "x": 100, "y": 200, "z": 0 }
      },
      {
        "accountId": 1002,
        "position": { "x": 150, "y": 210, "z": 0 }
      }
    ]
  }
}
```

#### 16-17. Heartbeat

클라이언트는 30초마다 Heartbeat 전송:
```
Request: Heartbeat
{
  "MsgId": "Heartbeat",
  "MsgSeq": 0,  // Heartbeat는 MsgSeq 불필요
  "StageId": 0,
  "ErrorCode": 0,
  "Body": {}
}

Response: HeartbeatRes (옵션)
{
  "MsgId": "HeartbeatRes",
  "MsgSeq": 0,
  "StageId": 0,
  "ErrorCode": 0,
  "Body": {
    "serverTime": 1701234567890
  }
}
```

#### 연결 끊김 및 재연결 대기

클라이언트 연결이 끊기면, Actor는 Stage에 유지됩니다:
```
연결 끊김 시:
- TCP: Socket.Close() 또는 네트워크 오류
- WebSocket: WebSocket.CloseAsync() 또는 네트워크 오류

서버 동작:
1. OnActorConnectionChanged(actor, isConnected=false, reason)
2. Actor는 Stage에 유지 (IsConnected=false)
3. 재연결 타임아웃 타이머 시작 (예: 30초)
4. 타임아웃 시 OnLeaveRoom → OnDestroy 호출
```

#### 명시적 퇴장 (LeaveRoom)

클라이언트가 명시적으로 방을 나갈 때:
```
Request: LeaveRoomReq
{
  "MsgId": "LeaveRoomReq",
  "MsgSeq": 5,
  "StageId": 12345,
  "ErrorCode": 0,
  "Body": {
    "reason": "User requested"
  }
}

서버 동작:
1. OnLeaveRoom(actor, LeaveReason.Normal)
2. actor.OnDestroy()
3. Actor 제거

Response: LeaveRoomRes
{
  "MsgId": "LeaveRoomRes",
  "MsgSeq": 5,
  "StageId": 0,
  "ErrorCode": 0,
  "Body": {
    "success": true
  }
}
```

#### 서버 주도 종료 (강제 킥)

서버가 플레이어를 강제 퇴장시킬 때:
```
Push: KickNotification
{
  "MsgId": "KickNotification",
  "MsgSeq": 0,
  "StageId": 12345,
  "ErrorCode": 0,
  "Body": {
    "reason": "Kicked by admin",
    "canReconnect": false
  }
}

서버 동작:
1. OnLeaveRoom(actor, LeaveReason.Kicked)
2. actor.OnDestroy()
3. 연결 종료
```

## 3. 메시지 타입

### 3.1 시스템 메시지

| MsgId | 방향 | 설명 |
|-------|------|------|
| ConnectWithToken | C→S | 토큰으로 연결 요청 (첫 패킷) |
| JoinRoomRes | S→C | 방 입장 응답 |
| Heartbeat | C→S | 하트비트 |
| HeartbeatRes | S→C | 하트비트 응답 (옵션) |
| KickNotification | S→C | 강제 종료 알림 |

**제거된 메시지:**
- ~~LoginReq/LoginRes~~ - HTTP API로 대체
- ~~CreateStageReq/CreateStageRes~~ - HTTP API로 대체
- ~~JoinStageReq/JoinStageRes~~ - ConnectWithToken으로 대체
- ~~CreateJoinStageReq/CreateJoinStageRes~~ - HTTP API + ConnectWithToken으로 대체

### 3.2 Room 관련 메시지

| MsgId | 방향 | 설명 |
|-------|------|------|
| LeaveRoomReq | C→S | 방 퇴장 요청 |
| LeaveRoomRes | S→C | 방 퇴장 응답 |
| PlayerJoinedNotify | S→C | 다른 플레이어 입장 알림 |
| PlayerLeftNotify | S→C | 다른 플레이어 퇴장 알림 |
| PlayerConnectedNotify | S→C | 플레이어 재연결 알림 |
| PlayerDisconnectedNotify | S→C | 플레이어 연결 끊김 알림 |

### 3.3 게임 메시지 (사용자 정의)

```
애플리케이션별로 자유롭게 정의

예시:
- PlayerMove: 플레이어 이동
- PlayerAttack: 공격
- ChatMessage: 채팅
- GameStateUpdate: 게임 상태 업데이트
- ItemPickup: 아이템 획득
- SkillCast: 스킬 사용
```

## 4. Request-Reply 패턴

### 4.1 MsgSeq를 통한 매칭

```csharp
public class GameClient
{
    private ushort _msgSeqCounter = 0;
    private readonly Dictionary<ushort, TaskCompletionSource<IPacket>> _pendingRequests = new();

    public async Task<IPacket> SendRequestAsync(string msgId, object body,
        TimeSpan timeout)
    {
        // MsgSeq 생성
        var msgSeq = ++_msgSeqCounter;

        // TaskCompletionSource 등록
        var tcs = new TaskCompletionSource<IPacket>();
        _pendingRequests[msgSeq] = tcs;

        // 패킷 전송
        var packet = CreatePacket(msgId, msgSeq, 0, body);
        await SendPacketAsync(packet);

        // 타임아웃과 함께 대기
        using var cts = new CancellationTokenSource(timeout);
        cts.Token.Register(() => tcs.TrySetCanceled());

        try
        {
            return await tcs.Task;
        }
        finally
        {
            _pendingRequests.Remove(msgSeq);
        }
    }

    public void OnPacketReceived(IPacket packet)
    {
        // MsgSeq 확인
        if (_pendingRequests.TryRemove(packet.MsgSeq, out var tcs))
        {
            // Request의 응답
            tcs.SetResult(packet);
        }
        else
        {
            // Push 메시지
            HandlePushMessage(packet);
        }
    }
}
```

### 4.2 사용 예시

```csharp
// 로그인
var loginRes = await client.SendRequestAsync("LoginReq", new
{
    username = "player1",
    password = "secret"
}, TimeSpan.FromSeconds(5));

if (loginRes.ErrorCode == 0)
{
    var data = loginRes.Payload.Parse<LoginResData>();
    Console.WriteLine($"Logged in as {data.AccountId}");
}

// Stage 입장
var joinRes = await client.SendRequestAsync("JoinStageReq", new
{
    playerName = "Player One"
}, TimeSpan.FromSeconds(5));

if (joinRes.ErrorCode == 0)
{
    Console.WriteLine("Joined stage successfully");
}
```

## 5. 재연결 처리

새 설계에서 재연결은 기존 roomToken을 사용합니다. Actor는 연결이 끊겨도 Stage에 유지되므로, 재연결 시 `OnJoinRoom`이 호출되지 않습니다.

### 5.1 재연결 플로우

```
[재연결 시나리오]

Client                              Room Server
  │                                       │
  │  1. 연결 끊김 감지                     │
  X◀──────────────────────────────────────│
  │                                       │
  │         (서버: OnActorConnectionChanged(false))
  │         (Actor는 Stage에 유지, 타임아웃 시작)
  │                                       │
  │  2. 재연결 시도 (3회)                  │
  │     Backoff: 1s, 2s, 4s               │
  │                                       │
  │  3. TCP/WebSocket Connect             │
  ├───────────────────────────────────────▶│
  │                                       │
  │  4. ConnectWithToken (기존 roomToken)  │
  ├───────────────────────────────────────▶│
  │                                       │
  │         5. ValidateToken               │
  │            AccountId로 기존 Actor 찾기  │
  │            Actor 세션 갱신              │
  │                                       │
  │         6. actor.OnAuthenticate()      │
  │            (재연결 시에도 호출!)         │
  │                                       │
  │         7. OnActorConnectionChanged    │
  │            (actor, isConnected=true)   │
  │                                       │
  │  8. JoinRoomRes (reconnected=true)     │
  │     + 현재 게임 상태                    │
  │◀───────────────────────────────────────┤
  │                                       │
  │  (게임 계속)                           │
  │                                       │
```

**핵심 차이점:**
- `OnJoinRoom`은 호출되지 않음 (이미 Stage에 있음)
- `OnCreate`도 호출되지 않음 (이미 생성됨)
- `OnAuthenticate`는 재연결 시에도 호출됨 (매 연결마다)
- 서버가 Actor의 현재 상태를 `OnAuthenticate`에서 클라이언트에 전송

### 5.2 재연결 메시지

재연결도 기존 roomToken을 사용합니다:

```
Request: ConnectWithToken (재연결)
{
  "MsgId": "ConnectWithToken",
  "MsgSeq": 1,
  "StageId": 0,
  "ErrorCode": 0,
  "Body": {
    "roomToken": "eyJhbGciOiJIUzI1NiIs..."  // 기존 토큰 재사용
  }
}

Response: JoinRoomRes (재연결 성공)
{
  "MsgId": "JoinRoomRes",
  "MsgSeq": 1,
  "StageId": 12345,
  "ErrorCode": 0,
  "Body": {
    "reconnected": true,       // 재연결 플래그
    "stageInfo": { ... },
    "players": [ ... ],
    "gameState": { ... }       // 현재 게임 상태
  }
}
```

재연결 실패 (Actor가 이미 제거됨):
```
Response: JoinRoomRes
{
  "MsgId": "JoinRoomRes",
  "MsgSeq": 1,
  "StageId": 0,
  "ErrorCode": 5,  // ActorNotFound - 타임아웃으로 제거됨
  "Body": {
    "message": "Actor not found. Reconnect timeout exceeded."
  }
}
```

### 5.3 클라이언트 재연결 로직

```csharp
public class GameClient
{
    private string? _roomToken;
    private string? _serverEndpoint;
    private const int MaxRetries = 3;
    private readonly int[] _backoffDelays = { 1000, 2000, 4000 }; // ms

    public async Task<bool> ReconnectAsync()
    {
        if (string.IsNullOrEmpty(_roomToken))
        {
            LOG.Warn("No room token available for reconnection");
            return false;
        }

        for (int attempt = 0; attempt < MaxRetries; attempt++)
        {
            try
            {
                LOG.Info($"Reconnect attempt {attempt + 1}/{MaxRetries}");

                // 서버에 재연결
                await ConnectAsync(_serverEndpoint);

                // 기존 토큰으로 연결 요청
                var response = await SendRequestAsync("ConnectWithToken", new
                {
                    roomToken = _roomToken
                }, TimeSpan.FromSeconds(5));

                if (response.ErrorCode == 0)
                {
                    var data = response.Payload.Parse<JoinRoomResData>();

                    if (data.Reconnected)
                    {
                        LOG.Info("Reconnected successfully - restoring game state");
                        // Actor.OnAuthenticate에서 상태 동기화 메시지가 Push됨
                    }
                    else
                    {
                        LOG.Info("Connected as new player");
                    }

                    return true;
                }
                else if (response.ErrorCode == 5) // ActorNotFound
                {
                    LOG.Warn("Actor removed due to timeout - need to rejoin");
                    // 토큰 무효 → HTTP API로 새 토큰 발급 필요
                    _roomToken = null;
                    break;
                }
                else
                {
                    LOG.Warn($"Reconnect failed: {response.ErrorCode}");
                    break;
                }
            }
            catch (Exception ex)
            {
                LOG.Error(ex, $"Reconnect attempt {attempt + 1} failed");

                if (attempt < MaxRetries - 1)
                {
                    await Task.Delay(_backoffDelays[attempt]);
                }
            }
        }

        return false;
    }
}
```

## 6. 에러 처리

### 6.1 에러 코드

```csharp
public enum ErrorCode : ushort
{
    Success = 0,

    // 시스템 오류 (1-999)
    UnknownError = 1,
    InvalidPacket = 2,
    Timeout = 3,
    StageNotFound = 4,
    ActorNotFound = 5,
    Unauthorized = 6,
    InternalError = 7,
    InvalidState = 8,
    RateLimitExceeded = 9,

    // Stage 오류 (1000-1999)
    StageFull = 1000,
    StageAlreadyExists = 1001,
    AlreadyInStage = 1002,
    NotInStage = 1003,

    // 사용자 정의 (2000+)
    CustomError1 = 2000,
    CustomError2 = 2001,
}
```

### 6.2 에러 처리 예시

```csharp
var response = await client.SendRequestAsync("JoinStageReq", joinData,
    TimeSpan.FromSeconds(5));

switch (response.ErrorCode)
{
    case 0: // Success
        OnJoinSuccess(response);
        break;

    case 1000: // StageFull
        ShowMessage("Stage is full. Please try another one.");
        break;

    case 6: // Unauthorized
        ShowMessage("Please login first.");
        await LoginAsync();
        break;

    default:
        ShowMessage($"Error: {response.ErrorCode}");
        break;
}
```

## 7. 클라이언트 구현 예시

### 7.1 Unity C# 클라이언트

```csharp
public class PlayHouseClient : MonoBehaviour
{
    private TcpClient _tcpClient;
    private NetworkStream _stream;
    private bool _isConnected;
    private ushort _msgSeqCounter = 0;
    private Dictionary<ushort, TaskCompletionSource<Packet>> _pendingRequests = new();

    public async Task ConnectAsync(string host, int port)
    {
        _tcpClient = new TcpClient();
        await _tcpClient.ConnectAsync(host, port);
        _stream = _tcpClient.GetStream();
        _isConnected = true;

        // 수신 루프 시작
        _ = ReceiveLoopAsync();

        Debug.Log("Connected to server");
    }

    public async Task<Packet> SendRequestAsync(string msgId, object body,
        float timeoutSeconds = 5f)
    {
        var msgSeq = ++_msgSeqCounter;
        var tcs = new TaskCompletionSource<Packet>();
        _pendingRequests[msgSeq] = tcs;

        // 패킷 직렬화 및 전송
        var packet = new Packet
        {
            MsgId = msgId,
            MsgSeq = msgSeq,
            StageId = 0,
            ErrorCode = 0,
            Body = JsonUtility.ToJson(body)
        };

        await SendPacketAsync(packet);

        // 타임아웃
        var timeoutTask = Task.Delay(TimeSpan.FromSeconds(timeoutSeconds));
        var completedTask = await Task.WhenAny(tcs.Task, timeoutTask);

        if (completedTask == timeoutTask)
        {
            _pendingRequests.Remove(msgSeq);
            throw new TimeoutException($"Request timeout: {msgId}");
        }

        return await tcs.Task;
    }

    public void SendPush(string msgId, long stageId, object body)
    {
        var packet = new Packet
        {
            MsgId = msgId,
            MsgSeq = 0,
            StageId = stageId,
            ErrorCode = 0,
            Body = JsonUtility.ToJson(body)
        };

        _ = SendPacketAsync(packet);
    }

    private async Task SendPacketAsync(Packet packet)
    {
        var data = SerializePacket(packet);
        await _stream.WriteAsync(data, 0, data.Length);
    }

    private async Task ReceiveLoopAsync()
    {
        var buffer = new byte[8192];

        while (_isConnected)
        {
            try
            {
                var bytesRead = await _stream.ReadAsync(buffer, 0, buffer.Length);

                if (bytesRead == 0)
                {
                    // 연결 종료
                    OnDisconnected();
                    break;
                }

                // 패킷 파싱
                var packets = ParsePackets(buffer, bytesRead);

                foreach (var packet in packets)
                {
                    OnPacketReceived(packet);
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"Receive error: {ex.Message}");
                OnDisconnected();
                break;
            }
        }
    }

    private void OnPacketReceived(Packet packet)
    {
        if (_pendingRequests.TryGetValue(packet.MsgSeq, out var tcs))
        {
            // Request-Reply
            _pendingRequests.Remove(packet.MsgSeq);
            tcs.SetResult(packet);
        }
        else
        {
            // Push 메시지
            HandlePushMessage(packet);
        }
    }

    private void HandlePushMessage(Packet packet)
    {
        switch (packet.MsgId)
        {
            case "GameStateUpdate":
                OnGameStateUpdate(packet);
                break;

            case "PlayerJoined":
                OnPlayerJoined(packet);
                break;

            case "PlayerLeft":
                OnPlayerLeft(packet);
                break;

            default:
                Debug.LogWarning($"Unknown push message: {packet.MsgId}");
                break;
        }
    }

    private void OnDisconnected()
    {
        _isConnected = false;
        Debug.Log("Disconnected from server");

        // 재연결 시도
        _ = ReconnectAsync();
    }
}
```

### 7.2 JavaScript/TypeScript 클라이언트

```typescript
class PlayHouseClient {
    private ws: WebSocket;
    private msgSeqCounter: number = 0;
    private pendingRequests: Map<number, {
        resolve: (packet: Packet) => void,
        reject: (error: Error) => void
    }> = new Map();

    async connect(url: string): Promise<void> {
        return new Promise((resolve, reject) => {
            this.ws = new WebSocket(url);
            this.ws.binaryType = 'arraybuffer';

            this.ws.onopen = () => {
                console.log('Connected');
                resolve();
            };

            this.ws.onerror = (error) => {
                reject(error);
            };

            this.ws.onmessage = (event) => {
                this.onMessage(event.data);
            };

            this.ws.onclose = () => {
                this.onDisconnected();
            };
        });
    }

    async sendRequest(msgId: string, body: any, timeoutMs: number = 5000): Promise<Packet> {
        return new Promise((resolve, reject) => {
            const msgSeq = ++this.msgSeqCounter;

            // 타임아웃 설정
            const timeout = setTimeout(() => {
                this.pendingRequests.delete(msgSeq);
                reject(new Error(`Request timeout: ${msgId}`));
            }, timeoutMs);

            // 요청 등록
            this.pendingRequests.set(msgSeq, {
                resolve: (packet) => {
                    clearTimeout(timeout);
                    resolve(packet);
                },
                reject: (error) => {
                    clearTimeout(timeout);
                    reject(error);
                }
            });

            // 패킷 전송
            const packet: Packet = {
                msgId,
                msgSeq,
                stageId: 0,
                errorCode: 0,
                body
            };

            this.sendPacket(packet);
        });
    }

    sendPush(msgId: string, stageId: number, body: any): void {
        const packet: Packet = {
            msgId,
            msgSeq: 0,
            stageId,
            errorCode: 0,
            body
        };

        this.sendPacket(packet);
    }

    private sendPacket(packet: Packet): void {
        const data = this.serializePacket(packet);
        this.ws.send(data);
    }

    private onMessage(data: ArrayBuffer): void {
        const packet = this.deserializePacket(data);

        if (this.pendingRequests.has(packet.msgSeq)) {
            // Request-Reply
            const pending = this.pendingRequests.get(packet.msgSeq)!;
            this.pendingRequests.delete(packet.msgSeq);
            pending.resolve(packet);
        } else {
            // Push 메시지
            this.handlePushMessage(packet);
        }
    }

    private handlePushMessage(packet: Packet): void {
        switch (packet.msgId) {
            case 'GameStateUpdate':
                this.onGameStateUpdate(packet);
                break;

            case 'PlayerJoined':
                this.onPlayerJoined(packet);
                break;

            case 'PlayerLeft':
                this.onPlayerLeft(packet);
                break;

            default:
                console.warn(`Unknown push message: ${packet.msgId}`);
                break;
        }
    }

    private onDisconnected(): void {
        console.log('Disconnected');
        this.reconnect();
    }

    private async reconnect(): Promise<void> {
        const maxRetries = 3;
        const backoffDelays = [1000, 2000, 4000];

        for (let i = 0; i < maxRetries; i++) {
            try {
                console.log(`Reconnect attempt ${i + 1}/${maxRetries}`);
                await this.connect(this.url);
                await this.sendReconnect();
                return;
            } catch (error) {
                console.error(`Reconnect failed: ${error}`);
                if (i < maxRetries - 1) {
                    await this.delay(backoffDelays[i]);
                }
            }
        }

        console.error('Failed to reconnect');
    }

    private delay(ms: number): Promise<void> {
        return new Promise(resolve => setTimeout(resolve, ms));
    }
}
```

## 8. 베스트 프랙티스

### 8.1 Do (권장)

```
1. MsgSeq 관리
   - Request는 고유 MsgSeq 할당
   - Push는 MsgSeq = 0

2. 타임아웃 설정
   - 모든 Request에 타임아웃 적용
   - 기본: 5초

3. Heartbeat
   - 30초마다 전송
   - 서버 타임아웃: 90초

4. 재연결
   - 자동 재연결 구현
   - Exponential Backoff 사용
   - 최대 3회 시도

5. 에러 처리
   - ErrorCode별 적절한 처리
   - 사용자 친화적 메시지 표시
```

### 8.2 Don't (금지)

```
1. MsgSeq 재사용
   - 응답 받기 전 같은 MsgSeq 사용 금지

2. 무한 대기
   - 타임아웃 없는 Request 금지

3. Heartbeat 무시
   - Heartbeat 전송 안하면 연결 종료됨

4. 동기 블로킹
   - UI 스레드에서 블로킹 호출 금지
   - async/await 사용

5. 에러 무시
   - ErrorCode != 0 무시 금지
   - 적절한 에러 처리 필수
```

## 9. 트러블슈팅

### 9.1 일반적인 문제

```
문제: 연결 후 즉시 끊김
원인: Heartbeat 미전송
해결: 30초마다 Heartbeat 전송

문제: Request 타임아웃
원인: 서버 응답 없음 또는 네트워크 지연
해결: 타임아웃 증가, 재시도 로직 추가

문제: MsgSeq 불일치
원인: 응답 전 같은 MsgSeq 재사용
해결: MsgSeq 증가 관리 개선

문제: 재연결 실패
원인: 토큰 만료
해결: 재로그인 후 다시 입장
```

## 10. 다음 단계

- `02-packet-structure.md`: 패킷 직렬화/역직렬화 상세
- `06-socket-transport.md`: 전송 계층 구현
- `03-stage-actor-model.md`: Stage/Actor 동작 방식
