# 메시지 송수신 (Messaging)

클라이언트-서버 간 메시지 통신 패턴을 상세히 다룹니다.

## 목차

1. [메시지 패턴 개요](#메시지-패턴-개요)
2. [Send (Fire-and-Forget)](#send-fire-and-forget)
3. [Request/RequestAsync (요청-응답)](#requestrequestasync-요청-응답)
4. [Push (서버 → 클라이언트)](#push-서버--클라이언트)
5. [Proto 메시지 사용](#proto-메시지-사용)
6. [에러 처리](#에러-처리)
7. [고급 패턴](#고급-패턴)

## 메시지 패턴 개요

PlayHouse는 세 가지 메시지 패턴을 제공합니다.

| 패턴 | 방향 | 응답 | 용도 |
|------|------|------|------|
| **Send** | Client → Server | ❌ 없음 | 상태 업데이트, 로그 전송 등 |
| **Request** | Client → Server | ✅ 있음 | 데이터 조회, 명령 실행 등 |
| **Push** | Server → Client | ❌ 없음 | 이벤트 알림, 브로드캐스트 등 |

### 메시지 흐름

```
[Send 패턴]
Client ---Send---> Server
(응답 없음)

[Request 패턴]
Client ---Request---> Server
Client <---Reply----- Server

[Push 패턴]
Client <---Push------ Server
(요청 없음)
```

## Send (Fire-and-Forget)

응답이 필요 없는 단방향 메시지입니다.

### 클라이언트: Send API

```csharp
using PlayHouse.Connector.Protocol;
using MyGame.Proto;

// Proto 메시지 생성
var statusUpdate = new PlayerStatus
{
    Position = new Vector3 { X = 10.5f, Y = 0, Z = 5.2f },
    Health = 100,
    Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
};

// Packet 생성 및 전송
using var packet = new Packet(statusUpdate);
connector.Send(packet);

// 응답 대기 없이 즉시 리턴
Console.WriteLine("Status update sent");
```

### 서버: Send 처리

```csharp
public class GameStage : IStage
{
    public async Task OnDispatch(IActor actor, IPacket packet)
    {
        switch (packet.MsgId)
        {
            case "PlayerStatus":
                var status = PlayerStatus.Parser.ParseFrom(packet.Payload.DataSpan);
                Console.WriteLine($"Player position: ({status.Position.X}, {status.Position.Z})");

                // Send는 응답하지 않음
                // actor.ActorSender.Reply(...) 호출하지 않음
                break;
        }
    }
}
```

### Send 사용 사례

- **위치 업데이트**: 실시간 위치 동기화
- **로그 전송**: 클라이언트 이벤트 로깅
- **하트비트**: 연결 유지 신호

**주의:** Send는 "Fire-and-Forget"이지만, **서버는 메시지를 수신**합니다. 단지 클라이언트가 응답을 기다리지 않을 뿐입니다.

## Request/RequestAsync (요청-응답)

클라이언트가 서버에 요청을 보내고 응답을 받는 패턴입니다.

### RequestAsync (권장)

async/await 패턴으로 응답을 기다립니다.

```csharp
var echoRequest = new EchoRequest
{
    Content = "Hello, Server!",
    Sequence = 1
};

using var packet = new Packet(echoRequest);
using var response = await connector.RequestAsync(packet);

// 응답 파싱
var echoReply = EchoReply.Parser.ParseFrom(response.Payload.DataSpan);
Console.WriteLine($"Server replied: {echoReply.Content}");
Console.WriteLine($"Sequence: {echoReply.Sequence}");
```

### Request (콜백 방식)

Unity 등 메인 스레드 처리가 필요한 환경에서 유용합니다.

```csharp
var echoRequest = new EchoRequest
{
    Content = "Hello, Server!",
    Sequence = 1
};

using var packet = new Packet(echoRequest);

connector.Request(packet, response =>
{
    // 콜백 내에서 파싱 (response는 콜백 후 자동 dispose)
    var echoReply = EchoReply.Parser.ParseFrom(response.Payload.DataSpan);
    Console.WriteLine($"Server replied: {echoReply.Content}");
});

// 콜백 처리를 위해 주기적으로 호출
while (running)
{
    connector.MainThreadAction();
    await Task.Delay(16); // ~60 FPS
}
```

### 서버: Request 처리 및 응답

```csharp
public async Task OnDispatch(IActor actor, IPacket packet)
{
    switch (packet.MsgId)
    {
        case "EchoRequest":
            await HandleEchoRequest(actor, packet);
            break;

        case "GetPlayerDataRequest":
            await HandleGetPlayerData(actor, packet);
            break;
    }
}

private Task HandleEchoRequest(IActor actor, IPacket packet)
{
    // 요청 파싱
    var request = EchoRequest.Parser.ParseFrom(packet.Payload.DataSpan);

    // 응답 생성
    var reply = new EchoReply
    {
        Content = request.Content,
        Sequence = request.Sequence,
        ProcessedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
    };

    // 응답 전송
    actor.ActorSender.Reply(CPacket.Of(reply));

    return Task.CompletedTask;
}

private async Task HandleGetPlayerData(IActor actor, IPacket packet)
{
    var request = GetPlayerDataRequest.Parser.ParseFrom(packet.Payload.DataSpan);

    // 외부 API 호출 (AsyncIO 사용)
    var playerData = await LoadPlayerDataAsync(actor.ActorSender.AccountId);

    var reply = new GetPlayerDataReply
    {
        AccountId = actor.ActorSender.AccountId,
        Level = playerData.Level,
        Gold = playerData.Gold
    };

    actor.ActorSender.Reply(CPacket.Of(reply));
}
```

### 타임아웃 처리

```csharp
try
{
    using var packet = new Packet(request);
    using var response = await connector.RequestAsync(packet);

    // 응답 처리
    var reply = Reply.Parser.ParseFrom(response.Payload.DataSpan);
}
catch (ConnectorException ex) when (ex.ErrorCode == (ushort)ConnectorErrorCode.RequestTimeout)
{
    Console.WriteLine("Request timeout!");
    // 재시도 로직 등
}
```

### 타임아웃 설정

```csharp
var connector = new PlayHouse.Connector.Connector();
connector.Init(new ConnectorConfig
{
    RequestTimeoutMs = 5000  // 5초 타임아웃
});
```

## Push (서버 → 클라이언트)

서버가 클라이언트의 요청 없이 메시지를 보내는 패턴입니다.

### 서버: Push 메시지 전송

```csharp
public async Task OnDispatch(IActor actor, IPacket packet)
{
    if (packet.MsgId == "BroadcastTrigger")
    {
        var trigger = BroadcastNotify.Parser.ParseFrom(packet.Payload.DataSpan);

        // Push 메시지 생성
        var pushMessage = new BroadcastNotify
        {
            EventType = trigger.EventType,
            Data = trigger.Data,
            FromAccountId = long.Parse(actor.ActorSender.AccountId)
        };

        // 클라이언트에 Push 전송
        actor.ActorSender.SendToClient(CPacket.Of(pushMessage));

        // Request에 대한 응답도 별도로 전송
        actor.ActorSender.Reply(CPacket.Empty("BroadcastTriggerReply"));
    }
}
```

### 모든 클라이언트에 브로드캐스트

```csharp
public class GameStage : IStage
{
    private readonly Dictionary<string, IActor> _actors = new();

    public Task OnPostJoinStage(IActor actor)
    {
        _actors[actor.ActorSender.AccountId] = actor;
        return Task.CompletedTask;
    }

    public async Task OnDispatch(IActor actor, IPacket packet)
    {
        if (packet.MsgId == "ChatMessage")
        {
            var chatMsg = ChatMessage.Parser.ParseFrom(packet.Payload.DataSpan);

            // 모든 클라이언트에 브로드캐스트
            var broadcastMsg = new ChatBroadcast
            {
                SenderAccountId = actor.ActorSender.AccountId,
                Message = chatMsg.Message,
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };

            foreach (var otherActor in _actors.Values)
            {
                otherActor.ActorSender.SendToClient(CPacket.Of(broadcastMsg));
            }

            // 발신자에게 성공 응답
            actor.ActorSender.Reply(CPacket.Empty("ChatMessageReply"));
        }
    }
}
```

### 클라이언트: Push 수신

```csharp
// OnReceive 콜백 등록
connector.OnReceive += (stageId, stageType, packet) =>
{
    switch (packet.MsgId)
    {
        case "BroadcastNotify":
            var notify = BroadcastNotify.Parser.ParseFrom(packet.Payload.DataSpan);
            Console.WriteLine($"Received broadcast: {notify.EventType} - {notify.Data}");
            break;

        case "ChatBroadcast":
            var chat = ChatBroadcast.Parser.ParseFrom(packet.Payload.DataSpan);
            Console.WriteLine($"[{chat.SenderAccountId}]: {chat.Message}");
            break;
    }
};

// 주기적으로 MainThreadAction() 호출하여 콜백 처리
while (running)
{
    connector.MainThreadAction();
    await Task.Delay(16); // ~60 FPS
}
```

### Push 사용 사례

- **채팅**: 다른 플레이어의 메시지 수신
- **이벤트 알림**: 게임 이벤트 발생 알림
- **상태 변경**: 다른 플레이어의 상태 변경 알림
- **브로드캐스트**: 전체 공지사항

## Proto 메시지 사용

### Proto 파일 정의

`Proto/game.proto`:

```protobuf
syntax = "proto3";

package mygame;

// 위치 업데이트 (Send 패턴)
message PlayerStatus {
    Vector3 position = 1;
    int32 health = 2;
    int64 timestamp = 3;
}

message Vector3 {
    float x = 1;
    float y = 2;
    float z = 3;
}

// Echo 요청-응답 (Request 패턴)
message EchoRequest {
    string content = 1;
    int32 sequence = 2;
}

message EchoReply {
    string content = 1;
    int32 sequence = 2;
    int64 processed_at = 3;
}

// 브로드캐스트 (Push 패턴)
message BroadcastNotify {
    string event_type = 1;
    string data = 2;
    int64 from_account_id = 3;
}
```

### .csproj 설정

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Google.Protobuf" Version="3.25.1" />
    <PackageReference Include="Grpc.Tools" Version="2.60.0" PrivateAssets="All" />
  </ItemGroup>

  <ItemGroup>
    <Protobuf Include="Proto/**/*.proto" GrpcServices="None" />
  </ItemGroup>
</Project>
```

### Packet 생성 패턴

```csharp
using PlayHouse.Connector.Protocol;
using PlayHouse.Core.Shared;
using Google.Protobuf;

// ✅ 올바른 패턴: Proto 메시지 사용
var echoRequest = new EchoRequest { Content = "Hello", Sequence = 1 };
using var packet = new Packet(echoRequest);

// ✅ 서버에서 응답 생성
actor.ActorSender.Reply(CPacket.Of(echoReply));

// ❌ 잘못된 패턴: Empty 메시지 (테스트 외에는 사용 금지)
// using var packet = Packet.Empty("EchoRequest");
```

## 에러 처리

### 클라이언트: 에러 콜백

```csharp
connector.OnError += (stageId, stageType, errorCode, request) =>
{
    Console.WriteLine($"Error occurred: Code={errorCode}, Request={request.MsgId}");

    switch (errorCode)
    {
        case 404:
            Console.WriteLine("Resource not found");
            break;
        case 500:
            Console.WriteLine("Server internal error");
            break;
        default:
            Console.WriteLine($"Unknown error: {errorCode}");
            break;
    }
};

// MainThreadAction() 호출하여 에러 콜백 처리
connector.MainThreadAction();
```

### 서버: 에러 응답

```csharp
public async Task OnDispatch(IActor actor, IPacket packet)
{
    switch (packet.MsgId)
    {
        case "FailRequest":
            // 에러 코드와 함께 응답
            actor.ActorSender.Reply(500); // HTTP 스타일 에러 코드
            break;

        case "NotFoundRequest":
            actor.ActorSender.Reply(404);
            break;

        case "UnauthorizedRequest":
            actor.ActorSender.Reply(401);
            break;
    }
}
```

### RequestAsync 예외 처리

```csharp
try
{
    using var packet = new Packet(request);
    using var response = await connector.RequestAsync(packet);

    // 정상 응답 처리
    var reply = Reply.Parser.ParseFrom(response.Payload.DataSpan);
}
catch (ConnectorException ex)
{
    switch ((ConnectorErrorCode)ex.ErrorCode)
    {
        case ConnectorErrorCode.RequestTimeout:
            Console.WriteLine("Request timeout");
            break;
        case ConnectorErrorCode.Disconnected:
            Console.WriteLine("Disconnected from server");
            break;
        default:
            Console.WriteLine($"Server error: {ex.ErrorCode}");
            break;
    }
}
```

## 고급 패턴

### 병렬 요청 처리

여러 요청을 동시에 전송하고 모든 응답을 기다립니다.

```csharp
var tasks = Enumerable.Range(0, 10).Select(async i =>
{
    var request = new EchoRequest { Content = $"Message {i}", Sequence = i };
    using var packet = new Packet(request);
    return await connector.RequestAsync(packet);
}).ToList();

var responses = await Task.WhenAll(tasks);

foreach (var response in responses)
{
    var reply = EchoReply.Parser.ParseFrom(response.Payload.DataSpan);
    Console.WriteLine($"Received: Sequence={reply.Sequence}");
    response.Dispose();
}
```

### 순차 요청 처리

이전 응답을 바탕으로 다음 요청을 전송합니다.

```csharp
// 1. 플레이어 데이터 조회
var getPlayerReq = new GetPlayerDataRequest { AccountId = "player-123" };
using var packet1 = new Packet(getPlayerReq);
using var response1 = await connector.RequestAsync(packet1);

var playerData = GetPlayerDataReply.Parser.ParseFrom(response1.Payload.DataSpan);

// 2. 플레이어 데이터를 사용하여 다음 요청
var buyItemReq = new BuyItemRequest
{
    ItemId = "sword-01",
    CurrentGold = playerData.Gold
};
using var packet2 = new Packet(buyItemReq);
using var response2 = await connector.RequestAsync(packet2);

var buyItemReply = BuyItemReply.Parser.ParseFrom(response2.Payload.DataSpan);
Console.WriteLine($"Purchase success: {buyItemReply.Success}");
```

### Request와 Push 조합

Request 응답 + Push 알림을 함께 사용하는 패턴입니다.

```csharp
// 서버
public async Task OnDispatch(IActor actor, IPacket packet)
{
    if (packet.MsgId == "StartGameRequest")
    {
        // 1. 즉시 수락 응답
        actor.ActorSender.Reply(CPacket.Empty("StartGameReply"));

        // 2. 게임 준비 완료 후 Push 전송
        await PrepareGameAsync();

        var readyNotify = new GameReadyNotify
        {
            GameId = Guid.NewGuid().ToString(),
            StartTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };
        actor.ActorSender.SendToClient(CPacket.Of(readyNotify));
    }
}
```

```csharp
// 클라이언트
connector.OnReceive += (stageId, stageType, packet) =>
{
    if (packet.MsgId == "GameReadyNotify")
    {
        var notify = GameReadyNotify.Parser.ParseFrom(packet.Payload.DataSpan);
        Console.WriteLine($"Game ready! GameId={notify.GameId}");
        StartGame(notify.GameId);
    }
};

// Request 전송
using var packet = Packet.Empty("StartGameRequest");
using var response = await connector.RequestAsync(packet);
Console.WriteLine("Game start requested");

// Push 대기 (OnReceive 콜백에서 처리)
while (running)
{
    connector.MainThreadAction();
    await Task.Delay(16);
}
```

### 조건부 브로드캐스트

특정 조건을 만족하는 플레이어에게만 메시지를 전송합니다.

```csharp
public class GameStage : IStage
{
    private readonly Dictionary<string, IActor> _actors = new();

    // 팀별로 다른 메시지 전송
    private void BroadcastToTeam(string teamId, IMessage message)
    {
        foreach (var actor in _actors.Values)
        {
            // 실제로는 Actor에 팀 정보를 저장해야 함
            if (GetActorTeam(actor) == teamId)
            {
                actor.ActorSender.SendToClient(CPacket.Of(message));
            }
        }
    }

    // 특정 범위 내 플레이어에게 전송
    private void BroadcastToRange(Vector3 position, float range, IMessage message)
    {
        foreach (var actor in _actors.Values)
        {
            var actorPos = GetActorPosition(actor);
            if (Vector3.Distance(position, actorPos) <= range)
            {
                actor.ActorSender.SendToClient(CPacket.Of(message));
            }
        }
    }
}
```

### Packet Auto-Dispose

`using` 선언으로 Packet 리소스를 자동 정리합니다.

```csharp
// ✅ 권장: using으로 자동 정리
using var packet = new Packet(request);
using var response = await connector.RequestAsync(packet);
// 스코프 벗어날 때 자동으로 Dispose 호출

// ❌ 수동 정리 (잊어버릴 위험)
var packet = new Packet(request);
try
{
    var response = await connector.RequestAsync(packet);
    response.Dispose();
}
finally
{
    packet.Dispose();
}
```

## 성능 최적화 팁

### 메시지 크기 최소화

```csharp
// ✅ 필요한 필드만 전송
message PlayerStatus {
    Vector3 position = 1;  // 12 bytes
    int32 health = 2;       // 4 bytes
}

// ❌ 불필요한 데이터 포함
message PlayerStatusBad {
    Vector3 position = 1;
    int32 health = 2;
    string full_player_data = 3;  // 수 KB!
    repeated Item inventory = 4;   // 수백 개 아이템
}
```

### 배치 처리

여러 작은 메시지를 하나로 묶어 전송합니다.

```csharp
// Proto 정의
message BatchUpdate {
    repeated PlayerStatus player_updates = 1;
    repeated EnemyStatus enemy_updates = 2;
}

// 서버
var batch = new BatchUpdate();
foreach (var player in players)
{
    batch.PlayerUpdates.Add(player.GetStatus());
}
actor.ActorSender.SendToClient(CPacket.Of(batch));
```

### 메시지 풀링 (고급)

PlayHouse는 내부적으로 메시지 풀링을 지원합니다. 사용자 코드에서는 `using`만 사용하면 됩니다.

```csharp
// PlayHouse가 내부적으로 Packet 재사용
using var packet = new Packet(message);
connector.Send(packet);
// using 종료 시 Packet이 풀로 반환됨 (재사용 가능)
```

## 문제 해결

### "Packet already disposed"

**원인:** 이미 Dispose된 Packet을 사용 시도

**해결:**
```csharp
// ❌ 잘못된 패턴
IPacket savedPacket = null;
connector.Request(packet, response =>
{
    savedPacket = response; // ❌ 콜백 후 자동 dispose됨!
});

// ✅ 올바른 패턴
byte[] savedPayload = null;
connector.Request(packet, response =>
{
    savedPayload = response.Payload.DataSpan.ToArray(); // 데이터 복사
});
```

### "Request timeout"

**원인:**
- 서버가 응답하지 않음
- 네트워크 지연
- 타임아웃 설정이 너무 짧음

**해결:**
```csharp
// 타임아웃 증가
connector.Init(new ConnectorConfig
{
    RequestTimeoutMs = 30000  // 30초로 증가
});

// 서버에서 반드시 Reply 호출
actor.ActorSender.Reply(CPacket.Of(reply));
```

### Push 메시지가 수신되지 않음

**원인:** `MainThreadAction()` 호출하지 않음

**해결:**
```csharp
// 메인 루프에서 주기적으로 호출
while (running)
{
    connector.MainThreadAction();
    await Task.Delay(16); // ~60 FPS
}
```

## 다음 단계

- Server-to-Server Communication (추후 추가)
- Timer & GameLoop (추후 추가)
- Advanced Patterns (추후 추가)

## 참고 자료

- E2E 테스트: `tests/e2e/PlayHouse.E2E/Verifiers/MessagingVerifier.cs`
- Stage 구현 예제: `tests/e2e/PlayHouse.E2E.Shared/Infrastructure/TestStageImpl.cs`
- Push 검증: `tests/e2e/PlayHouse.E2E/Verifiers/PushVerifier.cs`
