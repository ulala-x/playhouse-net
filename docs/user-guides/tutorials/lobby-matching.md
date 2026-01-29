# 튜토리얼: 로비 + 매칭 시스템 구축

이 튜토리얼에서는 PlayHouse-NET을 사용하여 실제 게임에서 사용되는 **로비 → 매칭 → 게임룸** 시스템을 처음부터 끝까지 구축합니다.

## 목표

- 1시간 안에 완료 가능한 단계별 가이드
- 실제 게임 구조에 가까운 시스템 구축
- PlayHouse의 서버간 통신 패턴 이해

## 완성된 시스템 흐름

```
┌─────────┐
│ Client  │
└────┬────┘
     │
     │ 1. ConnectToLobby
     ├──────────────────────────────────────────┐
     │                                           ▼
     │                                    ┌─────────────┐
     │                                    │ LobbyStage  │
     │                                    │ (Play Srv)  │
     │                                    └──────┬──────┘
     │                                           │
     │ 2. RequestMatch                          │
     ├──────────────────────────────────────────┤
     │                                           │
     │                                           │ 3. Forward to API
     │                                           ├────────────────┐
     │                                           │                ▼
     │                                           │         ┌──────────────────┐
     │                                           │         │ MatchmakingCtrl  │
     │                                           │         │    (API Srv)     │
     │                                           │         └────────┬─────────┘
     │                                           │                  │
     │                                           │                  │ 4. Find/Create Match
     │                                           │                  │ 5. CreateStage (GameRoom)
     │                                           │                  │
     │                                           │                  ▼
     │                                           │         ┌──────────────────┐
     │                                           │         │ GameRoomStage    │
     │                                           │         │   (Play Srv)     │
     │                                           │         └────────┬─────────┘
     │                                           │                  │
     │ 6. MatchFound (Push)                     │                  │
     │◄─────────────────────────────────────────┤◄─────────────────┤
     │                                           │                  │
     │ 7. JoinGameRoom                          │                  │
     ├──────────────────────────────────────────┼──────────────────►
     │                                           │                  │
     │ 8. GameStart                             │                  │
     │◄─────────────────────────────────────────┼──────────────────┤
     │                                           │                  │
```

## 아키텍처 구성 요소

### 1. LobbyStage (Play Server)
- 플레이어 대기실
- 매칭 요청을 API 서버로 라우팅
- 매칭 완료 알림을 클라이언트에 Push

### 2. MatchmakingController (API Server)
- 매칭 큐 관리
- 플레이어 매칭 로직 실행
- GameRoomStage 생성 요청

### 3. GameRoomStage (Play Server)
- 실제 게임이 진행되는 공간
- 매칭된 플레이어들만 입장
- 게임 로직 처리

## 단계별 구현

### 1단계: Proto 메시지 정의

먼저 시스템 전체에서 사용할 메시지를 정의합니다.

