# API Controller 구현

API Server는 클라이언트나 다른 서버로부터 오는 요청을 처리하는 컨트롤러 기반 구조를 제공합니다. 이 가이드에서는 API Controller를 구현하고 사용하는 방법을 설명합니다.

## 개요

API Controller는 다음과 같은 역할을 합니다.

- 클라이언트 요청 처리
- 서버간 메시지 처리
- Stage 생성 및 관리
- 다른 서버로 메시지 라우팅

## 1. 기본 구조

### 1.1 IApiController 인터페이스

모든 API Controller는 `IApiController` 인터페이스를 구현해야 합니다.

```csharp
using PlayHouse.Abstractions.Api;

public class UserController : IApiController
{
    // 핸들러 등록
    public void Handles(IHandlerRegister register)
    {
        // 메시지 ID와 핸들러 메서드를 연결
        register.Add("LoginRequest", nameof(HandleLogin));
        register.Add<CreateRoomRequest>(nameof(HandleCreateRoom));
    }

    // 핸들러 메서드
    private async Task HandleLogin(IPacket packet, IApiLink link)
    {
        var request = LoginRequest.Parser.ParseFrom(packet.Payload.DataSpan);

        // 로직 처리
        var userId = await AuthenticateUser(request.Username, request.Password);

        // 응답 전송
        link.Reply(CPacket.Of(new LoginResponse
        {
            Success = userId != null,
            UserId = userId ?? ""
        }));
    }

    private async Task HandleCreateRoom(IPacket packet, IApiLink link)
    {
        var request = CreateRoomRequest.Parser.ParseFrom(packet.Payload.DataSpan);

        // Play Server에 Stage 생성
        var roomId = GenerateRoomId();
        var result = await link.CreateStage(
            "play-1",
            "GameRoom",
            roomId,
            CPacket.Of(new CreateRoomPayload { MaxPlayers = request.MaxPlayers })
        );

        link.Reply(CPacket.Of(new CreateRoomResponse
        {
            Success = result.Result,
            RoomId = roomId
        }));
    }
}
```

### 1.2 핸들러 등록 방식

`IHandlerRegister`는 여러 방식으로 핸들러를 등록할 수 있습니다.

```csharp
public void Handles(IHandlerRegister register)
{
    // 1. 문자열 메시지 ID + 메서드 이름 (권장)
    // - Per-request controller 인스턴스 생성
    // - Scoped DI 지원
    register.Add("LoginRequest", nameof(HandleLogin));

    // 2. 타입 기반 메시지 ID + 메서드 이름 (권장)
    register.Add<CreateRoomRequest>(nameof(HandleCreateRoom));

    // 3. 직접 핸들러 함수 전달 (비권장)
    // - Controller 인스턴스가 캡처됨
    // - Scoped DI가 제대로 동작하지 않을 수 있음
    register.Add("OldStyleRequest", HandleOldStyle);
}
```

권장하는 방식은 메서드 이름을 전달하는 방식(1, 2)입니다. 이 방식은 요청마다 새로운 컨트롤러 인스턴스를 생성하여 Scoped DI를 올바르게 지원합니다.

## 2. IApiLink 사용

핸들러 메서드는 `IApiLink` 파라미터를 통해 다양한 기능에 접근할 수 있습니다.

### 2.1 기본 속성

```csharp
private async Task HandleRequest(IPacket packet, IApiLink link)
{
    // 요청 타입 확인
    bool isRequest = link.IsRequest; // true이면 Reply 필요

    // 요청 컨텍스트 정보
    long stageId = link.StageId;           // 요청한 Stage ID
    string accountId = link.AccountId;     // 요청한 사용자 ID
    string fromNid = link.FromNid;         // 요청 출처 서버 NID
    ushort serviceId = link.ServiceId;     // 현재 서버의 Service ID
    ServerType serverType = link.ServerType; // 현재 서버 타입

    // AccountId 설정 (필요시)
    link.AccountId = "user123";
}
```

### 2.2 응답 전송

```csharp
private async Task HandleRequest(IPacket packet, IApiLink link)
{
    // 성공 응답
    link.Reply(CPacket.Of(new Response { Data = "success" }));

    // 에러 응답 (에러 코드만)
    link.Reply(404); // Not Found
    link.Reply(500); // Internal Server Error

    // 빈 응답
    link.Reply(CPacket.Empty("EmptyResponse"));
}
```

### 2.3 Stage 생성

#### CreateStage

새로운 Stage를 생성합니다. 이미 존재하면 실패합니다.

