#nullable enable

using System.Net.Http.Json;
using FluentAssertions;
using PlayHouse.Connector;
using PlayHouse.Infrastructure.Http;
using PlayHouse.Proto;
using PlayHouse.Tests.E2E.Proto;
using PlayHouse.Tests.E2E.TestFixtures;
using PlayHouse.Tests.Shared;
using Xunit;

namespace PlayHouse.Tests.E2E;

/// <summary>
/// PlayHouse Actor/Stage 생명주기 E2E 테스트.
///
/// 이 테스트는 PlayHouse의 실제 Actor/Stage 생명주기를 검증합니다:
/// 1. HTTP API로 방 생성 → Stage.OnCreate(), Stage.OnPostCreate() 호출
/// 2. TCP 연결 및 인증 → Stage.OnJoinRoom() → Actor.OnCreate() 호출
/// 3. 메시지 전송 → Stage.OnDispatch() → 메시지 핸들러 호출
/// 4. Actor/Stage 생명주기 이벤트 추적 및 검증
///
/// 주의: Actor.OnAuthenticate()와 Actor.OnDestroy()는 현재 구현에서 호출되지 않습니다.
/// </summary>
public class ActorLifecycleE2ETests : IAsyncLifetime
{
    private TestServerFixture _fixture = null!;
    private TestServer _server = null!;
    private HttpClient _httpClient = null!;

    public async Task InitializeAsync()
    {
        ChatStage.Reset();
        ChatActor.Reset();

        _fixture = new TestServerFixture()
            .RegisterStage<ChatStage>("chat-stage")
            .RegisterActor<ChatActor>("chat-stage");

        _server = await _fixture.StartServerAsync();
        await Task.Delay(100); // 서버 초기화 대기

        _httpClient = _server.CreateHttpClient();
    }

    public async Task DisposeAsync()
    {
        _httpClient?.Dispose();
        await _fixture.DisposeAsync();
    }

    [Fact(DisplayName = "1. HTTP API로 방 생성 → Stage.OnCreate() + Stage.OnPostCreate() 호출")]
    public async Task HttpCreateRoom_TriggersStageLifecycleCallbacks()
    {
        // ============================================
        // Given: 방 생성 요청 준비
        // ============================================
        var createRequest = new PlayHouse.Infrastructure.Http.CreateRoomRequest
        {
            RoomType = "chat-stage",
            Nickname = "TestUser"
        };

        // ============================================
        // When: HTTP API로 방 생성
        // ============================================
        var httpResponse = await _httpClient.PostAsJsonAsync("/api/rooms/create", createRequest);
        httpResponse.IsSuccessStatusCode.Should().BeTrue();

        var roomInfo = await httpResponse.Content.ReadFromJsonAsync<CreateRoomResponse>();
        roomInfo.Should().NotBeNull();
        roomInfo!.RoomToken.Should().NotBeNullOrWhiteSpace();

        // ============================================
        // Then: Stage 생명주기 콜백 검증
        // ============================================
        await Task.Delay(200); // 비동기 처리 대기

        var stageEvents = ChatStage.GetLifecycleEvents();
        stageEvents.Should().Contain(e => e.EventType == "OnCreate", "Stage.OnCreate()가 호출되어야 함");
        stageEvents.Should().Contain(e => e.EventType == "OnPostCreate", "Stage.OnPostCreate()가 호출되어야 함");

        ChatStage.GetTotalOnCreateCalls().Should().BeGreaterOrEqualTo(1);
    }

    [Fact(DisplayName = "2. TCP 연결 + 인증 → Stage.OnJoinRoom() → Actor.OnCreate() 호출")]
    public async Task TcpAuthenticationAndJoinRoom_TriggersActorCreation()
    {
        // ============================================
        // Given: HTTP API로 방 생성 및 토큰 발급
        // ============================================
        var createRequest = new PlayHouse.Infrastructure.Http.CreateRoomRequest
        {
            RoomType = "chat-stage",
            Nickname = "TestUser"
        };

        var httpResponse = await _httpClient.PostAsJsonAsync("/api/rooms/create", createRequest);
        var roomInfo = await httpResponse.Content.ReadFromJsonAsync<CreateRoomResponse>();
        roomInfo.Should().NotBeNull();

        // ============================================
        // When: TCP 연결 및 인증
        // ============================================
        await using var client = new PlayHouseClient(null, null);
        await client.ConnectAsync(roomInfo!.Endpoint, "temp-token");

        var authReplyReceived = new TaskCompletionSource<AuthenticateReply>();
        client.On<AuthenticateReply>(reply => authReplyReceived.TrySetResult(reply));

        var authRequest = new AuthenticateRequest { RoomToken = roomInfo.RoomToken };
        await client.SendAsync(authRequest);

        var authReply = await authReplyReceived.Task.WaitAsync(TimeSpan.FromSeconds(3));
        authReply.Should().NotBeNull();
        authReply.Authenticated.Should().BeTrue();

        // ============================================
        // Then: Stage와 Actor 생명주기 콜백 검증
        // ============================================
        await Task.Delay(300); // Actor 생성 대기

        // Stage.OnJoinRoom 호출 확인
        var stageEvents = ChatStage.GetLifecycleEvents();
        stageEvents.Should().Contain(e =>
            e.EventType == "OnJoinRoom" && e.ActorId == authReply.AccountId,
            "Stage.OnJoinRoom()이 호출되어야 함");

        // Actor.OnCreate 호출 확인
        var onCreateCalled = await ChatActor.WaitForEventAsync("OnCreate", authReply.AccountId, TimeSpan.FromSeconds(2));
        onCreateCalled.Should().BeTrue("Actor.OnCreate()가 호출되어야 함");

        // Actor 인스턴스 확인
        var actorInstance = ChatActor.GetInstance(authReply.AccountId);
        actorInstance.Should().NotBeNull("Actor 인스턴스가 생성되어야 함");
        actorInstance!.OnCreateCallCount.Should().Be(1, "OnCreate()가 1회 호출되어야 함");
    }

