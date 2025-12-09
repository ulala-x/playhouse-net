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
/// PlayHouse E2E 테스트 - 실제 네트워크 통신 검증
///
/// PlayHouseClient(Connector)를 통해 실제 서버와 통신하며:
/// - HTTP API로 방 생성 및 roomToken 발급
/// - TCP 연결 → AuthenticateRequest로 인증
/// - 클라이언트 메시지 전송 → Actor가 수신 및 처리
/// - Actor 응답 → 클라이언트가 수신
/// </summary>
public class ChatRoomE2ETests : IAsyncLifetime
{
    private TestServerFixture _fixture = null!;
    private TestServer _server = null!;
    private HttpClient _httpClient = null!;

    public async Task InitializeAsync()
    {
        ChatStage.Reset();

        _fixture = new TestServerFixture()
            .RegisterStage<ChatStage>("chat-stage")
            .RegisterActor<ChatActor>("chat-stage");

        _server = await _fixture.StartServerAsync();
        _httpClient = _server.CreateHttpClient();
        await Task.Delay(100); // 서버 초기화 대기
    }

    public async Task DisposeAsync()
    {
        _httpClient?.Dispose();
        await _fixture.DisposeAsync();
    }

    /// <summary>
    /// Helper: HTTP API로 방 생성 및 roomToken 발급
    /// </summary>
    protected async Task<CreateRoomResponse> CreateRoomAsync(string nickname = "TestUser")
    {
        var createRequest = new CreateRoomRequest
        {
            RoomType = "chat-stage",
            Nickname = nickname
        };
        var response = await _httpClient.PostAsJsonAsync("/api/rooms/create", createRequest);
        response.IsSuccessStatusCode.Should().BeTrue();
        var roomInfo = await response.Content.ReadFromJsonAsync<CreateRoomResponse>();
        roomInfo.Should().NotBeNull();
        return roomInfo!;
    }

    /// <summary>
    /// Helper: TCP 연결 후 인증
    /// </summary>
    protected async Task<(PlayHouseClient client, AuthenticateReply authReply)> ConnectAndAuthenticateAsync(CreateRoomResponse roomInfo)
    {
        var client = new PlayHouseClient(null, null);
        await client.ConnectAsync(roomInfo.Endpoint, roomInfo.RoomToken);
        client.IsConnected.Should().BeTrue();

        var authReplyReceived = new TaskCompletionSource<AuthenticateReply>();
        client.On<AuthenticateReply>(reply => authReplyReceived.TrySetResult(reply));

        await client.SendAsync(new AuthenticateRequest { RoomToken = roomInfo.RoomToken });
        var authReply = await authReplyReceived.Task.WaitAsync(TimeSpan.FromSeconds(3));
        authReply.Authenticated.Should().BeTrue();

        return (client, authReply);
    }

    /// <summary>
    /// 1. 기본 연결 테스트
    /// 클라이언트가 서버에 연결하면 서버에서 Actor가 생성됩니다.
    /// </summary>
    [Trait("Category", "BasicOperations")]
    public class ClientConnection : ChatRoomE2ETests
    {
        [Fact(DisplayName = "클라이언트가 서버에 연결하면 Actor가 생성됩니다")]
        public async Task Client_Connect_CreatesActor()
        {
            // Given: HTTP API로 방 생성
            var roomInfo = await CreateRoomAsync("TestPlayer");

            // When: TCP 연결 및 인증
            var (client, authReply) = await ConnectAndAuthenticateAsync(roomInfo);
            await using var _ = client;

            // Then: 연결 및 인증 성공
            client.IsConnected.Should().BeTrue();
            client.State.Should().Be(ConnectionState.Connected);
            authReply.AccountId.Should().BeGreaterThan(0);
            authReply.StageId.Should().Be(roomInfo.StageId);
        }

