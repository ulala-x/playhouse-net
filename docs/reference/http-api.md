# PlayHouse-NET HTTP API

## 1. 개요

PlayHouse-NET은 서버 관리 및 모니터링을 위한 RESTful HTTP API를 내장 제공합니다. ASP.NET Core 기반으로 구현되어 Swagger UI를 통한 API 문서 자동 생성 및 테스트를 지원합니다.

### 1.1 주요 기능

- **Stage 관리**: 생성, 조회, 삭제
- **서버 모니터링**: 상태 조회, 통계
- **Health Check**: 헬스체크 엔드포인트
- **인증**: Token 기반 인증 (옵션)

### 1.2 기술 스택

- **프레임워크**: ASP.NET Core 8.0+
- **문서화**: Swagger/OpenAPI
- **포맷**: JSON
- **인증**: JWT (옵션)

## 2. API 엔드포인트 설계

### 2.1 전체 엔드포인트 목록

#### 방 입장 API (Web Server → Room Server)

| 메서드 | 경로 | 설명 | 인증 |
|--------|------|------|------|
| POST | `/api/rooms/get-or-create` | 방 조회 또는 생성 + 토큰 발급 | 필요 |
| POST | `/api/rooms/join` | 기존 방 입장 토큰 발급 | 필요 |
| DELETE | `/api/rooms/{stageId}/leave` | 방 퇴장 (Actor 제거) | 필요 |

#### 관리 API

| 메서드 | 경로 | 설명 | 인증 |
|--------|------|------|------|
| GET | `/health` | 헬스체크 | 불필요 |
| GET | `/api/server/info` | 서버 정보 조회 | 불필요 |
| GET | `/api/server/stats` | 서버 통계 조회 | 필요 |
| POST | `/api/stages` | Stage 생성 | 필요 |
| GET | `/api/stages` | Stage 목록 조회 | 필요 |
| GET | `/api/stages/{stageId}` | Stage 상세 조회 | 필요 |
| DELETE | `/api/stages/{stageId}` | Stage 삭제 | 필요 |
| GET | `/api/stages/{stageId}/actors` | Stage 내 Actor 목록 | 필요 |
| POST | `/api/sessions/{sessionId}/close` | 세션 강제 종료 | 필요 |

### 2.2 Swagger UI

```
기본 URL: http://localhost:8080/swagger
- API 목록 및 스키마
- 실시간 테스트 도구
- Request/Response 예시
```

## 3. 방 입장 API

이 섹션의 API들은 Web Server에서 호출하여 클라이언트의 방 입장을 처리합니다.
클라이언트는 이 API의 응답으로 받은 `roomToken`을 사용하여 Room Server에 직접 소켓 연결합니다.

### 3.1 POST /api/rooms/get-or-create

방을 조회하거나 새로 생성하고, 입장 토큰을 발급합니다.

#### Request

```http
POST /api/rooms/get-or-create HTTP/1.1
Host: room-server:8080
Authorization: Bearer {server-to-server-token}
Content-Type: application/json

{
  "roomType": "BattleStage",
  "roomId": null,                     // null이면 새 방 생성 또는 적합한 방 찾기
  "accountId": 1001,                  // 입장할 플레이어 AccountId
  "userInfo": {                       // Stage.OnJoinRoom에 전달될 콘텐츠 패킷
    "characterId": 123,
    "nickname": "Player One",
    "level": 10,
    "team": "blue"
  },
  "createOptions": {                  // 새 방 생성 시 옵션 (옵션)
    "maxPlayers": 4,
    "gameMode": "DeathMatch",
    "mapName": "Arena01"
  }
}
```

#### Response (200 OK) - 새 방 생성

```json
{
  "success": true,
  "isNewRoom": true,
  "stageId": 12345,
  "roomToken": "eyJhbGciOiJIUzI1NiIs...",
  "endpoint": "tcp://room-server:9000",
  "tokenExpiresAt": "2024-12-09T10:35:00Z",
  "roomInfo": {
    "roomType": "BattleStage",
    "maxPlayers": 4,
    "currentPlayers": 0,
    "createdAt": "2024-12-09T10:30:00Z"
  }
}
```

