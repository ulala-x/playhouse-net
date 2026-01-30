# PlayHouse-NET 개요

## 이름의 의미: 극장 메타포

PlayHouse의 모든 개념은 **극장(Theater)** 메타포를 따릅니다.

```
🎭 PlayHouse = 극장
    └── 🎬 Play Server = 연극 (실시간 공연이 펼쳐지는 곳)
            └── 🎪 Stage = 무대 (연극이 진행되는 공간)
                    └── 🎭 Actor = 배우 (무대 위에서 연기하는 참가자)
```

| 극장 용어 | PlayHouse 개념 | 실제 역할 |
|----------|---------------|----------|
| **극장 (PlayHouse)** | 프레임워크 | 모든 공연을 관리하는 시스템 |
| **연극 (Play)** | Play Server | 실시간 게임이 진행되는 서버 |
| **무대 (Stage)** | Stage | 게임방, 로비 등 게임이 펼쳐지는 공간 |
| **배우 (Actor)** | Actor | 무대 위 참가자 (플레이어) |

> 💡 마치 극장에서 여러 무대(Stage)가 있고, 각 무대에서 배우(Actor)들이 연기하듯이,
> PlayHouse에서는 여러 게임방(Stage)이 있고, 각 방에서 플레이어(Actor)들이 게임을 합니다.

---

## 한 줄 요약

**인게임 로직(Play Server)** + **아웃게임 로직(API Server)** + **Sender로 손쉬운 서버 간 통신**

---

## 전체 구조 (Big Picture)

```
┌─────────────────────────────────────────────────────────────────┐
│                          클라이언트                              │
└─────────────────────────────┬───────────────────────────────────┘
                              │
          ┌───────────────────┴───────────────────┐
          ▼                                       ▼
┌─────────────────────┐               ┌─────────────────────┐
│    Play Server      │◄───Sender────►│    API Server       │
│    (인게임 로직)     │               │   (아웃게임 로직)    │
│                     │               │                     │
│  ┌───────────────┐  │               │  - DB 조회/저장      │
│  │    Stage      │  │               │  - 외부 API 연동     │
│  │   (게임 방)    │  │               │  - 랭킹, 상점 등     │
│  │               │  │               │                     │
│  │  Actor A      │  │               └─────────────────────┘
│  │  Actor B      │  │
│  │  Actor C      │  │
│  └───────────────┘  │
└─────────────────────┘
```

---

## 두 가지 서버 타입

### Play Server (인게임)

| 항목 | 설명 |
|------|------|
| **역할** | 실시간 게임 로직 처리 |
| **특징** | Stage/Actor 모델, Stateful |
| **예시** | 게임 방, 로비, 실시간 채팅, 매치 |

**핵심 개념:**
- **Stage** = 게임방 (채팅방, 배틀필드, 로비)
- **Actor** = 참가자 (플레이어, 게임 세션)

### API Server (아웃게임)

| 항목 | 설명 |
|------|------|
| **역할** | Stateless 요청 처리 |
| **특징** | HTTP API 스타일, 빠른 응답, 수평 확장 용이 |
| **예시** | DB 조회, 랭킹, 상점, 결제, 외부 연동 |

**핵심 개념:**
- **IApiController** = API 핸들러 (HTTP Controller와 유사)

---

## Sender: 손쉬운 서버 간 통신

PlayHouse의 가장 강력한 기능 중 하나는 **Sender**를 통한 손쉬운 서버 간 통신입니다.

### Play Server → API Server

```csharp
// Stage에서 API Server로 요청-응답
var response = await StageSender.RequestToApiService(
    leaderboardServiceId,
    CPacket.Of(new GetRankRequest { PlayerId = actor.ActorSender.AccountId })
);
var rank = GetRankResponse.Parser.ParseFrom(response.Payload.DataSpan);
```

### API Server → Play Server

```csharp
// API Server에서 특정 Stage로 메시지 전송
ApiSender.SendToStage(playServerId, stageId, CPacket.Of(notification));
```

### Play Server → Play Server (Stage 간)

```csharp
// 다른 Play Server의 Stage로 메시지 전송
StageSender.SendToStage(targetPlayServerId, targetStageId, CPacket.Of(message));
```

**이게 전부입니다** - 복잡한 네트워크 코드가 필요 없습니다!

---

## 5분 만에 이해하기

| 개념 | 설명 | 비유 |
|------|------|------|
| **Play Server** | 인게임 (실시간, Stage/Actor) | 게임 진행 서버 |
| **API Server** | 아웃게임 (DB, 외부 연동) | 백오피스 서버 |
| **Stage** | 게임방 (채팅방, 배틀필드) | 방, 채널 |
| **Actor** | 참가자 (플레이어) | 플레이어 세션 |
| **Sender** | 서버 간 통신 | RPC 클라이언트 |

---

## 구현할 것

| 서버 타입 | 구현 대상 | 설명 |
|----------|----------|------|
| Play Server | `IStage`, `IActor` | 게임 로직 |
| API Server | `IApiController` | 데이터/외부 연동 |

**프레임워크가 처리하는 것:**
- 연결 관리 (TCP, WebSocket)
- 메시지 라우팅
- 동시성 제어 (Lock-Free)
- 직렬화/역직렬화
- 서버 간 통신

---

## 간단한 예시

### Play Server: Stage 구현

```csharp
public class GameRoom : IStage
{
    public IStageSender StageSender { get; private set; } = null!;

    public Task<(bool, IPacket)> OnCreate(IPacket packet)
    {
        // Stage 생성 시 초기화
        return Task.FromResult((true, CPacket.Empty));
    }

    public Task OnDispatch(IActor actor, IPacket packet)
    {
        // 클라이언트 메시지 처리
        actor.ActorSender.Reply(CPacket.Of(new EchoResponse()));
        return Task.CompletedTask;
    }

    // ... 나머지 메서드
}
```

### API Server: Controller 구현

```csharp
public class RankingController : IApiController
{
    public void Handles(IHandlerRegister register)
    {
        register.Add<GetRankRequest>(nameof(HandleGetRank));
    }

    private async Task HandleGetRank(IPacket packet, IApiSender sender)
    {
        var req = GetRankRequest.Parser.ParseFrom(packet.Payload.DataSpan);
        var rank = await _db.GetRankAsync(req.PlayerId);
        sender.Reply(CPacket.Of(new GetRankResponse { Rank = rank }));
    }
}
```

---

## 다음 단계

- **Stage/Actor 자세히**: [stage-actor.md](./stage-actor.md)
- **서버 간 통신 가이드**: [../guides/server-communication.md](../guides/server-communication.md)
- **바로 시작하기**: [../getting-started/quick-start.md](../getting-started/quick-start.md)
