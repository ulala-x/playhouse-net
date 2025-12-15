# 에이전틱 코딩을 위한 아키텍처 및 테스트 설계 전략

> **에이전틱 코딩(Agentic Coding)**: AI 에이전트가 코드를 읽고, 이해하고, 수정할 수 있도록 구조와 테스트를 설계하는 개발 방식

## 1. 설계 목표

- **Test as Specification**: 유닛 테스트는 스펙 문서처럼, 통합 테스트는 API 사용 가이드처럼 읽혀야 한다.
- **Modification Efficiency**: 새로운 기능을 추가할 때 기존 코드 수정이 최소화되어야 한다.
- **Testability**: 외부 의존성을 Fake로 교체해서 단위 테스트할 수 있어야 한다.
- **Context Efficiency**: AI 에이전트나 새로운 개발자가 한 번의 컨텍스트 로딩으로 구조를 파악할 수 있어야 한다.

## 2. 아키텍처 원칙

- **단방향 의존성**: 의존성은 항상 외부 → 내부, 구체 → 추상 방향으로 흐른다.
- **의존성 주입**: 외부 의존성(API, DB, 파일시스템)은 반드시 인터페이스를 통해 주입받는다.
- **도메인 순수성**: 핵심 비즈니스 로직은 웹 프레임워크나 ORM 등 인프라에 직접 의존하지 않는다. 단, 다음 경우는 예외다:
    - I/O가 핵심 관심사인 경우 (API 클라이언트, 파일 처리 라이브러리)
    - 직렬화가 도메인의 일부인 경우 (CSV 파서, Protocol Buffer 래퍼)
    - 단일 레이어로 충분한 경우 (간단한 스크립트, CLI 도구)
    - 특정 프레임워크 통합이 목적인 경우 (Spring Boot Starter, Spring Security 확장)
- **명시적 경계**: 모듈 간 의존 관계는 코드의 import나 생성자 시그니처만 보고도 파악 가능해야 한다.
- **경계 매핑**: 외부 데이터와 도메인 객체 간의 변환은 오직 Adapter 계층에서만 수행한다.
- **언어 관습 존중**: 인터페이스 및 추상화 방식은 해당 프로그래밍 언어의 표준 관습(Idiom)을 따른다.
- **문맥의 지역성**: 관련된 로직과 데이터는 물리적으로 가깝게 배치하여 에이전트의 탐색 비용을 줄인다.

## 3. 디렉토리 구조 원칙

- **레이어 가시성**: 최상위 디렉토리는 아키텍처 레이어를 반영하여 의존성 방향이 디렉토리 구조만 보고도 파악되어야 한다.
- **도메인 독립성**: 도메인 모델은 인프라 구현 방식에 독립적으로 설계한다. 여러 외부 소스나 저장소를 사용하더라도 domain 레이어는 구현체별로 분리하지 않는다.
- **인프라 분리**: 외부 시스템과의 통신은 infrastructure 하위에 시스템별로 분리하여 구현한다.

### 의존성 규칙

```
presentation(또는 api) → domain ← infrastructure
                         (domain은 아무것도 의존하지 않음)
```

> 레이어 이름은 프로젝트 특성에 맞게 조정한다. (예: api, application, presentation 등)

## 4. 적용 및 배제 기준

### 적용 기준

- **인터페이스**: 실제로 교체 가능성이 있거나(Fake 사용 등), 명확한 테스트 격리가 필요한 경우에만 생성한다.
- **파일 분리**: 구현체가 하나뿐인 경우 과도한 파일 분리를 지양하고 문맥 유지를 위해 동일 파일 내 배치를 우선 고려한다.

### 하지 말 것 (Anti-Patterns)

- **YAGNI 위반**: 현재 사용하지 않는 추상화나 기능을 미래를 위해 미리 작성하지 않는다.
- **맹목적 패턴 적용**: 아키텍처 패턴(Controller, Repository 등)을 맥락 없이 이름만 빌려 쓰지 않는다.
- **과잉 분리**: 단순한 로직을 억지로 여러 레이어로 쪼개어 토큰을 낭비하지 않는다.
- **DTO 지옥**: 단순히 레이어를 통과하기만 하는 중복된 데이터 클래스를 만들지 않는다.

