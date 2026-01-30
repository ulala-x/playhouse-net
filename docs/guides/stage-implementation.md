# Stage 구현 가이드

> 작성일: 2026-01-29
> 버전: 1.0
> 목적: PlayHouse Stage 구현 방법 및 생명주기 가이드

## 개요

Stage는 PlayHouse에서 게임 로직이 실행되는 공간입니다. 여러 Actor(플레이어)가 하나의 Stage에 입장하여 상호작용하며, Stage는 게임 상태를 관리하고 메시지를 처리합니다.

### Stage의 역할

- **게임 상태 관리**: 게임룸, 매치, 던전 등의 상태 보관
- **메시지 처리**: 클라이언트와 서버 간 메시지 라우팅 및 처리
- **Actor 관리**: Stage에 입장한 플레이어(Actor)들의 생명주기 관리
- **타이머/게임루프**: 주기적인 게임 로직 실행

## IStage 인터페이스

Stage를 구현하려면 `IStage` 인터페이스를 구현해야 합니다.

```csharp
using PlayHouse.Abstractions;
using PlayHouse.Abstractions.Play;

public class MyStage : IStage
{
    public IStageLink StageLink { get; }

    public MyStage(IStageLink stageLink)
    {
        StageLink = stageLink;
    }

    // 생명주기 메서드들...
}
```

### IStageLink

`IStageLink`는 Stage에서 사용할 수 있는 통신 및 관리 기능을 제공합니다:

- **StageId**: Stage의 고유 식별자
- **StageType**: Stage 타입 이름
- **타이머 관리**: AddRepeatTimer, AddCountTimer, CancelTimer
- **Stage 관리**: CloseStage
- **비동기 작업**: AsyncCompute, AsyncIO
- **클라이언트 통신**: SendToClient
- **게임루프**: StartGameLoop, StopGameLoop

## Stage 생명주기

Stage는 다음과 같은 생명주기를 가집니다:

```
1. OnCreate()        → Stage 생성 및 초기화
2. OnPostCreate()    → 타이머 설정, 데이터 로드 등
3. OnJoinStage()     → Actor 입장 처리 (여러 번 호출 가능)
4. OnDispatch()      → 메시지 처리 (반복)
5. OnDestroy()       → Stage 종료 및 정리
```

### 1. OnCreate - Stage 생성

Stage가 생성될 때 최초로 호출되며, Stage의 초기 상태를 설정합니다.

```csharp
public class GameRoomStage : IStage
{
    private string _roomName = "";
    private int _maxPlayers = 0;
    private readonly Dictionary<string, IActor> _players = new();

    public IStageLink StageLink { get; }

    public GameRoomStage(IStageLink stageLink)
    {
        StageLink = stageLink;
    }

    public Task<(bool result, IPacket reply)> OnCreate(IPacket packet)
    {
        // 생성 요청 파싱
        var request = CreateRoomRequest.Parser.ParseFrom(packet.Payload.DataSpan);

        // Stage 상태 초기화
        _roomName = request.RoomName;
        _maxPlayers = request.MaxPlayers;

        // 생성 성공 응답
        var reply = new CreateRoomReply
        {
            RoomName = _roomName,
            MaxPlayers = _maxPlayers,
            Success = true
        };

        return Task.FromResult<(bool, IPacket)>((true, CPacket.Of(reply)));
    }

    // 나머지 메서드들...
}
```

**중요 사항:**
- `OnCreate`가 `(false, reply)`를 반환하면 Stage 생성이 실패하고 `OnDestroy`가 호출됩니다
- `(true, reply)`를 반환하면 `OnPostCreate`가 호출됩니다
- `reply`는 Stage 생성을 요청한 클라이언트나 서버에 전달됩니다

### 2. OnPostCreate - 추가 설정

Stage 생성이 성공한 후 호출되며, 타이머나 외부 데이터 로드 등을 수행합니다.

