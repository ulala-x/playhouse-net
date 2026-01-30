# 서버간 통신

PlayHouse는 분산 서버 아키텍처를 지원하며, 다양한 방식으로 서버간 통신을 할 수 있습니다. 이 가이드에서는 서버간 통신의 모든 방식을 설명합니다.

## 개요

서버간 통신은 `ISender` 인터페이스를 통해 제공되며, 다음과 같은 컨텍스트에서 사용할 수 있습니다.

- `IActorSender` (Actor 핸들러)
- `IStageSender` (Stage 핸들러)
- `IApiSender` (API Controller 핸들러)

모든 통신 방식은 두 가지 패턴을 지원합니다.

- **Send**: 단방향 메시지 전송 (응답 없음)
- **Request**: 요청-응답 패턴 (응답 대기)

## 1. API 서버 통신

### 1.1 특정 API 서버로 전송

서버 ID를 지정하여 특정 API 서버와 통신합니다.

```csharp
// Stage에서 특정 API 서버로 Send
public class MyStage : IStage
{
    public async Task OnDispatch(IActor actor, IPacket packet)
    {
        var request = new PlayerDataRequest { PlayerId = actor.ActorSender.AccountId };

        // 단방향 전송
        StageSender.SendToApi("api-1", CPacket.Of(request));

        // 요청-응답 (async/await)
        var response = await StageSender.RequestToApi("api-1", CPacket.Of(request));
        var playerData = PlayerDataResponse.Parser.ParseFrom(response.Payload.DataSpan);

        // 요청-응답 (callback)
        StageSender.RequestToApi("api-1", CPacket.Of(request), (errorCode, reply) =>
        {
            if (errorCode == 0 && reply != null)
            {
                var data = PlayerDataResponse.Parser.ParseFrom(reply.Payload.DataSpan);
                // 처리...
            }
        });
    }
}
```

### 1.2 서비스 단위로 전송 (권장)

같은 ServiceId를 가진 API 서버 그룹에 로드밸런싱하여 전송합니다.

```csharp
// ServiceId를 이용한 전송 (RoundRobin 기본)
public async Task OnDispatch(IActor actor, IPacket packet)
{
    var request = new LeaderboardRequest { GameMode = "ranked" };
    ushort leaderboardServiceId = 100;

    // RoundRobin 방식으로 전송 (기본값)
    StageSender.SendToApiService(leaderboardServiceId, CPacket.Of(request));

    // Weighted 정책 사용 (Weight가 높은 서버 우선)
    StageSender.SendToApiService(
        leaderboardServiceId,
        CPacket.Of(request),
        ServerSelectionPolicy.Weighted
    );

    // 요청-응답 (async/await)
    var response = await StageSender.RequestToApiService(
        leaderboardServiceId,
        CPacket.Of(request)
    );
    var leaderboard = LeaderboardResponse.Parser.ParseFrom(response.Payload.DataSpan);

    // Weighted 정책으로 요청
    var weightedResponse = await StageSender.RequestToApiService(
        leaderboardServiceId,
        CPacket.Of(request),
        ServerSelectionPolicy.Weighted
    );
}
```

**서버 선택 정책**

- `ServerSelectionPolicy.RoundRobin` (기본값): 순차적으로 서버 선택
- `ServerSelectionPolicy.Weighted`: Weight가 높은 서버 우선 선택

## 2. Stage 통신

Play 서버의 특정 Stage와 직접 통신합니다.

```csharp
// Stage A에서 Stage B로 메시지 전송
public class StageA : IStage
{
    public async Task OnDispatch(IActor actor, IPacket packet)
    {
        string targetPlayServerId = "play-2";
        long targetStageId = 1001;

        var message = new InterStageMessage
        {
            FromStageId = StageSender.StageId,
            Content = "Hello from Stage A"
        };

        // 단방향 전송
        StageSender.SendToStage(targetPlayServerId, targetStageId, CPacket.Of(message));

        // 요청-응답 (async/await)
        var response = await StageSender.RequestToStage(
            targetPlayServerId,
            targetStageId,
            CPacket.Of(message)
        );
        var reply = InterStageReply.Parser.ParseFrom(response.Payload.DataSpan);

        // 요청-응답 (callback)
        StageSender.RequestToStage(
            targetPlayServerId,
            targetStageId,
            CPacket.Of(message),
            (errorCode, reply) =>
            {
                if (errorCode == 0 && reply != null)
                {
                    var data = InterStageReply.Parser.ParseFrom(reply.Payload.DataSpan);
                    // 처리...
                }
            }
        );
    }
}
```

**Stage간 메시지 수신**