## 5. 테스트 설계 전략

### 테스트 원칙

- **Given-When-Then 구조**: 모든 테스트는 전제-행동-결과의 흐름이 명확히 구분되어야 한다.
- **독립성(Isolation)**: 각 테스트는 순서에 상관없이 개별적으로 실행 가능해야 한다.
- **Fake 우선**: 외부 의존성은 Mocking 프레임워크보다 Fake 객체 구현을 우선 사용하여 로직의 투명성을 높인다. 단, HTTP Client, 비동기 함수, 타이머 등 Fake 구현 비용이 과도한 경우에는 Mock 사용을 허용한다.
- **명시적 셋업**: 테스트 설정 과정(Setup)이 숨겨져 있지 않고, 테스트 케이스 내부에서 흐름을 파악할 수 있어야 한다.

### 테스트 수준별 가이드

#### 유닛 테스트 (Unit Test)

- **대상**: 도메인 엔티티, 값 객체, 순수 비즈니스 로직
- **기준**: 외부 의존성 없이 메모리 내에서 즉시 실행되어야 하며, 비즈니스 규칙을 검증한다.
- **가독성**: 테스트 코드가 **스펙 문서(Specification Document)처럼 읽혀야** 한다. 각 테스트는 시스템이 어떻게 동작해야 하는지를 명확히 서술한다.

#### 통합/e2e 테스트 (Integration Test)

- **대상**: 유스케이스 흐름, 어댑터, 외부 시스템 연동
- **기준**: 주요 성공/실패 시나리오를 검증하며, 외부 시스템은 Fake로 대체하되 필요시 계약 테스트(Contract Test)를 병행한다.
- **가독성**: 테스트 코드가 **API 문서(API Documentation)처럼 읽혀야** 한다. 각 테스트는 엔드포인트의 입력, 출력, 부작용을 명확히 보여준다.

### 테스트 구조화 원칙

- **기능 단위 그룹핑**: 테스트 파일 또는 테스트 클래스는 하나의 기능(Feature)을 대표한다.
- **카테고리별 조직화**: 관련된 테스트는 카테고리(Nested Class, Describe Block 등)로 묶어 **한눈에 파악 가능하도록** 계층 구조를 만든다.
- **시나리오 계층화**: 정상 흐름(Happy Path)을 먼저 배치하고, 예외/엣지 케이스는 별도 그룹으로 분리한다.
- **스펙 항목 대응**: 요구사항 문서의 각 항목이 테스트 그룹과 1:1로 매핑되어야 한다.
- **목차로서의 테스트**: 테스트 목록만 출력했을 때 기능 명세서처럼 읽혀야 한다.

#### 통합 테스트 카테고리 표준

```
1. 기본 동작 (Basic Operations)      - API의 핵심 기능 검증
2. 응답 데이터 검증 (Response Validation) - 반환값의 형식과 제약조건
3. 입력 파라미터 검증 (Input Validation)  - 파라미터 조합과 경계값
4. 엣지 케이스 (Edge Cases)          - 예외 상황과 오류 처리
5. 실무 활용 예제 (Usage Examples)    - 실제 사용 패턴 시연
```

### 테스트 작성 스타일

- **네이밍 컨벤션**: 함수명은 `[테스트대상]_[상황/조건]_[기대결과]` 형식을 따라 영문으로 작성한다.
- **표시 이름**: 테스트 실행 시 표시되는 이름은 한글로 작성하여 가독성을 높인다 (예: JUnit의 `@DisplayName`, Kotest의 문자열 리터럴 등).
- **실패 메시지**: Assertion 실패 시, 단순히 false를 반환하지 않고 기대값, 실제값, 당시의 변수 상태를 포함한 상세 메시지를 출력해야 한다.
- **테스트 데이터**: `1`, `abc` 같은 무의미한 값 대신 `vip_user`, `out_of_stock_item` 등 의도가 드러나는 데이터를 사용한다.
- **구현 검증 금지**: 메서드가 호출되었는지(Verify)를 검증하지 말고, 시스템의 상태나 반환값(State)이 올바른지 검증한다.
- **조건문 금지**: 테스트 내에 if문을 사용하지 않는다. 분기가 필요하면 별도 테스트로 분리한다.

## 6. 에러 처리 전략