#### Response (200 OK) - 기존 방 입장

```json
{
  "success": true,
  "isNewRoom": false,
  "stageId": 12345,
  "roomToken": "eyJhbGciOiJIUzI1NiIs...",
  "endpoint": "tcp://room-server:9000",
  "tokenExpiresAt": "2024-12-09T10:35:00Z",
  "roomInfo": {
    "roomType": "BattleStage",
    "maxPlayers": 4,
    "currentPlayers": 2,
    "createdAt": "2024-12-09T10:25:00Z"
  }
}
```

#### Response (400 Bad Request) - 방이 가득 참

```json
{
  "success": false,
  "error": "RoomFull",
  "message": "Room is full (4/4 players)"
}
```

**roomToken 페이로드 (서버에서 생성, 검증용):**

```json
{
  "accountId": 1001,
  "stageId": 12345,
  "userInfo": { "characterId": 123, "nickname": "Player One", "level": 10 },
  "iat": 1701234500,
  "exp": 1701234800   // 5분 후 만료
}
```

### 3.2 POST /api/rooms/join

특정 방에 대한 입장 토큰만 발급합니다. 방이 존재해야 합니다.

#### Request

```http
POST /api/rooms/join HTTP/1.1
Host: room-server:8080
Authorization: Bearer {server-to-server-token}
Content-Type: application/json

{
  "stageId": 12345,
  "accountId": 1001,
  "userInfo": {
    "characterId": 123,
    "nickname": "Player One",
    "level": 10
  }
}
```

#### Response (200 OK)

```json
{
  "success": true,
  "stageId": 12345,
  "roomToken": "eyJhbGciOiJIUzI1NiIs...",
  "endpoint": "tcp://room-server:9000",
  "tokenExpiresAt": "2024-12-09T10:35:00Z",
  "roomInfo": {
    "roomType": "BattleStage",
    "maxPlayers": 4,
    "currentPlayers": 2
  }
}
```

#### Response (404 Not Found)

```json
{
  "success": false,
  "error": "RoomNotFound",
  "message": "Room not found: 12345"
}
```

### 3.3 DELETE /api/rooms/{stageId}/leave

방에서 특정 Actor를 제거합니다. 연결이 끊긴 Actor를 정리할 때 사용합니다.

#### Request

```http
DELETE /api/rooms/12345/leave HTTP/1.1
Host: room-server:8080
Authorization: Bearer {server-to-server-token}
Content-Type: application/json

{
  "accountId": 1001,
  "reason": "timeout"
}
```

#### Response (204 No Content)

```
(빈 응답)
```

#### Response (404 Not Found)

```json
{
  "success": false,
  "error": "ActorNotFound",
  "message": "Actor 1001 not found in room 12345"
}
```

### 3.4 방 입장 흐름 다이어그램

```
[클라이언트 방 입장 흐름]

Client                  Web Server              Room Server
  │                         │                        │
  │  1. POST /join-room     │                        │
  │     (credentials,       │                        │
  │      roomType)          │                        │
  ├────────────────────────▶│                        │
  │                         │                        │
  │                         │  2. POST /api/rooms/   │
  │                         │     get-or-create      │
  │                         │     (accountId,        │
  │                         │      roomType,         │
  │                         │      userInfo)         │
  │                         ├───────────────────────▶│
  │                         │                        │
  │                         │     3. CreateStage     │
  │                         │        (if needed)     │
  │                         │        OnCreate()      │
  │                         │        OnPostCreate()  │
  │                         │                        │
  │                         │  4. Response           │
  │                         │     (roomToken,        │
  │                         │      endpoint)         │
  │                         │◀───────────────────────┤
  │                         │                        │
  │  5. Response            │                        │
  │     (roomToken,         │                        │
  │      endpoint)          │                        │
  │◀────────────────────────┤                        │
  │                         │                        │
  │  6. TCP/WS Connect (with roomToken)              │
  ├──────────────────────────────────────────────────▶│
  │                         │                        │
  │                         │  7. ValidateToken      │
  │                         │     OnJoinRoom()       │
  │                         │     actor.OnCreate()   │
  │                         │     OnAuthenticate()   │
  │                         │                        │
  │  8. JoinRoomRes                                  │
  │◀──────────────────────────────────────────────────┤
  │                         │                        │
```