```csharp
private async Task HandleCreateRoom(IPacket packet, IApiLink link)
{
    var request = CreateRoomRequest.Parser.ParseFrom(packet.Payload.DataSpan);

    var roomId = GenerateRoomId();
    var createPayload = new CreateRoomPayload
    {
        RoomName = request.RoomName,
        MaxPlayers = request.MaxPlayers,
        GameMode = request.GameMode
    };

    // CreateStage (async/await)
    var result = await link.CreateStage(
        playNid: "play-1",
        stageType: "GameRoom",
        stageId: roomId,
        packet: CPacket.Of(createPayload)
    );

    if (result.Result)
    {
        // Stage 생성 성공
        // result.Reply는 IStage.OnCreate()의 reply 반환값
        var stageReply = CreateStageReply.Parser.ParseFrom(result.Reply.Payload.DataSpan);

        link.Reply(CPacket.Of(new CreateRoomResponse
        {
            Success = true,
            RoomId = roomId,
            PlayServerId = "play-1"
        }));
    }
    else
    {
        // Stage 생성 실패
        link.Reply(500);
    }
}
```

#### CreateStage (Callback 버전)

```csharp
private void HandleCreateRoomCallback(IPacket packet, IApiLink link)
{
    var request = CreateRoomRequest.Parser.ParseFrom(packet.Payload.DataSpan);
    var roomId = GenerateRoomId();

    link.CreateStage(
        "play-1",
        "GameRoom",
        roomId,
        CPacket.Of(new CreateRoomPayload { MaxPlayers = request.MaxPlayers }),
        (errorCode, result) =>
        {
            if (errorCode == 0 && result != null && result.Result)
            {
                link.Reply(CPacket.Of(new CreateRoomResponse
                {
                    Success = true,
                    RoomId = roomId
                }));
            }
            else
            {
                link.Reply(500);
            }
        }
    );
}
```

#### GetOrCreateStage

Stage가 존재하면 가져오고, 없으면 생성합니다.

```csharp
private async Task HandleJoinOrCreateRoom(IPacket packet, IApiLink link)
{
    var request = JoinOrCreateRoomRequest.Parser.ParseFrom(packet.Payload.DataSpan);

    var roomId = request.RoomId;
    var createPayload = new CreateRoomPayload
    {
        RoomName = request.RoomName,
        MaxPlayers = 10
    };

    var result = await link.GetOrCreateStage(
        playNid: "play-1",
        stageType: "GameRoom",
        stageId: roomId,
        createPacket: CPacket.Of(createPayload)
    );

    link.Reply(CPacket.Of(new JoinOrCreateRoomResponse
    {
        Success = result.Result,
        IsCreated = result.IsCreated, // true면 새로 생성, false면 기존 Stage
        RoomId = roomId
    }));
}
```

## 3. 의존성 주입 (DI)

### 3.1 Constructor Injection

API Controller는 생성자 주입을 통해 서비스를 사용할 수 있습니다.

```csharp
public class UserController : IApiController
{
    private readonly IUserRepository _userRepository;
    private readonly ILogger<UserController> _logger;
    private readonly IConfiguration _configuration;

    public UserController(
        IUserRepository userRepository,
        ILogger<UserController> logger,
        IConfiguration configuration)
    {
        _userRepository = userRepository;
        _logger = logger;
        _configuration = configuration;
    }

    public void Handles(IHandlerRegister register)
    {
        register.Add<LoginRequest>(nameof(HandleLogin));
    }

    private async Task HandleLogin(IPacket packet, IApiLink link)
    {
        var request = LoginRequest.Parser.ParseFrom(packet.Payload.DataSpan);

        _logger.LogInformation("Login attempt: {Username}", request.Username);

        var user = await _userRepository.FindByUsername(request.Username);

        link.Reply(CPacket.Of(new LoginResponse
        {
            Success = user != null,
            UserId = user?.Id ?? ""
        }));
    }
}
```

### 3.2 Scoped 서비스 주의사항

메서드 이름 기반 등록(`nameof()` 사용)을 사용해야 Scoped DI가 올바르게 동작합니다.

```csharp
// 올바른 방식: Per-request 인스턴스 생성
public void Handles(IHandlerRegister register)
{
    register.Add<LoginRequest>(nameof(HandleLogin));
}

// 잘못된 방식: Controller 인스턴스가 캡처됨
public void Handles(IHandlerRegister register)
{
    register.Add<LoginRequest>(HandleLogin); // 비권장
}
```

### 3.3 서비스 등록

```csharp
// Program.cs
builder.Services.AddScoped<IUserRepository, UserRepository>();
builder.Services.AddSingleton<ICacheService, RedisCacheService>();
builder.Services.AddTransient<IEmailService, EmailService>();
```

## 4. 실전 예제

### 예제 1: 사용자 인증 및 Stage 참가