- **단일 예외 + 에러 코드**: 여러 Exception 클래스를 만들지 않고, 단일 도메인 예외에 에러 코드를 부여하여 처리한다.
- **에러 코드 중앙화**: 모든 에러 코드는 한 곳에 정의하여 에이전트가 빠르게 파악할 수 있게 한다.
- **컨텍스트 포함**: 모든 예외는 에이전트가 발생 원인을 추론할 수 있도록 충분한 디버깅 정보(변수 값, 상태 등)를 메시지에 포함해야 한다.
- **경계에서 래핑**: Adapter 계층에서 발생하는 기술적 예외는 도메인 에러 코드로 변환하여 전파한다.
- **중앙화된 처리**: 비즈니스 로직 내부에서 예외를 삼키지(Catch & Ignore) 않고, 최상위 진입점에서 에러 코드 기반으로 일괄 처리하여 사용자 응답으로 변환한다.

---

# PlayHouse E2E 테스트 가이드

## 7. PlayHouse 시스템 아키텍처

```
                    ┌─────────────────────────────────────┐
                    │          External Clients           │
                    │        (Web, Mobile, Game)          │
                    └──────────┬──────────────┬───────────┘
                               │              │
              HTTP/REST        │              │  TCP/WebSocket
          (정보 요청)          │              │  (실시간 통신)
                               │              │
           ┌───────────────────┘              └───────────────────┐
           │                                                      │
           ▼                                                      ▼
┌─────────────────────────────────┐          ┌─────────────────────────────────┐
│          Web Server             │          │          Play Server            │
│     (ASP.NET Core 등)           │          │        (독립 프로세스)           │
│                                 │          │                                 │
│  ┌───────────────────────────┐  │          │  - Stage 관리                   │
│  │    API Server 모듈        │  │  ZMQ   │  - Actor 실행                   │
│  │  (PlayHouse.Api 라이브러리)│──┼─────────►│  - Client 연결 (TCP/WS)         │
│  │                           │  │ Router   │                                 │
│  │  - IApiSender (DI 주입)   │◄─┼──────────┤                                 │
│  │  - Stage 생성 요청        │  │          │                                 │
│  └───────────────────────────┘  │          └─────────────────────────────────┘
│                                 │                        ▲
└─────────────────────────────────┘                        │ ZMQ
                                              ┌────────────┴────────────┐
                                              │                         │
                                              ▼                         ▼
                                   ┌─────────────────┐       ┌─────────────────┐
                                   │  Play Server 2  │◄─────►│  Play Server N  │
                                   └─────────────────┘ ZMQ └─────────────────┘
```

## 8. PlayHouse API 사용 가이드

### 8.1 Play Server 부트스트랩

Play Server는 Stage와 Actor를 관리하고, 클라이언트와 실시간 통신을 담당합니다.

```csharp
// Play Server 시작
var playServer = new PlayServerBootstrap()
    .Configure(options =>
    {
        options.ServiceId = 1;                          // 서비스 식별자
        options.ServerId = 1;                           // 서버 인스턴스 ID
        options.BindEndpoint = "tcp://0.0.0.0:5000";    // ZMQ 서버 간 통신
        options.ClientEndpoint = "tcp://0.0.0.0:6000";  // 클라이언트 TCP
    })
    .UseStage<GameRoomStage>("GameRoom")  // Stage 타입 등록
    .UseActor<PlayerActor>()              // Actor 타입 등록
    .Build();

await playServer.StartAsync();
```

### 8.2 API Server 부트스트랩

API Server는 웹서버에 통합되어 Play Server와 ZMQ로 통신합니다.

```csharp
// API Server 시작 (웹서버에 통합)
var apiServer = new ApiServerBootstrap()
    .Configure(options =>
    {
        options.ServiceId = 2;
        options.ServerId = 1;
        options.BindEndpoint = "tcp://0.0.0.0:5100";
    })
    .UseController<GameApiController>()
    .Build();

// ASP.NET Core DI에 등록
builder.Services.AddSingleton<IApiSender>(apiServer.ApiSender);
await apiServer.StartAsync();
```

### 8.3 Stage 구현

Stage는 게임 룸/방/매치를 표현하며, 여러 Actor가 모여 상호작용하는 공간입니다.

