# 통합 테스트를 단일 검증 프로그램으로 전환 계획 (v4 - Proto Message Driven)

## 핵심 개념: 게임 클라이언트-서버와 동일한 패턴

### 일반적인 게임 클라이언트-서버 개발
```csharp
// Client
var request = new LoginRequest { Username = "user1" };
var response = await client.SendAsync(request);

// ✅ 응답 패킷으로만 검증
var reply = LoginReply.Parser.ParseFrom(response);
Assert.IsTrue(reply.Success);
Assert.Equals("user1", reply.Username);
```

### PlayHouse 검증 프로그램 (정확히 동일)
```csharp
// Client
var request = new AuthenticateRequest { UserId = "user1" };
var response = await connector.RequestAsync(CPacket.Of(request));

// ✅ 응답 패킷으로만 검증
var reply = AuthenticateReply.Parser.ParseFrom(response.Payload.DataSpan);
Assert.IsTrue(reply.Success);
Assert.NotEmpty(reply.AccountId);

// ❌ 서버 내부 접근 금지
// Assert.IsTrue(TestActorImpl.OnAuthenticateCalled); // 이런 거 하지 않음!
// Assert.Equals(1, _playServer.SessionManager.SessionCount); // 이런 거 하지 않음!
```

**차이점**: 대상이 게임 로직이 아닌 **PlayHouse Framework의 기능 (Connector, Stage, Actor, API 등)**

---

## 개요

현재 PlayHouse.Tests.Integration의 통합 테스트(2,100+ 줄, 18개 파일, 73개 테스트 케이스)를 **playhouse-sample-net 스타일의 단일 검증 프로그램**으로 전환합니다.

### 🔥 핵심 원칙 3가지

#### 1. Server Once Pattern (playhouse-sample-net 패턴)
```csharp
// Program.Main
var serverContext = await StartServersAsync(); // 서버 1회 시작
try
{
    await RunInteractiveMode(serverContext); // 메뉴 루프
}
finally
{
    await StopServersAsync(serverContext); // 서버 1회 종료
}
```

- ✅ 서버는 프로그램 시작 시 **한 번만 구동**
- ✅ 클라이언트도 프로그램 시작 시 **한 번만 생성**
- ✅ Verifier는 **이미 생성된 클라이언트로 테스트만 실행**
- ✅ 특수 케이스(Connect 실패)만 임시 클라이언트 생성

#### 2. Proto Message Driven
```csharp
// ✅ 모든 테스트는 proto message 사용
var request = new EchoRequest { Content = "Hello" };
await connector.RequestAsync(CPacket.Of(request));

// ❌ CPacket.Empty 사용 금지
// await connector.RequestAsync(CPacket.Empty("CreateStage"));
```

- ✅ **모든 메시지는 proto 정의** (CPacket.Empty 금지)
- ✅ **Proto 메시지 = 테스트 기능 목록**

#### 3. Client Response Only (E2E 원칙)
```csharp
// ✅ 클라이언트 응답 패킷으로만 검증
var response = await connector.RequestAsync(packet);
Assert.Equals("EchoReply", response.MsgId);

var reply = EchoReply.Parser.ParseFrom(response.Payload.DataSpan);
Assert.Contains("Hello", reply.Content);

// ❌ 서버 인스턴스 접근 금지
// Assert.IsTrue(TestStageImpl.OnCreateCalled);
// Assert.Contains(TestApiController.ReceivedMsgIds, "EchoRequest");
```

- ✅ **응답 패킷 내용으로만 검증**
- ✅ **OnReceive 콜백으로 Push 메시지 검증**
- ❌ **서버 내부 상태 접근 금지** (TestStageImpl, TestActorImpl은 응답만 생성)

#### 4. 테스트 격리 전략 (Server Once 환경)

서버가 한 번만 시작되므로 **테스트 간 상태 격리**가 중요합니다.

```csharp
// ✅ 각 Verifier는 고유한 UserId/AccountId/StageId 사용
public class ConnectionVerifier : VerifierBase
{
    protected override async Task SetupAsync()
    {
        // 고유한 UserId로 인증 (격리)
        var authReq = new AuthenticateRequest { UserId = "conn_test_user" };
        var authRes = await Connector.RequestAsync(CPacket.Of(authReq));
        // ...
    }
}

public class MessagingVerifier : VerifierBase
{
    protected override async Task SetupAsync()
    {
        // 다른 UserId 사용 (격리)
        var authReq = new AuthenticateRequest { UserId = "msg_test_user" };
        var authRes = await Connector.RequestAsync(CPacket.Of(authReq));
        // ...
    }
}

public class StageToStageVerifier : VerifierBase
{
    protected override async Task SetupAsync()
    {
        // 고유한 StageId 사용 (격리)
        var createStageReq = new TriggerCreateStageRequest
        {
            StageType = "TestStage",
            StageId = 10000 + GetHashCode() // Verifier 인스턴스마다 고유
        };
        // ...
    }
}
```

**격리 원칙:**
- ✅ 각 Verifier는 **고유한 UserId 접두사** 사용 (예: `"conn_test"`, `"msg_test"`, `"stage_test"`)
- ✅ StageId가 필요한 경우 **Verifier별 고유 범위** 사용 (예: Connection: 1-100, Messaging: 101-200)
- ✅ 서버 상태 초기화가 필요한 경우 **최소한의 정리 메시지** 전송
- ❌ 서버 재시작 금지 (성능 저하)

### 테스트 실패 처리

```csharp
// ❌ 실패 시 멈추지 않음
protected override async Task RunTestsAsync()
{
    await RunTest("Test1", Test_Method1); // 실패해도
    await RunTest("Test2", Test_Method2); // 계속 실행
    await RunTest("Test3", Test_Method3); // 계속 실행
}

// 모든 테스트 결과 출력
// 1..73
// ok 1 - Connection: Connect_Success
// not ok 2 - Connection: Connect_InvalidHost
//   # Expected false, got true
// ok 3 - Messaging: Request_Success
// ...
// # 70 tests passed, 3 failed

// Exit code: 실패 있으면 1, 모두 성공하면 0
```

