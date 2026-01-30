# 튜토리얼: 로비 + 매칭 시스템 구축

이 튜토리얼에서는 PlayHouse-NET을 사용하여 실제 게임에서 사용되는 **로비 → 매칭 → 게임룸** 시스템을 처음부터 끝까지 구축합니다.

## 목표

- 1시간 안에 완료 가능한 단계별 가이드
- 실제 게임 구조에 가까운 시스템 구축
- PlayHouse의 서버간 통신 패턴 이해

## 전체 아키텍처

```
┌─────────────────────────────────────────────────────────────────┐
│                         클라이언트                               │
└───────────────────────────┬─────────────────────────────────────┘
                            │
         ┌──────────────────┴───────────────────┐
         │ ① HTTP 요청 (매칭/로비)                │
         ▼                                      │
┌─────────────────────────┐                     │
│      API Server         │                     │
│   (LobbyController)     │                     │
│                         │                     │
│  - 매칭 큐 관리          │                     │
│  - 매칭 로직 실행        │                     │
│  ApiSender.CreateStage()│                     │
│         │               │                     │
└─────────┼───────────────┘                     │
          ▼                                     │
┌─────────────────────────┐                     │
│      Play Server        │◀────────────────────┘
│   (GameRoomStage)       │    ② TCP 연결
│                         │    (매칭 후 방 정보로)
│  ┌───────────────────┐  │
│  │   GameRoomStage   │  │
│  │   ┌───────────┐   │  │
│  │   │PlayerActor│   │  │
│  │   └───────────┘   │  │
│  └───────────────────┘  │
└─────────────────────────┘
```

### 흐름 설명

1. **클라이언트 → API Server (HTTP)**
   - 매칭 요청 (`POST /matchmaking/request`)
   - 매칭 상태 확인 (`GET /matchmaking/status`)
   - API Server가 매칭 로직 실행

2. **매칭 완료 시**
   - API Server가 Play Server에 GameRoom 생성 (`ApiSender.CreateStage`)
   - 방 정보(playNid, stageId, stageType) 반환

3. **클라이언트 → Play Server (TCP)**
   - 반환받은 정보로 GameRoom에 직접 연결
   - 인증 후 게임 시작

> **핵심**: 로비와 매칭은 API Server에서 처리하고, 클라이언트는 매칭 완료 후에만 Play Server에 접속합니다.

---

## 아키텍처 구성 요소

### 1. LobbyController (API Server)
- 매칭 큐 관리
- 플레이어 매칭 로직 실행
- GameRoomStage 생성 요청
- **Stateless HTTP 처리**

### 2. GameRoomStage (Play Server)
- 실제 게임이 진행되는 공간
- 매칭된 플레이어들만 입장
- 게임 로직 처리
- **Stateful 실시간 처리**

---

## 단계별 구현

### 1단계: Proto 메시지 정의

먼저 시스템 전체에서 사용할 메시지를 정의합니다.

**Proto/lobby_matching.proto**
```protobuf
syntax = "proto3";

option csharp_namespace = "PlayHouse.Tutorial.LobbyMatching";

// ==================== 인증 (Play Server) ====================
message AuthenticateRequest {
  string player_name = 1;
  string session_token = 2;  // API Server에서 받은 토큰
}

message AuthenticateResponse {
  bool success = 1;
  string account_id = 2;
}

// ==================== 매칭 (API Server) ====================
// HTTP-style 요청/응답

message RequestMatchRequest {
  string account_id = 1;
  string player_name = 2;
  string game_mode = 1; // "1v1", "2v2", etc.
  int32 player_rating = 2;
}

message RequestMatchResponse {
  bool success = 1;
  string match_ticket_id = 2;  // 매칭 대기 티켓
  string message = 3;
}

message CheckMatchStatusRequest {
  string match_ticket_id = 1;
}

message CheckMatchStatusResponse {
  string status = 1;  // "waiting", "matched", "cancelled"
  RoomInfo room_info = 2;  // matched일 때만 존재
}

message CancelMatchRequest {
  string match_ticket_id = 1;
}

message CancelMatchResponse {
  bool success = 1;
}

// ==================== 방 정보 ====================
message RoomInfo {
  string play_nid = 1;      // Play Server NID
  string server_address = 2;
  int32 port = 3;
  int64 stage_id = 4;
  string stage_type = 5;    // "GameRoom"
  string game_mode = 6;
  repeated string player_ids = 7;
  string session_token = 8;  // 인증용 토큰
}

// ==================== 게임룸 (Play Server) ====================
message CreateGameRoomPayload {
  string game_mode = 1;
  repeated string player_ids = 2;
}

message GameStartNotify {
  repeated string players = 1;
  string game_mode = 2;
  int64 start_time = 3;
}

message GameEndNotify {
  string winner_id = 1;
  map<string, int32> scores = 2;
}
```