```csharp
public class GameRoomStage : IStage
{
    public IStageSender StageSender { get; private set; } = null!;
    private readonly List<IActor> _actors = new();

    // Stage 생성 시 호출 (API 서버의 CreateStage 요청)
    public Task<(bool result, IPacket reply)> OnCreate(IPacket packet)
    {
        var request = packet.Parse<CreateRoomRequest>();
        return Task.FromResult((true, Packet.Of(new CreateRoomResponse
        {
            StageId = StageSender.StageId
        })));
    }

    public Task OnPostCreate() => Task.CompletedTask;
    public Task OnDestroy() => Task.CompletedTask;

    // Actor가 Stage에 입장할 때
    public Task<bool> OnJoinStage(IActor actor)
    {
        _actors.Add(actor);
        return Task.FromResult(true);
    }

    public Task OnPostJoinStage(IActor actor)
    {
        // 다른 플레이어들에게 입장 알림
        foreach (var other in _actors.Where(a => a != actor))
        {
            other.ActorSender.SendToClient(Packet.Of(new PlayerJoinedNotice
            {
                PlayerId = actor.ActorSender.AccountId
            }));
        }
        return Task.CompletedTask;
    }

    public ValueTask OnConnectionChanged(IActor actor, bool isConnected)
        => ValueTask.CompletedTask;

    // 클라이언트 메시지 처리
    public Task OnDispatch(IActor actor, IPacket packet)
    {
        switch (packet.MsgId)
        {
            case "EchoRequest":
                var echoReq = packet.Parse<EchoRequest>();
                StageSender.Reply(Packet.Of(new EchoReply
                {
                    Content = echoReq.Content,
                    Sequence = echoReq.Sequence
                }));
                break;
        }
        return Task.CompletedTask;
    }

    // 서버 간 메시지 처리
    public Task OnDispatch(IPacket packet) => Task.CompletedTask;
}
```

### 8.4 Actor 구현

Actor는 클라이언트와 1:1로 매핑되는 플레이어 표현입니다.

```csharp
public class PlayerActor : IActor
{
    public IActorSender ActorSender { get; private set; } = null!;

    public Task OnCreate() => Task.CompletedTask;
    public Task OnDestroy() => Task.CompletedTask;

    // 인증 처리 (클라이언트가 Authenticate 요청 시)
    public Task<bool> OnAuthenticate(IPacket authPacket)
    {
        var request = authPacket.Parse<AuthenticateRequest>();

        // 인증 검증 로직
        if (IsValidToken(request.Token))
        {
            ActorSender.AccountId = request.UserId;  // 필수: AccountId 설정
            return Task.FromResult(true);
        }
        return Task.FromResult(false);  // false → 연결 종료
    }

    // 인증 성공 후 호출 (API 서버에서 정보 로드)
    public Task OnPostAuthenticate()
    {
        return Task.CompletedTask;
    }

    private bool IsValidToken(string token) => token == "valid-token";
}
```

### 8.5 Connector (클라이언트) 사용

Connector는 클라이언트에서 Play Server에 연결하는 라이브러리입니다.

```csharp
// 1. Connector 초기화
var connector = new Connector();
connector.Init(new ConnectorConfig
{
    Host = "localhost",
    Port = 6000,
    ConnectionType = ConnectionType.Tcp
});

// 2. 이벤트 핸들러 등록
connector.OnConnect += (success) => Console.WriteLine($"Connected: {success}");
connector.OnReceive += (stageId, packet) => HandleMessage(stageId, packet);
connector.OnError += (stageId, errorCode, request) => HandleError(errorCode);
connector.OnDisconnect += () => Console.WriteLine("Disconnected");

// 3. 연결 및 인증
await connector.ConnectAsync();
var authResponse = await connector.AuthenticateAsync(
    Packet.Of(new AuthenticateRequest { UserId = "player1", Token = "valid-token" })
);

// 4. 메시지 전송
// Stage 없는 경우 (stageId = 0)
await connector.RequestAsync(Packet.Of(new EchoRequest { Content = "Hello" }));

// Stage 있는 경우
long stageId = 1001;
await connector.RequestAsync(stageId, Packet.Of(new GameActionRequest { Action = "move" }));
```