---

## 테스트 기능 목록 및 Proto Message 매핑 (73개)

### 1. Connection (8개 테스트)
| # | 기능 | Proto Message |
|---|------|--------------|
| 1 | TCP 연결 성공 | AuthenticateRequest → AuthenticateReply |
| 2 | TCP 연결 실패 (잘못된 호스트) | (연결 실패) |
| 3 | ConnectAsync 성공 | AuthenticateRequest → AuthenticateReply |
| 4 | ConnectAsync 실패 | (연결 실패) |
| 5 | 클라이언트 주도 연결 해제 | AuthenticateRequest → Disconnect |
| 6 | 서버 종료로 인한 연결 해제 | (OnDisconnect 콜백) |
| 7 | Authenticate 성공 (Async) | AuthenticateRequest → AuthenticateReply |
| 8 | Authenticate 성공 (Callback) | AuthenticateRequest → AuthenticateReply |

**검증 방식**: `IsConnected()`, `OnConnect` 콜백, `AuthenticateReply.Success`

### 2. Messaging (10개 테스트)
| # | 기능 | Proto Message |
|---|------|--------------|
| 1 | Send 메시지 전송 | EchoRequest (fire-and-forget) |
| 2 | Request 콜백 패턴 | EchoRequest → EchoReply |
| 3 | Request 에러 응답 | FailRequest → ErrorPacket |
| 4 | RequestAsync 성공 | EchoRequest → EchoReply |
| 5 | RequestAsync 타임아웃 | EchoRequest (타임아웃) |
| 6 | RequestAsync 에러코드 | FailRequest → ErrorPacket |
| 7 | OnReceive Push 1개 | BroadcastTrigger → BroadcastNotify (Push) |
| 8 | OnReceive Push 여러 개 | BroadcastTrigger → BroadcastNotify × 3 (Push) |
| 9 | 순차 요청 5개 | EchoRequest × 5 → EchoReply × 5 |
| 10 | 병렬 요청 10개 | EchoRequest × 10 → EchoReply × 10 |

**검증 방식**: `EchoReply.Content`, `OnReceive` 콜백, Exception 발생

### 3. Push (2개 테스트)
| # | 기능 | Proto Message |
|---|------|--------------|
| 1 | Push 메시지 1개 수신 | BroadcastTrigger → BroadcastNotify (Push) |
| 2 | Push 메시지 3개 이상 순서 보장 | BroadcastTrigger → BroadcastNotify × 3+ (Push) |

**검증 방식**: `OnReceive` 콜백으로 Push 수신, `BroadcastNotify.EventType`

### 4. PacketAutoDispose (6개 테스트)
| # | 기능 | Proto Message |
|---|------|--------------|
| 1 | Request 콜백 패턴 자동 Dispose | EchoRequest → EchoReply |
| 2 | 여러 Request 콜백 자동 Dispose | EchoRequest × 5 → EchoReply × 5 |
| 3 | OnReceive 메시지 자동 Dispose | BroadcastTrigger → BroadcastNotify (Push) |
| 4 | RequestAsync 호출자 Dispose 책임 | EchoRequest → EchoReply (수동 Dispose) |
| 5 | 여러 RequestAsync 호출자 Dispose | EchoRequest × 5 → EchoReply × 5 |
| 6 | Async + Callback 혼합 패턴 | EchoRequest (혼합) |

**검증 방식**: Dispose 후 연결 유지 (`IsConnected()`)

### 5. ServerLifecycle (1개 테스트)
| # | 기능 | Proto Message |
|---|------|--------------|
| 1 | 서버 중지로 인한 OnDisconnect 콜백 | AuthenticateRequest → (서버 종료) |

**검증 방식**: `OnDisconnect` 콜백 호출

### 6. ActorCallback (3개 테스트)
| # | 기능 | Proto Message |
|---|------|--------------|
| 1 | IActor.OnAuthenticate 콜백 | AuthenticateRequest → AuthenticateReply |
| 2 | IActor.OnPostAuthenticate 콜백 | AuthenticateRequest → AuthenticateReply |
| 3 | IActor.OnCreate 콜백 | AuthenticateRequest (Stage Join) |

**검증 방식**: `AuthenticateReply.Success` (OnAuthenticate 성공 = Success true)

### 7. ActorSender (4개 테스트)
| # | 기능 | Proto Message |
|---|------|--------------|
| 1 | IActorSender.AccountId | GetAccountIdRequest → GetAccountIdReply |
| 2 | IActorSender.LeaveStage | LeaveStageRequest → LeaveStageReply |
| 3 | IActorSender.Reply | EchoRequest → EchoReply |
| 4 | IActorSender.Reply(errorCode) | FailRequest → ErrorPacket |

**검증 방식**: `GetAccountIdReply.AccountId`, `LeaveStageReply.Success`, ErrorPacket

### 8. StageCallback (5개 테스트)
| # | 기능 | Proto Message |
|---|------|--------------|
| 1 | IStage.OnCreate 콜백 | AuthenticateRequest (Stage 생성) |
| 2 | IStage.OnJoinStage 콜백 | AuthenticateRequest (Actor Join) |
| 3 | IStage.OnPostJoinStage 콜백 | AuthenticateRequest (Actor Join) |
| 4 | IStage.OnDispatch 콜백 | EchoRequest → EchoReply |
| 5 | IStage.OnDestroy 콜백 | CloseStageRequest → CloseStageReply |

**검증 방식**: `AuthenticateReply.Success`, `EchoReply.Content`, `CloseStageReply.Success`