빌드 구성 (.csproj에 추가):
```xml
<ItemGroup>
  <Protobuf Include="Proto\**\*.proto" GrpcServices="None" />
</ItemGroup>
```

빌드:
```bash
dotnet build
```

### 2단계: LobbyController 구현 (API Server)

API Server에서 매칭 로직을 처리하는 컨트롤러입니다.

**Api/LobbyController.cs**
```csharp
using PlayHouse.Abstractions;
using PlayHouse.Abstractions.Api;
using PlayHouse.Tutorial.LobbyMatching;
using System.Collections.Concurrent;

public class LobbyController : IApiController
{
    // Play Server 정보 (실제 환경에서는 서비스 디스커버리 사용)
    private const string PlayNid = "play-1";
    private const string PlayServerAddress = "127.0.0.1";
    private const int PlayServerPort = 12000;

    // 매칭 대기 큐 (GameMode -> Queue)
    private static readonly ConcurrentDictionary<string, ConcurrentQueue<MatchTicket>> _matchQueues = new();

    // 매칭 티켓 저장소
    private static readonly ConcurrentDictionary<string, MatchTicket> _tickets = new();

    // Stage ID 카운터
    private static long _stageIdCounter = 1000000;

    private record MatchTicket
    {
        public string TicketId { get; init; } = "";
        public string AccountId { get; init; } = "";
        public string PlayerName { get; init; } = "";
        public string GameMode { get; init; } = "";
        public int Rating { get; init; }
        public DateTime EnqueueTime { get; init; }
        public string Status { get; set; } = "waiting";  // waiting, matched, cancelled
        public RoomInfo? RoomInfo { get; set; }
    }

    public void Handles(IHandlerRegister register)
    {
        register.Add<RequestMatchRequest>(nameof(HandleRequestMatch));
        register.Add<CheckMatchStatusRequest>(nameof(HandleCheckMatchStatus));
        register.Add<CancelMatchRequest>(nameof(HandleCancelMatch));
    }

    /// <summary>
    /// 매칭 요청 처리
    /// </summary>
    private async Task HandleRequestMatch(IPacket packet, IApiSender sender)
    {
        var request = RequestMatchRequest.Parser.ParseFrom(packet.Payload.DataSpan);

        Console.WriteLine($"[LobbyController] Match request: {request.AccountId}, Mode={request.GameMode}, Rating={request.PlayerRating}");

        // 매칭 티켓 생성
        var ticket = new MatchTicket
        {
            TicketId = Guid.NewGuid().ToString("N"),
            AccountId = request.AccountId,
            PlayerName = request.PlayerName,
            GameMode = request.GameMode,
            Rating = request.PlayerRating,
            EnqueueTime = DateTime.UtcNow,
            Status = "waiting"
        };

        _tickets[ticket.TicketId] = ticket;

        // 매칭 큐에 추가
        var queue = _matchQueues.GetOrAdd(request.GameMode, _ => new ConcurrentQueue<MatchTicket>());
        queue.Enqueue(ticket);

        // 즉시 매칭 시도 (간단한 예제: 2명이면 매칭)
        var matchResult = await TryCreateMatch(request.GameMode, sender);

        if (matchResult != null && _tickets.TryGetValue(ticket.TicketId, out var updatedTicket))
        {
            // 매칭 완료됨
            sender.Reply(CPacket.Of(new RequestMatchResponse
            {
                Success = true,
                MatchTicketId = ticket.TicketId,
                Message = "Match found!"
            }));
        }
        else
        {
            // 매칭 대기 중
            sender.Reply(CPacket.Of(new RequestMatchResponse
            {
                Success = true,
                MatchTicketId = ticket.TicketId,
                Message = "Waiting for opponent..."
            }));
        }
    }

    /// <summary>
    /// 매칭 상태 확인
    /// </summary>
    private Task HandleCheckMatchStatus(IPacket packet, IApiSender sender)
    {
        var request = CheckMatchStatusRequest.Parser.ParseFrom(packet.Payload.DataSpan);

        if (!_tickets.TryGetValue(request.MatchTicketId, out var ticket))
        {
            sender.Reply(CPacket.Of(new CheckMatchStatusResponse
            {
                Status = "not_found"
            }));
            return Task.CompletedTask;
        }

        var response = new CheckMatchStatusResponse
        {
            Status = ticket.Status
        };

        if (ticket.Status == "matched" && ticket.RoomInfo != null)
        {
            response.RoomInfo = ticket.RoomInfo;
        }

        sender.Reply(CPacket.Of(response));
        return Task.CompletedTask;
    }

    /// <summary>
    /// 매칭 취소
    /// </summary>
    private Task HandleCancelMatch(IPacket packet, IApiSender sender)
    {
        var request = CancelMatchRequest.Parser.ParseFrom(packet.Payload.DataSpan);

        if (_tickets.TryGetValue(request.MatchTicketId, out var ticket))
        {
            ticket.Status = "cancelled";
            _tickets.TryRemove(request.MatchTicketId, out _);
        }

        sender.Reply(CPacket.Of(new CancelMatchResponse { Success = true }));
        return Task.CompletedTask;
    }

    /// <summary>
    /// 매칭 시도 및 게임룸 생성
    /// </summary>
    private async Task<RoomInfo?> TryCreateMatch(string gameMode, IApiSender sender)
    {
        if (!_matchQueues.TryGetValue(gameMode, out var queue))
            return null;

        // 큐에서 2명 꺼내기 (waiting 상태만)
        var players = new List<MatchTicket>();
        var tempQueue = new ConcurrentQueue<MatchTicket>();

        while (queue.TryDequeue(out var ticket))
        {
            if (ticket.Status == "waiting" && players.Count < 2)
            {
                players.Add(ticket);
            }
            else if (ticket.Status == "waiting")
            {
                tempQueue.Enqueue(ticket);
            }
            // cancelled 티켓은 버림
        }

        // 남은 티켓 다시 큐에 넣기
        while (tempQueue.TryDequeue(out var ticket))
        {
            queue.Enqueue(ticket);
        }

        if (players.Count < 2)
        {
            // 다시 큐에 넣기
            foreach (var p in players)
            {
                queue.Enqueue(p);
            }
            return null;
        }

        // 게임룸 생성
        var stageId = Interlocked.Increment(ref _stageIdCounter);
        var playerIds = players.Select(p => p.AccountId).ToList();

        Console.WriteLine($"[LobbyController] Creating match: StageId={stageId}, Players={string.Join(", ", playerIds)}");

        // Play Server에 GameRoom Stage 생성
        var createPayload = new CreateGameRoomPayload { GameMode = gameMode };
        createPayload.PlayerIds.AddRange(playerIds);

        try
        {
            var createResult = await sender.CreateStage(
                PlayNid,          // Play Server NID
                "GameRoom",       // Stage 타입
                stageId,          // Stage ID
                CPacket.Of(createPayload)
            );

            if (!createResult.Result)
            {
                Console.WriteLine($"[LobbyController] Failed to create game room");

                // 실패 시 플레이어들을 다시 큐에 넣기
                foreach (var p in players)
                {
                    queue.Enqueue(p);
                }
                return null;
            }

            Console.WriteLine($"[LobbyController] Game room created: {stageId}");

            // 방 정보 생성
            var roomInfo = new RoomInfo
            {
                PlayNid = PlayNid,
                ServerAddress = PlayServerAddress,
                Port = PlayServerPort,
                StageId = stageId,
                StageType = "GameRoom",
                GameMode = gameMode,
                SessionToken = GenerateSessionToken()
            };
            roomInfo.PlayerIds.AddRange(playerIds);

            // 각 플레이어의 티켓 업데이트
            foreach (var player in players)
            {
                if (_tickets.TryGetValue(player.TicketId, out var ticket))
                {
                    ticket.Status = "matched";
                    ticket.RoomInfo = roomInfo;
                }
            }

            return roomInfo;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[LobbyController] Error: {ex.Message}");

            // 실패 시 플레이어들을 다시 큐에 넣기
            foreach (var p in players)
            {
                queue.Enqueue(p);
            }
            return null;
        }
    }

    private static string GenerateSessionToken()
    {
        return Convert.ToBase64String(Guid.NewGuid().ToByteArray());
    }
}
```