```csharp
public class StageB : IStage
{
    // 서버간 메시지 핸들러 (클라이언트가 아닌 다른 Stage로부터)
    public Task OnDispatch(IPacket packet)
    {
        var message = InterStageMessage.Parser.ParseFrom(packet.Payload.DataSpan);

        // 응답 전송
        StageSender.Reply(CPacket.Of(new InterStageReply
        {
            Response = $"Echo: {message.Content}"
        }));

        return Task.CompletedTask;
    }
}
```

## 3. 시스템 메시지 통신

시스템 레벨의 메시지를 서버에 전송합니다. 시스템 메시지는 `ISystemController`에 등록된 핸들러가 처리합니다.

```csharp
// Stage에서 API 서버로 시스템 메시지 전송
public async Task OnDispatch(IActor actor, IPacket packet)
{
    var systemMsg = new SystemMaintenanceNotify
    {
        Message = "Server maintenance in 5 minutes"
    };

    // 단방향 시스템 메시지
    StageSender.SendToSystem("api-1", CPacket.Of(systemMsg));

    // 요청-응답 시스템 메시지
    // 주의: 수신 서버의 시스템 핸들러가 명시적으로 Reply를 호출해야 함
    var response = await StageSender.RequestToSystem("api-1", CPacket.Of(systemMsg));
    var systemReply = SystemMaintenanceReply.Parser.ParseFrom(response.Payload.DataSpan);
}
```

**시스템 메시지 핸들러 등록**

```csharp
// API Server 또는 Play Server에서 시스템 핸들러 등록
public class MySystemController : ISystemController
{
    public void Handles(ISystemHandlerRegister register)
    {
        register.Add("SystemMaintenanceNotify", HandleMaintenance);
    }

    private Task HandleMaintenance(IPacket packet, ISender sender)
    {
        var notify = SystemMaintenanceNotify.Parser.ParseFrom(packet.Payload.DataSpan);

        // 시스템 메시지 처리
        Console.WriteLine($"Maintenance: {notify.Message}");

        // RequestToSystem의 경우 명시적으로 응답 필요
        sender.Reply(CPacket.Of(new SystemMaintenanceReply { Acknowledged = true }));

        return Task.CompletedTask;
    }
}
```

## 4. 실전 예제

### 예제 1: 매치메이킹 시스템

```csharp
// API Server: 매치메이킹 요청 처리
public class MatchmakingController : IApiController
{
    public void Handles(IHandlerRegister register)
    {
        register.Add<MatchmakingRequest>(nameof(HandleMatchmaking));
    }

    private async Task HandleMatchmaking(IPacket packet, IApiSender sender)
    {
        var request = MatchmakingRequest.Parser.ParseFrom(packet.Payload.DataSpan);

        // 1. 매치 찾기 로직
        var matchId = FindOrCreateMatch(request.GameMode);
        var playServerId = SelectPlayServer();

        // 2. Play Server에 Stage 생성
        var createPayload = new CreateMatchStagePayload { MatchId = matchId };
        var result = await sender.CreateStage(
            playServerId,
            "MatchStage",
            matchId,
            CPacket.Of(createPayload)
        );

        // 3. 클라이언트에 응답
        sender.Reply(CPacket.Of(new MatchmakingResponse
        {
            Success = result.Result,
            PlayServerId = playServerId,
            StageId = matchId
        }));
    }
}
```

### 예제 2: 크로스 서버 채팅

```csharp
// Stage에서 다른 서버의 Stage로 채팅 전달
public class GameStage : IStage
{
    public async Task OnDispatch(IActor actor, IPacket packet)
    {
        if (packet.MsgId == "CrossServerChatRequest")
        {
            var request = CrossServerChatRequest.Parser.ParseFrom(packet.Payload.DataSpan);

            // 다른 Play Server의 Stage로 메시지 전송
            var chatMessage = new InterStageChatMessage
            {
                FromPlayer = actor.ActorSender.AccountId,
                Message = request.Message,
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };

            StageSender.SendToStage(
                request.TargetPlayServerId,
                request.TargetStageId,
                CPacket.Of(chatMessage)
            );

            actor.ActorSender.Reply(CPacket.Of(new CrossServerChatResponse
            {
                Success = true
            }));
        }
    }

    // 다른 Stage로부터 채팅 수신
    public Task OnDispatch(IPacket packet)
    {
        if (packet.MsgId == "InterStageChatMessage")
        {
            var message = InterStageChatMessage.Parser.ParseFrom(packet.Payload.DataSpan);

            // 현재 Stage의 모든 플레이어에게 브로드캐스트
            BroadcastToAllPlayers(message);
        }

        return Task.CompletedTask;
    }
}
```

### 예제 3: 분산 리더보드