    [Fact(DisplayName = "3. 메시지 전송 → Stage.OnDispatch() → 메시지 핸들러 → 응답")]
    public async Task MessageSending_TriggersStageDispatchAndReply()
    {
        // ============================================
        // Given: 인증 완료된 클라이언트
        // ============================================
        var createRequest = new PlayHouse.Infrastructure.Http.CreateRoomRequest
        {
            RoomType = "chat-stage",
            Nickname = "TestUser"
        };

        var httpResponse = await _httpClient.PostAsJsonAsync("/api/rooms/create", createRequest);
        var roomInfo = await httpResponse.Content.ReadFromJsonAsync<CreateRoomResponse>();

        await using var client = new PlayHouseClient(null, null);
        await client.ConnectAsync(roomInfo!.Endpoint, roomInfo.RoomToken);

        var authReplyReceived = new TaskCompletionSource<AuthenticateReply>();
        client.On<AuthenticateReply>(reply => authReplyReceived.TrySetResult(reply));
        await client.SendAsync(new AuthenticateRequest { RoomToken = roomInfo.RoomToken });
        var authReply = await authReplyReceived.Task.WaitAsync(TimeSpan.FromSeconds(3));

        await Task.Delay(200); // Actor 생성 대기

        // ============================================
        // When: EchoRequest 메시지 전송
        // ============================================
        var echoReplyReceived = new TaskCompletionSource<EchoReply>();
        client.On<EchoReply>(reply => echoReplyReceived.TrySetResult(reply));

        var echoRequest = new EchoRequest
        {
            Content = "Test message for dispatch",
            Sequence = 1
        };

        await client.SendAsync(echoRequest);

        // ============================================
        // Then: 메시지 라우팅 및 응답 검증
        // ============================================
        var echoReply = await echoReplyReceived.Task.WaitAsync(TimeSpan.FromSeconds(3));
        echoReply.Should().NotBeNull();
        echoReply.Content.Should().Be("Test message for dispatch");
        echoReply.Sequence.Should().Be(1);

        // Stage.OnDispatch() 호출 확인
        await Task.Delay(200);
        var routingEvents = ChatStage.GetMessageRoutingEvents();
        routingEvents.Should().Contain(e =>
            e.MessageId == EchoRequest.Descriptor.Name &&
            e.ActorId == authReply.AccountId,
            "Stage가 EchoRequest를 Actor로 라우팅해야 함");

        ChatStage.GetTotalMessageRoutingCount(EchoRequest.Descriptor.Name)
            .Should().BeGreaterOrEqualTo(1);
    }