### 3.5 Backend SDK (PlayHouse.Backend)

Web Server에서 Room Server HTTP API를 쉽게 호출할 수 있도록 `PlayHouse.Backend` NuGet 패키지를 제공합니다.

#### 설치

```bash
dotnet add package PlayHouse.Backend
```

#### 서비스 등록

```csharp
// Program.cs
var builder = WebApplication.CreateBuilder(args);

// PlayHouse Backend SDK 등록
builder.Services.AddPlayHouseBackend(options =>
{
    options.RoomServerUrl = "http://room-server:8080";
    options.ServerSecret = "your-server-to-server-secret";
    options.RequestTimeoutMs = 5000;
});

var app = builder.Build();
```

#### IRoomServerClient 인터페이스

```csharp
#nullable enable

/// <summary>
/// Room Server HTTP API 클라이언트 인터페이스.
/// Web Server에서 DI로 주입받아 사용합니다.
/// </summary>
public interface IRoomServerClient
{
    /// <summary>
    /// 방을 조회하거나 새로 생성하고, 입장 토큰을 발급합니다.
    /// </summary>
    /// <param name="request">방 입장 요청</param>
    /// <param name="cancellationToken">취소 토큰</param>
    /// <returns>방 입장 응답 (roomToken, endpoint 포함)</returns>
    Task<GetOrCreateRoomResponse> GetOrCreateRoomAsync(
        GetOrCreateRoomRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 특정 방에 대한 입장 토큰을 발급합니다.
    /// </summary>
    /// <param name="request">방 입장 요청</param>
    /// <param name="cancellationToken">취소 토큰</param>
    /// <returns>방 입장 응답</returns>
    Task<JoinRoomResponse> JoinRoomAsync(
        JoinRoomRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 방에서 Actor를 제거합니다 (연결 끊긴 Actor 정리용).
    /// </summary>
    /// <param name="stageId">Stage ID</param>
    /// <param name="accountId">Account ID</param>
    /// <param name="reason">퇴장 사유</param>
    /// <param name="cancellationToken">취소 토큰</param>
    Task LeaveRoomAsync(
        int stageId,
        long accountId,
        string? reason = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Room Server 헬스체크
    /// </summary>
    Task<HealthCheckResponse> HealthCheckAsync(
        CancellationToken cancellationToken = default);
}
```

#### DTO 정의

```csharp
/// <summary>방 생성/입장 요청</summary>
public record GetOrCreateRoomRequest
{
    public required string RoomType { get; init; }
    public int? RoomId { get; init; }
    public required long AccountId { get; init; }
    public required object UserInfo { get; init; }  // 게임별 컨텐츠 패킷
    public CreateRoomOptions? CreateOptions { get; init; }
}

/// <summary>방 생성 옵션</summary>
public record CreateRoomOptions
{
    public int MaxPlayers { get; init; } = 4;
    public string? GameMode { get; init; }
    public string? MapName { get; init; }
    public Dictionary<string, object>? CustomData { get; init; }
}

/// <summary>방 생성/입장 응답</summary>
public record GetOrCreateRoomResponse
{
    public bool Success { get; init; }
    public bool IsNewRoom { get; init; }
    public int StageId { get; init; }
    public string? RoomToken { get; init; }
    public string? Endpoint { get; init; }
    public DateTime? TokenExpiresAt { get; init; }
    public RoomInfo? RoomInfo { get; init; }
    public string? Error { get; init; }
    public string? Message { get; init; }
}

/// <summary>방 입장 요청</summary>
public record JoinRoomRequest
{
    public required int StageId { get; init; }
    public required long AccountId { get; init; }
    public required object UserInfo { get; init; }
}

/// <summary>방 입장 응답</summary>
public record JoinRoomResponse
{
    public bool Success { get; init; }
    public int StageId { get; init; }
    public string? RoomToken { get; init; }
    public string? Endpoint { get; init; }
    public DateTime? TokenExpiresAt { get; init; }
    public RoomInfo? RoomInfo { get; init; }
    public string? Error { get; init; }
    public string? Message { get; init; }
}

/// <summary>방 정보</summary>
public record RoomInfo
{
    public string? RoomType { get; init; }
    public int MaxPlayers { get; init; }
    public int CurrentPlayers { get; init; }
    public DateTime? CreatedAt { get; init; }
}
```