### 9. StageToStage (5개 테스트)
| # | 기능 | Proto Message |
|---|------|--------------|
| 1 | SendToStage (다른 서버) | TriggerSendToStageRequest → TriggerSendToStageReply |
| 2 | RequestToStage Async (다른 서버) | TriggerRequestToStageRequest → TriggerRequestToStageReply |
| 3 | RequestToStage Callback (다른 서버) | TriggerRequestToStageCallbackRequest → TriggerRequestToStageCallbackReply (Push) |
| 4 | SendToStage (같은 서버) | TriggerSendToStageRequest → TriggerSendToStageReply |
| 5 | RequestToStage (같은 서버) | TriggerRequestToStageRequest → TriggerRequestToStageReply |

**검증 방식**: `TriggerSendToStageReply.Success`, `TriggerRequestToStageReply.Response`

### 10. StageToApi (5개 테스트)
| # | 기능 | Proto Message |
|---|------|--------------|
| 1 | Stage → API SendToApi | TriggerSendToApiRequest → TriggerSendToApiReply |
| 2 | AsyncBlock 내 SendToApi | TriggerAsyncBlockSendToApiRequest → TriggerAsyncBlockSendToApiAccepted |
| 3 | S2S 직접 라우팅 | (proto 정의 없음, 제외 또는 추가 필요) |
| 4 | AsyncBlock 내 RequestToApi | TriggerAsyncBlockRequestToApiRequest → TriggerAsyncBlockRequestToApiReply (Push) |
| 5 | Stage → API 기본 요청/응답 | TriggerRequestToApiRequest → TriggerRequestToApiReply |

**검증 방식**: `TriggerSendToApiReply.Success`, Push 메시지 수신

### 11. ApiToApi (5개 테스트)
| # | 기능 | Proto Message |
|---|------|--------------|
| 1 | API → API SendToApi | InterApiMessage |
| 2 | API → API RequestToApi | ApiEchoRequest → ApiEchoReply |
| 3 | API 양방향 통신 | InterApiMessage ↔ InterApiReply |
| 4 | RequestToApi 핸들러 방식 | TriggerRequestToApiServerRequest → TriggerRequestToApiServerReply |
| 5 | SendToApi 핸들러 방식 | TriggerSendToApiServerRequest → TriggerSendToApiServerReply |

**검증 방식**: `ApiEchoReply.Content`, `InterApiReply.Response`

### 12. ApiToPlay (3개 테스트)
| # | 기능 | Proto Message |
|---|------|--------------|
| 1 | API → Play CreateStage | TriggerCreateStageRequest → TriggerCreateStageReply |
| 2 | API → Play GetOrCreateStage (신규) | TriggerGetOrCreateStageRequest → TriggerGetOrCreateStageReply (is_created=true) |
| 3 | API → Play GetOrCreateStage (기존) | TriggerGetOrCreateStageRequest → TriggerGetOrCreateStageReply (is_created=false) |

**검증 방식**: `TriggerCreateStageReply.Success`, `TriggerGetOrCreateStageReply.IsCreated`

### 13. SelfConnection (2개 테스트)
| # | 기능 | Proto Message |
|---|------|--------------|
| 1 | 자기 자신에게 SendToApi | InterApiMessage |
| 2 | 자기 자신에게 RequestToApi | ApiEchoRequest → ApiEchoReply |

**검증 방식**: `ApiEchoReply.Content`

### 14. AsyncBlock (2개 테스트)
| # | 기능 | Proto Message |
|---|------|--------------|
| 1 | AsyncBlock Pre/Post 실행 | AsyncBlockRequest → AsyncBlockAccepted, Push AsyncBlockReply |
| 2 | AsyncBlock 여러 요청 (5개) | AsyncBlockRequest × 5 → Push AsyncBlockReply × 5 |

**검증 방식**: 즉시 `AsyncBlockAccepted`, Push로 `AsyncBlockReply` 수신

### 15. Timer (2개 테스트)
| # | 기능 | Proto Message |
|---|------|--------------|
| 1 | RepeatTimer 반복 실행 | StartRepeatTimerRequest → StartTimerReply, Push TimerTickNotify × 3+ |
| 2 | CountTimer 정확한 횟수 | StartCountTimerRequest → StartTimerReply, Push TimerTickNotify × 5 |

**검증 방식**: `StartTimerReply.TimerId`, Push로 `TimerTickNotify` 카운트

### 16. AutoDispose (3개 테스트)
| # | 기능 | Proto Message |
|---|------|--------------|
| 1 | OnDispatch 내 RequestToApi 자동 Dispose | TriggerAutoDisposeApiRequest → TriggerAutoDisposeApiReply |
| 2 | OnDispatch 내 RequestToStage 자동 Dispose | TriggerAutoDisposeStageRequest → TriggerAutoDisposeStageReply |
| 3 | Timer 콜백 내 RequestAsync 자동 Dispose | StartTimerWithRequestRequest → StartTimerWithRequestReply, Push TimerRequestResultNotify |

**검증 방식**: 응답 정상 수신 = 자동 Dispose 성공

### 17. DIIntegration (5개 테스트)
| # | 기능 | Proto Message |
|---|------|--------------|
| 1 | Stage DI 주입 | GetDIValueRequest → EchoReply |
| 2 | Actor DI 주입 | AuthenticateRequest → AuthenticateReply |
| 3 | IPlayServerControl DI 해결 | (API 서버에서 직접 요청) |
| 4 | IServerInfoCenter DI 해결 | (API 서버에서 직접 요청) |
| 5 | Stage/Actor 싱글톤 서비스 공유 | GetDIValueRequest → EchoReply |

**검증 방식**: `EchoReply.Content`에 DI 서비스 값 포함