### 3단계: PlayerActor 구현

모든 GameRoom에서 사용할 Actor입니다.

**Actors/PlayerActor.cs**
```csharp
using PlayHouse.Abstractions;
using PlayHouse.Abstractions.Play;
using PlayHouse.Tutorial.LobbyMatching;

public class PlayerActor : IActor
{
    public IActorSender ActorSender { get; }

    public string PlayerName { get; private set; } = "";

    public PlayerActor(IActorSender actorSender)
    {
        ActorSender = actorSender;
    }

    public Task OnCreate()
    {
        Console.WriteLine($"[PlayerActor] Created");
        return Task.CompletedTask;
    }

    public Task<(bool result, IPacket? reply)> OnAuthenticate(IPacket authPacket)
    {
        var request = AuthenticateRequest.Parser.ParseFrom(authPacket.Payload.DataSpan);

        // 실제 게임에서는 session_token 검증
        PlayerName = request.PlayerName;
        ActorSender.AccountId = Guid.NewGuid().ToString(); // 임시 ID

        Console.WriteLine($"[PlayerActor] Authenticated: {PlayerName} (ID: {ActorSender.AccountId})");

        var response = new AuthenticateResponse
        {
            Success = true,
            AccountId = ActorSender.AccountId
        };

        return Task.FromResult<(bool, IPacket?)>((true, CPacket.Of(response)));
    }

    public Task OnPostAuthenticate()
    {
        return Task.CompletedTask;
    }

    public Task OnDestroy()
    {
        Console.WriteLine($"[PlayerActor] Destroyed: {PlayerName}");
        return Task.CompletedTask;
    }
}
```