#### Web Server 사용 예시

```csharp
[ApiController]
[Route("api/game")]
public class GameController : ControllerBase
{
    private readonly IRoomServerClient _roomServerClient;
    private readonly ILogger<GameController> _logger;

    public GameController(
        IRoomServerClient roomServerClient,
        ILogger<GameController> logger)
    {
        _roomServerClient = roomServerClient;
        _logger = logger;
    }

    /// <summary>
    /// 클라이언트의 방 입장 요청 처리
    /// </summary>
    [HttpPost("join-room")]
    [Authorize]  // JWT 인증
    public async Task<IActionResult> JoinRoom(
        [FromBody] ClientJoinRoomRequest request)
    {
        // JWT에서 accountId 추출
        var accountId = long.Parse(User.FindFirst("sub")?.Value ?? "0");

        try
        {
            // Room Server에 방 입장 요청
            var response = await _roomServerClient.GetOrCreateRoomAsync(new GetOrCreateRoomRequest
            {
                RoomType = request.RoomType,
                RoomId = request.RoomId,
                AccountId = accountId,
                UserInfo = new
                {
                    CharacterId = request.CharacterId,
                    Nickname = request.Nickname,
                    Level = request.Level
                },
                CreateOptions = new CreateRoomOptions
                {
                    MaxPlayers = request.MaxPlayers ?? 4,
                    GameMode = request.GameMode
                }
            });

            if (!response.Success)
            {
                _logger.LogWarning("Failed to join room: {Error}", response.Message);
                return BadRequest(new { error = response.Error, message = response.Message });
            }

            // 클라이언트에게 roomToken과 endpoint 반환
            return Ok(new
            {
                success = true,
                roomToken = response.RoomToken,
                endpoint = response.Endpoint,
                stageId = response.StageId,
                expiresAt = response.TokenExpiresAt,
                roomInfo = response.RoomInfo
            });
        }
        catch (RoomServerException ex)
        {
            _logger.LogError(ex, "Room server error");
            return StatusCode(503, new { error = "RoomServerUnavailable", message = ex.Message });
        }
    }
}

/// <summary>클라이언트 요청 DTO</summary>
public record ClientJoinRoomRequest
{
    public required string RoomType { get; init; }
    public int? RoomId { get; init; }
    public int CharacterId { get; init; }
    public string? Nickname { get; init; }
    public int Level { get; init; }
    public int? MaxPlayers { get; init; }
    public string? GameMode { get; init; }
}
```

#### 에러 처리

```csharp
/// <summary>Room Server 통신 예외</summary>
public class RoomServerException : Exception
{
    public string? ErrorCode { get; }
    public int? HttpStatusCode { get; }

    public RoomServerException(string message, string? errorCode = null, int? httpStatusCode = null)
        : base(message)
    {
        ErrorCode = errorCode;
        HttpStatusCode = httpStatusCode;
    }
}

// 사용 예시
try
{
    var response = await _roomServerClient.GetOrCreateRoomAsync(request);
}
catch (RoomServerException ex) when (ex.ErrorCode == "RoomFull")
{
    // 방이 가득 참 - 다른 방 찾기 또는 새 방 생성 로직
}
catch (RoomServerException ex) when (ex.HttpStatusCode == 503)
{
    // Room Server 불가 - 폴백 로직 또는 재시도
}
```