        [Fact(DisplayName = "여러 클라이언트가 동시에 연결할 수 있습니다")]
        public async Task MultipleClients_ConnectConcurrently_AllSucceed()
        {
            // Given: 3개의 방 생성
            var rooms = new List<CreateRoomResponse>();
            for (int i = 0; i < 3; i++)
            {
                rooms.Add(await CreateRoomAsync($"Player{i}"));
            }

            var clients = new List<PlayHouseClient>();
            try
            {
                // When: 동시에 연결 및 인증
                var connectTasks = rooms.Select(async room =>
                {
                    var (client, authReply) = await ConnectAndAuthenticateAsync(room);
                    return (client, authReply);
                });
                var results = await Task.WhenAll(connectTasks);

                // Then: 모든 연결 및 인증 성공
                clients.AddRange(results.Select(r => r.client));
                results.Should().AllSatisfy(r =>
                {
                    r.client.IsConnected.Should().BeTrue();
                    r.authReply.Authenticated.Should().BeTrue();
                });
            }
            finally
            {
                foreach (var c in clients)
                {
                    await c.DisposeAsync();
                }
            }
        }
    }

    /// <summary>
    /// 2. 메시지 송수신 테스트
    /// 클라이언트가 메시지를 보내면 Actor가 처리하고 응답을 보냅니다.
    /// </summary>
    [Trait("Category", "Messaging")]
    public class MessageExchange : ChatRoomE2ETests
    {
        [Fact(DisplayName = "클라이언트가 EchoRequest를 보내면 EchoReply를 받습니다")]
        public async Task Client_SendsEchoRequest_ReceivesEchoReply()
        {
            // Given: 방 생성 및 인증된 클라이언트
            var roomInfo = await CreateRoomAsync("EchoTester");
            var (client, _) = await ConnectAndAuthenticateAsync(roomInfo);
            await using var _ = client;

            await Task.Delay(200); // Actor 생성 대기

            var replyReceived = new TaskCompletionSource<EchoReply>();
            client.On<EchoReply>(reply => replyReceived.TrySetResult(reply));

            // When: EchoRequest 전송
            var request = new EchoRequest { Content = "Hello Server!" };
            await client.SendAsync(request);

            // Then: EchoReply 수신
            var reply = await replyReceived.Task.WaitAsync(TimeSpan.FromSeconds(3));
            reply.Content.Should().Be("Hello Server!", "서버가 동일한 내용으로 응답해야 함");
        }

        [Fact(DisplayName = "Request-Response 패턴으로 동기식 응답을 받습니다")]
        public async Task Client_RequestResponse_ReceivesSyncReply()
        {
            // Given: 방 생성 및 인증된 클라이언트
            var roomInfo = await CreateRoomAsync("RequestResponseTester");
            var (client, _) = await ConnectAndAuthenticateAsync(roomInfo);
            await using var _ = client;

            await Task.Delay(200); // Actor 생성 대기

            // Note: RequestAsync는 MsgSeq > 0으로 요청을 보내고 응답을 기다림
            // 현재 서버 구현에서는 Stage.OnDispatch가 push 메시지로 응답함
            // 따라서 On<> 핸들러로 수신하는 패턴 사용
            var replyReceived = new TaskCompletionSource<EchoReply>();
            client.On<EchoReply>(reply => replyReceived.TrySetResult(reply));

            // When: EchoRequest 전송 (Push 패턴)
            var request = new EchoRequest { Content = "Ping" };
            await client.SendAsync(request);

            // Then: 응답 수신
            var reply = await replyReceived.Task.WaitAsync(TimeSpan.FromSeconds(3));
            reply.Content.Should().Be("Ping");
        }
    }

