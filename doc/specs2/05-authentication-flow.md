# 05. 인증 및 Stage 입장 프레임워크 구현 가이드

## 문서 목적

이 문서는 Play 서버의 인증 및 Stage 입장 **프레임워크 내부 구현**을 설명합니다. 컨텐츠 개발자가 구현할 코드가 아닌, 프레임워크가 제공해야 하는 인증 처리 흐름의 구현 방법을 다룹니다.

---

## 1. 아키텍처 개요

### 1.1 인증 흐름 (2-tier)

```
Client → Play Server → API Server
        (인증 + Stage)  (Stateless 요청 처리)
```

### 1.2 시퀀스 다이어그램

```
┌────────┐      ┌────────────┐      ┌──────────────┐      ┌──────────┐
│ Client │      │ TcpSession │      │  BaseStage   │      │ BaseActor│
└───┬────┘      └──────┬─────┘      └──────┬───────┘      └────┬─────┘
    │                  │                   │                   │
    │  TCP Connect     │                   │                   │
    │ ────────────────>│                   │                   │
    │                  │  OnConnect()      │                   │
    │                  │ ─────────────────>│                   │
    │                  │                   │                   │
    │  AuthPacket      │                   │                   │
    │ ────────────────>│                   │                   │
    │                  │  JoinStageReq     │                   │
    │                  │ ─────────────────>│                   │
    │                  │                   │  CreateActor()    │
    │                  │                   │ ─────────────────>│
    │                  │                   │  OnCreate()       │
    │                  │                   │ ─────────────────>│
    │                  │                   │  OnAuthenticate() │
    │                  │                   │ ─────────────────>│
    │                  │                   │  true/false       │
    │                  │                   │ <─────────────────│
    │                  │                   │                   │
    │                  │                   │  [if true]        │
    │                  │                   │  OnPostAuthenticate()
    │                  │                   │ ─────────────────>│
    │                  │                   │                   │
    │                  │                   │  OnJoinStage()    │
    │                  │                   │ <──── (IStage)────│
    │                  │                   │  OnPostJoinStage()│
    │                  │                   │ <──── (IStage)────│
    │                  │                   │                   │
    │  JoinStageRes    │                   │                   │
    │ <────────────────│                   │                   │
    │                  │                   │                   │
```

---

## 2. JoinStageCmd 구현 (핵심 인증 흐름)

클라이언트의 Stage 입장 요청을 처리하는 프레임워크 명령입니다.