### 18. ConnectorCallbackPerformance (2개 테스트)
| # | 기능 | Proto Message |
|---|------|--------------|
| 1 | Request 콜백 8KB 메시지 50개 | EchoRequest × 50 → EchoReply × 50 |
| 2 | Request 콜백 큐 처리 10개 | EchoRequest × 10 → EchoReply × 10 |

**검증 방식**: 모든 응답 수신 + 실행 시간 < 1000ms

---

## Proto Message 전체 목록 (48개)

### 인증 (2개)
- `AuthenticateRequest` / `AuthenticateReply`

### 기본 통신 (3개)
- `EchoRequest` / `EchoReply`
- `FailRequest`

### Push (2개)
- `BroadcastTrigger` / `BroadcastNotify`

### Actor (4개)
- `GetAccountIdRequest` / `GetAccountIdReply`
- `LeaveStageRequest` / `LeaveStageReply`

### Stage (2개)
- `CloseStageRequest` / `CloseStageReply`

### Stage 간 통신 (8개)
- `TriggerSendToStageRequest` / `TriggerSendToStageReply`
- `TriggerRequestToStageRequest` / `TriggerRequestToStageReply`
- `TriggerRequestToStageCallbackRequest` / `TriggerRequestToStageCallbackReply`
- `InterStageMessage` / `InterStageReply`

### AsyncBlock (3개)
- `AsyncBlockRequest` / `AsyncBlockReply` / `AsyncBlockAccepted`

### Timer (4개)
- `StartRepeatTimerRequest` / `StartCountTimerRequest`
- `StartTimerReply` / `TimerTickNotify`

### Stage → API (8개)
- `TriggerSendToApiRequest` / `TriggerSendToApiReply`
- `TriggerAsyncBlockSendToApiRequest` / `TriggerAsyncBlockSendToApiAccepted`
- `TriggerAsyncBlockRequestToApiRequest` / `TriggerAsyncBlockRequestToApiReply`
- `TriggerRequestToApiRequest` / `TriggerRequestToApiReply`

### API 간 통신 (8개)
- `InterApiMessage` / `InterApiReply`
- `ApiEchoRequest` / `ApiEchoReply`
- `TriggerRequestToApiServerRequest` / `TriggerRequestToApiServerReply`
- `TriggerSendToApiServerRequest` / `TriggerSendToApiServerReply`

### 자동 Dispose (9개)
- `TriggerAutoDisposeApiRequest` / `TriggerAutoDisposeApiReply`
- `TriggerAutoDisposeStageRequest` / `TriggerAutoDisposeStageReply`
- `StartTimerWithRequestRequest` / `StartTimerWithRequestReply`
- `TimerRequestResultNotify`
- `TimerApiRequest` / `TimerApiReply` (내부 사용)

### API → Play (4개)
- `TriggerCreateStageRequest` / `TriggerCreateStageReply`
- `TriggerGetOrCreateStageRequest` / `TriggerGetOrCreateStageReply`

### DI (1개)
- `GetDIValueRequest`

---

## 프로젝트 구조

```
tests/verification/
├── PlayHouse.Verification.Shared/       # 공유 인프라 라이브러리
│   ├── PlayHouse.Verification.Shared.csproj
│   ├── Infrastructure/
│   │   ├── TestStageImpl.cs            # 응답 패킷만 생성 (상태 기록 X)
│   │   ├── TestActorImpl.cs            # 응답 패킷만 생성 (상태 기록 X)
│   │   ├── DITestStage.cs              # DI 테스트용
│   │   ├── DITestActor.cs
│   │   ├── TestApiController.cs        # API 핸들러 (응답만 생성)
│   │   └── TestSystemController.cs
│   ├── Utils/
│   │   ├── ServerFactory.cs            # 서버 생성 유틸리티
│   │   └── AssertHelper.cs             # 어서션 헬퍼
│   └── Proto/
│       └── test_messages.proto         # 48개 proto 메시지
│
└── PlayHouse.Verification/             # 단일 통합 검증 프로그램
    ├── PlayHouse.Verification.csproj
    ├── Program.cs                      # Server Once Pattern 구현
    ├── ServerContext.cs                # 공유 서버/클라이언트 컨텍스트
    ├── VerificationRunner.cs           # 검증 실행 오케스트레이터
    ├── VerifierBase.cs                 # Verifier 기본 클래스
    └── Verifiers/                      # 18개 Verifier 클래스
        ├── ConnectionVerifier.cs       # 8 tests
        ├── MessagingVerifier.cs        # 10 tests
        ├── PushVerifier.cs             # 2 tests
        ├── PacketAutoDisposeVerifier.cs # 6 tests
        ├── ServerLifecycleVerifier.cs  # 1 test
        ├── ActorCallbackVerifier.cs    # 3 tests
        ├── ActorSenderVerifier.cs      # 4 tests
        ├── StageCallbackVerifier.cs    # 5 tests
        ├── StageToStageVerifier.cs     # 5 tests
        ├── StageToApiVerifier.cs       # 5 tests
        ├── ApiToApiVerifier.cs         # 5 tests
        ├── ApiToPlayVerifier.cs        # 3 tests
        ├── SelfConnectionVerifier.cs   # 2 tests
        ├── AsyncBlockVerifier.cs       # 2 tests
        ├── TimerVerifier.cs            # 2 tests
        ├── AutoDisposeVerifier.cs      # 3 tests
        ├── DIIntegrationVerifier.cs    # 5 tests
        └── ConnectorCallbackPerformanceVerifier.cs # 2 tests
```

---

## 코드 구조 설계

### Program.cs (Server Once Pattern)