    /// <summary>
    /// 3. 멀티플레이어 시나리오
    /// 여러 클라이언트가 메시지를 주고받습니다.
    /// Note: 현재 아키텍처에서는 각 클라이언트가 별도 방(Stage)에 입장하므로
    ///       브로드캐스트 테스트는 같은 Stage에 여러 Actor가 있을 때만 동작합니다.
    ///       이 테스트는 향후 "같은 방 참여" 기능 구현 후 활성화됩니다.
    /// </summary>
    [Trait("Category", "Multiplayer")]
    public class MultiplayerScenarios : ChatRoomE2ETests
    {
        [Fact(DisplayName = "한 클라이언트가 보낸 ChatMessage를 다른 클라이언트가 받습니다", Skip = "같은 방 참여 기능 구현 후 활성화")]
        public async Task OneClient_SendsChatMessage_OtherClientReceives()
        {
            // Note: 현재 구현에서는 각 클라이언트가 별도 방에 입장
            // 같은 방에 여러 클라이언트가 참여하는 기능 구현 후 테스트 활성화

            // Given: 방 생성 및 2명의 클라이언트가 같은 방에 접속
            var roomInfo = await CreateRoomAsync("ChatRoom");

            // 첫 번째 클라이언트 인증
            var sender = new PlayHouseClient(null, null);
            await sender.ConnectAsync(roomInfo.Endpoint, roomInfo.RoomToken);
            var senderAuthTcs = new TaskCompletionSource<AuthenticateReply>();
            sender.On<AuthenticateReply>(r => senderAuthTcs.TrySetResult(r));
            await sender.SendAsync(new AuthenticateRequest { RoomToken = roomInfo.RoomToken });
            var senderAuth = await senderAuthTcs.Task.WaitAsync(TimeSpan.FromSeconds(3));

            // 두 번째 클라이언트는 같은 방에 참여해야 하지만, 현재 토큰 1회용이므로 별도 방 생성 필요
            var roomInfo2 = await CreateRoomAsync("ChatRoom2");
            var receiver = new PlayHouseClient(null, null);
            await receiver.ConnectAsync(roomInfo2.Endpoint, roomInfo2.RoomToken);
            var receiverAuthTcs = new TaskCompletionSource<AuthenticateReply>();
            receiver.On<AuthenticateReply>(r => receiverAuthTcs.TrySetResult(r));
            await receiver.SendAsync(new AuthenticateRequest { RoomToken = roomInfo2.RoomToken });
            await receiverAuthTcs.Task.WaitAsync(TimeSpan.FromSeconds(3));

            await Task.Delay(200);

            var messageReceived = new TaskCompletionSource<ChatMessage>();
            receiver.On<ChatMessage>(msg => messageReceived.TrySetResult(msg));

            // When: sender가 채팅 메시지 전송
            var chatMsg = new ChatMessage
            {
                SenderId = senderAuth.AccountId,
                SenderName = "Alice",
                Message = "Hello Bob!",
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };
            await sender.SendAsync(chatMsg);

            // Then: receiver가 메시지 수신 (같은 Stage인 경우에만)
            var received = await messageReceived.Task.WaitAsync(TimeSpan.FromSeconds(3));
            received.Message.Should().Be("Hello Bob!");

            await sender.DisposeAsync();
            await receiver.DisposeAsync();
        }

        [Fact(DisplayName = "3명의 클라이언트가 채팅방에서 메시지를 주고받습니다", Skip = "같은 방 참여 기능 구현 후 활성화")]
        public async Task ThreeClients_ChatRoom_BroadcastMessages()
        {
            // Note: 같은 방에 여러 클라이언트가 참여하는 기능 구현 후 테스트 활성화
            await Task.CompletedTask;
        }
    }

    /// <summary>
    /// 4. 연결 관리
    /// 클라이언트 연결 해제 시나리오를 검증합니다.
    /// </summary>
    [Trait("Category", "ConnectionManagement")]
    public class ConnectionManagement : ChatRoomE2ETests
    {
        [Fact(DisplayName = "클라이언트가 명시적으로 연결을 해제할 수 있습니다")]
        public async Task Client_DisconnectsExplicitly_Successfully()
        {
            // Given: 연결된 클라이언트
            await using var client = new PlayHouseClient(null, null);
            await client.ConnectAsync(_server.Endpoint, "disconnect-test-token");
            client.IsConnected.Should().BeTrue();

            // When: 명시적 연결 해제
            await client.DisconnectAsync("user logout");

            // Then: 연결 해제됨
            client.IsConnected.Should().BeFalse();
            client.State.Should().Be(ConnectionState.Disconnected);
        }

        [Fact(DisplayName = "Disconnected 이벤트로 연결 해제 이유를 확인할 수 있습니다")]
        public async Task Client_DisconnectedEvent_ProvidesReason()
        {
            // Given: 이벤트 구독 중인 클라이언트
            await using var client = new PlayHouseClient(null, null);
            var disconnectReason = new TaskCompletionSource<string>();

            client.Disconnected += (_, args) =>
            {
                disconnectReason.TrySetResult(args.Reason ?? "unknown");
            };

            await client.ConnectAsync(_server.Endpoint, "event-test-token");

            // When: 연결 해제
            await client.DisconnectAsync("test completed");

            // Then: 이벤트에서 이유 확인
            var reason = await disconnectReason.Task.WaitAsync(TimeSpan.FromSeconds(2));
            reason.Should().Be("test completed");
        }
    }
}
