#nullable enable

using FluentAssertions;
using PlayHouse.Connector;
using PlayHouse.Tests.E2E.Proto;
using PlayHouse.Tests.E2E.TestFixtures;
using PlayHouse.Tests.Shared;
using Xunit;

namespace PlayHouse.Tests.E2E;

/// <summary>
/// PlayHouse E2E 테스트 - 실제 네트워크 통신 검증
///
/// PlayHouseClient(Connector)를 통해 실제 서버와 통신하며:
/// - 클라이언트 연결 → 서버에서 Actor 생성
/// - 클라이언트 메시지 전송 → Actor가 수신 및 처리
/// - Actor 응답 → 클라이언트가 수신
/// </summary>
public class ChatRoomE2ETests : IAsyncLifetime
{
    private TestServerFixture _fixture = null!;
    private TestServer _server = null!;

    public async Task InitializeAsync()
    {
        ChatStage.Reset();

        _fixture = new TestServerFixture()
            .RegisterStage<ChatStage>("chat-stage")
            .RegisterActor<ChatActor>("chat-stage");

        _server = await _fixture.StartServerAsync();
        await Task.Delay(100); // 서버 초기화 대기
    }

    public async Task DisposeAsync()
    {
        await _fixture.DisposeAsync();
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
            // Given: PlayHouseClient 생성
            await using var client = new PlayHouseClient(null, null);

            // When: 서버에 TCP 연결
            var result = await client.ConnectAsync(_server.Endpoint, "player-1-token");

            // Then: 연결 성공
            result.Success.Should().BeTrue("클라이언트가 서버에 연결되어야 함");
            client.IsConnected.Should().BeTrue();
            client.State.Should().Be(ConnectionState.Connected);

            // And: JoinStageRequest 전송하고 JoinStageReply 수신
            var joinRequest = new JoinStageRequest
            {
                AccountId = 0,
                Nickname = "TestPlayer",
                AuthToken = "player-1-token"
            };

            var joinResponse = await client.RequestAsync<JoinStageRequest, JoinStageReply>(
                joinRequest,
                TimeSpan.FromSeconds(3));

            // And: JoinStageReply 검증
            joinResponse.Success.Should().BeTrue($"ErrorCode={joinResponse.ErrorCode}, ErrorMessage={joinResponse.ErrorMessage}");
            joinResponse.ErrorCode.Should().Be(0);
            joinResponse.Data.Should().NotBeNull();
            joinResponse.Data!.AccountId.Should().BeGreaterThan(0);
            joinResponse.Data.StageId.Should().Be(1);
        }

        [Fact(DisplayName = "여러 클라이언트가 동시에 연결할 수 있습니다")]
        public async Task MultipleClients_ConnectConcurrently_AllSucceed()
        {
            // Given: 3개의 클라이언트
            var clients = new List<PlayHouseClient>
            {
                new(null, null),
                new(null, null),
                new(null, null)
            };

            try
            {
                // When: 동시에 연결
                var connectTasks = clients.Select((c, i) =>
                    c.ConnectAsync(_server.Endpoint, $"player-{i}-token")
                );
                var results = await Task.WhenAll(connectTasks);

                // Then: 모든 연결 성공
                results.Should().AllSatisfy(r => r.Success.Should().BeTrue());
                clients.Should().AllSatisfy(c => c.IsConnected.Should().BeTrue());
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
            // Given: 연결된 클라이언트
            await using var client = new PlayHouseClient(null, null);
            await client.ConnectAsync(_server.Endpoint, "echo-test-token");

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
            // Given: 연결된 클라이언트
            await using var client = new PlayHouseClient(null, null);
            await client.ConnectAsync(_server.Endpoint, "request-response-token");

            // When: Request-Response 패턴 사용
            var request = new EchoRequest { Content = "Ping" };
            var response = await client.RequestAsync<EchoRequest, EchoReply>(
                request,
                timeout: TimeSpan.FromSeconds(3)
            );

            // Then: 응답 수신
            response.Success.Should().BeTrue("Request는 반드시 응답을 받아야 함");
            response.Data.Should().NotBeNull();
            response.Data!.Content.Should().Be("Ping");
        }
    }

    /// <summary>
    /// 3. 멀티플레이어 시나리오
    /// 여러 클라이언트가 메시지를 주고받습니다.
    /// </summary>
    [Trait("Category", "Multiplayer")]
    public class MultiplayerScenarios : ChatRoomE2ETests
    {
        [Fact(DisplayName = "한 클라이언트가 보낸 ChatMessage를 다른 클라이언트가 받습니다")]
        public async Task OneClient_SendsChatMessage_OtherClientReceives()
        {
            // Given: 2명의 클라이언트가 접속
            await using var sender = new PlayHouseClient(null, null);
            await using var receiver = new PlayHouseClient(null, null);

            await sender.ConnectAsync(_server.Endpoint, "sender-token");
            await receiver.ConnectAsync(_server.Endpoint, "receiver-token");

            var messageReceived = new TaskCompletionSource<ChatMessage>();
            receiver.On<ChatMessage>(msg => messageReceived.TrySetResult(msg));

            // When: sender가 채팅 메시지 전송
            var chatMsg = new ChatMessage
            {
                SenderId = 1001,
                SenderName = "Alice",
                Message = "Hello Bob!",
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };
            await sender.SendAsync(chatMsg);

            // Then: receiver가 메시지 수신
            var received = await messageReceived.Task.WaitAsync(TimeSpan.FromSeconds(3));
            received.Message.Should().Be("Hello Bob!");
            received.SenderName.Should().Be("Alice");
            received.SenderId.Should().Be(1001);
        }

        [Fact(DisplayName = "3명의 클라이언트가 채팅방에서 메시지를 주고받습니다")]
        public async Task ThreeClients_ChatRoom_BroadcastMessages()
        {
            // Given: 3명의 클라이언트 접속
            await using var client1 = new PlayHouseClient(null, null);
            await using var client2 = new PlayHouseClient(null, null);
            await using var client3 = new PlayHouseClient(null, null);

            await client1.ConnectAsync(_server.Endpoint, "player1-token");
            await client2.ConnectAsync(_server.Endpoint, "player2-token");
            await client3.ConnectAsync(_server.Endpoint, "player3-token");

            var client2Received = new TaskCompletionSource<ChatMessage>();
            var client3Received = new TaskCompletionSource<ChatMessage>();

            client2.On<ChatMessage>(msg => client2Received.TrySetResult(msg));
            client3.On<ChatMessage>(msg => client3Received.TrySetResult(msg));

            // When: client1이 메시지 브로드캐스트
            var chatMsg = new ChatMessage
            {
                SenderId = 1,
                SenderName = "Player1",
                Message = "Hello everyone!",
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };
            await client1.SendAsync(chatMsg);

            // Then: client2, client3가 모두 수신
            var received2 = await client2Received.Task.WaitAsync(TimeSpan.FromSeconds(3));
            var received3 = await client3Received.Task.WaitAsync(TimeSpan.FromSeconds(3));

            received2.Message.Should().Be("Hello everyone!");
            received3.Message.Should().Be("Hello everyone!");
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