## 9. E2E 테스트 패턴

E2E 테스트는 **API 사용 가이드처럼 읽혀야** 합니다. 테스트 코드가 곧 사용 예제입니다.

### 9.1 테스트 픽스처 설정

```csharp
public class PlayHouseE2ETests : IAsyncLifetime
{
    private PlayServer _playServer = null!;
    private ApiServer _apiServer = null!;

    public async Task InitializeAsync()
    {
        // Play Server 시작
        _playServer = new PlayServerBootstrap()
            .Configure(options =>
            {
                options.ServiceId = 1;
                options.ServerId = 1;
                options.BindEndpoint = "tcp://127.0.0.1:15000";
                options.ClientEndpoint = "tcp://127.0.0.1:16000";
            })
            .UseStage<TestGameStage>("TestGame")
            .UseActor<TestPlayerActor>()
            .Build();

        await _playServer.StartAsync();

        // API Server 시작
        _apiServer = new ApiServerBootstrap()
            .Configure(options =>
            {
                options.ServiceId = 2;
                options.ServerId = 1;
                options.BindEndpoint = "tcp://127.0.0.1:15100";
            })
            .Build();

        await _apiServer.StartAsync();
    }

    public async Task DisposeAsync()
    {
        await _apiServer.StopAsync();
        await _playServer.StopAsync();
    }
}
```

### 9.2 연결 및 인증 테스트

```csharp
[Fact(DisplayName = "클라이언트가 서버에 연결하고 인증할 수 있다")]
public async Task Client_Should_Connect_And_Authenticate()
{
    // Given: Connector 설정
    var connector = new Connector();
    connector.Init(new ConnectorConfig
    {
        Host = "127.0.0.1",
        Port = 16000,
        ConnectionType = ConnectionType.Tcp
    });

    // When: 연결
    var connected = await connector.ConnectAsync();

    // Then: 연결 성공
    connected.Should().BeTrue();
    connector.IsConnected().Should().BeTrue();

    // When: 인증
    var authResponse = await connector.AuthenticateAsync(
        Packet.Of(new AuthenticateRequest { UserId = "player1", Token = "valid-token" })
    );

    // Then: 인증 성공
    connector.IsAuthenticated().Should().BeTrue();
    authResponse.MsgId.Should().Be("AuthenticateResponse");
}
```

### 9.3 메시지 송수신 테스트

```csharp
[Fact(DisplayName = "클라이언트가 에코 요청을 보내면 동일한 내용으로 응답받는다")]
public async Task Client_Should_Send_And_Receive_Echo_Messages()
{
    // Given: 인증된 Connector
    var connector = await CreateAuthenticatedConnector();

    // When: 에코 요청 전송
    var echoRequest = Packet.Of(new EchoRequest
    {
        Content = "Hello, PlayHouse!",
        Sequence = 1
    });
    var response = await connector.RequestAsync(echoRequest);

    // Then: 동일한 내용으로 응답
    var echoReply = response.Parse<EchoReply>();
    echoReply.Content.Should().Be("Hello, PlayHouse!");
    echoReply.Sequence.Should().Be(1);
}
```

### 9.4 Stage 생성 및 입장 테스트

```csharp
[Fact(DisplayName = "API로 Stage를 생성하고 클라이언트가 입장할 수 있다")]
public async Task Api_Creates_Stage_And_Client_Joins()
{
    // Given: API Sender와 인증된 Connector
    var apiSender = _apiServer.ApiSender;
    var connector = await CreateAuthenticatedConnector();

    // When: API로 Stage 생성
    var createResult = await apiSender.CreateStage(
        playNid: "1:1",
        stageType: "TestGame",
        stageId: 0,  // 0 = 자동 생성
        packet: Packet.Of(new CreateRoomRequest { RoomName = "Test Room" })
    );

    // Then: Stage 생성 성공
    createResult.ErrorCode.Should().Be(0);
    var stageId = createResult.StageId;
    stageId.Should().BeGreaterThan(0);

    // When: Stage에 메시지 전송
    var response = await connector.RequestAsync(stageId,
        Packet.Of(new GameActionRequest { Action = "ready" })
    );

    // Then: 게임 액션 응답 확인
    response.MsgId.Should().Be("GameActionResponse");
}
```

