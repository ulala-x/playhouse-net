# Stage/Actor 모델

> Play Server에서 인게임 로직을 처리하는 핵심 모델입니다.
> 전체 구조는 [개요](./overview.md)를 참고하세요.

---

## 극장에서 게임 서버로

PlayHouse는 **극장 메타포**를 차용했습니다. 극장에서 연극이 진행되는 방식을 떠올려보세요:

```
🎭 극장에서...                          🎮 PlayHouse에서...
─────────────────────────────────────────────────────────────
무대(Stage)가 있고                  →   게임방(Stage)이 있고
배우(Actor)들이 무대 위에서 연기하며  →   플레이어(Actor)들이 방 안에서 게임하며
각 배우는 자신의 역할(상태)을 가지고  →   각 플레이어는 자신의 데이터를 가지고
무대 위에서만 서로 상호작용합니다     →   같은 방 안에서만 서로 상호작용합니다
```

> 💡 **왜 이런 이름일까요?**
> - **Stage (무대)**: 연극이 펼쳐지는 공간 → 게임이 펼쳐지는 공간
> - **Actor (배우)**: 무대 위에서 연기하는 사람 → 게임에 참여하는 플레이어
> - **Play Server (연극)**: 공연이 진행되는 곳 → 실시간 게임이 진행되는 서버
> - **PlayHouse (극장)**: 모든 공연을 관리 → 프레임워크 전체

---

## 개념

### 🎪 Stage = 무대 (게임방)

```
┌─────────────────────────────────┐
│           Stage                 │
│         (게임 방/무대)           │
│                                 │
│   ┌───────┐ ┌───────┐ ┌───────┐ │
│   │Actor A│ │Actor B│ │Actor C│ │
│   │(배우1) │ │(배우2) │ │(배우3) │ │
│   └───────┘ └───────┘ └───────┘ │
│                                 │
│   - 독립된 메시지 큐             │
│   - 타이머 관리                  │
│   - 서버 간 통신                 │
└─────────────────────────────────┘
```

| 특징 | 설명 |
|------|------|
| **논리적 컨테이너** | 채팅방, 배틀필드, 로비 등 |
| **독립된 메시지 큐** | 동시성 걱정 없음 (Lock-Free) |
| **여러 Actor 포함** | 참가자들을 관리 |
| **타이머 내장** | 주기적 작업 처리 |

### 🎭 Actor = 배우 (참가자)

| 특징 | 설명 |
|------|------|
| **클라이언트와 1:1 매핑** | 연결된 플레이어 |
| **Stage 내에서만 존재** | 무대 없이 배우는 없다 |
| **독립된 상태 관리** | 플레이어별 데이터 |
| **인증 처리** | AccountId 설정 필수 |

---

## 최소 구현 예시

### Stage 구현

```csharp
public class ChatRoom : IStage
{
    // 프레임워크가 자동으로 주입
    public IStageLink StageLink { get; private set; } = null!;

    #region Stage Lifecycle

    public Task<(bool result, IPacket reply)> OnCreate(IPacket packet)
    {
        // Stage 생성 시 초기화 로직
        return Task.FromResult((true, CPacket.Empty));
    }

    public Task OnPostCreate()
    {
        // 타이머 등록, 초기 데이터 로드 등
        return Task.CompletedTask;
    }

    public Task OnDestroy()
    {
        // Stage 종료 시 정리 로직
        return Task.CompletedTask;
    }

    #endregion

    #region Actor Management

    public Task<bool> OnJoinStage(IActor actor)
    {
        // Actor 입장 허용 여부 결정
        return Task.FromResult(true);
    }

    public Task OnPostJoinStage(IActor actor)
    {
        // Actor 입장 후 처리 (환영 메시지 등)
        return Task.CompletedTask;
    }

    public ValueTask OnConnectionChanged(IActor actor, bool isConnected)
    {
        // 연결 상태 변경 처리
        return ValueTask.CompletedTask;
    }

    #endregion

    #region Message Dispatch

    public Task OnDispatch(IActor actor, IPacket packet)
    {
        // 클라이언트 메시지 처리
        actor.ActorLink.Reply(CPacket.Of(new ChatResponse { Message = "OK" }));
        return Task.CompletedTask;
    }

    public Task OnDispatch(IPacket packet)
    {
        // 서버 간 메시지 처리 (다른 Stage나 API Server로부터)
        StageLink.Reply(CPacket.Of(new AckResponse()));
        return Task.CompletedTask;
    }

    #endregion
}
```

### Actor 구현

```csharp
public class ChatUser : IActor
{
    // 프레임워크가 자동으로 주입
    public IActorLink ActorLink { get; private set; } = null!;

    // 플레이어별 상태
    public string Nickname { get; private set; } = "";

    #region Lifecycle

    public Task OnCreate()
    {
        // Actor 생성 시 초기화
        return Task.CompletedTask;
    }

    public Task OnDestroy()
    {
        // Actor 퇴장 시 정리
        return Task.CompletedTask;
    }

    #endregion

    #region Authentication

    public Task<(bool result, IPacket? reply)> OnAuthenticate(IPacket authPacket)
    {
        var req = AuthRequest.Parser.ParseFrom(authPacket.Payload.DataSpan);

        // 토큰 검증 등 인증 로직
        if (ValidateToken(req.Token))
        {
            // 필수: AccountId 설정
            ActorLink.AccountId = req.UserId;
            Nickname = req.Nickname;

            return Task.FromResult<(bool, IPacket?)>((
                true,
                CPacket.Of(new AuthResponse { Success = true })
            ));
        }

        return Task.FromResult<(bool, IPacket?)>((false, null));
    }

    public Task OnPostAuthenticate()
    {
        // 인증 후 처리 (API Server에서 사용자 데이터 로드 등)
        return Task.CompletedTask;
    }

    #endregion

    private bool ValidateToken(string token) => !string.IsNullOrEmpty(token);
}
```

