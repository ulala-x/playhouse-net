#nullable enable

using FluentAssertions;
using PlayHouse.Connector;
using PlayHouse.Tests.E2E.Proto;
using PlayHouse.Tests.E2E.TestFixtures;
using PlayHouse.Tests.Shared;
using Xunit;

namespace PlayHouse.Tests.E2E;

/// <summary>
/// Bootstrap 시스템 사용 가이드 - E2E 테스트
///
/// Bootstrap API로 서버를 설정하고, PlayHouseClient로 실제 연결하여
/// 전체 시스템이 잘 동작하는지 검증합니다.
/// </summary>
public class BootstrapExampleTests : IAsyncLifetime
{
    private TestServerFixture _fixture = null!;
    private TestServer _server = null!;

    public async Task InitializeAsync()
    {
        // 테스트 상태 초기화
        ChatStage.Reset();
        ChatActor.Reset();
        await Task.Delay(10);

        // Bootstrap으로 서버 설정
        _fixture = new TestServerFixture()
            .RegisterStage<ChatStage>("chat-stage")
            .RegisterActor<ChatActor>("chat-stage");

        _server = await _fixture.StartServerAsync();
    }

    public async Task DisposeAsync()
    {
        await _fixture.DisposeAsync();
    }

    /// <summary>
    /// 1. Bootstrap 기본 사용법
    ///
    /// Bootstrap API로 서버를 시작하고 클라이언트로 연결하는 방법을 보여줍니다.
    /// </summary>
    [Trait("Category", "UsageExamples")]
    public class BasicBootstrapUsage : BootstrapExampleTests
    {
        [Fact(DisplayName = "[예제] Bootstrap으로 서버 시작 후 클라이언트 연결하기")]
        public async Task Example_BootstrapServerStartup_ConnectClient()
        {
            // Given: Bootstrap으로 설정된 서버 (InitializeAsync에서 시작)
            _server.Should().NotBeNull("서버가 시작되어야 함");
            _server.Server.Should().NotBeNull("PlayHouseServer가 초기화되어야 함");

            // When: PlayHouseClient로 연결
            await using var client = new PlayHouseClient(null, null);
            var result = await client.ConnectAsync(_server.Endpoint, "bootstrap-example-token");

            // Then: 연결 성공
            result.Success.Should().BeTrue("Bootstrap 서버에 연결할 수 있어야 함");
            client.IsConnected.Should().BeTrue();
            client.State.Should().Be(ConnectionState.Connected);
        }

        [Fact(DisplayName = "[예제] Fluent API로 Stage와 Actor 등록 후 클라이언트로 메시지 전송")]
        public async Task Example_FluentApiRegistration_SendMessage()
        {
            // Given: Fluent API로 Stage와 Actor가 등록된 서버
            await using var client = new PlayHouseClient(null, null);
            await client.ConnectAsync(_server.Endpoint, "fluent-api-token");

            // When: 메시지 전송
            var echoRequest = new EchoRequest { Content = "Fluent API works!" };
            await client.SendAsync(echoRequest);

            await Task.Delay(100); // 메시지 처리 대기

            // Then: 연결 유지됨 (메시지 전송 성공)
            client.IsConnected.Should().BeTrue();
        }
    }

    /// <summary>
    /// 2. 실무 시나리오
    ///
    /// 실제 프로젝트에서 사용할 수 있는 Bootstrap 패턴을 보여줍니다.
    /// </summary>
    [Trait("Category", "UsageExamples")]
    public class PracticalScenarios : BootstrapExampleTests
    {
        [Fact(DisplayName = "[시나리오] 멀티플레이어 게임 로비: 여러 플레이어가 동시에 접속")]
        public async Task Scenario_MultiplayerGameLobby_MultiplePlayers()
        {
            // Given: 게임 로비를 위한 Bootstrap 서버
            var lobbyFixture = new TestServerFixture()
                .RegisterStage<ChatStage>("game-lobby")
                .RegisterActor<ChatActor>("game-lobby");

            var lobbyServer = await lobbyFixture.StartServerAsync();

            // When: 4명의 플레이어가 로비에 접속
            var players = new List<PlayHouseClient>();
            var playerNames = new[] { "Player1", "Player2", "Player3", "Player4" };

            try
            {
                foreach (var name in playerNames)
                {
                    var player = new PlayHouseClient(null, null);
                    await player.ConnectAsync(lobbyServer.Endpoint, $"{name}-token");
                    players.Add(player);
                }

                // Then: 모든 플레이어가 로비에 접속됨
                players.Should().HaveCount(4);
                players.Should().AllSatisfy(p => p.IsConnected.Should().BeTrue());
            }
            finally
            {
                foreach (var player in players)
                {
                    await player.DisposeAsync();
                }
                await lobbyFixture.DisposeAsync();
            }
        }

        [Fact(DisplayName = "[시나리오] 채팅방: 플레이어들이 메시지를 주고받음", Skip = "서버 브로드캐스트 구현 대기 중")]
        public async Task Scenario_ChatRoom_PlayersExchangeMessages()
        {
            // Given: 채팅방 서버와 2명의 플레이어
            await using var sender = new PlayHouseClient(null, null);
            await using var receiver = new PlayHouseClient(null, null);

            await sender.ConnectAsync(_server.Endpoint, "sender-token");
            await receiver.ConnectAsync(_server.Endpoint, "receiver-token");

            var messageReceived = new TaskCompletionSource<ChatMessage>();
            receiver.On<ChatMessage>(msg => messageReceived.TrySetResult(msg));

            // When: 발신자가 메시지 전송
            var chatMessage = new ChatMessage
            {
                SenderId = 1001,
                SenderName = "Alice",
                Message = "Hello Bob!",
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };
            await sender.SendAsync(chatMessage);

            // Then: 수신자가 메시지를 받음
            var received = await messageReceived.Task.WaitAsync(TimeSpan.FromSeconds(2));
            received.Message.Should().Be("Hello Bob!");
            received.SenderName.Should().Be("Alice");
        }