#### 옵션 설정

```csharp
/// <summary>Backend SDK 설정</summary>
public class PlayHouseBackendOptions
{
    public const string SectionName = "PlayHouse:Backend";

    /// <summary>Room Server HTTP 주소</summary>
    [Required]
    public required string RoomServerUrl { get; init; }

    /// <summary>서버 간 인증 시크릿</summary>
    [Required]
    public required string ServerSecret { get; init; }

    /// <summary>요청 타임아웃 (ms)</summary>
    [Range(1000, 60000)]
    public int RequestTimeoutMs { get; init; } = 5000;

    /// <summary>재시도 횟수</summary>
    [Range(0, 5)]
    public int RetryCount { get; init; } = 3;

    /// <summary>회로 차단기 임계값</summary>
    [Range(3, 20)]
    public int CircuitBreakerThreshold { get; init; } = 5;
}
```

#### appsettings.json 예시

```json
{
  "PlayHouse": {
    "Backend": {
      "RoomServerUrl": "http://room-server:8080",
      "ServerSecret": "your-server-to-server-secret",
      "RequestTimeoutMs": 5000,
      "RetryCount": 3,
      "CircuitBreakerThreshold": 5
    }
  }
}
```

## 4. 헬스체크 API

### 4.1 GET /health

서버 상태 확인 (로드 밸런서용)

#### Request

```http
GET /health HTTP/1.1
Host: localhost:8080
```

#### Response (200 OK)

```json
{
  "status": "healthy",
  "timestamp": "2024-12-09T10:30:00Z",
  "uptime": 3600,
  "version": "1.0.0"
}
```

#### Response (503 Service Unavailable)

```json
{
  "status": "unhealthy",
  "timestamp": "2024-12-09T10:30:00Z",
  "uptime": 3600,
  "reason": "Database connection failed"
}
```

## 5. 서버 정보 API

### 5.1 GET /api/server/info

서버 기본 정보 조회

#### Request

```http
GET /api/server/info HTTP/1.1
Host: localhost:8080
```

#### Response (200 OK)

```json
{
  "serverName": "room-server-1",
  "version": "1.0.0",
  "startTime": "2024-12-09T09:00:00Z",
  "uptime": 5400,
  "platform": "linux-x64",
  "dotnetVersion": "8.0.0",
  "endpoints": {
    "http": "http://0.0.0.0:8080",
    "tcp": "tcp://0.0.0.0:9000",
    "ws": "ws://0.0.0.0:9001"
  }
}
```

### 5.2 GET /api/server/stats

서버 통계 조회

#### Request

```http
GET /api/server/stats HTTP/1.1
Host: localhost:8080
Authorization: Bearer {token}
```

#### Response (200 OK)

```json
{
  "timestamp": "2024-12-09T10:30:00Z",
  "stages": {
    "total": 150,
    "byType": {
      "BattleStage": 100,
      "LobbyStage": 50
    }
  },
  "actors": {
    "total": 500,
    "byStage": {
      "average": 3.33,
      "max": 10
    }
  },
  "sessions": {
    "total": 500,
    "connected": 480,
    "authenticated": 450
  },
  "messages": {
    "receivedPerSec": 10000,
    "sentPerSec": 15000,
    "queuedTotal": 120
  },
  "performance": {
    "cpuUsage": 45.5,
    "memoryUsage": 1024,
    "gcCollections": {
      "gen0": 1000,
      "gen1": 100,
      "gen2": 10
    }
  }
}
```

## 6. Stage 관리 API

### 6.1 POST /api/stages

Stage 생성

#### Request

```http
POST /api/stages HTTP/1.1
Host: localhost:8080
Authorization: Bearer {token}
Content-Type: application/json

{
  "stageId": 12345,
  "stageType": "BattleStage",
  "initData": {
    "maxPlayers": 4,
    "gameMode": "DeathMatch",
    "mapName": "Arena01"
  }
}
```