**Proto/lobby_matching.proto**
```protobuf
syntax = "proto3";

option csharp_namespace = "PlayHouse.Tutorial.LobbyMatching";

// ==================== 인증 ====================
message AuthenticateRequest {
  string player_name = 1;
}

message AuthenticateResponse {
  bool success = 1;
  string account_id = 2;
}

// ==================== 로비 ====================
message ConnectToLobbyRequest {
  string stage_type = 1; // "Lobby"
}

message ConnectToLobbyResponse {
  bool success = 1;
  int64 lobby_stage_id = 2;
}

// ==================== 매칭 ====================
message RequestMatchRequest {
  string game_mode = 1; // "1v1", "2v2", etc.
  int32 player_rating = 2;
}

message RequestMatchResponse {
  bool success = 1;
  string message = 2;
}

message MatchFoundNotify {
  int64 game_room_id = 1;
  string game_room_type = 2;
  repeated string matched_players = 3; // AccountIds
}

// ==================== API: 매칭 처리 ====================
message MatchmakingApiRequest {
  string account_id = 1;
  string game_mode = 2;
  int32 player_rating = 3;

  // 응답을 보낼 위치
  string lobby_stage_nid = 4;
  int64 lobby_stage_id = 5;
}

message MatchmakingApiResponse {
  bool success = 1;
  int64 game_room_id = 2;
  string message = 3;
}

// ==================== 게임룸 ====================
message CreateGameRoomRequest {
  string game_mode = 1;
  repeated string player_ids = 2;
}

message CreateGameRoomResponse {
  bool success = 1;
  int64 room_id = 2;
}

message JoinGameRoomRequest {
  int64 room_id = 1;
}

message JoinGameRoomResponse {
  bool success = 1;
  string message = 2;
}

message GameStartNotify {
  repeated string players = 1;
  string game_mode = 2;
  int64 start_time = 3;
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

### 2단계: 공통 Actor 구현

모든 Stage에서 사용할 공통 Actor를 구현합니다.

**PlayerActor.cs**
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

        // 실제 게임에서는 DB 조회, 토큰 검증 등 수행
        PlayerName = request.PlayerName;
        ActorSender.AccountId = Guid.NewGuid().ToString(); // 임시 ID 생성

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

### 3단계: LobbyStage 구현

플레이어가 대기하면서 매칭을 요청하는 로비를 구현합니다.

**LobbyStage.cs**
```csharp
using PlayHouse.Abstractions;
using PlayHouse.Abstractions.Play;
using PlayHouse.Tutorial.LobbyMatching;
using System.Collections.Concurrent;

public class LobbyStage : IStage
{
    public IStageSender StageSender { get; }

    // 로비에 있는 플레이어 목록
    private readonly ConcurrentDictionary<string, PlayerActor> _players = new();

    // 매칭 요청 중인 플레이어 (AccountId -> GameMode)
    private readonly ConcurrentDictionary<string, string> _matchingPlayers = new();

    public LobbyStage(IStageSender stageSender)
    {
        StageSender = stageSender;
    }

    #region Stage Lifecycle

    public Task<(bool result, IPacket reply)> OnCreate(IPacket packet)
    {
        Console.WriteLine($"[LobbyStage] Created: StageId={StageSender.StageId}");

        var response = new ConnectToLobbyResponse
        {
            Success = true,
            LobbyStageId = StageSender.StageId
        };

        return Task.FromResult<(bool, IPacket)>((true, CPacket.Of(response)));
    }

    public Task OnPostCreate()
    {
        // 로비 상태를 주기적으로 출력 (30초마다)
        StageSender.AddRepeatTimer(
            TimeSpan.FromSeconds(30),
            TimeSpan.FromSeconds(30),
            () =>
            {
                Console.WriteLine($"[LobbyStage] Status: {_players.Count} players, {_matchingPlayers.Count} matching");
                return Task.CompletedTask;
            }
        );

        return Task.CompletedTask;
    }

    public Task OnDestroy()
    {
        Console.WriteLine($"[LobbyStage] Destroyed: StageId={StageSender.StageId}");
        return Task.CompletedTask;
    }

    #endregion

    #region Actor Management

    public Task<bool> OnJoinStage(IActor actor)
    {
        var playerActor = (PlayerActor)actor;
        _players.TryAdd(actor.ActorSender.AccountId, playerActor);

        Console.WriteLine($"[LobbyStage] Player joined: {playerActor.PlayerName} ({_players.Count} total)");
        return Task.FromResult(true);
    }

    public Task OnPostJoinStage(IActor actor)
    {
        // 로비 환영 메시지 전송 (선택사항)
        return Task.CompletedTask;
    }

    public ValueTask OnConnectionChanged(IActor actor, bool isConnected)
    {
        if (!isConnected)
        {
            // 연결 끊김 시 매칭 대기열에서 제거
            _players.TryRemove(actor.ActorSender.AccountId, out _);
            _matchingPlayers.TryRemove(actor.ActorSender.AccountId, out _);

            Console.WriteLine($"[LobbyStage] Player disconnected: {actor.ActorSender.AccountId}");
        }

        return ValueTask.CompletedTask;
    }

    #endregion

