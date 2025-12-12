#nullable enable

using FluentAssertions;
using PlayHouse.Abstractions.Play;
using PlayHouse.Bootstrap;
using PlayHouse.Connector;
using PlayHouse.Connector.Protocol;
using PlayHouse.Tests.E2E.Infrastructure;
using Xunit;
using ClientConnector = PlayHouse.Connector.Connector;
using ClientPacket = PlayHouse.Connector.Protocol.IPacket;

namespace PlayHouse.Tests.E2E.ConnectorTests;

/// <summary>
/// 6.1 Connector 연결/인증 E2E 테스트
///
/// E2E 테스트 원칙:
/// - Connector 공개 API만 사용하여 검증
/// - 서버 내부 상태는 검증하지 않음 (통합테스트로 이동)
/// </summary>
[Collection("E2E Connection Tests")]
public class ConnectionTests : IAsyncLifetime
{
    private PlayServer? _playServer;
    private readonly ClientConnector _connector;
    private readonly List<bool> _connectResults = new();
    private int _disconnectCount;

    private const long DefaultStageId = 12345L;

    public ConnectionTests()
    {
        _connector = new ClientConnector();
        _connector.OnConnect += result => _connectResults.Add(result);
        _connector.OnDisconnect += () => Interlocked.Increment(ref _disconnectCount);
    }

    public async Task InitializeAsync()
    {
        _playServer = new PlayServerBootstrap()
            .Configure(options =>
            {
                options.ServerId = 1;
                options.BindEndpoint = "tcp://127.0.0.1:0";
                options.TcpPort = 0;
                options.RequestTimeoutMs = 30000;
                options.AuthenticateMessageId = "AuthenticateRequest";
                // DefaultStageType 설정하지 않음 (인증만 처리)
            })
            .Build();

        await _playServer.StartAsync();
    }

    public async Task DisposeAsync()
    {
        _connector.Disconnect();
        if (_playServer != null)
        {
            await _playServer.DisposeAsync();
        }
    }

    #region 6.1.1 연결 테스트

    [Fact(DisplayName = "TCP 연결 성공 - IsConnected() == true, OnConnect(true) 콜백")]
    public async Task Connect_Success_IsConnectedTrueAndOnConnectTrue()
    {
        // Given
        _connector.Init(new ConnectorConfig());

        // When
        var result = await _connector.ConnectAsync("127.0.0.1", _playServer!.ActualTcpPort, DefaultStageId);
        await ProcessCallbacksAsync();

        // Then - E2E 검증: Connector 공개 API만 사용
        result.Should().BeTrue("ConnectAsync 반환값이 true여야 함");
        _connector.IsConnected().Should().BeTrue("IsConnected()가 true여야 함");
        _connectResults.Should().Contain(true, "OnConnect(true) 콜백이 호출되어야 함");
    }

    [Fact(DisplayName = "TCP 연결 실패 (잘못된 host) - IsConnected() == false, OnConnect(false) 콜백")]
    public async Task Connect_InvalidHost_IsConnectedFalseAndOnConnectFalse()
    {
        // Given
        _connector.Init(new ConnectorConfig());

        // When - 존재하지 않는 포트로 연결 시도
        var result = await _connector.ConnectAsync("127.0.0.1", 59999, DefaultStageId);
        await ProcessCallbacksAsync();

        // Then - E2E 검증
        result.Should().BeFalse("ConnectAsync 반환값이 false여야 함");
        _connector.IsConnected().Should().BeFalse("IsConnected()가 false여야 함");
        _connectResults.Should().Contain(false, "OnConnect(false) 콜백이 호출되어야 함");
    }

    [Fact(DisplayName = "ConnectAsync 성공 - await ConnectAsync() == true, IsConnected() == true")]
    public async Task ConnectAsync_Success_ReturnsTrueAndIsConnectedTrue()
    {
        // Given
        _connector.Init(new ConnectorConfig());

        // When
        var result = await _connector.ConnectAsync("127.0.0.1", _playServer!.ActualTcpPort, DefaultStageId);

        // Then - E2E 검증
        result.Should().BeTrue("await ConnectAsync()가 true를 반환해야 함");
        _connector.IsConnected().Should().BeTrue("IsConnected()가 true여야 함");
    }

    [Fact(DisplayName = "ConnectAsync 실패 - await ConnectAsync() == false")]
    public async Task ConnectAsync_Failure_ReturnsFalse()
    {
        // Given
        _connector.Init(new ConnectorConfig());

        // When
        var result = await _connector.ConnectAsync("127.0.0.1", 59999, DefaultStageId);

        // Then - E2E 검증
        result.Should().BeFalse("await ConnectAsync()가 false를 반환해야 함");
    }