#### Response (201 Created)

```json
{
  "stageId": 12345,
  "stageType": "BattleStage",
  "createdAt": "2024-12-09T10:30:00Z",
  "status": "active",
  "actorCount": 0
}
```

#### Response (400 Bad Request)

```json
{
  "error": "InvalidRequest",
  "message": "Stage ID already exists"
}
```

### 6.2 GET /api/stages

Stage 목록 조회

#### Request

```http
GET /api/stages?type=BattleStage&page=1&size=20 HTTP/1.1
Host: localhost:8080
Authorization: Bearer {token}
```

#### Query Parameters

| 파라미터 | 타입 | 필수 | 설명 |
|---------|------|------|------|
| type | string | X | Stage 타입 필터 |
| page | int | X | 페이지 번호 (기본: 1) |
| size | int | X | 페이지 크기 (기본: 20) |

#### Response (200 OK)

```json
{
  "total": 100,
  "page": 1,
  "size": 20,
  "stages": [
    {
      "stageId": 12345,
      "stageType": "BattleStage",
      "createdAt": "2024-12-09T10:30:00Z",
      "actorCount": 4,
      "status": "active"
    },
    {
      "stageId": 12346,
      "stageType": "BattleStage",
      "createdAt": "2024-12-09T10:31:00Z",
      "actorCount": 2,
      "status": "active"
    }
  ]
}
```

### 6.3 GET /api/stages/{stageId}

Stage 상세 조회

#### Request

```http
GET /api/stages/12345 HTTP/1.1
Host: localhost:8080
Authorization: Bearer {token}
```

#### Response (200 OK)

```json
{
  "stageId": 12345,
  "stageType": "BattleStage",
  "createdAt": "2024-12-09T10:30:00Z",
  "status": "active",
  "actorCount": 4,
  "actors": [
    {
      "accountId": 1001,
      "joinedAt": "2024-12-09T10:31:00Z"
    },
    {
      "accountId": 1002,
      "joinedAt": "2024-12-09T10:31:30Z"
    }
  ],
  "customData": {
    "gameMode": "DeathMatch",
    "mapName": "Arena01",
    "elapsedTime": 120
  }
}
```

#### Response (404 Not Found)

```json
{
  "error": "NotFound",
  "message": "Stage not found: 12345"
}
```

### 6.4 DELETE /api/stages/{stageId}

Stage 강제 삭제

#### Request

```http
DELETE /api/stages/12345 HTTP/1.1
Host: localhost:8080
Authorization: Bearer {token}
```

#### Response (204 No Content)

```
(빈 응답)
```

#### Response (404 Not Found)

```json
{
  "error": "NotFound",
  "message": "Stage not found: 12345"
}
```

### 6.5 GET /api/stages/{stageId}/actors

Stage 내 Actor 목록 조회

#### Request

```http
GET /api/stages/12345/actors HTTP/1.1
Host: localhost:8080
Authorization: Bearer {token}
```

#### Response (200 OK)

```json
{
  "stageId": 12345,
  "actorCount": 4,
  "actors": [
    {
      "accountId": 1001,
      "sessionId": 5001,
      "joinedAt": "2024-12-09T10:31:00Z",
      "lastActivity": "2024-12-09T10:35:00Z"
    },
    {
      "accountId": 1002,
      "sessionId": 5002,
      "joinedAt": "2024-12-09T10:31:30Z",
      "lastActivity": "2024-12-09T10:35:10Z"
    }
  ]
}
```

## 7. 세션 관리 API

### 7.1 POST /api/sessions/{sessionId}/close

세션 강제 종료 (강제 킥)

#### Request

```http
POST /api/sessions/5001/close HTTP/1.1
Host: localhost:8080
Authorization: Bearer {token}
Content-Type: application/json

{
  "reason": "Kicked by admin",
  "notifyClient": true
}
```

#### Response (204 No Content)