    #region Message Dispatch

    public async Task OnDispatch(IActor actor, IPacket packet)
    {
        switch (packet.MsgId)
        {
            case nameof(RequestMatchRequest):
                await HandleMatchRequest(actor, packet);
                break;
            default:
                Console.WriteLine($"[LobbyStage] Unknown message: {packet.MsgId}");
                actor.ActorSender.Reply(404); // Not Found
                break;
        }
    }

    public async Task OnDispatch(IPacket packet)
    {
        // 서버간 메시지 처리
        switch (packet.MsgId)
        {
            case nameof(MatchFoundNotify):
                await HandleMatchFoundNotify(packet);
                break;
            default:
                Console.WriteLine($"[LobbyStage] Unknown server message: {packet.MsgId}");
                break;
        }
    }

    #endregion

    #region Handlers

    private async Task HandleMatchRequest(IActor actor, IPacket packet)
    {
        var request = RequestMatchRequest.Parser.ParseFrom(packet.Payload.DataSpan);
        var accountId = actor.ActorSender.AccountId;

        // 이미 매칭 중인지 확인
        if (_matchingPlayers.ContainsKey(accountId))
        {
            actor.ActorSender.Reply(CPacket.Of(new RequestMatchResponse
            {
                Success = false,
                Message = "Already in matchmaking queue"
            }));
            return;
        }

        // 매칭 대기열 추가
        _matchingPlayers.TryAdd(accountId, request.GameMode);

        Console.WriteLine($"[LobbyStage] Match requested: {accountId}, Mode={request.GameMode}, Rating={request.PlayerRating}");

        // API 서버의 매칭 컨트롤러로 요청 전달
        var apiRequest = new MatchmakingApiRequest
        {
            AccountId = accountId,
            GameMode = request.GameMode,
            PlayerRating = request.PlayerRating,
            LobbyStageNid = StageSender.Nid,  // 응답을 받을 위치
            LobbyStageId = StageSender.StageId
        };

        try
        {
            // ServiceId 1000 = Matchmaking API 서버들
            const ushort MatchmakingServiceId = 1000;

            using var response = await StageSender.RequestToApiService(
                MatchmakingServiceId,
                CPacket.Of(apiRequest)
            );

            var apiResponse = MatchmakingApiResponse.Parser.ParseFrom(response.Payload.DataSpan);

            // 클라이언트에 응답
            actor.ActorSender.Reply(CPacket.Of(new RequestMatchResponse
            {
                Success = apiResponse.Success,
                Message = apiResponse.Message
            }));
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[LobbyStage] Match request failed: {ex.Message}");

            // 매칭 큐에서 제거
            _matchingPlayers.TryRemove(accountId, out _);

            actor.ActorSender.Reply(CPacket.Of(new RequestMatchResponse
            {
                Success = false,
                Message = "Matchmaking service unavailable"
            }));
        }
    }

    private Task HandleMatchFoundNotify(IPacket packet)
    {
        var notify = MatchFoundNotify.Parser.ParseFrom(packet.Payload.DataSpan);

        Console.WriteLine($"[LobbyStage] Match found: RoomId={notify.GameRoomId}, Players={string.Join(", ", notify.MatchedPlayers)}");

        // 매칭된 플레이어들에게 알림 전송
        foreach (var accountId in notify.MatchedPlayers)
        {
            // 매칭 큐에서 제거
            _matchingPlayers.TryRemove(accountId, out _);

            // 플레이어가 아직 연결되어 있는지 확인
            if (_players.TryGetValue(accountId, out var player))
            {
                // Push 메시지로 매칭 완료 알림
                player.ActorSender.SendToClient(CPacket.Of(notify));

                Console.WriteLine($"[LobbyStage] Sent match notification to {accountId}");
            }
        }

        return Task.CompletedTask;
    }

    #endregion
}
```

### 4단계: MatchmakingController 구현 (API Server)

API 서버에서 매칭 로직을 처리하는 컨트롤러를 구현합니다.

**MatchmakingController.cs**
```csharp
using PlayHouse.Abstractions;
using PlayHouse.Abstractions.Api;
using PlayHouse.Tutorial.LobbyMatching;
using System.Collections.Concurrent;