```csharp
// 참조: D:\project\kairos\playhouse\playhouse-net\PlayHouse\PlayHouse\Core\Play\Base\Command\JoinStageCmd.cs
internal class JoinStageCmd : IBaseStageCmd
{
    private readonly IServerInfoCenter _serverInfoCenter;
    private readonly PlayProducer _playProducer;
    private readonly LOG<JoinStageCmd> _log = new();

    public JoinStageCmd(IServerInfoCenter serverInfoCenter, PlayProducer playProducer)
    {
        _serverInfoCenter = serverInfoCenter;
        _playProducer = playProducer;
    }

    public async Task Execute(BaseStage baseStage, RoutePacket routePacket)
    {
        var req = JoinStageReq.Parser.ParseFrom(routePacket.Span);
        var routeHeader = routePacket.RouteHeader;
        var stageType = baseStage.StageSender.StageType;

        try
        {
            // 1. XActorSender 생성
            var actorSender = new XActorSender(
                accountId: routeHeader.AccountId,
                sessionNid: req.SessionNid,
                sid: req.Sid,
                apiNid: routeHeader.From,
                baseStage: baseStage,
                serverInfoCenter: _serverInfoCenter
            );

            // 2. IActor 인스턴스 생성 (PlayProducer 팩토리 사용)
            var actor = _playProducer.GetActor(stageType, actorSender);

            // 3. IActor.OnCreate() 호출
            await actor.OnCreate();
            _log.Debug(() => $"Actor created for stage {baseStage.StageSender.StageId}");

            // 4. IActor.OnAuthenticate() 호출
            var authPacket = CPacket.Of(req.PayloadId, req.Payload);
            var authResult = await actor.OnAuthenticate(authPacket);

            if (!authResult)
            {
                // 인증 실패 → Actor 정리 및 에러 응답
                _log.Warn(() => $"Authentication failed for account {routeHeader.AccountId}");
                await actor.OnDestroy();
                baseStage.StageSender.Reply((ushort)BaseErrorCode.AuthenticationFailed);
                return;
            }

            // 5. AccountId 유효성 검증 (필수 설정 확인)
            if (string.IsNullOrEmpty(actorSender.AccountId))
            {
                _log.Error(() => "AccountId must be set in OnAuthenticate");
                await actor.OnDestroy();
                throw new InvalidOperationException(
                    "OnAuthenticate returned true but AccountId was not set. " +
                    "Content developer must set ActorSender.AccountId on successful authentication.");
            }

            // 6. IActor.OnPostAuthenticate() 호출
            await actor.OnPostAuthenticate();
            _log.Debug(() => $"Post-authenticate completed for {actorSender.AccountId}");

            // 7. IStage.OnJoinStage() 호출
            var joinResult = await baseStage.Stage.OnJoinStage(actor);

            if (!joinResult)
            {
                // 입장 거부 → Actor 정리 및 에러 응답
                _log.Warn(() => $"Join stage rejected for {actorSender.AccountId}");
                await actor.OnDestroy();
                baseStage.StageSender.Reply((ushort)BaseErrorCode.JoinStageFailed);
                return;
            }

            // 8. BaseActor 생성 및 등록
            var baseActor = new BaseActor(actor, actorSender);
            baseStage.AddActor(baseActor);

            // 9. IStage.OnPostJoinStage() 호출
            await baseStage.Stage.OnPostJoinStage(actor);
            _log.Info(() => $"Actor {actorSender.AccountId} joined stage {baseStage.StageSender.StageId}");

            // 10. 성공 응답
            var res = new JoinStageRes
            {
                PayloadId = "",
                Payload = ByteString.Empty
            };
            baseStage.StageSender.Reply(res);
        }
        catch (Exception ex)
        {
            _log.Error(() => $"JoinStage failed: {ex.Message}");
            _log.Error(() => $"StackTrace: {ex.StackTrace}");
            baseStage.StageSender.Reply((ushort)BaseErrorCode.UncheckedContentsError);
        }
    }
}
```

---

## 3. CreateJoinStageCmd 구현 (Stage 생성 + 입장)

Stage가 없으면 생성하고 입장하는 복합 명령입니다.