```
(빈 응답)
```

#### Response (404 Not Found)

```json
{
  "error": "NotFound",
  "message": "Session not found: 5001"
}
```

## 8. 인증

### 8.1 JWT 기반 인증

#### 토큰 발급 (별도 구현 필요)

```http
POST /api/auth/login HTTP/1.1
Host: localhost:8080
Content-Type: application/json

{
  "username": "admin",
  "password": "secret"
}
```

#### Response

```json
{
  "token": "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...",
  "expiresIn": 3600
}
```

#### 토큰 사용

```http
GET /api/server/stats HTTP/1.1
Host: localhost:8080
Authorization: Bearer eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...
```

### 8.2 API Key 인증 (대안)

```http
GET /api/server/stats HTTP/1.1
Host: localhost:8080
X-API-Key: your-api-key-here
```

## 9. 에러 응답 포맷

### 9.1 표준 에러 응답

```json
{
  "error": "ErrorCode",
  "message": "Human readable error message",
  "timestamp": "2024-12-09T10:30:00Z",
  "path": "/api/stages/12345",
  "details": {
    "additionalInfo": "value"
  }
}
```

### 9.2 HTTP 상태 코드

| 코드 | 의미 | 사용 시나리오 |
|------|------|---------------|
| 200 | OK | 성공 (조회) |
| 201 | Created | 성공 (생성) |
| 204 | No Content | 성공 (삭제) |
| 400 | Bad Request | 잘못된 요청 |
| 401 | Unauthorized | 인증 실패 |
| 403 | Forbidden | 권한 없음 |
| 404 | Not Found | 리소스 없음 |
| 409 | Conflict | 중복된 리소스 |
| 500 | Internal Server Error | 서버 오류 |
| 503 | Service Unavailable | 서비스 불가 |

## 10. 구현 예시

### 10.1 ASP.NET Controller

```csharp
[ApiController]
[Route("api/stages")]
[Authorize] // JWT 인증 필요
public class StageController : ControllerBase
{
    private readonly IRoomServer _roomServer;
    private readonly ILogger<StageController> _logger;

    public StageController(IRoomServer roomServer, ILogger<StageController> logger)
    {
        _roomServer = roomServer;
        _logger = logger;
    }

    /// <summary>
    /// Stage 생성
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(StageResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> CreateStage([FromBody] CreateStageRequest request)
    {
        try
        {
            var result = await _roomServer.CreateStageAsync(
                request.StageId,
                request.StageType,
                request.InitData
            );

            return CreatedAtAction(
                nameof(GetStage),
                new { stageId = request.StageId },
                new StageResponse
                {
                    StageId = result.StageId,
                    StageType = result.StageType,
                    CreatedAt = DateTime.UtcNow,
                    Status = "active",
                    ActorCount = 0
                }
            );
        }
        catch (StageAlreadyExistsException ex)
        {
            return BadRequest(new ErrorResponse
            {
                Error = "StageAlreadyExists",
                Message = ex.Message
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create stage");
            return StatusCode(500, new ErrorResponse
            {
                Error = "InternalError",
                Message = "Failed to create stage"
            });
        }
    }

    /// <summary>
    /// Stage 목록 조회
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(StageListResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetStages(
        [FromQuery] string? type = null,
        [FromQuery] int page = 1,
        [FromQuery] int size = 20)
    {
        var stages = await _roomServer.GetStagesAsync(type, page, size);

        return Ok(new StageListResponse
        {
            Total = stages.Total,
            Page = page,
            Size = size,
            Stages = stages.Items
        });
    }

    /// <summary>
    /// Stage 상세 조회
    /// </summary>
    [HttpGet("{stageId}")]
    [ProducesResponseType(typeof(StageDetailResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetStage(long stageId)
    {
        var stage = await _roomServer.GetStageAsync(stageId);

        if (stage == null)
        {
            return NotFound(new ErrorResponse
            {
                Error = "NotFound",
                Message = $"Stage not found: {stageId}"
            });
        }

        return Ok(stage);
    }

    /// <summary>
    /// Stage 삭제
    /// </summary>
    [HttpDelete("{stageId}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteStage(long stageId)
    {
        var deleted = await _roomServer.DeleteStageAsync(stageId);

        if (!deleted)
        {
            return NotFound(new ErrorResponse
            {
                Error = "NotFound",
                Message = $"Stage not found: {stageId}"
            });
        }

        return NoContent();
    }
}
```