```csharp
namespace PlayHouse.Verification;

class Program
{
    static async Task<int> Main(string[] args)
    {
        var config = ParseArguments(args);

        // 🔥 서버/클라이언트 한 번만 시작
        var serverContext = await StartServersAsync();

        try
        {
            if (config.CiMode)
                return await RunCiMode(config, serverContext);
            else if (config.Category != null)
                return await RunSelectedCategories(config, serverContext);
            else
                return await RunInteractiveMode(serverContext);
        }
        finally
        {
            // 🔥 프로그램 종료 시 한 번만 정리
            await StopServersAsync(serverContext);
        }
    }

    static async Task<ServerContext> StartServersAsync()
    {
        Console.WriteLine("[서버 시작 중...]");
        var factory = new ServerFactory();

        // 1. PlayServer (TCP 동적, ZMQ 고정)
        var playServer = factory.CreatePlayServer(tcpPort: 0, zmqPort: 15000);
        await playServer.StartAsync();
        var actualTcpPort = ServerFactory.GetActualTcpPort(playServer);
        Console.WriteLine($"✓ PlayServer started on ZMQ:15000, TCP:{actualTcpPort}");

        // 2. ApiServer 2개 (서버간 통신 테스트용)
        var apiServer1 = factory.CreateApiServer(zmqPort: 15300, serverId: "1");
        var apiServer2 = factory.CreateApiServer(zmqPort: 15301, serverId: "2");
        await apiServer1.StartAsync();
        await apiServer2.StartAsync();
        Console.WriteLine($"✓ ApiServer-1 started on ZMQ:15300");
        Console.WriteLine($"✓ ApiServer-2 started on ZMQ:15301");

        // 🔥 ApiServer 양방향 연결 대기 (헬스체크)
        await WaitForApiServerConnectionAsync(apiServer1, apiServer2);

        // 3. 클라이언트 생성 (한 번만!)
        var connector = new ClientConnector();
        connector.Init(new ConnectorConfig
        {
            ServerAddress = "127.0.0.1",
            ServerPort = actualTcpPort,
            RequestTimeoutMs = 30000
        });
        Console.WriteLine($"✓ Client connector initialized\n");

        return new ServerContext
        {
            PlayServer = playServer,
            ApiServer1 = apiServer1,
            ApiServer2 = apiServer2,
            Connector = connector,
            TcpPort = actualTcpPort
        };
    }

    static async Task WaitForApiServerConnectionAsync(ApiServer s1, ApiServer s2)
    {
        // ApiServer 양방향 연결 헬스체크 (최대 30초)
        const int maxAttempts = 30;
        bool s1ToS2 = false, s2ToS1 = false;

        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            await Task.Delay(1000);

            if (!s1ToS2)
            {
                try
                {
                    var req = new ApiEchoRequest { Content = "HealthCheck" };
                    var res = await s1.ApiSender!.RequestToApi("2", CPacket.Of(req));
                    if (!res.MsgId.StartsWith("Error:"))
                    {
                        s1ToS2 = true;
                        Console.WriteLine("[Program] ApiServer1 → ApiServer2 연결 완료");
                    }
                }
                catch { }
            }

            if (!s2ToS1)
            {
                try
                {
                    var req = new ApiEchoRequest { Content = "HealthCheck" };
                    var res = await s2.ApiSender!.RequestToApi("1", CPacket.Of(req));
                    if (!res.MsgId.StartsWith("Error:"))
                    {
                        s2ToS1 = true;
                        Console.WriteLine("[Program] ApiServer2 → ApiServer1 연결 완료");
                    }
                }
                catch { }
            }

            if (s1ToS2 && s2ToS1)
            {
                Console.WriteLine("[Program] ApiServer 양방향 연결 완료\n");
                return;
            }
        }

        throw new TimeoutException("ApiServer 양방향 연결 실패");
    }

    static async Task StopServersAsync(ServerContext ctx)
    {
        Console.WriteLine("\n[서버 종료 중...]");
        ctx.Connector?.Dispose();
        if (ctx.PlayServer != null) await ctx.PlayServer.DisposeAsync();
        if (ctx.ApiServer1 != null) await ctx.ApiServer1.DisposeAsync();
        if (ctx.ApiServer2 != null) await ctx.ApiServer2.DisposeAsync();
        Console.WriteLine("✓ All servers stopped");
    }

    static async Task<int> RunInteractiveMode(ServerContext ctx)
    {
        var runner = new VerificationRunner(ctx);

        while (true)
        {
            Console.Clear();
            PrintMenu(runner);

            var input = Console.ReadLine();
            if (!int.TryParse(input, out var choice))
                continue;

            if (choice == 0) break;

            if (choice == 1)
            {
                var result = await runner.RunAllAsync();
                PrintResults(result);
            }
            else if (choice >= 2)
            {
                var category = runner.GetCategory(choice - 2);
                var result = await runner.RunCategoryAsync(category);
                PrintResults(result);
            }

            Console.WriteLine("\nPress any key to continue...");
            Console.ReadKey();
        }

        return 0;
    }

    static async Task<int> RunCiMode(Config config, ServerContext ctx)
    {
        var runner = new VerificationRunner(ctx);
        var result = await runner.RunAllAsync(verbose: config.Verbose);

        // TAP 출력
        Console.WriteLine($"1..{result.TotalTests}");
        for (int i = 0; i < result.Tests.Count; i++)
        {
            var test = result.Tests[i];
            if (test.Passed)
            {
                Console.WriteLine($"ok {i + 1} - {test.CategoryName}: {test.TestName}");
            }
            else
            {
                Console.WriteLine($"not ok {i + 1} - {test.CategoryName}: {test.TestName}");
                Console.WriteLine($"  # {test.Error}");
            }
        }

        Console.WriteLine($"# {result.PassedCount} tests passed, {result.FailedCount} failed");

        // TAP 파일 저장
        var tapFile = Path.Combine(Directory.GetCurrentDirectory(), "verification-results.tap");
        await File.WriteAllTextAsync(tapFile, /* TAP 내용 */);

        // Exit code: 실패 있으면 1
        return result.FailedCount > 0 ? 1 : 0;
    }

    static void PrintMenu(VerificationRunner runner)
    {
        Console.WriteLine("========================================");
        Console.WriteLine("PlayHouse Verification Program");
        Console.WriteLine("========================================");
        Console.WriteLine("1. Run All Tests (73 tests)");

        int index = 2;
        foreach (var category in runner.GetCategories())
        {
            Console.WriteLine($"{index}. {category.Name} ({category.TestCount})");
            index++;
        }

        Console.WriteLine("0. Exit");
        Console.WriteLine("========================================");
        Console.Write("Select option: ");
    }
}
```

