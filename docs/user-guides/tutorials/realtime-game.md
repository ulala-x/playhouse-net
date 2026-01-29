# 실시간 게임 개발 튜토리얼 (GameLoop)

> 작성일: 2026-01-29
> 예상 소요 시간: 1-2시간
> 난이도: 중급

PlayHouse의 고해상도 GameLoop를 활용한 실시간 멀티플레이 게임 서버 구축 튜토리얼입니다. 60fps 게임루프 기반으로 플레이어 입력 처리, 상태 동기화, 충돌 처리를 다룹니다.

## 목차

1. [완성된 결과 미리보기](#완성된-결과-미리보기)
2. [GameLoop 개념 이해](#gameloop-개념-이해)
3. [프로젝트 설정](#프로젝트-설정)
4. [Proto 메시지 정의](#proto-메시지-정의)
5. [GameStage 구현](#gamestage-구현)
6. [클라이언트 통합](#클라이언트-통합)
7. [서버 권위 모델](#서버-권위-모델)
8. [성능 최적화](#성능-최적화)
9. [테스트](#테스트)

## 완성된 결과 미리보기

이 튜토리얼을 완료하면 다음과 같은 실시간 게임 서버를 구축하게 됩니다:

```
[실시간 2D 이동 게임]

- 60fps (16.67ms) 고정 타임스텝 게임루프
- 플레이어 입력 처리 (WASD 이동)
- 서버 권위 물리 시뮬레이션
- 20Hz (50ms) 상태 동기화
- 간단한 맵 경계 충돌 처리
- 클라이언트 측 예측 (선택사항)
```

### 흐름도

```
[클라이언트 A]                [게임 서버]                [클라이언트 B]
      │                           │                           │
      ├─ MoveInput(↑) ────────────→│                           │
      │                    ┌───────┴────────┐                  │
      │                    │  GameLoop Tick │                  │
      │                    │  - Input 처리  │                  │
      │                    │  - 물리 업데이트 │                  │
      │                    │  - 충돌 검사   │                  │
      │                    └───────┬────────┘                  │
      │                           │                           │
      │←──── GameState ────────────┼──── GameState ────────────→│
      │    (Player A: x,y)         │    (Player A: x,y)        │
      │    (Player B: x,y)         │    (Player B: x,y)        │
```

## GameLoop 개념 이해

### 고정 타임스텝 vs 가변 타임스텝

PlayHouse GameLoop는 **고정 타임스텝(Fixed Timestep)** 패턴을 사용합니다.

```
[가변 타임스텝 - 일반 타이머]
Frame 1: 14ms 경과 → Update(14ms)
Frame 2: 18ms 경과 → Update(18ms)  ← 물리가 불안정!
Frame 3: 16ms 경과 → Update(16ms)

[고정 타임스텝 - GameLoop]
Frame 1: 14ms 경과 → 누산기 = 14ms → (스킵)
Frame 2: 18ms 경과 → 누산기 = 32ms → Update(16ms) × 2회  ← 일정한 물리!
Frame 3: 16ms 경과 → 누산기 = 16ms → Update(16ms) × 1회
```

### 왜 고정 타임스텝인가?

```csharp
// ❌ 가변 타임스텝 문제
player.Position += player.Velocity * deltaTime;
// deltaTime이 변하면 → 같은 속도인데 이동 거리가 달라짐!
// 충돌 검사, 점프 높이 등이 프레임레이트에 의존

// ✅ 고정 타임스텝 장점
player.Position += player.Velocity * 0.01667f; // 항상 16.67ms
// 결정론적 시뮬레이션 → 리플레이, 롤백 가능
// 물리 안정성 → 예측 가능한 게임 플레이
```

### 틱 레이트와 네트워크 동기화

```
[게임루프 틱레이트: 60Hz (16.67ms)]
Tick 1 ─ Tick 2 ─ Tick 3 ─ Tick 4 ─ Tick 5 ─ Tick 6 ─▶
│        │        │        │        │        │
└────────┴────────┴────────┴────────┴────────┘
            50ms 경과 → 상태 브로드캐스트

[네트워크 동기화: 20Hz (50ms)]
- 게임 로직: 60fps로 정밀하게 업데이트
- 네트워크: 20fps로 대역폭 절약
- 3 틱마다 1번 상태 전송
```

**권장 설정:**

| 게임 장르 | 틱레이트 | 동기화 주기 | 용도 |
|----------|---------|----------|------|
| 전략 게임 | 10-20 Hz (50-100ms) | 5-10 Hz | 느린 턴제 게임 |
| 액션 게임 | 30-60 Hz (16-33ms) | 10-20 Hz | 실시간 전투 |
| FPS 게임 | 60-120 Hz (8-16ms) | 20-30 Hz | 정밀한 조준 |

## 프로젝트 설정

### 디렉토리 구조

```
RealtimeGameServer/
├── RealtimeGameServer.csproj
├── Program.cs
├── Proto/
│   └── realtime_game.proto
├── Stages/
│   └── BattleStage.cs
├── Actors/
│   └── PlayerActor.cs
└── GameLogic/
    ├── Player.cs
    ├── GameWorld.cs
    └── PhysicsEngine.cs
```

### 패키지 설치

```bash
dotnet new console -n RealtimeGameServer
cd RealtimeGameServer
dotnet add package PlayHouse
dotnet add package Google.Protobuf
```

## Proto 메시지 정의

`Proto/realtime_game.proto`:

```protobuf
syntax = "proto3";

package realtimegame;

option csharp_namespace = "RealtimeGameServer.Proto";

// ============================================
// 입력 메시지
// ============================================

// 플레이어 이동 입력 (Send 패턴 - 빠른 전송)
message MoveInput {
    float horizontal = 1;  // -1.0 ~ 1.0 (A/D 또는 좌/우)
    float vertical = 2;    // -1.0 ~ 1.0 (W/S 또는 상/하)
    int64 client_tick = 3; // 클라이언트 틱 번호 (지연 보상용)
}

// 액션 입력 (Request 패턴 - 서버 승인 필요)
message ActionInput {
    enum ActionType {
        NONE = 0;
        JUMP = 1;
        ATTACK = 2;
        SKILL = 3;
    }
    ActionType action = 1;
    int64 client_tick = 2;
}

message ActionInputReply {
    bool success = 1;
    string reason = 2;  // 실패 이유 (쿨다운, 스태미나 부족 등)
}

// ============================================
// 상태 동기화 메시지
// ============================================

// 게임 상태 스냅샷 (Push 패턴)
message GameState {
    int32 server_tick = 1;           // 서버 틱 번호
    int64 timestamp = 2;             // Unix timestamp (ms)
    repeated PlayerState players = 3; // 모든 플레이어 상태
}

// 플레이어 상태
message PlayerState {
    string account_id = 1;
    Vector2 position = 2;
    Vector2 velocity = 3;
    float rotation = 4;      // 0 ~ 360도
    int32 health = 5;
    bool is_grounded = 6;    // 땅에 닿았는지 여부
}

// 2D 벡터
message Vector2 {
    float x = 1;
    float y = 2;
}

// ============================================
// 게임 이벤트 메시지
// ============================================

// 플레이어 참가 알림
message PlayerJoinedNotify {
    string account_id = 1;
    Vector2 spawn_position = 2;
}

// 플레이어 퇴장 알림
message PlayerLeftNotify {
    string account_id = 1;
    string reason = 2;
}

// 충돌 이벤트
message CollisionEvent {
    string attacker_id = 1;
    string target_id = 2;
    int32 damage = 3;
    Vector2 collision_point = 4;
}

// ============================================
// 인증 메시지
// ============================================

message AuthenticateRequest {
    string user_id = 1;
    string token = 2;
}

message AuthenticateReply {
    string account_id = 1;
    bool success = 2;
}
```

### .csproj 설정

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <OutputType>Exe</OutputType>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="PlayHouse" Version="*" />
    <PackageReference Include="Google.Protobuf" Version="3.25.1" />
    <PackageReference Include="Grpc.Tools" Version="2.60.0" PrivateAssets="All" />
  </ItemGroup>

  <ItemGroup>
    <Protobuf Include="Proto/**/*.proto" GrpcServices="None" />
  </ItemGroup>
</Project>
```

## GameStage 구현

### 게임 로직 클래스

`GameLogic/Player.cs`:

```csharp
using RealtimeGameServer.Proto;

namespace RealtimeGameServer.GameLogic;

/// <summary>
/// 플레이어 게임 상태
/// </summary>
public class Player
{
    public string AccountId { get; set; } = string.Empty;
    public Vector2 Position { get; set; } = new();
    public Vector2 Velocity { get; set; } = new();
    public float Rotation { get; set; }
    public int Health { get; set; } = 100;
    public bool IsGrounded { get; set; } = true;

    // 입력 버퍼 (클라이언트 입력 저장)
    public MoveInput? LastInput { get; set; }

    // 게임 설정
    private const float MoveSpeed = 5.0f;      // 이동 속도 (units/s)
    private const float JumpForce = 10.0f;     // 점프 힘
    private const float Gravity = 20.0f;       // 중력 (units/s²)
    private const float Friction = 0.9f;       // 마찰력

    /// <summary>
    /// 물리 업데이트 (고정 타임스텝)
    /// </summary>
    public void Update(float deltaTime)
    {
        // 입력 기반 이동
        if (LastInput != null)
        {
            Velocity.X = LastInput.Horizontal * MoveSpeed;
            Velocity.Y += LastInput.Vertical * MoveSpeed * deltaTime;
        }

        // 중력 적용
        if (!IsGrounded)
        {
            Velocity.Y -= Gravity * deltaTime;
        }

        // 마찰력 적용 (땅에 있을 때)
        if (IsGrounded)
        {
            Velocity.X *= Friction;
        }

        // 위치 업데이트
        Position.X += Velocity.X * deltaTime;
        Position.Y += Velocity.Y * deltaTime;

        // 회전 업데이트 (이동 방향)
        if (Math.Abs(Velocity.X) > 0.1f)
        {
            Rotation = Velocity.X > 0 ? 0 : 180;
        }
    }

    /// <summary>
    /// 점프 시도
    /// </summary>
    public bool TryJump()
    {
        if (!IsGrounded)
            return false;

        Velocity.Y = JumpForce;
        IsGrounded = false;
        return true;
    }

    /// <summary>
    /// 현재 상태를 Proto 메시지로 변환
    /// </summary>
    public PlayerState ToProto()
    {
        return new PlayerState
        {
            AccountId = AccountId,
            Position = new Vector2 { X = Position.X, Y = Position.Y },
            Velocity = new Vector2 { X = Velocity.X, Y = Velocity.Y },
            Rotation = Rotation,
            Health = Health,
            IsGrounded = IsGrounded
        };
    }
}
```

`GameLogic/GameWorld.cs`:

```csharp
using RealtimeGameServer.Proto;

namespace RealtimeGameServer.GameLogic;

/// <summary>
/// 게임 월드 (맵, 충돌, 경계 등)
/// </summary>
public class GameWorld
{
    // 맵 경계
    public const float MapMinX = -50f;
    public const float MapMaxX = 50f;
    public const float MapMinY = 0f;
    public const float MapMaxY = 50f;

    private readonly Dictionary<string, Player> _players = new();

    /// <summary>
    /// 플레이어 추가
    /// </summary>
    public void AddPlayer(string accountId, Vector2 spawnPosition)
    {
        var player = new Player
        {
            AccountId = accountId,
            Position = new Vector2 { X = spawnPosition.X, Y = spawnPosition.Y },
            Velocity = new Vector2(),
            Health = 100,
            IsGrounded = true
        };

        _players[accountId] = player;
    }

    /// <summary>
    /// 플레이어 제거
    /// </summary>
    public void RemovePlayer(string accountId)
    {
        _players.Remove(accountId);
    }

    /// <summary>
    /// 플레이어 가져오기
    /// </summary>
    public Player? GetPlayer(string accountId)
    {
        return _players.GetValueOrDefault(accountId);
    }

    /// <summary>
    /// 모든 플레이어 가져오기
    /// </summary>
    public IReadOnlyDictionary<string, Player> GetAllPlayers()
    {
        return _players;
    }

    /// <summary>
    /// 물리 업데이트 (모든 플레이어)
    /// </summary>
    public void UpdatePhysics(float deltaTime)
    {
        foreach (var player in _players.Values)
        {
            player.Update(deltaTime);
            ClampToMapBounds(player);
            CheckGroundCollision(player);
        }
    }

    /// <summary>
    /// 맵 경계 제한
    /// </summary>
    private void ClampToMapBounds(Player player)
    {
        // X축 경계
        if (player.Position.X < MapMinX)
        {
            player.Position.X = MapMinX;
            player.Velocity.X = 0;
        }
        else if (player.Position.X > MapMaxX)
        {
            player.Position.X = MapMaxX;
            player.Velocity.X = 0;
        }

        // Y축 경계
        if (player.Position.Y < MapMinY)
        {
            player.Position.Y = MapMinY;
            player.Velocity.Y = 0;
            player.IsGrounded = true;
        }
        else if (player.Position.Y > MapMaxY)
        {
            player.Position.Y = MapMaxY;
            player.Velocity.Y = 0;
        }
    }

    /// <summary>
    /// 땅 충돌 검사 (간단한 버전)
    /// </summary>
    private void CheckGroundCollision(Player player)
    {
        // 바닥에 닿았는지 확인
        if (player.Position.Y <= MapMinY && player.Velocity.Y <= 0)
        {
            player.IsGrounded = true;
            player.Velocity.Y = 0;
        }
        else
        {
            player.IsGrounded = false;
        }
    }

    /// <summary>
    /// 랜덤 스폰 위치 생성
    /// </summary>
    public static Vector2 GetRandomSpawnPosition()
    {
        var random = new Random();
        return new Vector2
        {
            X = (float)(random.NextDouble() * (MapMaxX - MapMinX) + MapMinX),
            Y = MapMaxY / 2 // 중간 높이에서 스폰
        };
    }
}
```

### BattleStage 구현

`Stages/BattleStage.cs`:

```csharp
using PlayHouse.Abstractions;
using PlayHouse.Abstractions.Play;
using PlayHouse.Core.Shared;
using RealtimeGameServer.Proto;
using RealtimeGameServer.GameLogic;

namespace RealtimeGameServer.Stages;

public class BattleStage : IStage
{
    public IStageSender StageSender { get; }

    private readonly GameWorld _gameWorld = new();
    private readonly Dictionary<string, IActor> _actors = new();

    private int _serverTick = 0;
    private const int TickRate = 60; // 60 FPS
    private const int SyncRate = 20; // 20 FPS (3틱마다 동기화)

    public BattleStage(IStageSender stageSender)
    {
        StageSender = stageSender;
    }

    public Task<(bool result, IPacket reply)> OnCreate(IPacket packet)
    {
        Console.WriteLine($"[BattleStage] Stage created: {StageSender.StageId}");

        var reply = Packet.Empty("CreateStageReply");
        return Task.FromResult<(bool, IPacket)>((true, reply));
    }

    public Task OnPostCreate()
    {
        // 60fps (16.67ms) 게임루프 시작
        StageSender.StartGameLoop(
            fixedTimestep: TimeSpan.FromMilliseconds(1000.0 / TickRate),
            callback: OnGameLoopTick
        );

        Console.WriteLine($"[BattleStage] GameLoop started at {TickRate} Hz");
        return Task.CompletedTask;
    }

    public Task OnDestroy()
    {
        if (StageSender.IsGameLoopRunning)
        {
            StageSender.StopGameLoop();
        }

        Console.WriteLine($"[BattleStage] Stage destroyed: {StageSender.StageId}");
        return Task.CompletedTask;
    }

    public Task<bool> OnJoinStage(IActor actor)
    {
        Console.WriteLine($"[BattleStage] Actor joining: {actor.ActorSender.AccountId}");
        return Task.FromResult(true);
    }

    public Task OnPostJoinStage(IActor actor)
    {
        _actors[actor.ActorSender.AccountId] = actor;

        // 플레이어를 게임 월드에 추가
        var spawnPosition = GameWorld.GetRandomSpawnPosition();
        _gameWorld.AddPlayer(actor.ActorSender.AccountId, spawnPosition);

        // 다른 플레이어들에게 참가 알림
        var joinNotify = new PlayerJoinedNotify
        {
            AccountId = actor.ActorSender.AccountId,
            SpawnPosition = spawnPosition
        };

        BroadcastToAll(CPacket.Of(joinNotify));

        Console.WriteLine($"[BattleStage] Player spawned at ({spawnPosition.X:F2}, {spawnPosition.Y:F2})");
        return Task.CompletedTask;
    }

    public ValueTask OnConnectionChanged(IActor actor, bool isConnected)
    {
        Console.WriteLine($"[BattleStage] Connection changed: {actor.ActorSender.AccountId} = {isConnected}");
        return ValueTask.CompletedTask;
    }

    public Task OnDisconnect(IActor actor)
    {
        _actors.Remove(actor.ActorSender.AccountId);
        _gameWorld.RemovePlayer(actor.ActorSender.AccountId);

        // 다른 플레이어들에게 퇴장 알림
        var leaveNotify = new PlayerLeftNotify
        {
            AccountId = actor.ActorSender.AccountId,
            Reason = "Disconnected"
        };

        BroadcastToAll(CPacket.Of(leaveNotify));

        Console.WriteLine($"[BattleStage] Player left: {actor.ActorSender.AccountId}");
        return Task.CompletedTask;
    }

    public Task OnDispatch(IActor actor, IPacket packet)
    {
        switch (packet.MsgId)
        {
            case "MoveInput":
                HandleMoveInput(actor, packet);
                break;

            case "ActionInput":
                HandleActionInput(actor, packet);
                break;

            default:
                Console.WriteLine($"[BattleStage] Unknown message: {packet.MsgId}");
                break;
        }

        return Task.CompletedTask;
    }

    public Task OnDispatch(IPacket packet)
    {
        // 서버간 메시지 처리 (필요 시)
        return Task.CompletedTask;
    }

    /// <summary>
    /// 게임루프 틱 콜백 (60fps)
    /// </summary>
    private Task OnGameLoopTick(TimeSpan deltaTime, TimeSpan totalElapsed)
    {
        _serverTick++;

        // 1. 입력 처리는 이미 OnDispatch에서 완료 (입력 버퍼에 저장됨)

        // 2. 물리 업데이트
        _gameWorld.UpdatePhysics((float)deltaTime.TotalSeconds);

        // 3. 충돌 검사 (선택사항)
        // CheckCollisions();

        // 4. 상태 동기화 (20Hz = 3틱마다 1번)
        if (_serverTick % (TickRate / SyncRate) == 0)
        {
            BroadcastGameState();
        }

        // 5. 주기적 로깅 (1초마다)
        if (_serverTick % TickRate == 0)
        {
            Console.WriteLine($"[BattleStage] Tick {_serverTick}, Players: {_actors.Count}, Elapsed: {totalElapsed.TotalSeconds:F1}s");
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// 이동 입력 처리 (Send 패턴 - 빠른 전송)
    /// </summary>
    private void HandleMoveInput(IActor actor, IPacket packet)
    {
        var input = MoveInput.Parser.ParseFrom(packet.Payload.DataSpan);
        var player = _gameWorld.GetPlayer(actor.ActorSender.AccountId);

        if (player != null)
        {
            // 입력 버퍼에 저장 (다음 틱에서 물리 업데이트 시 사용)
            player.LastInput = input;
        }
    }

    /// <summary>
    /// 액션 입력 처리 (Request 패턴 - 서버 승인 필요)
    /// </summary>
    private void HandleActionInput(IActor actor, IPacket packet)
    {
        var input = ActionInput.Parser.ParseFrom(packet.Payload.DataSpan);
        var player = _gameWorld.GetPlayer(actor.ActorSender.AccountId);

        if (player == null)
        {
            actor.ActorSender.Reply(CPacket.Of(new ActionInputReply
            {
                Success = false,
                Reason = "Player not found"
            }));
            return;
        }

        bool success = false;
        string reason = string.Empty;

        switch (input.Action)
        {
            case ActionInput.Types.ActionType.Jump:
                success = player.TryJump();
                reason = success ? string.Empty : "Already in air";
                break;

            case ActionInput.Types.ActionType.Attack:
                // 공격 로직 (구현 생략)
                success = true;
                break;

            default:
                reason = "Unknown action";
                break;
        }

        // 응답 전송
        actor.ActorSender.Reply(CPacket.Of(new ActionInputReply
        {
            Success = success,
            Reason = reason
        }));

        if (success)
        {
            Console.WriteLine($"[BattleStage] {actor.ActorSender.AccountId} performed {input.Action}");
        }
    }

    /// <summary>
    /// 게임 상태 브로드캐스트 (20fps)
    /// </summary>
    private void BroadcastGameState()
    {
        var gameState = new GameState
        {
            ServerTick = _serverTick,
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };

        foreach (var player in _gameWorld.GetAllPlayers().Values)
        {
            gameState.Players.Add(player.ToProto());
        }

        BroadcastToAll(CPacket.Of(gameState));
    }

    /// <summary>
    /// 모든 클라이언트에 메시지 전송
    /// </summary>
    private void BroadcastToAll(IPacket packet)
    {
        foreach (var actor in _actors.Values)
        {
            actor.ActorSender.SendToClient(packet);
        }
    }
}
```

### PlayerActor 구현

`Actors/PlayerActor.cs`:

```csharp
using PlayHouse.Abstractions;
using PlayHouse.Abstractions.Play;
using PlayHouse.Core.Shared;
using RealtimeGameServer.Proto;

namespace RealtimeGameServer.Actors;

public class PlayerActor : IActor
{
    public IActorSender ActorSender { get; }

    public PlayerActor(IActorSender actorSender)
    {
        ActorSender = actorSender;
    }

    public Task OnCreate()
    {
        Console.WriteLine($"[PlayerActor] Actor created");
        return Task.CompletedTask;
    }

    public Task OnDestroy()
    {
        Console.WriteLine($"[PlayerActor] Actor destroyed: {ActorSender.AccountId}");
        return Task.CompletedTask;
    }

    public Task<(bool result, IPacket? reply)> OnAuthenticate(IPacket authPacket)
    {
        var authRequest = AuthenticateRequest.Parser.ParseFrom(authPacket.Payload.DataSpan);

        // 간단한 인증 (실제로는 토큰 검증 등 필요)
        var accountId = authRequest.UserId;
        ActorSender.AccountId = accountId;

        Console.WriteLine($"[PlayerActor] Authenticated: {accountId}");

        var reply = new AuthenticateReply
        {
            AccountId = accountId,
            Success = true
        };

        return Task.FromResult<(bool, IPacket?)>((true, CPacket.Of(reply)));
    }

    public Task OnPostAuthenticate()
    {
        Console.WriteLine($"[PlayerActor] Post-authenticate: {ActorSender.AccountId}");
        return Task.CompletedTask;
    }
}
```

## 클라이언트 통합

### 클라이언트 예제 코드

```csharp
using PlayHouse.Connector;
using PlayHouse.Connector.Protocol;
using RealtimeGameServer.Proto;

public class GameClient
{
    private readonly PlayHouse.Connector.Connector _connector;
    private bool _running = true;

    // 클라이언트 측 예측 (선택사항)
    private long _clientTick = 0;
    private Vector2 _predictedPosition = new();

    public GameClient()
    {
        _connector = new PlayHouse.Connector.Connector();
    }

    public async Task RunAsync()
    {
        // 초기화
        _connector.Init(new ConnectorConfig
        {
            RequestTimeoutMs = 10000
        });

        // Push 메시지 수신 핸들러
        _connector.OnReceive += OnReceivePush;

        // 연결
        Console.WriteLine("Connecting to server...");
        var connected = await _connector.ConnectAsync("127.0.0.1", 12000, 1L, "BattleStage");
        if (!connected)
        {
            Console.WriteLine("Connection failed!");
            return;
        }

        Console.WriteLine("Connected!");

        // 인증
        var authRequest = new AuthenticateRequest
        {
            UserId = $"Player-{Guid.NewGuid():N}",
            Token = "test-token"
        };

        using var authPacket = new Packet(authRequest);
        using var authResponse = await _connector.AuthenticateAsync(authPacket);

        var authReply = AuthenticateReply.Parser.ParseFrom(authResponse.Payload.DataSpan);
        Console.WriteLine($"Authenticated: {authReply.AccountId}");

        // 게임 루프 시작
        _ = Task.Run(GameLoopAsync);
        _ = Task.Run(InputLoopAsync);

        // 메인 루프 (Push 처리)
        while (_running)
        {
            _connector.MainThreadAction();
            await Task.Delay(16); // ~60 FPS
        }

        // 종료
        _connector.Disconnect();
        await _connector.DisposeAsync();
    }

    /// <summary>
    /// 입력 처리 루프
    /// </summary>
    private async Task InputLoopAsync()
    {
        while (_running)
        {
            // 키보드 입력 읽기 (실제로는 게임 엔진에서 처리)
            var key = Console.ReadKey(true).Key;

            var input = new MoveInput
            {
                Horizontal = 0,
                Vertical = 0,
                ClientTick = _clientTick
            };

            switch (key)
            {
                case ConsoleKey.W:
                    input.Vertical = 1.0f;
                    break;
                case ConsoleKey.S:
                    input.Vertical = -1.0f;
                    break;
                case ConsoleKey.A:
                    input.Horizontal = -1.0f;
                    break;
                case ConsoleKey.D:
                    input.Horizontal = 1.0f;
                    break;
                case ConsoleKey.Spacebar:
                    // 점프 (Request 패턴)
                    await SendJumpAsync();
                    continue;
                case ConsoleKey.Escape:
                    _running = false;
                    return;
            }

            // 이동 입력 전송 (Send 패턴 - Fire-and-Forget)
            using var packet = new Packet(input);
            _connector.Send(packet);

            // 클라이언트 측 예측 (선택사항)
            _predictedPosition.X += input.Horizontal * 5.0f * 0.016f;
            _predictedPosition.Y += input.Vertical * 5.0f * 0.016f;

            await Task.Delay(16); // ~60 FPS
        }
    }

    /// <summary>
    /// 점프 액션 (Request 패턴)
    /// </summary>
    private async Task SendJumpAsync()
    {
        var actionInput = new ActionInput
        {
            Action = ActionInput.Types.ActionType.Jump,
            ClientTick = _clientTick
        };

        using var packet = new Packet(actionInput);
        using var response = await _connector.RequestAsync(packet);

        var reply = ActionInputReply.Parser.ParseFrom(response.Payload.DataSpan);
        if (reply.Success)
        {
            Console.WriteLine("Jump successful!");
        }
        else
        {
            Console.WriteLine($"Jump failed: {reply.Reason}");
        }
    }

    /// <summary>
    /// 게임 루프 (클라이언트 틱)
    /// </summary>
    private async Task GameLoopAsync()
    {
        while (_running)
        {
            _clientTick++;
            await Task.Delay(16); // ~60 FPS
        }
    }

    /// <summary>
    /// Push 메시지 수신 핸들러
    /// </summary>
    private void OnReceivePush(long stageId, string stageType, IPacket packet)
    {
        switch (packet.MsgId)
        {
            case "GameState":
                HandleGameState(packet);
                break;

            case "PlayerJoinedNotify":
                var joinNotify = PlayerJoinedNotify.Parser.ParseFrom(packet.Payload.DataSpan);
                Console.WriteLine($"Player joined: {joinNotify.AccountId}");
                break;

            case "PlayerLeftNotify":
                var leftNotify = PlayerLeftNotify.Parser.ParseFrom(packet.Payload.DataSpan);
                Console.WriteLine($"Player left: {leftNotify.AccountId}");
                break;
        }
    }

    /// <summary>
    /// 게임 상태 업데이트
    /// </summary>
    private void HandleGameState(IPacket packet)
    {
        var gameState = GameState.Parser.ParseFrom(packet.Payload.DataSpan);

        Console.Clear();
        Console.WriteLine($"=== Game State (Tick: {gameState.ServerTick}) ===");

        foreach (var player in gameState.Players)
        {
            Console.WriteLine($"Player: {player.AccountId}");
            Console.WriteLine($"  Position: ({player.Position.X:F2}, {player.Position.Y:F2})");
            Console.WriteLine($"  Velocity: ({player.Velocity.X:F2}, {player.Velocity.Y:F2})");
            Console.WriteLine($"  Health: {player.Health}");
            Console.WriteLine($"  Grounded: {player.IsGrounded}");
        }

        Console.WriteLine("\nControls: W/A/S/D = Move, Space = Jump, Esc = Quit");
    }
}

// Program.cs
public class Program
{
    public static async Task Main(string[] args)
    {
        var client = new GameClient();
        await client.RunAsync();
    }
}
```

### 서버 시작 코드

`Program.cs` (서버):

```csharp
using Microsoft.Extensions.Logging;
using PlayHouse.Core.Play.Bootstrap;
using RealtimeGameServer.Stages;
using RealtimeGameServer.Actors;

var server = new PlayServerBootstrap()
    .Configure(options =>
    {
        options.ServerId = "battle-server-1";
        options.BindEndpoint = "tcp://127.0.0.1:11200";
        options.TcpPort = 12000;
        options.AuthenticateMessageId = "AuthenticateRequest";
        options.DefaultStageType = "BattleStage";
    })
    .UseStage<BattleStage, PlayerActor>("BattleStage")
    .UseLoggerFactory(LoggerFactory.Create(builder =>
    {
        builder.AddConsole();
        builder.SetMinimumLevel(LogLevel.Information);
    }))
    .Build();

await server.StartAsync();
Console.WriteLine("Battle server started on port 12000");
Console.WriteLine("Press Ctrl+C to stop.");

await Task.Delay(-1);
```

## 서버 권위 모델

### 클라이언트 측 예측 vs 서버 권위

```
[클라이언트 측 예측]
1. 클라이언트: 입력 발생 → 즉시 로컬 예측 (부드러운 움직임)
2. 클라이언트 → 서버: MoveInput 전송
3. 서버: 입력 검증 및 물리 시뮬레이션 (권위)
4. 서버 → 클라이언트: GameState (실제 위치)
5. 클라이언트: 예측 위치와 서버 위치 비교
   - 오차 작으면: 무시
   - 오차 크면: 서버 위치로 보정 (Reconciliation)
```

### 지연 보상 기법 (Lag Compensation)

간단한 버전:

```csharp
// 서버에서 클라이언트 틱을 활용한 지연 보상
private void HandleMoveInput(IActor actor, IPacket packet)
{
    var input = MoveInput.Parser.ParseFrom(packet.Payload.DataSpan);
    var player = _gameWorld.GetPlayer(actor.ActorSender.AccountId);

    if (player != null)
    {
        // 클라이언트 틱과 서버 틱 차이 계산
        var tickDiff = _serverTick - input.ClientTick;

        // 지연이 너무 크면 무시 (치팅 방지)
        if (tickDiff > 60) // 1초 이상
        {
            Console.WriteLine($"[LagCompensation] Input too old, discarded");
            return;
        }

        // 입력 적용
        player.LastInput = input;

        // 로그 (디버깅용)
        if (tickDiff > 10)
        {
            Console.WriteLine($"[LagCompensation] High latency: {tickDiff} ticks (~{tickDiff * 16}ms)");
        }
    }
}
```

### 서버 권위 검증 예제

```csharp
// 서버에서 이동 속도 검증 (치팅 방지)
private bool ValidatePlayerMovement(Player player, Vector2 newPosition)
{
    var distance = Math.Sqrt(
        Math.Pow(newPosition.X - player.Position.X, 2) +
        Math.Pow(newPosition.Y - player.Position.Y, 2)
    );

    const float MaxSpeedPerTick = 5.0f * (1.0f / 60.0f); // 5 units/s ÷ 60 fps
    const float Tolerance = 1.5f; // 여유분 (지연 고려)

    if (distance > MaxSpeedPerTick * Tolerance)
    {
        Console.WriteLine($"[AntiCheat] Invalid movement detected: {distance:F2} > {MaxSpeedPerTick * Tolerance:F2}");
        return false;
    }

    return true;
}
```

## 성능 최적화

### 1. 상태 동기화 최적화

```csharp
// ✅ 좋은 예: 변경된 플레이어만 전송
private void BroadcastGameStateDelta()
{
    var deltaState = new GameState
    {
        ServerTick = _serverTick,
        Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
    };

    foreach (var player in _gameWorld.GetAllPlayers().Values)
    {
        // 이동했거나 상태가 변경된 플레이어만 추가
        if (HasPlayerChanged(player))
        {
            deltaState.Players.Add(player.ToProto());
        }
    }

    if (deltaState.Players.Count > 0)
    {
        BroadcastToAll(CPacket.Of(deltaState));
    }
}

private bool HasPlayerChanged(Player player)
{
    // 속도가 0이 아니거나, 체력이 변경되었거나, 등등
    return Math.Abs(player.Velocity.X) > 0.01f ||
           Math.Abs(player.Velocity.Y) > 0.01f ||
           !player.IsGrounded;
}
```

### 2. 관심 영역 관리 (Area of Interest)

```csharp
// 가까운 플레이어에게만 상태 전송
private void BroadcastGameStateAOI()
{
    const float ViewRadius = 30.0f;

    foreach (var (accountId, actor) in _actors)
    {
        var viewer = _gameWorld.GetPlayer(accountId);
        if (viewer == null) continue;

        var localState = new GameState
        {
            ServerTick = _serverTick,
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };

        foreach (var player in _gameWorld.GetAllPlayers().Values)
        {
            var distance = Math.Sqrt(
                Math.Pow(player.Position.X - viewer.Position.X, 2) +
                Math.Pow(player.Position.Y - viewer.Position.Y, 2)
            );

            if (distance <= ViewRadius)
            {
                localState.Players.Add(player.ToProto());
            }
        }

        actor.ActorSender.SendToClient(CPacket.Of(localState));
    }
}
```

### 3. 틱레이트 동적 조정

```csharp
// 플레이어 수에 따라 동기화 주기 조정
private int GetDynamicSyncInterval()
{
    var playerCount = _actors.Count;

    if (playerCount <= 10)
        return 3; // 20Hz (3틱마다)
    else if (playerCount <= 50)
        return 6; // 10Hz (6틱마다)
    else
        return 12; // 5Hz (12틱마다)
}

private Task OnGameLoopTick(TimeSpan deltaTime, TimeSpan totalElapsed)
{
    _serverTick++;
    _gameWorld.UpdatePhysics((float)deltaTime.TotalSeconds);

    var syncInterval = GetDynamicSyncInterval();
    if (_serverTick % syncInterval == 0)
    {
        BroadcastGameState();
    }

    return Task.CompletedTask;
}
```

### 4. 메모리 풀링 활용

PlayHouse는 내부적으로 Packet 풀링을 지원합니다. 사용자 코드에서는 `using`만 사용하면 됩니다.

```csharp
// ✅ 자동으로 풀링됨
using var packet = new Packet(gameState);
BroadcastToAll(packet);
// using 종료 시 자동으로 풀로 반환
```

## 테스트

### 단위 테스트 예제

```csharp
using Xunit;
using RealtimeGameServer.GameLogic;
using RealtimeGameServer.Proto;

public class GameWorldTests
{
    [Fact]
    public void Player_Movement_Should_Respect_Map_Bounds()
    {
        // Arrange
        var world = new GameWorld();
        var spawnPos = new Vector2 { X = 0, Y = 0 };
        world.AddPlayer("player1", spawnPos);

        var player = world.GetPlayer("player1");
        player.Velocity.X = -1000; // 매우 빠른 속도

        // Act
        world.UpdatePhysics(1.0f); // 1초 업데이트

        // Assert
        Assert.True(player.Position.X >= GameWorld.MapMinX);
        Assert.Equal(0, player.Velocity.X); // 경계에서 속도가 0이 됨
    }

    [Fact]
    public void Player_Jump_Should_Only_Work_When_Grounded()
    {
        // Arrange
        var world = new GameWorld();
        var spawnPos = new Vector2 { X = 0, Y = 0 };
        world.AddPlayer("player1", spawnPos);

        var player = world.GetPlayer("player1");
        player.IsGrounded = true;

        // Act & Assert
        Assert.True(player.TryJump()); // 첫 점프 성공
        Assert.False(player.TryJump()); // 공중에서 점프 실패
    }

    [Fact]
    public void Gravity_Should_Pull_Player_Down()
    {
        // Arrange
        var world = new GameWorld();
        var spawnPos = new Vector2 { X = 0, Y = 10 };
        world.AddPlayer("player1", spawnPos);

        var player = world.GetPlayer("player1");
        player.IsGrounded = false;
        var initialY = player.Position.Y;

        // Act
        world.UpdatePhysics(0.1f); // 0.1초 업데이트

        // Assert
        Assert.True(player.Position.Y < initialY); // Y가 감소 (아래로 이동)
        Assert.True(player.Velocity.Y < 0); // 아래 방향 속도
    }
}
```

### 통합 테스트 예제

```csharp
using Xunit;
using PlayHouse.Connector;
using RealtimeGameServer.Proto;

public class BattleStageIntegrationTests : IAsyncLifetime
{
    private PlayHouse.Connector.Connector _client1;
    private PlayHouse.Connector.Connector _client2;

    public async Task InitializeAsync()
    {
        // 클라이언트 2명 연결
        _client1 = new PlayHouse.Connector.Connector();
        _client2 = new PlayHouse.Connector.Connector();

        _client1.Init(new ConnectorConfig { RequestTimeoutMs = 5000 });
        _client2.Init(new ConnectorConfig { RequestTimeoutMs = 5000 });

        await _client1.ConnectAsync("127.0.0.1", 12000, 1L, "BattleStage");
        await _client2.ConnectAsync("127.0.0.1", 12000, 1L, "BattleStage");

        // 인증
        using var auth1 = new Packet(new AuthenticateRequest { UserId = "player1" });
        using var auth2 = new Packet(new AuthenticateRequest { UserId = "player2" });

        await _client1.AuthenticateAsync(auth1);
        await _client2.AuthenticateAsync(auth2);
    }

    public async Task DisposeAsync()
    {
        _client1?.Disconnect();
        _client2?.Disconnect();

        await (_client1?.DisposeAsync() ?? ValueTask.CompletedTask);
        await (_client2?.DisposeAsync() ?? ValueTask.CompletedTask);
    }

    [Fact]
    public async Task GameState_Should_Contain_All_Players()
    {
        // Arrange
        GameState receivedState = null;
        _client1.OnReceive += (stageId, stageType, packet) =>
        {
            if (packet.MsgId == "GameState")
            {
                receivedState = GameState.Parser.ParseFrom(packet.Payload.DataSpan);
            }
        };

        // Act
        await Task.Delay(100); // 상태 동기화 대기
        _client1.MainThreadAction();

        // Assert
        Assert.NotNull(receivedState);
        Assert.Equal(2, receivedState.Players.Count);
    }

    [Fact]
    public async Task MoveInput_Should_Update_Player_Position()
    {
        // Arrange
        GameState initialState = null;
        GameState updatedState = null;

        _client1.OnReceive += (stageId, stageType, packet) =>
        {
            if (packet.MsgId == "GameState")
            {
                var state = GameState.Parser.ParseFrom(packet.Payload.DataSpan);
                if (initialState == null)
                    initialState = state;
                else
                    updatedState = state;
            }
        };

        // Act
        await Task.Delay(100); // 초기 상태 수신
        _client1.MainThreadAction();

        // 이동 입력 전송
        var moveInput = new MoveInput { Horizontal = 1.0f, Vertical = 0 };
        using var packet = new Packet(moveInput);
        _client1.Send(packet);

        await Task.Delay(200); // 업데이트 대기
        _client1.MainThreadAction();

        // Assert
        Assert.NotNull(initialState);
        Assert.NotNull(updatedState);

        var player1Initial = initialState.Players.First(p => p.AccountId == "player1");
        var player1Updated = updatedState.Players.First(p => p.AccountId == "player1");

        Assert.True(player1Updated.Position.X > player1Initial.Position.X);
    }
}
```

## 다음 단계

실시간 게임 개발의 고급 주제:

1. **상태 보간 (Interpolation)**
   - 서버 상태 사이의 부드러운 전환
   - `Lerp`, `Slerp` 활용

2. **예측 오류 보정 (Reconciliation)**
   - 클라이언트 예측과 서버 상태의 차이 처리
   - 부드러운 보정 애니메이션

3. **입력 버퍼링**
   - 네트워크 지터 대응
   - 입력 큐 관리

4. **롤백 넷코드 (Rollback Netcode)**
   - 격투 게임 스타일 네트워크
   - 결정론적 시뮬레이션 필수

5. **서버 사이드 리플레이**
   - 게임 틱 기록 및 재생
   - 치팅 검증

## 참고 자료

- [타이머 및 게임루프 가이드](../06-timer-gameloop.md)
- [타이머 시스템 사양](../../specifications/04-timer-system.md)
- [메시지 송수신 가이드](../03-messaging.md)
- [Stage 구현 가이드](../04-stage-implementation.md)

### 외부 참고 자료

- [Fix Your Timestep](https://gafferongames.com/post/fix_your_timestep/) - Glenn Fiedler
- [Networked Physics](https://gafferongames.com/post/networked_physics_2004/) - Glenn Fiedler
- [Source Multiplayer Networking](https://developer.valvesoftware.com/wiki/Source_Multiplayer_Networking) - Valve

## 문제 해결

### "Game loop already running"

**원인:** Stage에 이미 게임루프가 실행 중

**해결:**
```csharp
if (!StageSender.IsGameLoopRunning)
{
    StageSender.StartGameLoop(config, callback);
}
```

### 플레이어가 경계를 벗어남

**원인:** `ClampToMapBounds`가 물리 업데이트 후 호출되지 않음

**해결:**
```csharp
public void UpdatePhysics(float deltaTime)
{
    foreach (var player in _players.Values)
    {
        player.Update(deltaTime);
        ClampToMapBounds(player); // 반드시 호출!
    }
}
```

### 상태 동기화가 너무 느림

**원인:** SyncRate가 너무 낮음

**해결:**
```csharp
// 20Hz → 30Hz로 증가
private const int SyncRate = 30; // 30 FPS
private const int SyncInterval = TickRate / SyncRate; // 2틱마다
```

### 높은 지연 시 플레이어 워프

**원인:** 클라이언트 측 예측 없음

**해결:** 클라이언트에서 입력 즉시 로컬 예측 적용
```csharp
// 클라이언트 코드
_predictedPosition.X += input.Horizontal * MoveSpeed * deltaTime;
RenderPlayer(_predictedPosition); // 예측 위치로 렌더링

// 서버 상태 수신 시 보정
if (Distance(_predictedPosition, serverPosition) > threshold)
{
    _predictedPosition = Lerp(_predictedPosition, serverPosition, 0.2f);
}
```

---

**축하합니다!** 실시간 게임 서버 구축 튜토리얼을 완료했습니다. 이제 PlayHouse GameLoop를 활용하여 다양한 실시간 멀티플레이 게임을 개발할 수 있습니다.