```csharp
// Stage에서 리더보드 서비스로 점수 업데이트
public class GameStage : IStage
{
    private const ushort LeaderboardServiceId = 100;

    public async Task OnDispatch(IActor actor, IPacket packet)
    {
        if (packet.MsgId == "GameFinished")
        {
            var result = GameFinished.Parser.ParseFrom(packet.Payload.DataSpan);

            // 리더보드 서비스로 점수 전송 (RoundRobin)
            var updateRequest = new UpdateLeaderboardRequest
            {
                PlayerId = actor.ActorSender.AccountId,
                Score = result.FinalScore,
                GameMode = "ranked"
            };

            var response = await StageSender.RequestToApiService(
                LeaderboardServiceId,
                CPacket.Of(updateRequest)
            );

            var updateResult = UpdateLeaderboardResponse.Parser.ParseFrom(
                response.Payload.DataSpan
            );

            actor.ActorSender.Reply(CPacket.Of(new GameFinishedResponse
            {
                NewRank = updateResult.NewRank,
                ScoreUpdated = true
            }));
        }
    }
}
```

## 5. 에러 처리

### 타임아웃 처리

```csharp
public async Task OnDispatch(IActor actor, IPacket packet)
{
    try
    {
        var response = await StageSender.RequestToApi(
            "api-1",
            CPacket.Of(request)
        );
        // 성공
    }
    catch (TimeoutException)
    {
        // 타임아웃 처리 (기본 30초)
        actor.ActorSender.Reply(503); // Service Unavailable
    }
    catch (Exception ex)
    {
        // 기타 에러 처리
        actor.ActorSender.Reply(500); // Internal Server Error
    }
}
```

### Callback 에러 처리

```csharp
StageSender.RequestToApi("api-1", CPacket.Of(request), (errorCode, reply) =>
{
    if (errorCode != 0)
    {
        // 에러 코드 처리
        actor.ActorSender.Reply(errorCode);
        return;
    }

    if (reply == null)
    {
        actor.ActorSender.Reply(500);
        return;
    }

    // 정상 처리
    var response = Response.Parser.ParseFrom(reply.Payload.DataSpan);
    actor.ActorSender.Reply(CPacket.Of(response));
});
```

## 6. 주의사항

### Send vs Request

- **Send**: Fire-and-forget이 아닙니다. 서버가 메시지를 수신하지만 응답을 기대하지 않습니다.
- **Request**: 반드시 수신 측에서 `Reply()`를 호출해야 합니다. 그렇지 않으면 타임아웃이 발생합니다.

### 응답 패킷 자동 해제

```csharp
// async/await 사용 시 using으로 자동 해제 (권장)
using var response = await StageSender.RequestToApi("api-1", CPacket.Of(request));
var data = Response.Parser.ParseFrom(response.Payload.DataSpan);

// 또는 명시적 해제
var response = await StageSender.RequestToApi("api-1", CPacket.Of(request));
try
{
    var data = Response.Parser.ParseFrom(response.Payload.DataSpan);
}
finally
{
    response.Dispose();
}
```

### ServiceId 구성

동일한 역할을 하는 서버들은 같은 ServiceId를 사용해야 합니다.

```csharp
// API Server 설정 예시
builder.UsePlayHouse<ApiServer>(options =>
{
    options.ServerType = ServerType.Api;
    options.ServiceId = 100; // 리더보드 서비스
    options.ServerId = "leaderboard-1";
});

// 다른 인스턴스
builder.UsePlayHouse<ApiServer>(options =>
{
    options.ServerType = ServerType.Api;
    options.ServiceId = 100; // 동일한 ServiceId
    options.ServerId = "leaderboard-2";
});
```

이렇게 하면 `SendToApiService(100, ...)` 호출 시 `leaderboard-1`과 `leaderboard-2` 사이에서 로드밸런싱됩니다.

## 7. 요약

| 메서드 | 대상 | 로드밸런싱 | 용도 |
|--------|------|-----------|------|
| `SendToApi` / `RequestToApi` | 특정 API 서버 | X | 특정 서버 지정 필요 시 |
| `SendToApiService` / `RequestToApiService` | API 서비스 그룹 | O | 상태 없는 API 호출 (권장) |
| `SendToStage` / `RequestToStage` | 특정 Stage | X | Stage간 직접 통신 |
| `SendToSystem` / `RequestToSystem` | 시스템 핸들러 | X | 시스템 레벨 메시지 |

서버간 통신을 설계할 때는 다음을 고려하세요.

- 가능한 ServiceId 기반 통신 사용 (확장성)
- 타임아웃 및 에러 처리 구현
- 응답 패킷의 적절한 해제
- 메시지 프로토콜의 버전 관리