### ServerContext.cs

```csharp
namespace PlayHouse.Verification;

/// <summary>
/// 프로그램 전체에서 공유하는 서버/클라이언트 컨텍스트
/// </summary>
public class ServerContext
{
    public PlayServer PlayServer { get; set; } = null!;
    public ApiServer ApiServer1 { get; set; } = null!;
    public ApiServer ApiServer2 { get; set; } = null!;
    public ClientConnector Connector { get; set; } = null!;
    public int TcpPort { get; set; }
}
```

### VerifierBase.cs

```csharp
namespace PlayHouse.Verification;

/// <summary>
/// 모든 Verifier의 기본 클래스
/// </summary>
public abstract class VerifierBase
{
    private readonly List<TestResult> _results = new();

    // 🔥 ServerContext로 이미 구동 중인 서버/클라이언트 접근
    protected ServerContext ServerContext { get; }
    protected ClientConnector Connector => ServerContext.Connector;
    protected PlayServer PlayServer => ServerContext.PlayServer;
    protected ApiServer ApiServer1 => ServerContext.ApiServer1;
    protected ApiServer ApiServer2 => ServerContext.ApiServer2;

    protected AssertHelper Assert { get; } = new();

    public abstract string CategoryName { get; }

    protected VerifierBase(ServerContext serverContext)
    {
        ServerContext = serverContext;
    }

    public async Task<CategoryResult> RunAllTestsAsync()
    {
        _results.Clear();

        await SetupAsync();

        try
        {
            await RunTestsAsync();
        }
        finally
        {
            await TeardownAsync();
        }

        return new CategoryResult
        {
            CategoryName = CategoryName,
            Tests = _results.ToList()
        };
    }

    /// <summary>
    /// 각 Verifier가 오버라이드하여 테스트 실행
    /// </summary>
    protected abstract Task RunTestsAsync();

    /// <summary>
    /// 각 Verifier가 필요시 오버라이드
    /// 🔥 서버 시작 금지! 클라이언트 상태 초기화만
    /// </summary>
    protected virtual Task SetupAsync() => Task.CompletedTask;

    /// <summary>
    /// 각 Verifier가 필요시 오버라이드
    /// 🔥 서버 종료 금지! 클라이언트 상태 정리만
    /// </summary>
    protected virtual Task TeardownAsync() => Task.CompletedTask;

    /// <summary>
    /// 테스트 실행 (예외 처리 포함)
    /// 🔥 실패해도 멈추지 않고 계속 실행
    /// </summary>
    protected async Task RunTest(string testName, Func<Task> testFunc, int timeoutMs = 60000)
    {
        var sw = Stopwatch.StartNew();

        try
        {
            using var cts = new CancellationTokenSource(timeoutMs);
            var testTask = testFunc();
            var timeoutTask = Task.Delay(timeoutMs, cts.Token);
            var completedTask = await Task.WhenAny(testTask, timeoutTask);

            if (completedTask == timeoutTask)
            {
                throw new TimeoutException($"Test exceeded timeout of {timeoutMs}ms");
            }

            await testTask; // 실제 예외 전파

            _results.Add(new TestResult
            {
                CategoryName = CategoryName,
                TestName = testName,
                Passed = true,
                Duration = sw.Elapsed
            });
        }
        catch (Exception ex)
        {
            _results.Add(new TestResult
            {
                CategoryName = CategoryName,
                TestName = testName,
                Passed = false,
                Duration = sw.Elapsed,
                Error = ex.Message,
                StackTrace = ex.StackTrace
            });
            // ❌ throw 안 함! 다음 테스트 계속 실행
        }
    }

    public abstract int GetTestCount();
}

public record CategoryResult
{
    public required string CategoryName { get; init; }
    public required List<TestResult> Tests { get; init; }
}

public record TestResult
{
    public required string CategoryName { get; init; }
    public required string TestName { get; init; }
    public required bool Passed { get; init; }
    public required TimeSpan Duration { get; init; }
    public string? Error { get; init; }
    public string? StackTrace { get; init; }
}
```

### MessagingVerifier.cs 예제 (Client Response Only)