### 4단계: GameRoomStage 구현

매칭된 플레이어들이 실제 게임을 진행하는 Stage입니다.

**Stages/GameRoomStage.cs**
```csharp
using PlayHouse.Abstractions;
using PlayHouse.Abstractions.Play;
using PlayHouse.Tutorial.LobbyMatching;
using System.Collections.Concurrent;

public class GameRoomStage : IStage
{
    public IStageSender StageSender { get; }

    private string _gameMode = "";
    private readonly List<string> _expectedPlayerIds = new();
    private readonly ConcurrentDictionary<string, PlayerActor> _joinedPlayers = new();
    private bool _gameStarted = false;

    public GameRoomStage(IStageSender stageSender)
    {
        StageSender = stageSender;
    }

    #region Stage Lifecycle

    public Task<(bool result, IPacket reply)> OnCreate(IPacket packet)
    {
        var request = CreateGameRoomPayload.Parser.ParseFrom(packet.Payload.DataSpan);

        _gameMode = request.GameMode;
        _expectedPlayerIds.AddRange(request.PlayerIds);

        Console.WriteLine($"[GameRoomStage] Created: StageId={StageSender.StageId}, Mode={_gameMode}");
        Console.WriteLine($"[GameRoomStage] Expected players: {string.Join(", ", _expectedPlayerIds)}");

        return Task.FromResult<(bool, IPacket)>((true, CPacket.Empty));
    }

    public Task OnPostCreate()
    {
        // 타임아웃: 60초 내에 모든 플레이어가 입장하지 않으면 방 닫기
        StageSender.AddCountTimer(
            TimeSpan.FromSeconds(60),
            TimeSpan.FromSeconds(1),
            1,
            () =>
            {
                if (!_gameStarted)
                {
                    Console.WriteLine($"[GameRoomStage] Timeout: Not all players joined. Closing room.");
                    StageSender.CloseStage();
                }
                return Task.CompletedTask;
            }
        );

        return Task.CompletedTask;
    }

    public Task OnDestroy()
    {
        Console.WriteLine($"[GameRoomStage] Destroyed: StageId={StageSender.StageId}");
        return Task.CompletedTask;
    }

    #endregion

    #region Actor Management

    public Task<bool> OnJoinStage(IActor actor)
    {
        var accountId = actor.ActorSender.AccountId;

        // 매칭된 플레이어인지 확인
        if (!_expectedPlayerIds.Contains(accountId))
        {
            Console.WriteLine($"[GameRoomStage] Rejected: {accountId} not in expected player list");
            return Task.FromResult(false);
        }

        // 이미 참가했는지 확인
        if (_joinedPlayers.ContainsKey(accountId))
        {
            Console.WriteLine($"[GameRoomStage] Rejected: {accountId} already joined");
            return Task.FromResult(false);
        }

        _joinedPlayers.TryAdd(accountId, (PlayerActor)actor);
        Console.WriteLine($"[GameRoomStage] Player joined: {accountId} ({_joinedPlayers.Count}/{_expectedPlayerIds.Count})");

        return Task.FromResult(true);
    }

    public Task OnPostJoinStage(IActor actor)
    {
        // 모든 플레이어가 입장했는지 확인
        if (_joinedPlayers.Count == _expectedPlayerIds.Count && !_gameStarted)
        {
            StartGame();
        }

        return Task.CompletedTask;
    }

    public ValueTask OnConnectionChanged(IActor actor, bool isConnected)
    {
        if (!isConnected && _gameStarted)
        {
            Console.WriteLine($"[GameRoomStage] Player disconnected: {actor.ActorSender.AccountId}");
            // 실제 게임에서는 재접속 대기 또는 게임 종료 처리
        }

        return ValueTask.CompletedTask;
    }

    #endregion

    #region Message Dispatch

    public Task OnDispatch(IActor actor, IPacket packet)
    {
        Console.WriteLine($"[GameRoomStage] Received from {actor.ActorSender.AccountId}: {packet.MsgId}");

        // 게임 내 메시지 처리 (실제 게임 로직)
        return Task.CompletedTask;
    }

    public Task OnDispatch(IPacket packet)
    {
        // 서버간 메시지 처리
        return Task.CompletedTask;
    }

    #endregion

    #region Game Logic

    private void StartGame()
    {
        _gameStarted = true;

        Console.WriteLine($"[GameRoomStage] Game starting! Players: {string.Join(", ", _joinedPlayers.Keys)}");

        // 모든 플레이어에게 게임 시작 알림
        var startNotify = new GameStartNotify
        {
            GameMode = _gameMode,
            StartTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };
        startNotify.Players.AddRange(_joinedPlayers.Keys);

        foreach (var player in _joinedPlayers.Values)
        {
            player.ActorSender.SendToClient(CPacket.Of(startNotify));
        }

        // 게임 종료 타이머 (5분 후)
        StageSender.AddCountTimer(
            TimeSpan.FromMinutes(5),
            TimeSpan.FromSeconds(1),
            1,
            () =>
            {
                Console.WriteLine($"[GameRoomStage] Game ended. Closing room.");
                StageSender.CloseStage();
                return Task.CompletedTask;
            }
        );
    }

    #endregion
}
```