```csharp
public class GameController : IApiController
{
    private readonly IUserService _userService;
    private readonly IMatchmakingService _matchmaking;
    private readonly ILogger<GameController> _logger;

    public GameController(
        IUserService userService,
        IMatchmakingService matchmaking,
        ILogger<GameController> logger)
    {
        _userService = userService;
        _matchmaking = matchmaking;
        _logger = logger;
    }

    public void Handles(IHandlerRegister register)
    {
        register.Add<LoginRequest>(nameof(HandleLogin));
        register.Add<JoinGameRequest>(nameof(HandleJoinGame));
    }

    private async Task HandleLogin(IPacket packet, IApiLink link)
    {
        var request = LoginRequest.Parser.ParseFrom(packet.Payload.DataSpan);

        try
        {
            // 사용자 인증
            var user = await _userService.AuthenticateAsync(
                request.Username,
                request.Password
            );

            if (user == null)
            {
                link.Reply(401); // Unauthorized
                return;
            }

            _logger.LogInformation("User logged in: {UserId}", user.Id);

            link.Reply(CPacket.Of(new LoginResponse
            {
                Success = true,
                UserId = user.Id,
                DisplayName = user.DisplayName,
                Token = user.Token
            }));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Login failed");
            link.Reply(500);
        }
    }

    private async Task HandleJoinGame(IPacket packet, IApiLink link)
    {
        var request = JoinGameRequest.Parser.ParseFrom(packet.Payload.DataSpan);

        try
        {
            // 매치 찾기
            var match = await _matchmaking.FindMatchAsync(
                request.GameMode,
                request.Rating
            );

            string playServerId;
            long stageId;
            bool isNewMatch = false;

            if (match == null)
            {
                // 새 매치 생성
                stageId = GenerateMatchId();
                playServerId = SelectPlayServer();

                var createPayload = new CreateMatchPayload
                {
                    GameMode = request.GameMode,
                    RequiredRating = request.Rating
                };

                var result = await link.CreateStage(
                    playServerId,
                    "MatchStage",
                    stageId,
                    CPacket.Of(createPayload)
                );

                if (!result.Result)
                {
                    link.Reply(503); // Service Unavailable
                    return;
                }

                isNewMatch = true;
            }
            else
            {
                // 기존 매치 참가
                stageId = match.StageId;
                playServerId = match.PlayServerId;
            }

            link.Reply(CPacket.Of(new JoinGameResponse
            {
                Success = true,
                PlayServerId = playServerId,
                StageId = stageId,
                IsNewMatch = isNewMatch
            }));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Join game failed");
            link.Reply(500);
        }
    }

    private string SelectPlayServer()
    {
        // 로드밸런싱 로직
        return "play-1";
    }

    private long GenerateMatchId()
    {
        return DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    }
}
```

### 예제 2: 리더보드 서비스

```csharp
public class LeaderboardController : IApiController
{
    private readonly ILeaderboardRepository _repository;
    private readonly ICacheService _cache;
    private readonly ILogger<LeaderboardController> _logger;

    public LeaderboardController(
        ILeaderboardRepository repository,
        ICacheService cache,
        ILogger<LeaderboardController> logger)
    {
        _repository = repository;
        _cache = cache;
        _logger = logger;
    }

    public void Handles(IHandlerRegister register)
    {
        register.Add<UpdateLeaderboardRequest>(nameof(HandleUpdate));
        register.Add<GetLeaderboardRequest>(nameof(HandleGet));
    }

    private async Task HandleUpdate(IPacket packet, IApiLink link)
    {
        var request = UpdateLeaderboardRequest.Parser.ParseFrom(packet.Payload.DataSpan);

        try
        {
            // DB 업데이트
            var newRank = await _repository.UpdateScoreAsync(
                request.PlayerId,
                request.Score,
                request.GameMode
            );

            // 캐시 무효화
            await _cache.InvalidateAsync($"leaderboard:{request.GameMode}");

            _logger.LogInformation(
                "Score updated: {PlayerId} = {Score} (Rank: {Rank})",
                request.PlayerId, request.Score, newRank
            );

            link.Reply(CPacket.Of(new UpdateLeaderboardResponse
            {
                Success = true,
                NewRank = newRank,
                Score = request.Score
            }));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Update leaderboard failed");
            link.Reply(500);
        }
    }

    private async Task HandleGet(IPacket packet, IApiLink link)
    {
        var request = GetLeaderboardRequest.Parser.ParseFrom(packet.Payload.DataSpan);

        try
        {
            // 캐시 확인
            var cacheKey = $"leaderboard:{request.GameMode}:{request.Page}";
            var cached = await _cache.GetAsync<LeaderboardData>(cacheKey);

            if (cached != null)
            {
                link.Reply(CPacket.Of(CreateResponse(cached)));
                return;
            }

            // DB 조회
            var data = await _repository.GetTopPlayersAsync(
                request.GameMode,
                request.Page,
                request.PageSize
            );

            // 캐시 저장
            await _cache.SetAsync(cacheKey, data, TimeSpan.FromMinutes(5));

            link.Reply(CPacket.Of(CreateResponse(data)));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Get leaderboard failed");
            link.Reply(500);
        }
    }

    private GetLeaderboardResponse CreateResponse(LeaderboardData data)
    {
        var response = new GetLeaderboardResponse();
        response.Entries.AddRange(data.Entries.Select(e => new LeaderboardEntry
        {
            Rank = e.Rank,
            PlayerId = e.PlayerId,
            DisplayName = e.DisplayName,
            Score = e.Score
        }));
        return response;
    }
}
```