public class MatchmakingController : IApiController
{
    // 매칭 대기 큐 (GameMode -> List of Players)
    private static readonly ConcurrentDictionary<string, ConcurrentQueue<MatchmakingRequest>>
        _matchQueues = new();

    // 간단한 매칭 요청 구조체
    private class MatchmakingRequest
    {
        public string AccountId { get; set; } = "";
        public int Rating { get; set; }
        public string LobbyStageNid { get; set; } = "";
        public long LobbyStageId { get; set; }
        public DateTime EnqueueTime { get; set; }
    }

    public void Handles(IHandlerRegister register)
    {
        register.Add<MatchmakingApiRequest>(nameof(HandleMatchmaking));
    }

    private async Task HandleMatchmaking(IPacket packet, IApiSender sender)
    {
        var request = MatchmakingApiRequest.Parser.ParseFrom(packet.Payload.DataSpan);

        Console.WriteLine($"[MatchmakingCtrl] Request: {request.AccountId}, Mode={request.GameMode}, Rating={request.PlayerRating}");

        // 매칭 큐에 추가
        var matchRequest = new MatchmakingRequest
        {
            AccountId = request.AccountId,
            Rating = request.PlayerRating,
            LobbyStageNid = request.LobbyStageNid,
            LobbyStageId = request.LobbyStageId,
            EnqueueTime = DateTime.UtcNow
        };

        var queue = _matchQueues.GetOrAdd(request.GameMode, _ => new ConcurrentQueue<MatchmakingRequest>());
        queue.Enqueue(matchRequest);

        // 매칭 시도 (간단한 예제: 2명이면 매칭)
        if (queue.Count >= 2)
        {
            await TryCreateMatch(request.GameMode, sender);
        }
        else
        {
            // 아직 매칭 상대 없음
            sender.Reply(CPacket.Of(new MatchmakingApiResponse
            {
                Success = true,
                Message = "Waiting for opponent..."
            }));
        }
    }

    private async Task TryCreateMatch(string gameMode, IApiSender sender)
    {
        var queue = _matchQueues[gameMode];

        // 큐에서 2명 꺼내기
        var players = new List<MatchmakingRequest>();
        for (int i = 0; i < 2 && queue.TryDequeue(out var player); i++)
        {
            players.Add(player);
        }

        if (players.Count < 2)
        {
            // 다시 큐에 넣기
            foreach (var p in players)
            {
                queue.Enqueue(p);
            }

            sender.Reply(CPacket.Of(new MatchmakingApiResponse
            {
                Success = true,
                Message = "Waiting for opponent..."
            }));
            return;
        }

        // 게임룸 생성
        var roomId = GenerateRoomId();
        var playerIds = players.Select(p => p.AccountId).ToList();

        Console.WriteLine($"[MatchmakingCtrl] Creating match: RoomId={roomId}, Players={string.Join(", ", playerIds)}");

        // Play 서버에 GameRoom Stage 생성
        var createRoomRequest = new CreateGameRoomRequest
        {
            GameMode = gameMode
        };
        createRoomRequest.PlayerIds.AddRange(playerIds);

        try
        {
            // "play-1" Play 서버에 GameRoom 생성 (실제 환경에서는 로드밸런싱 필요)
            var playServerId = "play-1";

            var createResult = await sender.CreateStage(
                playServerId,
                "GameRoom",
                roomId,
                CPacket.Of(createRoomRequest)
            );

            if (!createResult.Result)
            {
                Console.WriteLine($"[MatchmakingCtrl] Failed to create game room");

                // 실패 시 플레이어들을 다시 큐에 넣기
                foreach (var p in players)
                {
                    queue.Enqueue(p);
                }

                sender.Reply(CPacket.Of(new MatchmakingApiResponse
                {
                    Success = false,
                    Message = "Failed to create game room"
                }));
                return;
            }

            Console.WriteLine($"[MatchmakingCtrl] Game room created: {roomId}");

            // 각 플레이어의 로비로 매칭 완료 알림 전송
            var matchNotify = new MatchFoundNotify
            {
                GameRoomId = roomId,
                GameRoomType = "GameRoom"
            };
            matchNotify.MatchedPlayers.AddRange(playerIds);

            foreach (var player in players)
            {
                sender.SendToStage(
                    player.LobbyStageNid,
                    player.LobbyStageId,
                    CPacket.Of(matchNotify)
                );
            }

            // 요청자에게 응답
            sender.Reply(CPacket.Of(new MatchmakingApiResponse
            {
                Success = true,
                GameRoomId = roomId,
                Message = "Match found!"
            }));
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[MatchmakingCtrl] Error: {ex.Message}");

            // 실패 시 플레이어들을 다시 큐에 넣기
            foreach (var p in players)
            {
                queue.Enqueue(p);
            }

            sender.Reply(CPacket.Of(new MatchmakingApiResponse
            {
                Success = false,
                Message = "Matchmaking failed"
            }));
        }
    }