        [Fact(DisplayName = "[시나리오] 서버 재시작: 클라이언트 재연결")]
        public async Task Scenario_ServerRestart_ClientReconnect()
        {
            // Given: 실행 중인 서버와 연결된 클라이언트
            var restartFixture = new TestServerFixture()
                .RegisterStage<ChatStage>("restart-test")
                .RegisterActor<ChatActor>("restart-test");

            var server = await restartFixture.StartServerAsync();
            var firstEndpoint = server.Endpoint;

            await using var client = new PlayHouseClient(null, null);
            await client.ConnectAsync(firstEndpoint, "restart-token");
            client.IsConnected.Should().BeTrue();

            // When: 서버 재시작
            await client.DisconnectAsync("server restarting");
            server = await restartFixture.RestartServerAsync();
            var newEndpoint = server.Endpoint;

            var reconnectResult = await client.ConnectAsync(newEndpoint, "restart-token");

            // Then: 새 엔드포인트로 재연결 성공
            reconnectResult.Success.Should().BeTrue();
            client.IsConnected.Should().BeTrue();
            newEndpoint.Should().NotBe(firstEndpoint, "재시작 시 새 포트를 사용");

            await restartFixture.DisposeAsync();
        }
    }

    /// <summary>
    /// 3. 고급 Bootstrap 패턴
    ///
    /// Bootstrap의 고급 기능과 테스트 최적화 패턴을 보여줍니다.
    /// </summary>
    [Trait("Category", "AdvancedPatterns")]
    public class AdvancedPatterns : BootstrapExampleTests
    {
        [Fact(DisplayName = "[패턴] 동적 포트 할당: 병렬 테스트 지원")]
        public async Task Pattern_DynamicPortAllocation_ParallelTests()
        {
            // Given: 동적 포트를 사용하는 3개의 독립적인 서버
            var fixtures = Enumerable.Range(0, 3)
                .Select(_ => new TestServerFixture()
                    .RegisterStage<ChatStage>("parallel-test")
                    .RegisterActor<ChatActor>("parallel-test"))
                .ToList();

            var servers = new List<TestServer>();
            var clients = new List<PlayHouseClient>();

            try
            {
                // When: 병렬로 서버 시작 및 클라이언트 연결
                foreach (var fixture in fixtures)
                {
                    var server = await fixture.StartServerAsync();
                    servers.Add(server);

                    var client = new PlayHouseClient(null, null);
                    await client.ConnectAsync(server.Endpoint, "parallel-token");
                    clients.Add(client);
                }

                // Then: 각 서버가 고유 포트를 사용하고 모든 클라이언트가 연결됨
                var ports = servers.Select(s => s.Port).ToList();
                ports.Should().OnlyHaveUniqueItems("각 서버는 고유한 포트를 가져야 함");
                clients.Should().AllSatisfy(c => c.IsConnected.Should().BeTrue());
            }
            finally
            {
                foreach (var client in clients)
                {
                    await client.DisposeAsync();
                }
                foreach (var fixture in fixtures)
                {
                    await fixture.DisposeAsync();
                }
            }
        }

        [Fact(DisplayName = "[패턴] IAsyncLifetime으로 공유 Fixture 관리")]
        public async Task Pattern_SharedFixture_IAsyncLifetime()
        {
            // Given: IAsyncLifetime으로 관리되는 공유 서버
            // (이 테스트 클래스가 이미 이 패턴을 사용 중)

            // When: 여러 클라이언트가 공유 서버에 연결
            await using var client1 = new PlayHouseClient(null, null);
            await using var client2 = new PlayHouseClient(null, null);

            await client1.ConnectAsync(_server.Endpoint, "shared-client-1");
            await client2.ConnectAsync(_server.Endpoint, "shared-client-2");

            // Then: 두 클라이언트 모두 같은 서버에 연결
            client1.IsConnected.Should().BeTrue();
            client2.IsConnected.Should().BeTrue();
        }

        [Fact(DisplayName = "[패턴] 테스트 격리: 각 테스트마다 독립적인 서버 인스턴스")]
        public async Task Pattern_TestIsolation_IndependentServerPerTest()
        {
            // Given: 이 테스트만을 위한 독립적인 서버
            var isolatedFixture = new TestServerFixture()
                .RegisterStage<ChatStage>("isolated-room")
                .RegisterActor<ChatActor>("isolated-room");

            var isolatedServer = await isolatedFixture.StartServerAsync();

            // When: 독립적인 서버에 클라이언트 연결
            await using var client = new PlayHouseClient(null, null);
            await client.ConnectAsync(isolatedServer.Endpoint, "isolated-token");

            // Then: 다른 테스트와 간섭 없이 동작
            client.IsConnected.Should().BeTrue();
            isolatedServer.Port.Should().NotBe(_server.Port, "독립적인 포트 사용");

            await isolatedFixture.DisposeAsync();
        }
    }
}