```csharp
public Task OnPostCreate()
{
    // 게임 시작 타이머 설정 (30초 후 자동 시작)
    StageLink.AddCountTimer(
        initialDelay: TimeSpan.FromSeconds(30),
        period: TimeSpan.FromSeconds(1),
        count: 1,
        callback: async () =>
        {
            // 게임 시작 로직
            await StartGame();
        }
    );

    return Task.CompletedTask;
}
```

**사용 예시:**
- 타이머 설정 (자동 시작, 주기적 상태 저장 등)
- 외부 API 호출 (게임 데이터 로드)
- 초기 상태 브로드캐스트

### 3. OnJoinStage - Actor 입장

Actor가 Stage에 입장하려고 할 때 호출됩니다. 입장 허용 여부를 결정할 수 있습니다.

```csharp
public Task<bool> OnJoinStage(IActor actor)
{
    // 입장 조건 검사
    if (_players.Count >= _maxPlayers)
    {
        // 방이 가득 찬 경우 입장 거부
        return Task.FromResult(false);
    }

    // Actor를 플레이어 목록에 추가
    _players[actor.ActorLink.AccountId] = actor;

    // 입장 허용
    return Task.FromResult(true);
}

public Task OnPostJoinStage(IActor actor)
{
    // Actor 입장 후 처리
    // 다른 플레이어들에게 입장 알림
    var notify = new PlayerJoinedNotify
    {
        AccountId = actor.ActorLink.AccountId,
        PlayerCount = _players.Count
    };

    // 모든 플레이어에게 브로드캐스트
    foreach (var player in _players.Values)
    {
        player.ActorLink.SendToClient(CPacket.Of(notify));
    }

    return Task.CompletedTask;
}
```

**중요 사항:**
- `OnJoinStage`가 `false`를 반환하면 Actor 입장이 거부됩니다
- `true`를 반환하면 `OnPostJoinStage`가 호출됩니다
- `OnPostJoinStage`에서는 다른 플레이어들에게 입장 알림 등을 보낼 수 있습니다

### 4. OnConnectionChanged - 연결 상태 변경

Actor의 네트워크 연결 상태가 변경될 때 호출됩니다.

```csharp
public ValueTask OnConnectionChanged(IActor actor, bool isConnected)
{
    if (isConnected)
    {
        // 재연결
        var notify = new PlayerReconnectedNotify
        {
            AccountId = actor.ActorLink.AccountId
        };
        BroadcastToOthers(actor, notify);
    }
    else
    {
        // 연결 끊김
        var notify = new PlayerDisconnectedNotify
        {
            AccountId = actor.ActorLink.AccountId
        };
        BroadcastToOthers(actor, notify);
    }

    return ValueTask.CompletedTask;
}
```

## 메시지 처리 (OnDispatch)

Stage는 두 가지 형태의 메시지를 처리합니다:

### 1. 클라이언트 메시지 (Actor 포함)

Actor가 보낸 메시지를 처리합니다.

```csharp
public async Task OnDispatch(IActor actor, IPacket packet)
{
    switch (packet.MsgId)
    {
        case "MoveRequest":
            await HandleMove(actor, packet);
            break;

        case "AttackRequest":
            await HandleAttack(actor, packet);
            break;

        case "ChatRequest":
            HandleChat(actor, packet);
            break;

        default:
            // 알 수 없는 메시지
            actor.ActorLink.Reply(500); // 에러 코드 반환
            break;
    }
}

private Task HandleMove(IActor actor, IPacket packet)
{
    var request = MoveRequest.Parser.ParseFrom(packet.Payload.DataSpan);

    // 이동 처리
    var newPosition = CalculateNewPosition(actor, request.Direction);

    // 응답 전송
    var reply = new MoveReply
    {
        Position = newPosition,
        Success = true
    };
    actor.ActorLink.Reply(CPacket.Of(reply));

    // 다른 플레이어들에게 브로드캐스트
    var notify = new PlayerMovedNotify
    {
        AccountId = actor.ActorLink.AccountId,
        Position = newPosition
    };
    BroadcastToOthers(actor, notify);

    return Task.CompletedTask;
}
```