### 5단계: 서버 설정 및 시작

**Program.cs** (API Server + Play Server 통합)
```csharp
using Microsoft.Extensions.Logging;
using PlayHouse.Core.Play.Bootstrap;
using PlayHouse.Core.Api.Bootstrap;

Console.WriteLine("=== Lobby + Matching Server Starting ===");

// 로거 생성
var loggerFactory = LoggerFactory.Create(builder =>
{
    builder.AddConsole();
    builder.SetMinimumLevel(LogLevel.Information);
});

// API Server 시작 (로비/매칭 처리)
var apiServer = new ApiServerBootstrap()
    .Configure(options =>
    {
        options.ServerId = "api-lobby-1";
        options.ServiceId = 1000;  // Lobby/Matching Service
        options.BindEndpoint = "tcp://127.0.0.1:11300";
        options.HttpPort = 5000;   // HTTP API 포트
    })
    .UseController<LobbyController>()
    .UseLoggerFactory(loggerFactory)
    .Build();

// Play Server 시작 (게임룸 처리)
var playServer = new PlayServerBootstrap()
    .Configure(options =>
    {
        options.ServerId = "play-1";
        options.BindEndpoint = "tcp://127.0.0.1:11200";
        options.TcpPort = 12000;
        options.AuthenticateMessageId = nameof(AuthenticateRequest);
    })
    .UseStage<GameRoomStage, PlayerActor>("GameRoom")
    .UseLoggerFactory(loggerFactory)
    .Build();

await Task.WhenAll(apiServer.StartAsync(), playServer.StartAsync());

Console.WriteLine("=== Servers Started ===");
Console.WriteLine("[API Server] HTTP: http://127.0.0.1:5000");
Console.WriteLine("[Play Server] TCP: 127.0.0.1:12000");
Console.WriteLine("Press Ctrl+C to stop");

await Task.Delay(-1);
```