---

## 서버 간 통신 (Stage에서)

Stage에서 다른 서버와 손쉽게 통신할 수 있습니다.

### API Server로 요청

```csharp
public async Task OnDispatch(IActor actor, IPacket packet)
{
    // API Server로 랭킹 조회 (ServiceId 기반, 로드밸런싱)
    var response = await StageLink.RequestToApiService(
        rankingServiceId,
        CPacket.Of(new GetRankRequest { PlayerId = actor.ActorLink.AccountId })
    );
    var rank = GetRankResponse.Parser.ParseFrom(response.Payload.DataSpan);

    // 클라이언트에 응답
    actor.ActorLink.Reply(CPacket.Of(new RankResponse { Rank = rank.Position }));
}
```

### 다른 Stage로 메시지 전송

```csharp
public Task OnDispatch(IActor actor, IPacket packet)
{
    // 다른 Play Server의 Stage로 단방향 메시지
    StageLink.SendToStage(
        targetPlayServerId,
        targetStageId,
        CPacket.Of(new CrossStageNotification { Message = "Hello!" })
    );

    return Task.CompletedTask;
}
```

### 요청-응답 패턴

```csharp
public async Task OnDispatch(IActor actor, IPacket packet)
{
    // 다른 Stage로 요청 후 응답 대기
    var response = await StageLink.RequestToStage(
        targetPlayServerId,
        targetStageId,
        CPacket.Of(new StatusRequest())
    );
    var status = StatusResponse.Parser.ParseFrom(response.Payload.DataSpan);

    actor.ActorLink.Reply(CPacket.Of(new ResultResponse { Status = status.Value }));
}
```

---

## 라이프사이클

### Stage 라이프사이클

```
CreateStage 요청
      │
      ▼
┌─────────────┐
│  OnCreate   │  ← Stage 초기화
└──────┬──────┘
       │ (성공 시)
       ▼
┌─────────────┐
│OnPostCreate │  ← 타이머 등록, 데이터 로드
└──────┬──────┘
       │
       ▼
┌─────────────┐
│   Active    │  ← 메시지 처리, Actor 관리
│   (운영 중)  │
└──────┬──────┘
       │ (CloseStage 호출)
       ▼
┌─────────────┐
│  OnDestroy  │  ← 정리
└─────────────┘
```

### Actor 라이프사이클

```
JoinStage 요청
      │
      ▼
┌──────────────┐
│   OnCreate   │  ← Actor 초기화
└──────┬───────┘
       │
       ▼
┌──────────────┐
│OnAuthenticate│  ← 인증 (AccountId 설정 필수!)
└──────┬───────┘
       │ (성공 시)
       ▼
┌───────────────────┐
│OnPostAuthenticate │  ← 사용자 데이터 로드
└──────┬────────────┘
       │
       ▼
┌──────────────┐
│ OnJoinStage  │  ← Stage가 입장 허용 여부 결정
└──────┬───────┘
       │ (허용 시)
       ▼
┌───────────────┐
│OnPostJoinStage│  ← 입장 완료 처리
└──────┬────────┘
       │
       ▼
┌──────────────┐
│    Active    │  ← 메시지 처리
│   (참가 중)   │
└──────┬───────┘
       │ (LeaveStage 또는 Stage 종료)
       ▼
┌──────────────┐
│  OnDestroy   │  ← 정리
└──────────────┘
```

---

## 핵심 API

### IStageLink (Stage에서 사용)

| 메서드 | 설명 |
|--------|------|
| `Reply(packet)` | 현재 요청에 응답 |
| `SendToApi(serverId, packet)` | 특정 API 서버로 전송 |
| `RequestToApiService(serviceId, packet)` | API 서비스로 요청-응답 |
| `SendToStage(serverId, stageId, packet)` | 다른 Stage로 전송 |
| `RequestToStage(serverId, stageId, packet)` | 다른 Stage로 요청-응답 |
| `AddRepeatTimer(delay, period, callback)` | 반복 타이머 등록 |
| `CloseStage()` | Stage 종료 |

### IActorLink (Actor에서 사용)

| 메서드 | 설명 |
|--------|------|
| `AccountId` | 사용자 식별자 (인증 시 설정 필수) |
| `Reply(packet)` | 현재 요청에 응답 |
| `SendToClient(packet)` | 클라이언트로 푸시 |
| `LeaveStageAsync()` | Stage에서 퇴장 |

---

## 더 알아보기

- **상세 구현 가이드**: [../guides/stage-implementation.md](../guides/stage-implementation.md)
- **Actor 구현 가이드**: [../guides/actor-implementation.md](../guides/actor-implementation.md)
- **서버 간 통신**: [../guides/server-communication.md](../guides/server-communication.md)
- **내부 동작**: [../internals/stage-actor.md](../internals/stage-actor.md)
