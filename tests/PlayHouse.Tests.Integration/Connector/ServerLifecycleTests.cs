#nullable enable

using FluentAssertions;
using PlayHouse.Bootstrap;
using PlayHouse.Connector;
using PlayHouse.Tests.Integration.Infrastructure;
using Xunit;
using ClientConnector = PlayHouse.Connector.Connector;

namespace PlayHouse.Tests.Integration.Connector;

/// <summary>
/// 서버 생명주기 테스트.
/// 서버 종료 등 다른 테스트에 영향을 줄 수 있는 테스트는 여기서 자체 서버로 실행합니다.
/// </summary>
[Collection("E2E Server Lifecycle Tests")]
public class ServerLifecycleTests : IAsyncLifetime
{
    private PlayServer? _playServer;
    private readonly ClientConnector _connector;
    private int _disconnectCount;
    private Timer? _callbackTimer;
    private readonly object _callbackLock = new();

    public ServerLifecycleTests()
    {
        _connector = new ClientConnector();
        _connector.OnDisconnect += () => Interlocked.Increment(ref _disconnectCount);
    }

    public async Task InitializeAsync()
    {
        _playServer = new PlayServerBootstrap()
            .Configure(options =>
            {
                options.ServerId = "1";
                options.BindEndpoint = "tcp://127.0.0.1:0";
                options.TcpPort = 0;
                options.RequestTimeoutMs = 30000;
                options.AuthenticateMessageId = "AuthenticateRequest";
                options.DefaultStageType = "TestStage";
            })
            .UseStage<TestStageImpl>("TestStage")
            .UseActor<TestActorImpl>()
            .Build();

        await _playServer.StartAsync();

        // 콜백 자동 처리 타이머 시작
        _callbackTimer = new Timer(_ =>
        {
            lock (_callbackLock)
            {
                _connector.MainThreadAction();
            }
        }, null, 0, 20);
    }

    public async Task DisposeAsync()
    {
        _callbackTimer?.Dispose();
        _callbackTimer = null;

        _connector.Disconnect();
        if (_playServer != null)
        {
            await _playServer.DisposeAsync();
        }
    }

    [Fact(DisplayName = "서버 연결 해제 - OnDisconnect 콜백 발생 (HeartbeatTimeout)")]
    public async Task ServerDisconnect_OnDisconnectCallbackInvoked()
    {
        // Given - 서버에 연결된 상태 (짧은 HeartbeatTimeout 사용)
        await ConnectToServerAsync(heartbeatTimeoutMs: 500);
        _disconnectCount = 0;

        // When - 서버가 연결 종료
        await _playServer!.StopAsync();

        // 콜백 대기 - HeartbeatTimeout(500ms) + 여유 시간
        var timeout = DateTime.UtcNow.AddSeconds(3);
        while (_disconnectCount == 0 && DateTime.UtcNow < timeout)
        {
            await Task.Delay(100);
        }

        // Then - E2E 검증
        _disconnectCount.Should().BeGreaterOrEqualTo(1, "HeartbeatTimeout으로 OnDisconnect 콜백이 호출되어야 함");
    }

    #region Helper Methods

    private async Task ConnectToServerAsync(int heartbeatTimeoutMs = 30000)
    {
        var stageId = Random.Shared.NextInt64(100000, long.MaxValue);
        _connector.Init(new ConnectorConfig
        {
            RequestTimeoutMs = 30000,
            HeartbeatTimeoutMs = heartbeatTimeoutMs
        });
        var connected = await _connector.ConnectAsync("127.0.0.1", _playServer!.ActualTcpPort, stageId);
        connected.Should().BeTrue("서버에 연결되어야 함");
        await Task.Delay(100);
    }

    #endregion
}