### 6단계: 클라이언트 구현

**ClientTest/Program.cs**
```csharp
using System.Net.Http.Json;
using PlayHouse.Connector;
using PlayHouse.Connector.Protocol;
using PlayHouse.Tutorial.LobbyMatching;

Console.WriteLine("=== Lobby Matching Client ===");

// HTTP 클라이언트 (API Server 통신용)
using var httpClient = new HttpClient
{
    BaseAddress = new Uri("http://127.0.0.1:5000")
};

// 플레이어 정보 입력
Console.Write("Enter your name: ");
var playerName = Console.ReadLine() ?? "Player";

Console.Write("Enter your rating (1000-2000): ");
var rating = int.Parse(Console.ReadLine() ?? "1500");

// Connector 생성 (Play Server 통신용)
var connector = new ClientConnector();
connector.Init(new ConnectorConfig { RequestTimeoutMs = 30000 });

// Push 메시지 핸들러
connector.SetOnReceive(OnReceivePush);

try
{
    // ========================================
    // Step 1: API Server에 매칭 요청
    // ========================================
    Console.WriteLine("\n[Step 1] Requesting match from API Server...");

    var matchRequest = new
    {
        AccountId = Guid.NewGuid().ToString(),
        PlayerName = playerName,
        GameMode = "1v1",
        PlayerRating = rating
    };

    var matchResponse = await httpClient.PostAsJsonAsync("/matchmaking/request", matchRequest);
    var matchResult = await matchResponse.Content.ReadFromJsonAsync<MatchApiResponse>();

    if (matchResult?.Success != true)
    {
        Console.WriteLine($"Match request failed: {matchResult?.Message}");
        return;
    }

    Console.WriteLine($"Match ticket: {matchResult.MatchTicketId}");
    Console.WriteLine($"Status: {matchResult.Message}");

    // ========================================
    // Step 2: 매칭 완료 대기 (폴링)
    // ========================================
    Console.WriteLine("\n[Step 2] Waiting for match...");

    RoomInfo? roomInfo = null;

    while (roomInfo == null)
    {
        await Task.Delay(1000);  // 1초마다 확인

        var statusResponse = await httpClient.GetFromJsonAsync<MatchStatusApiResponse>(
            $"/matchmaking/status?ticketId={matchResult.MatchTicketId}");

        Console.WriteLine($"  Status: {statusResponse?.Status}");

        if (statusResponse?.Status == "matched")
        {
            roomInfo = statusResponse.RoomInfo;
            Console.WriteLine("Match found!");
        }
        else if (statusResponse?.Status == "cancelled" || statusResponse?.Status == "not_found")
        {
            Console.WriteLine("Match cancelled or expired");
            return;
        }
    }

    Console.WriteLine($"\nRoom info received:");
    Console.WriteLine($"  - Server: {roomInfo.ServerAddress}:{roomInfo.Port}");
    Console.WriteLine($"  - StageId: {roomInfo.StageId}");
    Console.WriteLine($"  - Players: {string.Join(", ", roomInfo.PlayerIds)}");

    // ========================================
    // Step 3: Play Server에 TCP 연결
    // ========================================
    Console.WriteLine("\n[Step 3] Connecting to Play Server...");

    var connected = await connector.ConnectAsync(
        roomInfo.ServerAddress,
        roomInfo.Port,
        roomInfo.StageId,
        roomInfo.StageType
    );

    if (!connected)
    {
        Console.WriteLine("Connection failed");
        return;
    }
    Console.WriteLine("Connected to Play Server!");

    // ========================================
    // Step 4: 인증
    // ========================================
    Console.WriteLine("\n[Step 4] Authenticating...");

    var authRequest = new AuthenticateRequest
    {
        PlayerName = playerName,
        SessionToken = roomInfo.SessionToken
    };
    using var authPacket = new Packet(authRequest);
    using var authResponse = await connector.AuthenticateAsync(authPacket);

    if (!connector.IsAuthenticated())
    {
        Console.WriteLine("Authentication failed");
        return;
    }

    var authReply = AuthenticateResponse.Parser.ParseFrom(authResponse.Payload.DataSpan);
    Console.WriteLine($"Authenticated! AccountId: {authReply.AccountId}");

    // ========================================
    // Step 5: 게임 시작 대기
    // ========================================
    Console.WriteLine("\n[Step 5] Waiting for game to start...");
    Console.WriteLine("(Waiting for all players to join)");

    // Push 메시지 수신 대기
    while (true)
    {
        connector.MainThreadAction();
        await Task.Delay(100);
    }
}
catch (Exception ex)
{
    Console.WriteLine($"Error: {ex.Message}");
}
finally
{
    connector.Disconnect();
    await connector.DisposeAsync();
}

void OnReceivePush(IPacket packet)
{
    switch (packet.MsgId)
    {
        case "GameStartNotify":
            var startNotify = GameStartNotify.Parser.ParseFrom(packet.Payload.DataSpan);
            Console.WriteLine($"\n*** GAME STARTED! ***");
            Console.WriteLine($"Mode: {startNotify.GameMode}");
            Console.WriteLine($"Players: {string.Join(", ", startNotify.Players)}");
            break;

        case "GameEndNotify":
            var endNotify = GameEndNotify.Parser.ParseFrom(packet.Payload.DataSpan);
            Console.WriteLine($"\n*** GAME ENDED! ***");
            Console.WriteLine($"Winner: {endNotify.WinnerId}");
            break;

        default:
            Console.WriteLine($"Received: {packet.MsgId}");
            break;
    }
}

// API 응답 DTO
record MatchApiResponse(bool Success, string? MatchTicketId, string? Message);
record MatchStatusApiResponse(string? Status, RoomInfo? RoomInfo);
record RoomInfo(string PlayNid, string ServerAddress, int Port, long StageId, string StageType, string GameMode, List<string> PlayerIds, string SessionToken);
```