    private long GenerateRoomId()
    {
        // 유니크한 Room ID 생성 (실제로는 더 정교한 방식 사용)
        return DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    }
}
```

### 5단계: GameRoomStage 구현

매칭된 플레이어들이 실제 게임을 진행하는 Stage를 구현합니다.

**GameRoomStage.cs**
```csharp
using PlayHouse.Abstractions;
using PlayHouse.Abstractions.Play;
using PlayHouse.Tutorial.LobbyMatching;
using System.Collections.Concurrent;

public class GameRoomStage : IStage
{
    public IStageSender StageSender { get; }

    private string _gameMode = "";
    private readonly List<string> _expectedPlayerIds = new(); // 입장 예정 플레이어
    private readonly ConcurrentDictionary<string, PlayerActor> _joinedPlayers = new(); // 입장한 플레이어
    private bool _gameStarted = false;

    public GameRoomStage(IStageSender stageSender)
    {
        StageSender = stageSender;
    }

    #region Stage Lifecycle

    public Task<(bool result, IPacket reply)> OnCreate(IPacket packet)
    {
        var request = CreateGameRoomRequest.Parser.ParseFrom(packet.Payload.DataSpan);

        _gameMode = request.GameMode;
        _expectedPlayerIds.AddRange(request.PlayerIds);

        Console.WriteLine($"[GameRoomStage] Created: RoomId={StageSender.StageId}, Mode={_gameMode}, Players={string.Join(", ", _expectedPlayerIds)}");

        var response = new CreateGameRoomResponse
        {
            Success = true,
            RoomId = StageSender.StageId
        };

        return Task.FromResult<(bool, IPacket)>((true, CPacket.Of(response)));
    }

    public Task OnPostCreate()
    {
        // 타임아웃 타이머 설정 (60초 내에 모든 플레이어가 입장하지 않으면 방 닫기)
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
        Console.WriteLine($"[GameRoomStage] Destroyed: RoomId={StageSender.StageId}");
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
            Console.WriteLine($"[GameRoomStage] Player disconnected during game: {actor.ActorSender.AccountId}");

            // 실제 게임에서는 재접속 대기, 게임 종료 등 처리
        }