```csharp
// 참조: D:\project\kairos\playhouse\playhouse-net\PlayHouse\PlayHouse\Core\Play\Base\Command\CreateJoinStageCmd.cs
internal class CreateJoinStageCmd : IBaseStageCmd
{
    private readonly IServerInfoCenter _serverInfoCenter;
    private readonly PlayProducer _playProducer;
    private readonly PlayDispatcher _playDispatcher;

    public async Task Execute(BaseStage baseStage, RoutePacket routePacket)
    {
        var req = CreateJoinStageReq.Parser.ParseFrom(routePacket.Span);
        var routeHeader = routePacket.RouteHeader;
        var stageId = routeHeader.StageId;
        var isCreated = false;

        try
        {
            // Stage가 존재하지 않으면 생성
            if (baseStage == null)
            {
                // 1. Stage 생성
                var stageSender = new XStageSender(
                    serviceId: _serviceId,
                    stageId: stageId,
                    dispatcher: _playDispatcher,
                    clientCommunicator: _clientCommunicator,
                    reqCache: _reqCache
                );
                stageSender.SetStageType(req.StageType);

                var stage = _playProducer.GetStage(req.StageType, stageSender);
                baseStage = new BaseStage(stage, stageSender, _cmdHandler);

                // 2. IStage.OnCreate() 호출
                var createPacket = CPacket.Of(req.CreatePayloadId, req.CreatePayload);
                var (createResult, createReply) = await stage.OnCreate(createPacket);

                if (!createResult)
                {
                    stageSender.Reply((ushort)BaseErrorCode.StageCreateFailed);
                    return;
                }

                // 3. IStage.OnPostCreate() 호출
                await stage.OnPostCreate();

                // 4. Stage 등록
                _playDispatcher.RegisterStage(stageId, baseStage);
                isCreated = true;
            }

            // Actor 생성 및 입장 (JoinStageCmd와 동일한 로직)
            var actorSender = new XActorSender(
                routeHeader.AccountId,
                req.SessionNid,
                req.Sid,
                routeHeader.From,
                baseStage,
                _serverInfoCenter
            );

            var actor = _playProducer.GetActor(req.StageType, actorSender);
            await actor.OnCreate();

            var joinPacket = CPacket.Of(req.JoinPayloadId, req.JoinPayload);
            var authResult = await actor.OnAuthenticate(joinPacket);

            if (!authResult || string.IsNullOrEmpty(actorSender.AccountId))
            {
                await actor.OnDestroy();
                baseStage.StageSender.Reply((ushort)BaseErrorCode.AuthenticationFailed);
                return;
            }

            await actor.OnPostAuthenticate();

            var joinResult = await baseStage.Stage.OnJoinStage(actor);
            if (!joinResult)
            {
                await actor.OnDestroy();
                baseStage.StageSender.Reply((ushort)BaseErrorCode.JoinStageFailed);
                return;
            }

            baseStage.AddActor(new BaseActor(actor, actorSender));
            await baseStage.Stage.OnPostJoinStage(actor);

            // 응답
            var res = new CreateJoinStageRes
            {
                IsCreated = isCreated,
                CreatePayloadId = "",
                CreatePayload = ByteString.Empty,
                JoinPayloadId = "",
                JoinPayload = ByteString.Empty
            };
            baseStage.StageSender.Reply(res);
        }
        catch (Exception ex)
        {
            _log.Error(() => $"CreateJoinStage failed: {ex.Message}");
            baseStage?.StageSender.Reply((ushort)BaseErrorCode.UncheckedContentsError);
        }
    }
}
```

---

## 4. 연결 끊김 처리 (DisconnectNoticeCmd)

클라이언트 연결 끊김을 Stage에 알리는 명령입니다.

```csharp
// 참조: D:\project\kairos\playhouse\playhouse-net\PlayHouse\PlayHouse\Core\Play\Base\Command\DisconnectNoticeCmd.cs
internal class DisconnectNoticeCmd : IBaseStageCmd
{
    private readonly LOG<DisconnectNoticeCmd> _log = new();

    public async Task Execute(BaseStage baseStage, RoutePacket routePacket)
    {
        var msg = DisconnectNoticeMsg.Parser.ParseFrom(routePacket.Span);
        var accountId = routePacket.AccountId;

        _log.Debug(() => $"Disconnect notice for account {accountId}");

        var baseActor = baseStage.GetActor(accountId);
        if (baseActor == null)
        {
            _log.Warn(() => $"Actor not found for disconnect: {accountId}");
            return;
        }

        try
        {
            // IStage.OnConnectionChanged(actor, false) 호출
            await baseStage.Stage.OnConnectionChanged(baseActor.Actor, false);
        }
        catch (Exception ex)
        {
            _log.Error(() => $"OnConnectionChanged failed: {ex.Message}");
        }

        // 컨텐츠에서 타임아웃 후 LeaveStage() 호출할지 결정
        // 프레임워크는 연결 끊김 알림만 전달
    }
}
```

---

## 5. 재연결 처리 흐름

재연결 시 세션 정보 업데이트 및 Stage 알림입니다.