### 10.2 DTO 정의

```csharp
public class CreateStageRequest
{
    public long StageId { get; set; }
    public string StageType { get; set; }
    public Dictionary<string, object>? InitData { get; set; }
}

public class StageResponse
{
    public long StageId { get; set; }
    public string StageType { get; set; }
    public DateTime CreatedAt { get; set; }
    public string Status { get; set; }
    public int ActorCount { get; set; }
}

public class StageListResponse
{
    public int Total { get; set; }
    public int Page { get; set; }
    public int Size { get; set; }
    public List<StageResponse> Stages { get; set; }
}

public class ErrorResponse
{
    public string Error { get; set; }
    public string Message { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}
```

## 11. Swagger 설정

### 11.1 Program.cs 설정

```csharp
var builder = WebApplication.CreateBuilder(args);

// Swagger 추가
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "PlayHouse-NET API",
        Version = "v1",
        Description = "Realtime Game Server Management API"
    });

    // JWT 인증 설정
    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Description = "JWT Authorization header using the Bearer scheme",
        Name = "Authorization",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.ApiKey,
        Scheme = "Bearer"
    });

    options.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            Array.Empty<string>()
        }
    });

    // XML 주석 포함
    var xmlFile = $"{Assembly.GetExecutingAssembly().GetName().Name}.xml";
    var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
    options.IncludeXmlComments(xmlPath);
});

var app = builder.Build();

// Swagger UI 활성화
if (app.Environment.IsDevelopment() || app.Environment.IsProduction())
{
    app.UseSwagger();
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/swagger/v1/swagger.json", "PlayHouse-NET API v1");
        options.RoutePrefix = "swagger"; // /swagger 경로
    });
}

app.Run();
```

## 12. CORS 설정

### 12.1 개발 환경

```csharp
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

app.UseCors("AllowAll");
```

### 12.2 프로덕션 환경

```csharp
builder.Services.AddCors(options =>
{
    options.AddPolicy("Production", policy =>
    {
        policy.WithOrigins("https://yourdomain.com")
              .AllowAnyMethod()
              .AllowAnyHeader()
              .AllowCredentials();
    });
});

app.UseCors("Production");
```

## 13. 속도 제한 (Rate Limiting)

### 13.1 설정

```csharp
builder.Services.AddRateLimiter(options =>
{
    options.AddFixedWindowLimiter("api", opt =>
    {
        opt.PermitLimit = 100;
        opt.Window = TimeSpan.FromMinutes(1);
        opt.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
        opt.QueueLimit = 10;
    });
});

app.UseRateLimiter();
```

### 13.2 컨트롤러 적용

```csharp
[ApiController]
[Route("api/stages")]
[EnableRateLimiting("api")]
public class StageController : ControllerBase
{
    // ...
}
```

## 14. 모니터링 통합

### 14.1 메트릭 수집

```csharp
// Prometheus 메트릭 예시
[HttpGet("/metrics")]
public IActionResult GetMetrics()
{
    return Ok(_metricsCollector.Collect());
}
```

### 14.2 로깅

```csharp
_logger.LogInformation("Stage created: {StageId}, Type: {StageType}",
    request.StageId, request.StageType);

_logger.LogWarning("Stage not found: {StageId}", stageId);

_logger.LogError(ex, "Failed to delete stage: {StageId}", stageId);
```

## 15. 다음 단계

- `06-socket-transport.md`: Socket과 HTTP API의 통합 운영
- `01-architecture.md`: HTTP API와 Core Engine의 통합 구조