### 9.5 서버 Push 메시지 테스트

```csharp
[Fact(DisplayName = "서버가 클라이언트에게 Push 메시지를 전송할 수 있다")]
public async Task Server_Should_Push_Messages_To_Client()
{
    // Given: 두 클라이언트가 같은 Stage에 입장
    var connector1 = await CreateAuthenticatedConnector("player1");
    var connector2 = await CreateAuthenticatedConnector("player2");

    var pushMessages = new ConcurrentQueue<IPacket>();
    connector1.OnReceive += (stageId, packet) =>
    {
        if (packet.MsgId == "PlayerJoinedNotice")
            pushMessages.Enqueue(packet);
    };

    var stageId = await CreateStageAndJoin(connector1);

    // When: 두 번째 플레이어 입장
    await JoinStage(connector2, stageId);

    // Then: 첫 번째 플레이어가 입장 알림 수신
    await Task.Delay(100);
    pushMessages.Should().NotBeEmpty();
    var notice = pushMessages.First().Parse<PlayerJoinedNotice>();
    notice.PlayerId.Should().Be("player2");
}
```

### 9.6 재연결 테스트

```csharp
[Fact(DisplayName = "연결이 끊긴 후 재연결하여 게임을 계속할 수 있다")]
public async Task Client_Should_Reconnect_After_Disconnect()
{
    // Given: Stage에 입장한 클라이언트
    var connector = await CreateAuthenticatedConnector();
    var stageId = await CreateStageAndJoin(connector);

    // When: 연결 끊김
    connector.Disconnect();
    await Task.Delay(100);
    connector.IsConnected().Should().BeFalse();

    // When: 재연결
    await connector.ConnectAsync();
    await connector.AuthenticateAsync(
        Packet.Of(new AuthenticateRequest { UserId = "player1", Token = "valid-token" })
    );

    // Then: 재연결 후 Stage에 메시지 전송 가능
    var response = await connector.RequestAsync(stageId,
        Packet.Of(new GameActionRequest { Action = "ping" })
    );
    response.Should().NotBeNull();
}
```

### 9.7 타임아웃 테스트

```csharp
[Fact(DisplayName = "서버가 응답하지 않으면 타임아웃 예외가 발생한다")]
public async Task Request_Should_Timeout_When_Server_Not_Responding()
{
    // Given: 짧은 타임아웃 설정
    var connector = new Connector();
    connector.Init(new ConnectorConfig
    {
        Host = "127.0.0.1",
        Port = 16000,
        ConnectionType = ConnectionType.Tcp,
        RequestTimeout = TimeSpan.FromMilliseconds(500)
    });

    await connector.ConnectAsync();
    await connector.AuthenticateAsync(
        Packet.Of(new AuthenticateRequest { UserId = "player1", Token = "valid-token" })
    );

    // When & Then: 느린 요청에 대해 타임아웃 예외 발생
    await Assert.ThrowsAsync<ConnectorException>(async () =>
    {
        await connector.RequestAsync(
            Packet.Of(new SlowRequest { DelayMs = 2000 })
        );
    });
}
```

## 10. 테스트 Proto 정의

```protobuf
syntax = "proto3";
package PlayHouse.Tests.E2E.Proto;

message AuthenticateRequest {
    string user_id = 1;
    string token = 2;
}

message AuthenticateResponse {
    bool success = 1;
    string account_id = 2;
}

message CreateRoomRequest {
    string room_name = 1;
}

message CreateRoomResponse {
    int64 stage_id = 1;
}

message EchoRequest {
    string content = 1;
    int32 sequence = 2;
}

message EchoReply {
    string content = 1;
    int32 sequence = 2;
}

message GameActionRequest {
    string action = 1;
}

message GameActionResponse {
    bool success = 1;
    string message = 2;
}

message PlayerJoinedNotice {
    string player_id = 1;
}
```

## 11. 테스트 실행

```bash
# 전체 E2E 테스트 실행
dotnet test tests/PlayHouse.Tests.E2E --verbosity normal

# 특정 테스트만 실행
dotnet test tests/PlayHouse.Tests.E2E --filter "DisplayName~연결"

# 상세 로그
dotnet test tests/PlayHouse.Tests.E2E --verbosity detailed
```