```csharp
// ReconnectCmd.cs (프레임워크 구현)
internal class ReconnectCmd : IBaseStageCmd
{
    public async Task Execute(BaseStage baseStage, RoutePacket routePacket)
    {
        var msg = ReconnectMsg.Parser.ParseFrom(routePacket.Span);
        var accountId = routePacket.AccountId;

        var baseActor = baseStage.GetActor(accountId);
        if (baseActor == null)
        {
            _log.Warn(() => $"Actor not found for reconnect: {accountId}");
            baseStage.StageSender.Reply((ushort)BaseErrorCode.ActorNotFound);
            return;
        }

        // 세션 정보 업데이트 (새 연결 정보로 갱신)
        baseActor.ActorSender.Update(
            sessionNetworkId: msg.SessionNid,
            sessionId: msg.Sid,
            apiNetworkId: msg.ApiNid
        );

        try
        {
            // IStage.OnConnectionChanged(actor, true) 호출
            await baseStage.Stage.OnConnectionChanged(baseActor.Actor, true);
        }
        catch (Exception ex)
        {
            _log.Error(() => $"OnConnectionChanged(reconnect) failed: {ex.Message}");
        }

        baseStage.StageSender.Reply(0); // 성공
    }
}
```

---

## 6. XActorSender.Update() 구현

재연결 시 세션 정보를 업데이트하는 메서드입니다.

```csharp
// 참조: D:\project\kairos\playhouse\playhouse-net\PlayHouse\PlayHouse\Core\Play\XActorSender.cs
internal class XActorSender : IActorSender
{
    private string _sessionNid;
    private long _sid;
    private string _apiNid;

    // ... 다른 멤버들 ...

    /// <summary>
    /// 재연결 시 세션 정보 업데이트
    /// </summary>
    public void Update(string sessionNetworkId, long sessionId, string apiNetworkId)
    {
        _sessionNid = sessionNetworkId;
        _sid = sessionId;
        _apiNid = apiNetworkId;

        _log.Debug(() => $"Session updated - SessionNid: {_sessionNid}, Sid: {_sid}, ApiNid: {_apiNid}");
    }

    /// <summary>
    /// 클라이언트로 메시지 전송 (업데이트된 세션 정보 사용)
    /// </summary>
    public void SendToClient(IPacket packet)
    {
        _baseStage.StageSender.SendToClient(_sessionNid, _sid, packet);
    }
}
```

---

## 7. AccountId 검증 로직

인증 성공 시 AccountId 설정을 강제하는 프레임워크 검증입니다.

```csharp
// JoinStageCmd.cs 내 AccountId 검증 부분
private async Task ValidateAccountId(XActorSender actorSender, IActor actor)
{
    // AccountId가 설정되지 않은 경우 예외 발생
    if (string.IsNullOrEmpty(actorSender.AccountId))
    {
        _log.Error(() =>
            "OnAuthenticate returned true but AccountId was not set. " +
            "This is a content developer error. " +
            "ActorSender.AccountId must be set in OnAuthenticate when returning true.");

        await actor.OnDestroy();

        throw new InvalidOperationException(
            "AccountId must be set in OnAuthenticate when authentication succeeds. " +
            "Empty AccountId causes exception and connection termination.");
    }
}
```

**검증 규칙**:
- `OnAuthenticate()`가 `true`를 반환하면 `ActorSender.AccountId`가 설정되어 있어야 함
- 빈 문자열(`""`)이면 예외 발생 및 연결 종료
- 이 규칙은 컨텐츠 개발자에게 AccountId 설정을 강제함

---

## 8. 인증 프로토콜 메시지

```protobuf
// playhouse.proto (프레임워크 내부 프로토콜)

message JoinStageReq {
    string session_nid = 1;   // 세션 네트워크 ID
    int64 sid = 2;            // 세션 ID
    string payload_id = 3;    // 인증 패킷 MsgId
    bytes payload = 4;        // 인증 패킷 데이터
}

message JoinStageRes {
    string payload_id = 1;    // 응답 패킷 MsgId
    bytes payload = 2;        // 응답 패킷 데이터
}

message CreateJoinStageReq {
    string stage_type = 1;        // Stage 타입
    string create_payload_id = 2; // Stage 생성 패킷 MsgId
    bytes create_payload = 3;     // Stage 생성 패킷 데이터
    string session_nid = 4;       // 세션 네트워크 ID
    int64 sid = 5;                // 세션 ID
    string join_payload_id = 6;   // 입장 패킷 MsgId
    bytes join_payload = 7;       // 입장 패킷 데이터
}

message CreateJoinStageRes {
    bool is_created = 1;           // Stage 신규 생성 여부
    string create_payload_id = 2;  // Stage 생성 응답 MsgId
    bytes create_payload = 3;      // Stage 생성 응답 데이터
    string join_payload_id = 4;    // 입장 응답 MsgId
    bytes join_payload = 5;        // 입장 응답 데이터
}

message DisconnectNoticeMsg {
    int64 account_id = 1;          // 연결 끊긴 계정 ID
}

message ReconnectMsg {
    string session_nid = 1;        // 새 세션 네트워크 ID
    int64 sid = 2;                 // 새 세션 ID
    string api_nid = 3;            // 새 API 서버 NID
}
```