    [Fact(DisplayName = "4. 전체 플로우: 방 생성 → 인증 → 메시지 송수신 (통합 테스트)")]
    public async Task FullFlow_CreateRoom_Authenticate_SendMessage()
    {
        // ============================================
        // 1단계: HTTP API로 방 생성
        // ============================================
        var createRequest = new PlayHouse.Infrastructure.Http.CreateRoomRequest
        {
            RoomType = "chat-stage",
            Nickname = "FullFlowUser"
        };

        var httpResponse = await _httpClient.PostAsJsonAsync("/api/rooms/create", createRequest);
        httpResponse.IsSuccessStatusCode.Should().BeTrue();

        var roomInfo = await httpResponse.Content.ReadFromJsonAsync<CreateRoomResponse>();
        roomInfo.Should().NotBeNull();
        roomInfo!.RoomToken.Should().NotBeNullOrWhiteSpace();

        // ============================================
        // 2단계: TCP 연결 및 인증
        // ============================================
        await using var client = new PlayHouseClient(null, null);
        var connectResult = await client.ConnectAsync(roomInfo.Endpoint, "temp-token");
        connectResult.Success.Should().BeTrue();

        var authReplyReceived = new TaskCompletionSource<AuthenticateReply>();
        client.On<AuthenticateReply>(reply => authReplyReceived.TrySetResult(reply));
        await client.SendAsync(new AuthenticateRequest { RoomToken = roomInfo.RoomToken });

        var authReply = await authReplyReceived.Task.WaitAsync(TimeSpan.FromSeconds(3));
        authReply.Authenticated.Should().BeTrue();
        authReply.StageId.Should().Be(roomInfo.StageId);

        var accountId = authReply.AccountId;

        // ============================================
        // 3단계: Stage 및 Actor 생명주기 검증
        // ============================================
        await Task.Delay(300);

        var stageEvents = ChatStage.GetLifecycleEvents();
        stageEvents.Should().Contain(e => e.EventType == "OnCreate");
        stageEvents.Should().Contain(e => e.EventType == "OnJoinRoom" && e.ActorId == accountId);

        var actorInstance = ChatActor.GetInstance(accountId);
        actorInstance.Should().NotBeNull();
        actorInstance!.OnCreateCallCount.Should().Be(1);

        // ============================================
        // 4단계: 메시지 송수신 (Client → Server → Stage → Actor → Response)
        // ============================================
        var echoReplyReceived = new TaskCompletionSource<EchoReply>();
        client.On<EchoReply>(reply => echoReplyReceived.TrySetResult(reply));

        await client.SendAsync(new EchoRequest
        {
            Content = "Full flow integration test",
            Sequence = 100
        });

        var echoReply = await echoReplyReceived.Task.WaitAsync(TimeSpan.FromSeconds(3));
        echoReply.Content.Should().Be("Full flow integration test");
        echoReply.Sequence.Should().Be(100);

        // ============================================
        // 5단계: 메시지 라우팅 검증
        // ============================================
        await Task.Delay(200);
        var routingEvents = ChatStage.GetMessageRoutingEvents();
        routingEvents.Should().Contain(e =>
            e.MessageId == EchoRequest.Descriptor.Name &&
            e.ActorId == accountId);

        // 전체 플로우 성공 확인
        ChatActor.GetTotalOnCreateCalls().Should().BeGreaterOrEqualTo(1);
        ChatStage.GetTotalOnCreateCalls().Should().BeGreaterOrEqualTo(1);
        ChatStage.GetTotalOnJoinRoomCalls().Should().BeGreaterOrEqualTo(1);
    }

    [Fact(DisplayName = "5. 복수 클라이언트: 각각 독립된 Actor 인스턴스 생성 및 추적")]
    public async Task MultipleClients_EachHasIndependentActorInstance()
    {
        // ============================================
        // Given: 2개의 방 생성
        // ============================================
        var room1Response = await _httpClient.PostAsJsonAsync("/api/rooms/create",
            new PlayHouse.Infrastructure.Http.CreateRoomRequest
            {
                RoomType = "chat-stage",
                Nickname = "User1"
            });
        var room1Info = await room1Response.Content.ReadFromJsonAsync<CreateRoomResponse>();

        var room2Response = await _httpClient.PostAsJsonAsync("/api/rooms/create",
            new PlayHouse.Infrastructure.Http.CreateRoomRequest
            {
                RoomType = "chat-stage",
                Nickname = "User2"
            });
        var room2Info = await room2Response.Content.ReadFromJsonAsync<CreateRoomResponse>();

        // ============================================
        // When: 2개의 클라이언트 연결 및 인증
        // ============================================
        await using var client1 = new PlayHouseClient(null, null);
        await client1.ConnectAsync(room1Info!.Endpoint, room1Info.RoomToken);

        var authReply1Received = new TaskCompletionSource<AuthenticateReply>();
        client1.On<AuthenticateReply>(reply => authReply1Received.TrySetResult(reply));
        await client1.SendAsync(new AuthenticateRequest { RoomToken = room1Info.RoomToken });
        var auth1 = await authReply1Received.Task.WaitAsync(TimeSpan.FromSeconds(3));

        await using var client2 = new PlayHouseClient(null, null);
        await client2.ConnectAsync(room2Info!.Endpoint, room2Info.RoomToken);

        var authReply2Received = new TaskCompletionSource<AuthenticateReply>();
        client2.On<AuthenticateReply>(reply => authReply2Received.TrySetResult(reply));
        await client2.SendAsync(new AuthenticateRequest { RoomToken = room2Info.RoomToken });
        var auth2 = await authReply2Received.Task.WaitAsync(TimeSpan.FromSeconds(3));

        auth1.Authenticated.Should().BeTrue();
        auth2.Authenticated.Should().BeTrue();

        // ============================================
        // Then: 각각 독립된 Actor 인스턴스 확인
        // ============================================
        await Task.Delay(300);

        var actor1 = ChatActor.GetInstance(auth1.AccountId);
        var actor2 = ChatActor.GetInstance(auth2.AccountId);

        actor1.Should().NotBeNull();
        actor2.Should().NotBeNull();
        actor1.Should().NotBeSameAs(actor2, "각 클라이언트는 독립된 Actor 인스턴스를 가져야 함");

        actor1!.OnCreateCallCount.Should().Be(1);
        actor2!.OnCreateCallCount.Should().Be(1);

        // 전체 호출 횟수 확인
        ChatActor.GetTotalOnCreateCalls().Should().BeGreaterOrEqualTo(2);
    }
}