    [Fact(DisplayName = "Disconnect 호출 - IsConnected() == false")]
    public async Task Disconnect_ByClient_IsConnectedFalse()
    {
        // Given - 서버에 연결된 상태
        await ConnectToServerAsync();
        _connector.IsConnected().Should().BeTrue();

        // When - 클라이언트가 연결 해제
        _connector.Disconnect();
        await Task.Delay(100);

        // Then - E2E 검증
        _connector.IsConnected().Should().BeFalse("Disconnect 후 IsConnected()가 false여야 함");
        // Note: 클라이언트 주도 해제 시 OnDisconnect 콜백은 호출되지 않을 수 있음
    }

    [Fact(DisplayName = "서버 연결 해제 - OnDisconnect 콜백 발생")]
    public async Task ServerDisconnect_OnDisconnectCallbackInvoked()
    {
        // Given - 서버에 연결된 상태
        await ConnectToServerAsync();
        _disconnectCount = 0;

        // When - 서버가 연결 종료
        await _playServer!.StopAsync();

        // 콜백 대기
        var timeout = DateTime.UtcNow.AddSeconds(5);
        while (_disconnectCount == 0 && DateTime.UtcNow < timeout)
        {
            await Task.Delay(50);
            _connector.MainThreadAction();
        }

        // Then - E2E 검증
        _disconnectCount.Should().BeGreaterOrEqualTo(1, "OnDisconnect 콜백이 호출되어야 함");
    }

    #endregion

    #region 6.1.2 인증 테스트

    [Fact(DisplayName = "AuthenticateAsync 성공 - 응답 패킷 내용, IsAuthenticated() == true")]
    public async Task AuthenticateAsync_Success_ResponseAndIsAuthenticatedTrue()
    {
        // Given - 연결만 된 상태 (인증 전)
        await ConnectOnlyAsync();
        _connector.IsAuthenticated().Should().BeFalse("인증 전에는 IsAuthenticated()가 false여야 함");

        // When
        using var authPacket = Packet.Empty("AuthenticateRequest");
        var response = await _connector.AuthenticateAsync(authPacket);

        // Then - E2E 검증: 응답 패킷 내용, IsAuthenticated() 상태
        response.Should().NotBeNull("인증 응답을 받아야 함");
        _connector.IsAuthenticated().Should().BeTrue("인증 후 IsAuthenticated()가 true여야 함");
    }

    [Fact(DisplayName = "Authenticate (callback) 성공 - 콜백 호출, 응답 패킷 내용, IsAuthenticated() == true")]
    public async Task Authenticate_WithCallback_CallbackInvokedAndIsAuthenticatedTrue()
    {
        // Given - 연결만 된 상태
        await ConnectOnlyAsync();

        using var authPacket = Packet.Empty("AuthenticateRequest");
        ClientPacket? authResponse = null;
        var authCompleted = new ManualResetEventSlim(false);

        // When - 콜백과 함께 인증
        _connector.Authenticate(authPacket, response =>
        {
            authResponse = response;
            authCompleted.Set();
        });

        // 콜백 대기
        var timeout = DateTime.UtcNow.AddSeconds(5);
        while (!authCompleted.IsSet && DateTime.UtcNow < timeout)
        {
            await Task.Delay(50);
            _connector.MainThreadAction();
        }

        // Then - E2E 검증
        authCompleted.IsSet.Should().BeTrue("인증 콜백이 호출되어야 함");
        authResponse.Should().NotBeNull("응답 패킷을 받아야 함");
        _connector.IsAuthenticated().Should().BeTrue("인증 후 IsAuthenticated()가 true여야 함");
    }

    #endregion

    #region Helper Methods

    private async Task ConnectOnlyAsync(long stageId = 12345L)
    {
        _connector.Init(new ConnectorConfig { RequestTimeoutMs = 30000 });
        var connected = await _connector.ConnectAsync("127.0.0.1", _playServer!.ActualTcpPort, stageId);
        connected.Should().BeTrue("서버에 연결되어야 함");
        await ProcessCallbacksAsync();
    }

    private async Task ConnectToServerAsync(long stageId = 12345L)
    {
        await ConnectOnlyAsync(stageId);

        // 인증 수행
        using var authPacket = Packet.Empty("AuthenticateRequest");
        await _connector.AuthenticateAsync(authPacket);
        await ProcessCallbacksAsync();
    }

    private async Task ProcessCallbacksAsync()
    {
        await Task.Delay(50);
        _connector.MainThreadAction();
        await Task.Delay(50);
        _connector.MainThreadAction();
    }

    #endregion
}