```csharp
namespace PlayHouse.Verification.Verifiers;

/// <summary>
/// Messaging 기능 검증 (10개 테스트)
/// </summary>
public class MessagingVerifier : VerifierBase
{
    public override string CategoryName => "Messaging";

    private readonly List<(ushort stageId, CPacket packet)> _receivedPushes = new();

    public MessagingVerifier(ServerContext serverContext) : base(serverContext)
    {
    }

    protected override async Task SetupAsync()
    {
        // 클라이언트 상태 초기화만
        _receivedPushes.Clear();

        // OnReceive 콜백 등록
        Connector.OnReceive += (stageId, packet) =>
        {
            _receivedPushes.Add((stageId, packet));
        };

        // 연결 (필요시)
        if (!Connector.IsConnected())
        {
            Connector.Connect();
            await Task.Delay(100);
        }

        // 인증 (필요시)
        if (!Connector.IsAuthenticated())
        {
            var authReq = new AuthenticateRequest { UserId = "test_user" };
            var authRes = await Connector.RequestAsync(CPacket.Of(authReq));
            var authReply = AuthenticateReply.Parser.ParseFrom(authRes.Payload.DataSpan);
            Assert.IsTrue(authReply.Success, "Authentication should succeed");
        }
    }

    protected override async Task TeardownAsync()
    {
        // 클라이언트 상태 정리만
        _receivedPushes.Clear();
    }

    protected override async Task RunTestsAsync()
    {
        await RunTest("Send_ConnectionMaintained", Test_Send_ConnectionMaintained);
        await RunTest("Request_Success_CallbackInvoked", Test_Request_Success);
        await RunTest("Request_ErrorResponse", Test_Request_ErrorResponse);
        await RunTest("RequestAsync_Success", Test_RequestAsync_Success);
        await RunTest("RequestAsync_Timeout", Test_RequestAsync_Timeout);
        await RunTest("RequestAsync_ErrorResponse", Test_RequestAsync_ErrorResponse);
        await RunTest("OnReceive_PushMessage", Test_OnReceive_PushMessage);
        await RunTest("OnReceive_MultiplePushes", Test_OnReceive_MultiplePushes);
        await RunTest("MultipleRequests_Sequential", Test_MultipleRequests_Sequential);
        await RunTest("MultipleRequests_Parallel", Test_MultipleRequests_Parallel);
    }

    public override int GetTestCount() => 10;

    #region Test Methods

    private async Task Test_RequestAsync_Success()
    {
        // Given
        var request = new EchoRequest { Content = "Hello", Sequence = 1 };

        // When
        var response = await Connector.RequestAsync(CPacket.Of(request));

        // Then - ✅ 응답 패킷으로만 검증
        Assert.Equals("EchoReply", response.MsgId, "MsgId should be EchoReply");

        var reply = EchoReply.Parser.ParseFrom(response.Payload.DataSpan);
        Assert.Contains("Hello", reply.Content, "Content should contain 'Hello'");
        Assert.Equals(1, reply.Sequence, "Sequence should be 1");

        // ❌ 서버 접근 금지
        // Assert.IsTrue(TestStageImpl.OnDispatchCalled);
    }

    private async Task Test_OnReceive_PushMessage()
    {
        // Given
        _receivedPushes.Clear();
        var trigger = new BroadcastTrigger();

        // When
        await Connector.RequestAsync(CPacket.Of(trigger));
        await Task.Delay(500); // Push 수신 대기

        // Then - ✅ OnReceive 콜백으로 검증
        Assert.GreaterThan(_receivedPushes.Count, 0, "Should receive push message");

        var pushPacket = _receivedPushes[0].packet;
        Assert.Equals("BroadcastNotify", pushPacket.MsgId, "Push MsgId should be BroadcastNotify");

        var notify = BroadcastNotify.Parser.ParseFrom(pushPacket.Payload.DataSpan);
        Assert.NotEmpty(notify.EventType, "EventType should not be empty");
    }

    private async Task Test_RequestAsync_Timeout()
    {
        // Given - 타임아웃 짧게 설정
        var originalTimeout = Connector.Config.RequestTimeoutMs;
        Connector.Config.RequestTimeoutMs = 1000;

        try
        {
            var request = new EchoRequest { Content = "Timeout test" };

            // When - 서버가 응답 지연하도록 설정된 요청
            // (TestStageImpl에서 DelayMs 필드 확인하여 지연)

            // Then - ✅ Exception으로 검증
            bool timeoutOccurred = false;
            try
            {
                await Connector.RequestAsync(CPacket.Of(request));
            }
            catch (ConnectorException ex)
            {
                timeoutOccurred = ex.ErrorCode == ErrorCode.RequestTimeout;
            }

            Assert.IsTrue(timeoutOccurred, "Should throw timeout exception");
        }
        finally
        {
            Connector.Config.RequestTimeoutMs = originalTimeout;
        }
    }

    private async Task Test_MultipleRequests_Parallel()
    {
        // Given
        const int requestCount = 10;
        var tasks = new List<Task<CPacket>>();

        // When
        for (int i = 0; i < requestCount; i++)
        {
            var request = new EchoRequest { Content = $"Request {i}", Sequence = i };
            tasks.Add(Connector.RequestAsync(CPacket.Of(request)));
        }

        var responses = await Task.WhenAll(tasks);

        // Then - ✅ 모든 응답 수신 검증
        Assert.Equals(requestCount, responses.Length, "Should receive all responses");

        for (int i = 0; i < requestCount; i++)
        {
            var reply = EchoReply.Parser.ParseFrom(responses[i].Payload.DataSpan);
            Assert.Contains($"Request {i}", reply.Content, $"Response {i} should contain correct content");
        }
    }

    #endregion
}
```

---

## 실행 방식

### 1. 인터랙티브 모드

```bash
$ dotnet run --project tests/verification/PlayHouse.Verification

[서버 시작 중...]
✓ PlayServer started on ZMQ:15000, TCP:52341
✓ ApiServer-1 started on ZMQ:15300
✓ ApiServer-2 started on ZMQ:15301
[Program] ApiServer1 → ApiServer2 연결 완료
[Program] ApiServer2 → ApiServer1 연결 완료
[Program] ApiServer 양방향 연결 완료
✓ Client connector initialized

========================================
PlayHouse Verification Program
========================================
1. Run All Tests (73 tests)
2. Connection (8)
3. Messaging (10)
4. Push (2)
...
19. ConnectorCallbackPerformance (2)
0. Exit
========================================
Select option: _
```

### 2. CI 모드

```bash
$ dotnet run --project tests/verification/PlayHouse.Verification -- --ci

[서버 시작 중...]
...
1..73
ok 1 - Connection: Connect_Success
ok 2 - Connection: ConnectAsync_Success
not ok 3 - Connection: Connect_InvalidHost
  # Expected false, got true
...
ok 73 - ConnectorCallbackPerformance: RequestCallback_MainThreadQueue

# 72 tests passed, 1 failed

$ echo $?
1
```