### 2. 서버 간 메시지 (Actor 없음)

다른 서버나 Stage에서 보낸 메시지를 처리합니다. 메시지가 **Request-Response** 패턴인지 **Send (fire-and-forget)** 패턴인지에 따라 처리 방식이 다릅니다.

```csharp
public Task OnDispatch(IPacket packet)
{
    switch (packet.MsgId)
    {
        case "InterStageRequest":
            HandleInterStageRequest(packet);  // RequestToStage로 온 경우
            break;

        case "SystemNotify":
            HandleSystemNotify(packet);  // SendToStage로 온 경우
            break;

        default:
            break;
    }

    return Task.CompletedTask;
}

// RequestToStage로 온 요청: Reply() 호출 필요
private void HandleInterStageRequest(IPacket packet)
{
    var request = InterStageRequest.Parser.ParseFrom(packet.Payload.DataSpan);

    var reply = new InterStageReply
    {
        Response = $"Processed: {request.Content}"
    };

    // RequestToStage로 요청이 온 경우에만 Reply() 호출
    StageLink.Reply(CPacket.Of(reply));
}

// SendToStage로 온 알림: Reply() 호출하지 않음
private void HandleSystemNotify(IPacket packet)
{
    var notify = SystemNotify.Parser.ParseFrom(packet.Payload.DataSpan);

    // fire-and-forget이므로 응답 없이 처리만 수행
    ProcessSystemCommand(notify.Command);
}
```

**주의**: `SendToStage`로 보낸 메시지에 대해 `Reply()`를 호출하면 아무 일도 일어나지 않습니다.

## Stage 종료 (OnDestroy)

Stage가 종료될 때 호출됩니다. 리소스 정리, 데이터 저장 등을 수행합니다.

```csharp
public Task OnDestroy()
{
    // 모든 플레이어에게 종료 알림
    var notify = new RoomClosedNotify
    {
        Reason = "Room closed"
    };

    foreach (var player in _players.Values)
    {
        player.ActorLink.SendToClient(CPacket.Of(notify));
    }

    // 리소스 정리
    _players.Clear();

    return Task.CompletedTask;
}
```

**OnDestroy가 호출되는 경우:**
- `StageLink.CloseStage()` 호출 시
- `OnCreate`가 실패한 경우
- 서버 종료 시

## 유틸리티 메서드 예제

### 브로드캐스트

```csharp
private void BroadcastToAll(IMessage message)
{
    var packet = CPacket.Of(message);
    foreach (var player in _players.Values)
    {
        player.ActorLink.SendToClient(packet);
    }
}

private void BroadcastToOthers(IActor link, IMessage message)
{
    var packet = CPacket.Of(message);
    foreach (var player in _players.Values)
    {
        if (player.ActorLink.AccountId != link.ActorLink.AccountId)
        {
            player.ActorLink.SendToClient(packet);
        }
    }
}
```

### Stage 종료

```csharp
private void CloseRoomIfEmpty()
{
    if (_players.Count == 0)
    {
        // 플레이어가 모두 나간 경우 Stage 종료
        StageLink.CloseStage();
    }
}
```

## 전체 예제