### 클라이언트 흐름 정리

```
┌─────────────────────────────────────────────────────────────────────┐
│                    클라이언트 매칭 흐름                               │
├─────────────────────────────────────────────────────────────────────┤
│                                                                     │
│  [Step 1] HTTP 요청 → API Server                                    │
│           POST /matchmaking/request                                 │
│           → 응답: { ticketId, status: "waiting" }                   │
│                                                                     │
│  [Step 2] 폴링 → API Server                                         │
│           GET /matchmaking/status?ticketId=xxx                      │
│           → 응답: { status: "matched", roomInfo: {...} }            │
│                                                                     │
│  [Step 3] TCP 연결 → Play Server                                    │
│           ConnectAsync(serverAddress, port, stageId, stageType)     │
│                                                                     │
│  [Step 4] 인증 → Play Server                                        │
│           AuthenticateAsync(AuthenticateRequest)                    │
│                                                                     │
│  [Step 5] 게임 시작 대기 (Push 수신)                                  │
│           GameStartNotify 수신                                       │
│                                                                     │
└─────────────────────────────────────────────────────────────────────┘
```

---

## 7단계: 실행 및 테스트

### 1. 서버 시작

터미널 1:
```bash
dotnet run --project LobbyMatchingServer
```

출력:
```
=== Lobby + Matching Server Starting ===
=== Servers Started ===
[API Server] HTTP: http://127.0.0.1:5000
[Play Server] TCP: 127.0.0.1:12000
Press Ctrl+C to stop
```

### 2. 클라이언트 1 실행

터미널 2:
```bash
dotnet run --project ClientTest
```

입력 및 출력:
```
=== Lobby Matching Client ===
Enter your name: Alice
Enter your rating (1000-2000): 1500

[Step 1] Requesting match from API Server...
Match ticket: abc123...
Status: Waiting for opponent...

[Step 2] Waiting for match...
  Status: waiting
  Status: waiting
  ...
```