### 3. 선택적 실행

```bash
$ dotnet run -- --category Connection

[서버 시작 중...]
...
1..8
ok 1 - Connection: Connect_Success
ok 2 - Connection: Connect_InvalidHost
...
ok 8 - Connection: Authenticate_WithCallback

# 8 tests passed, 0 failed
```

---

## 단계적 구현 계획

### Phase 1: 인프라 구축 (Day 1)

**1.1 Shared 프로젝트**
- [ ] `tests/verification/PlayHouse.Verification.Shared/` 생성
- [ ] `PlayHouse.Verification.Shared.csproj` 생성
- [ ] `Proto/test_messages.proto` 복사 (기존 파일)
- [ ] `Infrastructure/` 디렉토리 생성
  - [ ] `TestStageImpl.cs` - 응답 패킷만 생성 (상태 기록 제거)
  - [ ] `TestActorImpl.cs` - 응답 패킷만 생성 (상태 기록 제거)
  - [ ] `TestApiController.cs` - API 핸들러 (응답만 생성)
  - [ ] `TestSystemController.cs` - 기존 파일 복사
  - [ ] `DITestStage.cs` / `DITestActor.cs` - DI 테스트용
- [ ] `Utils/` 디렉토리 생성
  - [ ] `ServerFactory.cs` - 서버 생성 유틸리티
  - [ ] `AssertHelper.cs` - 어서션 헬퍼
- [ ] 빌드 성공 확인

**1.2 Verification 프로젝트**
- [ ] `tests/verification/PlayHouse.Verification/` 생성
- [ ] `PlayHouse.Verification.csproj` 생성
- [ ] `ServerContext.cs` 구현
- [ ] `VerifierBase.cs` 구현
- [ ] `VerificationRunner.cs` 구현
- [ ] `Program.cs` 구현 (Server Once Pattern)
- [ ] 빌드 성공 확인

### Phase 2: 첫 번째 Verifier 구현 (Day 2)

**2.1 ConnectionVerifier (8 tests)**
- [ ] `Verifiers/ConnectionVerifier.cs` 생성
- [ ] 생성자에서 ServerContext 받기
- [ ] SetupAsync/TeardownAsync 구현
- [ ] 8개 테스트 메서드 작성
  - ✅ 응답 패킷으로만 검증
  - ❌ 서버 접근 금지
- [ ] 인터랙티브 모드 실행 테스트
- [ ] 8개 테스트 모두 통과 확인

### Phase 3: 나머지 Verifier 구현 (Day 3-10)

**Day 3-4: 기본 Connector 기능**
- [ ] `MessagingVerifier.cs` (10 tests)
- [ ] `PushVerifier.cs` (2 tests)
- [ ] `PacketAutoDisposeVerifier.cs` (6 tests)
- [ ] `ServerLifecycleVerifier.cs` (1 test)

**Day 5-6: Actor/Stage 콜백**
- [ ] `ActorCallbackVerifier.cs` (3 tests)
- [ ] `ActorSenderVerifier.cs` (4 tests)
- [ ] `StageCallbackVerifier.cs` (5 tests)

**Day 7-8: 서버간 통신**
- [ ] `StageToStageVerifier.cs` (5 tests)
- [ ] `StageToApiVerifier.cs` (5 tests)
- [ ] `ApiToApiVerifier.cs` (5 tests)
- [ ] `ApiToPlayVerifier.cs` (3 tests)
- [ ] `SelfConnectionVerifier.cs` (2 tests)

**Day 9: 고급 기능**
- [ ] `AsyncBlockVerifier.cs` (2 tests)
- [ ] `TimerVerifier.cs` (2 tests)
- [ ] `AutoDisposeVerifier.cs` (3 tests)

**Day 10: DI 및 성능**
- [ ] `DIIntegrationVerifier.cs` (5 tests)
- [ ] `ConnectorCallbackPerformanceVerifier.cs` (2 tests)

### Phase 4: 검증 및 정리 (Day 11)

- [ ] CI 모드에서 전체 73개 테스트 통과 확인
- [ ] `--category` 옵션 동작 확인
- [ ] TAP 출력 형식 검증
- [ ] Exit code 검증 (실패 시 1)
- [ ] GitHub Actions 워크플로우 추가
- [ ] README.md 작성
- [ ] 기존 `tests/PlayHouse.Tests.Integration/` 삭제
- [ ] playhouse-net.sln에서 Integration 프로젝트 제거

---

## 검증 방법

### 로컬 검증

```bash
# 빌드
dotnet build -c Release

# 인터랙티브 모드
dotnet run --project tests/verification/PlayHouse.Verification

# CI 모드
dotnet run --project tests/verification/PlayHouse.Verification -- --ci

# 특정 카테고리
dotnet run --project tests/verification/PlayHouse.Verification -- --category Connection
```

### CI 검증

```yaml
# .github/workflows/verification.yml
- name: Run Verification
  run: |
    dotnet run --project tests/verification/PlayHouse.Verification \
      --configuration Release \
      --no-build \
      -- --ci
  timeout-minutes: 10

- name: Upload TAP results
  if: always()
  uses: actions/upload-artifact@v3
  with:
    name: verification-results
    path: verification-results.tap
```

---

## 핵심 파일 경로

### 새로 생성
```
tests/verification/PlayHouse.Verification.Shared/
tests/verification/PlayHouse.Verification/
```

### 수정
```
playhouse-net.sln
.github/workflows/verification.yml
```

### 삭제 예정
```
tests/PlayHouse.Tests.Integration/ (전체)
```

---

## 참고 자료

- playhouse-sample-net: https://github.com/kairos-code-dev/playhouse-sample-net
- 기존 Integration Tests: `tests/PlayHouse.Tests.Integration/`
- CLAUDE.md E2E 테스트 원칙