### 예제 3: 서버간 통신 라우팅

```csharp
public class RoutingController : IApiController
{
    private readonly ILogger<RoutingController> _logger;

    public RoutingController(ILogger<RoutingController> logger)
    {
        _logger = logger;
    }

    public void Handles(IHandlerRegister register)
    {
        register.Add<SendGiftRequest>(nameof(HandleSendGift));
    }

    private async Task HandleSendGift(IPacket packet, IApiLink link)
    {
        var request = SendGiftRequest.Parser.ParseFrom(packet.Payload.DataSpan);

        try
        {
            // 수신자가 있는 Stage로 메시지 전송
            var targetStage = await FindPlayerStage(request.TargetPlayerId);

            if (targetStage == null)
            {
                link.Reply(404); // Not Found
                return;
            }

            // Stage로 선물 알림 전송
            var notification = new GiftNotification
            {
                FromPlayerId = request.FromPlayerId,
                ItemId = request.ItemId,
                Quantity = request.Quantity,
                Message = request.Message
            };

            link.SendToStage(
                targetStage.PlayServerId,
                targetStage.StageId,
                CPacket.Of(notification)
            );

            _logger.LogInformation(
                "Gift sent from {From} to {To}",
                request.FromPlayerId,
                request.TargetPlayerId
            );

            link.Reply(CPacket.Of(new SendGiftResponse { Success = true }));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Send gift failed");
            link.Reply(500);
        }
    }

    private async Task<StageLocation?> FindPlayerStage(string playerId)
    {
        // 플레이어가 현재 접속한 Stage 찾기
        // 실제로는 Redis나 DB에서 조회
        return null;
    }
}
```

## 5. 주의사항

### 5.1 응답 필수

Request 타입 메시지는 반드시 응답을 전송해야 합니다.

```csharp
private async Task HandleRequest(IPacket packet, IApiLink link)
{
    if (!link.IsRequest)
    {
        // Send 타입은 응답 불필요
        return;
    }

    try
    {
        // 처리 로직
        link.Reply(CPacket.Of(response));
    }
    catch (Exception)
    {
        // 예외 발생 시에도 반드시 응답
        link.Reply(500);
    }
}
```

### 5.2 비동기 처리

핸들러는 `async Task`로 선언하고 비동기 작업을 올바르게 처리해야 합니다.

```csharp
// 올바른 방식
private async Task HandleRequest(IPacket packet, IApiLink link)
{
    var data = await _repository.GetDataAsync();
    link.Reply(CPacket.Of(new Response { Data = data }));
}

// 잘못된 방식: async void
private async void HandleRequest(IPacket packet, IApiLink link)
{
    // 예외가 전파되지 않아 문제 발생
}
```

### 5.3 패킷 해제

Request로 받은 응답 패킷은 자동으로 해제됩니다. 명시적으로 해제할 필요가 없습니다.

```csharp
private async Task HandleRequest(IPacket packet, IApiLink link)
{
    // packet은 자동 해제됨 (using 불필요)
    var request = Request.Parser.ParseFrom(packet.Payload.DataSpan);

    // 다른 서버로 요청 시에는 using 권장
    using var response = await link.RequestToApiService(100, CPacket.Of(request));
    var data = Response.Parser.ParseFrom(response.Payload.DataSpan);

    link.Reply(CPacket.Of(data));
}
```

## 6. 요약

API Controller는 다음과 같은 패턴으로 구현합니다.

1. `IApiController` 인터페이스 구현
2. `Handles()`에서 메시지 핸들러 등록 (메서드 이름 사용 권장)
3. 핸들러 메서드에서 `IApiLink`를 통해 기능 사용
4. 의존성 주입을 통해 서비스 사용
5. 항상 응답 전송 (Request 타입인 경우)
6. 예외 처리 및 로깅 구현

API Server는 상태를 가지지 않는 무상태(stateless) 서비스로 설계하는 것이 권장됩니다. 상태가 필요한 로직은 Stage에서 처리하고, API Server는 라우팅 및 간단한 처리만 담당하도록 구성하세요.