---

## 9. 에러 코드

```csharp
public enum BaseErrorCode : ushort
{
    Success = 0,

    // 요청 관련 (1-99)
    NotRegisteredMessage = 1,        // 등록되지 않은 메시지
    InvalidParameter = 2,            // 잘못된 파라미터

    // 시스템 관련 (100-199)
    SystemError = 100,               // 시스템 에러
    RequestTimeout = 101,            // Request 타임아웃
    UncheckedContentsError = 102,    // 컨텐츠 코드에서 예외 발생

    // Stage 관련 (200-299)
    StageNotFound = 200,             // Stage를 찾을 수 없음
    StageAlreadyExists = 201,        // Stage가 이미 존재함
    StageCreateFailed = 202,         // Stage 생성 실패 (OnCreate false)

    // 인증 관련 (300-399)
    AuthenticationFailed = 300,      // OnAuthenticate()가 false 반환
    JoinStageFailed = 301,           // OnJoinStage()가 false 반환
    AccountIdNotSet = 302,           // AccountId가 설정되지 않음
    ActorNotFound = 303,             // 재연결 시 Actor를 찾을 수 없음
}
```

---

## 10. 구현 체크리스트

### 10.1 인증 흐름

- [ ] **JoinStageCmd** - Actor 생성 및 인증
  - [ ] XActorSender 생성
  - [ ] IActor.OnCreate() 호출
  - [ ] IActor.OnAuthenticate() 호출 및 결과 처리
  - [ ] AccountId 유효성 검증
  - [ ] IActor.OnPostAuthenticate() 호출
  - [ ] IStage.OnJoinStage() 호출 및 결과 처리
  - [ ] BaseActor 등록
  - [ ] IStage.OnPostJoinStage() 호출

- [ ] **CreateJoinStageCmd** - Stage 생성 + 입장
  - [ ] Stage 존재 확인
  - [ ] Stage 생성 (없으면)
  - [ ] JoinStageCmd와 동일한 인증 흐름

### 10.2 연결 관리

- [ ] **DisconnectNoticeCmd** - 연결 끊김 처리
  - [ ] IStage.OnConnectionChanged(actor, false) 호출

- [ ] **ReconnectCmd** - 재연결 처리
  - [ ] XActorSender.Update() 호출
  - [ ] IStage.OnConnectionChanged(actor, true) 호출

---

## 11. 참조 파일

| 파일 | 경로 | 용도 |
|------|------|------|
| **JoinStageCmd.cs** | `Core/Play/Base/Command/JoinStageCmd.cs` | Actor 입장 처리 |
| **CreateJoinStageCmd.cs** | `Core/Play/Base/Command/CreateJoinStageCmd.cs` | Stage 생성+입장 |
| **DisconnectNoticeCmd.cs** | `Core/Play/Base/Command/DisconnectNoticeCmd.cs` | 연결 끊김 알림 |
| **XActorSender.cs** | `Core/Play/XActorSender.cs` | Actor Sender 구현 |
| **BaseStage.cs** | `Core/Play/Base/BaseStage.cs` | Stage 이벤트 루프 |

---

## 변경 이력

| 버전 | 날짜 | 변경 내역 |
|------|------|-----------|
| 1.0 | 2025-12-10 | 초안 작성 |
| 2.0 | 2025-12-11 | 프레임워크 구현 코드로 전환 (컨텐츠 샘플 코드 제거) |