```csharp
using PlayHouse.Abstractions;
using PlayHouse.Abstractions.Play;
using PlayHouse.Core.Shared;
using Google.Protobuf;

public class GameRoomStage : IStage
{
    private string _roomName = "";
    private int _maxPlayers = 0;
    private bool _isGameStarted = false;
    private readonly Dictionary<string, IActor> _players = new();

    public IStageLink StageLink { get; }

    public GameRoomStage(IStageLink stageLink)
    {
        StageLink = stageLink;
    }

    public Task<(bool result, IPacket reply)> OnCreate(IPacket packet)
    {
        var request = CreateRoomRequest.Parser.ParseFrom(packet.Payload.DataSpan);

        _roomName = request.RoomName;
        _maxPlayers = request.MaxPlayers;

        var reply = new CreateRoomReply
        {
            RoomName = _roomName,
            MaxPlayers = _maxPlayers,
            Success = true
        };

        return Task.FromResult<(bool, IPacket)>((true, CPacket.Of(reply)));
    }

    public Task OnPostCreate()
    {
        return Task.CompletedTask;
    }

    public Task<bool> OnJoinStage(IActor actor)
    {
        if (_players.Count >= _maxPlayers)
        {
            return Task.FromResult(false);
        }

        _players[actor.ActorLink.AccountId] = actor;
        return Task.FromResult(true);
    }

    public Task OnPostJoinStage(IActor actor)
    {
        var notify = new PlayerJoinedNotify
        {
            AccountId = actor.ActorLink.AccountId,
            PlayerCount = _players.Count
        };

        BroadcastToAll(notify);
        return Task.CompletedTask;
    }

    public ValueTask OnConnectionChanged(IActor actor, bool isConnected)
    {
        var notify = isConnected
            ? new PlayerReconnectedNotify { AccountId = actor.ActorLink.AccountId }
            : new PlayerDisconnectedNotify { AccountId = actor.ActorLink.AccountId };

        BroadcastToOthers(actor, notify);
        return ValueTask.CompletedTask;
    }

    public async Task OnDispatch(IActor actor, IPacket packet)
    {
        switch (packet.MsgId)
        {
            case "StartGameRequest":
                await HandleStartGame(actor);
                break;

            case "LeaveRoomRequest":
                await HandleLeaveRoom(actor);
                break;

            default:
                actor.ActorLink.Reply(500);
                break;
        }
    }

    public Task OnDispatch(IPacket packet)
    {
        // 서버 간 메시지 처리
        return Task.CompletedTask;
    }

    public Task OnDestroy()
    {
        var notify = new RoomClosedNotify { Reason = "Room closed" };
        BroadcastToAll(notify);
        _players.Clear();
        return Task.CompletedTask;
    }

    private Task HandleStartGame(IActor actor)
    {
        if (_isGameStarted)
        {
            actor.ActorLink.Reply(CPacket.Of(new StartGameReply
            {
                Success = false,
                Error = "Already started"
            }));
            return Task.CompletedTask;
        }

        _isGameStarted = true;

        var reply = new StartGameReply { Success = true };
        actor.ActorLink.Reply(CPacket.Of(reply));

        var notify = new GameStartedNotify { RoomName = _roomName };
        BroadcastToAll(notify);

        return Task.CompletedTask;
    }

    private async Task HandleLeaveRoom(IActor actor)
    {
        actor.ActorLink.Reply(CPacket.Of(new LeaveRoomReply { Success = true }));

        _players.Remove(actor.ActorLink.AccountId);

        var notify = new PlayerLeftNotify
        {
            AccountId = actor.ActorLink.AccountId,
            PlayerCount = _players.Count
        };
        BroadcastToAll(notify);

        await actor.ActorLink.LeaveStageAsync();

        if (_players.Count == 0)
        {
            StageLink.CloseStage();
        }
    }

    private void BroadcastToAll(IMessage message)
    {
        var packet = CPacket.Of(message);
        foreach (var player in _players.Values)
        {
            player.ActorLink.SendToClient(packet);
        }
    }

    private void BroadcastToOthers(IActor link, IMessage message)
    {
        var packet = CPacket.Of(message);
        foreach (var player in _players.Values)
        {
            if (player.ActorLink.AccountId != link.ActorLink.AccountId)
            {
                player.ActorLink.SendToClient(packet);
            }
        }
    }
}
```

## 다음 단계

- [Actor 구현 가이드](05-actor-implementation.md) - Actor 구현 방법
- [타이머 및 게임루프](06-timer-gameloop.md) - 타이머와 게임루프 사용법