        return ValueTask.CompletedTask;
    }

    #endregion

    #region Message Dispatch

    public Task OnDispatch(IActor actor, IPacket packet)
    {
        // 게임 내 메시지 처리 (실제 게임 로직)
        Console.WriteLine($"[GameRoomStage] Received from {actor.ActorSender.AccountId}: {packet.MsgId}");

        // Echo 응답
        actor.ActorSender.Reply(CPacket.Of(new JoinGameRoomResponse
        {
            Success = true,
            Message = "In game room"
        }));

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

        // 실제 게임 로직 시작 (타이머, 게임 루프 등)
        // 예: 게임 종료 타이머
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

### 6단계: 서버 설정 및 시작

#### Play Server (Program.cs)

```csharp
using Microsoft.Extensions.Logging;
using PlayHouse.Core.Play.Bootstrap;

var playServer = new PlayServerBootstrap()
    .Configure(options =>
    {
        options.ServerId = "play-1";
        options.BindEndpoint = "tcp://127.0.0.1:11200";
        options.TcpPort = 12000; // 클라이언트 연결 포트
        options.AuthenticateMessageId = nameof(AuthenticateRequest);
        options.DefaultStageType = "Lobby";
    })
    .UseStage<LobbyStage, PlayerActor>("Lobby")
    .UseStage<GameRoomStage, PlayerActor>("GameRoom")
    .UseLoggerFactory(LoggerFactory.Create(builder =>
    {
        builder.AddConsole();
        builder.SetMinimumLevel(LogLevel.Information);
    }))
    .Build();

await playServer.StartAsync();
Console.WriteLine("Play Server started on port 12000");
await Task.Delay(-1);
```

#### API Server (ProgramApi.cs)

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using PlayHouse.Extensions;

var host = Host.CreateDefaultBuilder(args)
    .ConfigureServices((context, services) =>
    {
        services.AddApiServer(options =>
        {
            options.ServerId = "api-matching-1";
            options.ServiceId = 1000; // Matchmaking Service
            options.BindEndpoint = "tcp://127.0.0.1:11300";
        })
        .UseController<MatchmakingController>();
    })
    .Build();

Console.WriteLine("API Server started (ServiceId: 1000)");
await host.RunAsync();
```

### 7단계: 클라이언트 테스트 코드

전체 흐름을 테스트하는 클라이언트 코드입니다.

**ClientTest.cs**
```csharp
using PlayHouse.Connector;
using PlayHouse.Tutorial.LobbyMatching;

public class ClientTest
{
    public static async Task Main()
    {
        // 2명의 클라이언트 시뮬레이션
        var client1 = new TestClient("Player1", 1500);
        var client2 = new TestClient("Player2", 1480);

        var task1 = client1.RunAsync();
        var task2 = client2.RunAsync();

        await Task.WhenAll(task1, task2);

        Console.WriteLine("Test completed!");
    }
}

public class TestClient
{
    private readonly string _playerName;
    private readonly int _rating;
    private ClientConnector _connector = null!;
    private long _gameRoomId;

    public TestClient(string playerName, int rating)
    {
        _playerName = playerName;
        _rating = rating;
    }

    public async Task RunAsync()
    {
        _connector = new ClientConnector();
        _connector.Init(new ConnectorConfig
        {
            RequestTimeoutMs = 10000
        });

        // Push 메시지 핸들러 등록
        _connector.OnReceive = HandlePushMessage;

        try
        {
            // 1. 로비에 연결
            Console.WriteLine($"[{_playerName}] Connecting to lobby...");

            var lobbyStageId = 1L; // 모든 플레이어가 같은 로비 사용
            var connected = await _connector.ConnectAsync("127.0.0.1", 12000, lobbyStageId, "Lobby");
            if (!connected)
            {
                Console.WriteLine($"[{_playerName}] Connection failed");
                return;
            }

            // 2. 인증
            var authRequest = new AuthenticateRequest { PlayerName = _playerName };
            using var authPacket = CPacket.Of(authRequest);
            using var authResponse = await _connector.AuthenticateAsync(authPacket);

            var authResult = AuthenticateResponse.Parser.ParseFrom(authResponse.Payload.DataSpan);
            Console.WriteLine($"[{_playerName}] Authenticated: {authResult.AccountId}");

            // 3. 매칭 요청
            Console.WriteLine($"[{_playerName}] Requesting match...");

            var matchRequest = new RequestMatchRequest
            {
                GameMode = "1v1",
                PlayerRating = _rating
            };

            using var matchPacket = CPacket.Of(matchRequest);
            using var matchResponse = await _connector.RequestAsync(matchPacket);

            var matchResult = RequestMatchResponse.Parser.ParseFrom(matchResponse.Payload.DataSpan);
            Console.WriteLine($"[{_playerName}] Match response: {matchResult.Message}");

            // 4. 매칭 완료 알림 대기 (Push 메시지)
            Console.WriteLine($"[{_playerName}] Waiting for match...");

            // Push 메시지는 OnReceive 콜백으로 처리
            // 여기서는 일정 시간 대기
            await Task.Delay(10000);

            // 5. 게임룸 입장 (매칭 완료 후)
            if (_gameRoomId > 0)
            {
                Console.WriteLine($"[{_playerName}] Joining game room {_gameRoomId}...");

                _connector.Disconnect(); // 로비 연결 해제

                // 게임룸에 재연결
                await _connector.ConnectAsync("127.0.0.1", 12000, _gameRoomId, "GameRoom");
                await _connector.AuthenticateAsync(authPacket);

                Console.WriteLine($"[{_playerName}] Joined game room!");

                // 게임 시작 알림 대기
                await Task.Delay(5000);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[{_playerName}] Error: {ex.Message}");
        }
        finally
        {
            _connector.Disconnect();
            await _connector.DisposeAsync();
        }
    }

    private void HandlePushMessage(IPacket packet)
    {
        Console.WriteLine($"[{_playerName}] Received push: {packet.MsgId}");

        switch (packet.MsgId)
        {
            case nameof(MatchFoundNotify):
                var matchNotify = MatchFoundNotify.Parser.ParseFrom(packet.Payload.DataSpan);
                _gameRoomId = matchNotify.GameRoomId;
                Console.WriteLine($"[{_playerName}] Match found! RoomId={_gameRoomId}");
                break;

            case nameof(GameStartNotify):
                var gameStart = GameStartNotify.Parser.ParseFrom(packet.Payload.DataSpan);
                Console.WriteLine($"[{_playerName}] Game started! Mode={gameStart.GameMode}, Players={string.Join(", ", gameStart.Players)}");
                break;
        }
    }
}
```

### 8단계: 실행 및 검증

#### 1. 서버 시작

터미널 1 (Play Server):
```bash
dotnet run --project PlayServer
```

터미널 2 (API Server):
```bash
dotnet run --project ApiServer
```

#### 2. 클라이언트 실행

터미널 3 (Client Test):
```bash
dotnet run --project ClientTest
```

#### 3. 예상 출력

**Play Server:**
```
[LobbyStage] Created: StageId=1
[PlayerActor] Authenticated: Player1 (ID: abc-123)
[LobbyStage] Player joined: Player1 (1 total)
[LobbyStage] Match requested: abc-123, Mode=1v1, Rating=1500
[PlayerActor] Authenticated: Player2 (ID: def-456)
[LobbyStage] Player joined: Player2 (2 total)
[LobbyStage] Match requested: def-456, Mode=1v1, Rating=1480
[LobbyStage] Match found: RoomId=1674567890123, Players=abc-123, def-456
[LobbyStage] Sent match notification to abc-123
[LobbyStage] Sent match notification to def-456
[GameRoomStage] Created: RoomId=1674567890123, Mode=1v1, Players=abc-123, def-456
[GameRoomStage] Player joined: abc-123 (1/2)
[GameRoomStage] Player joined: def-456 (2/2)
[GameRoomStage] Game starting! Players: abc-123, def-456
```

**API Server:**
```
[MatchmakingCtrl] Request: abc-123, Mode=1v1, Rating=1500
[MatchmakingCtrl] Request: def-456, Mode=1v1, Rating=1480
[MatchmakingCtrl] Creating match: RoomId=1674567890123, Players=abc-123, def-456
[MatchmakingCtrl] Game room created: 1674567890123
```

**Client:**
```
[Player1] Connecting to lobby...
[Player1] Authenticated: abc-123
[Player1] Requesting match...
[Player1] Match response: Waiting for opponent...
[Player1] Waiting for match...
[Player2] Connecting to lobby...
[Player2] Authenticated: def-456
[Player2] Requesting match...
[Player2] Match response: Match found!
[Player1] Received push: MatchFoundNotify
[Player1] Match found! RoomId=1674567890123
[Player2] Received push: MatchFoundNotify
[Player2] Match found! RoomId=1674567890123
[Player1] Joining game room 1674567890123...
[Player1] Joined game room!
[Player2] Joining game room 1674567890123...
[Player2] Joined game room!
[Player1] Received push: GameStartNotify
[Player1] Game started! Mode=1v1, Players=abc-123, def-456
[Player2] Received push: GameStartNotify
[Player2] Game started! Mode=1v1, Players=abc-123, def-456
```

## 핵심 개념 정리

### 1. 서버간 통신 패턴

```csharp
// Stage → API Service (로드밸런싱)
using var response = await StageSender.RequestToApiService(
    serviceId,
    CPacket.Of(request)
);

// API → Play (Stage 생성)
var result = await sender.CreateStage(
    playServerId,
    stageType,
    stageId,
    CPacket.Of(payload)
);

// API → Stage (메시지 전송)
sender.SendToStage(
    playNid,
    stageId,
    CPacket.Of(message)
);
```

### 2. Push 메시지 (서버 → 클라이언트)

```csharp
// Stage에서 클라이언트로 Push
actor.ActorSender.SendToClient(CPacket.Of(notification));

// 클라이언트에서 수신
_connector.OnReceive = (packet) =>
{
    // Push 메시지 처리
};
```

### 3. Stage 생명주기

```
CreateStage 요청
    ↓
OnCreate → OnPostCreate
    ↓
(플레이어 입장)
OnJoinStage → OnPostJoinStage
    ↓
(메시지 처리)
OnDispatch
    ↓
(종료)
OnDestroy
```

## 다음 단계 및 개선 사항

### 1. 고급 매칭 로직
- ELO 기반 레이팅 매칭
- 대기 시간 고려 (시간이 지날수록 매칭 범위 확대)
- 매칭 취소 기능

### 2. 에러 처리
- 타임아웃 처리
- 재연결 로직
- 매칭 실패 시 재시도

### 3. 스케일링
- 여러 Play Server에 GameRoom 분산
- API Server 수평 확장 (ServiceId 동일)
- Redis를 사용한 매칭 큐 공유

### 4. 모니터링
- 매칭 대기 시간 측정
- 게임룸 사용률
- 플레이어 분포

### 5. 실전 기능
```csharp
// 매칭 취소
public async Task CancelMatchRequest(IActor actor, IPacket packet)
{
    _matchingPlayers.TryRemove(actor.ActorSender.AccountId, out _);
    // API 서버에 취소 요청...
}

// 게임 결과 처리
public async Task OnGameEnd(IPacket packet)
{
    // 점수 계산, 리더보드 업데이트
    // 리워드 지급
    // 로비로 복귀
}

// 재접속 처리
public Task<bool> OnReconnect(IActor actor)
{
    // 게임 상태 동기화
    // 진행 중인 게임 정보 전송
}
```

## 참고 문서

- [서버간 통신 가이드](../07-server-communication.md)
- [API Controller 구현](../08-api-controller.md)
- [빠른 시작 가이드](../01-quick-start.md)

## 문제 해결

### "Matchmaking service unavailable"
**원인:** API 서버가 시작되지 않았거나 ServiceId가 일치하지 않음

**해결:**
- API 서버가 실행 중인지 확인
- ServiceId가 1000으로 설정되었는지 확인

### "Player not in expected player list"
**원인:** 매칭되지 않은 플레이어가 게임룸에 접속 시도

**해결:**
- 매칭 완료 알림을 받은 후 게임룸 입장
- GameRoomId를 정확히 전달

### "Connection timeout"
**원인:** 클라이언트 타임아웃 설정이 짧거나 서버 응답 지연

**해결:**
```csharp
new ConnectorConfig
{
    RequestTimeoutMs = 30000 // 30초로 증가
}
```

이 튜토리얼을 완료하면 PlayHouse-NET의 핵심 패턴을 이해하고 실전 게임 서버를 구축할 수 있는 기반을 갖추게 됩니다.