### 3. 클라이언트 2 실행

터미널 3:
```bash
dotnet run --project ClientTest
```

입력:
```
Enter your name: Bob
Enter your rating (1000-2000): 1480
```

### 4. 매칭 완료 및 게임 시작

**Alice 화면:**
```
  Status: matched
Match found!

Room info received:
  - Server: 127.0.0.1:12000
  - StageId: 1000001
  - Players: alice-id, bob-id

[Step 3] Connecting to Play Server...
Connected to Play Server!

[Step 4] Authenticating...
Authenticated! AccountId: alice-id

[Step 5] Waiting for game to start...
(Waiting for all players to join)

*** GAME STARTED! ***
Mode: 1v1
Players: alice-id, bob-id
```

**서버 로그:**
```
[LobbyController] Match request: alice-id, Mode=1v1, Rating=1500
[LobbyController] Match request: bob-id, Mode=1v1, Rating=1480
[LobbyController] Creating match: StageId=1000001, Players=alice-id, bob-id
[LobbyController] Game room created: 1000001
[GameRoomStage] Created: StageId=1000001, Mode=1v1
[GameRoomStage] Expected players: alice-id, bob-id
[PlayerActor] Authenticated: Alice (ID: alice-id)
[GameRoomStage] Player joined: alice-id (1/2)
[PlayerActor] Authenticated: Bob (ID: bob-id)
[GameRoomStage] Player joined: bob-id (2/2)
[GameRoomStage] Game starting! Players: alice-id, bob-id
```

---

## 핵심 개념 정리

### 1. 2단계 접속 패턴

```
클라이언트 ──HTTP──▶ API Server (로비/매칭)
    │                    │
    │                    ▼ CreateStage()
    │                 Play Server
    │                    │
    ◀───── 방 정보 ──────┘
    │
    ──TCP──▶ Play Server (게임룸)
```

### 2. API Server → Play Server 통신

```csharp
// Stage 생성
var result = await sender.CreateStage(
    playNid,       // Play Server 식별자
    "GameRoom",    // Stage 타입
    stageId,       // Stage ID
    CPacket.Of(payload)  // 초기화 데이터
);
```

### 3. Push 메시지 (서버 → 클라이언트)

```csharp
// Stage에서 클라이언트로 Push
actor.ActorSender.SendToClient(CPacket.Of(notification));

// 클라이언트에서 수신
connector.SetOnReceive((packet) => {
    // Push 메시지 처리
});
```

---

## 다음 단계 및 개선 사항

### 1. 고급 매칭 로직
- ELO 기반 레이팅 매칭
- 대기 시간 고려 (시간이 지날수록 매칭 범위 확대)
- WebSocket을 사용한 실시간 매칭 상태 알림

### 2. 에러 처리
- 타임아웃 처리
- 재연결 로직
- 매칭 실패 시 재시도

### 3. 스케일링
- 여러 Play Server에 GameRoom 분산
- Redis를 사용한 매칭 큐 공유
- API Server 수평 확장

---

## 참고 문서

- [서버간 통신 가이드](../guides/server-communication.md)
- [API Controller 구현](../guides/api-controller.md)
- [Stage/Actor 모델](../concepts/stage-actor.md)

---

## 문제 해결

### "Match request failed"
**원인:** API Server가 시작되지 않았거나 HTTP 포트가 다름

**해결:**
- API Server가 실행 중인지 확인
- HTTP 포트 (5000) 확인

### "Player not in expected player list"
**원인:** 매칭되지 않은 플레이어가 게임룸에 접속 시도

**해결:**
- 매칭 완료 후 반환된 roomInfo로만 접속
- AccountId가 일치하는지 확인

### "Connection timeout"
**원인:** 타임아웃 설정이 짧거나 서버 응답 지연

**해결:**
```csharp
new ConnectorConfig
{
    RequestTimeoutMs = 30000 // 30초로 증가
}
```

이 튜토리얼을 완료하면 PlayHouse-NET의 핵심 패턴인 **API Server를 통한 로비/매칭 → Play Server에서 게임 진행** 구조를 이해하고 구현할 수 있습니다.
