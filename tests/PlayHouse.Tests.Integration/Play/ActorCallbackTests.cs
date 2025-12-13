#nullable enable

using FluentAssertions;
using PlayHouse.Connector;
using PlayHouse.Connector.Protocol;
using PlayHouse.Tests.Integration.Infrastructure;
using PlayHouse.Tests.Integration.Infrastructure.Fixtures;
using PlayHouse.Tests.Integration.Proto;
using Xunit;
using ClientConnector = PlayHouse.Connector.Connector;

namespace PlayHouse.Tests.Integration.Play;

/// <summary>
/// IActor 콜백 E2E 테스트
///
/// 이 테스트는 PlayHouse의 Actor 콜백 시스템 사용법을 보여줍니다.
/// E2E 테스트 원칙:
/// - 응답 검증: Connector 공개 API로 확인
/// - 콜백 호출 검증: 테스트 구현체의 검증
/// </summary>
[Collection("E2E Connector Tests")]
public class ActorCallbackTests : IAsyncLifetime
{
    private readonly SinglePlayServerFixture _fixture;
    private readonly ClientConnector _connector;
    private Timer? _callbackTimer;
    private readonly object _callbackLock = new();

    public ActorCallbackTests(SinglePlayServerFixture fixture)
    {
        _fixture = fixture;
        _connector = new ClientConnector();
    }

    public async Task InitializeAsync()
    {
        // 콜백 자동 처리 타이머 시작
        _callbackTimer = new Timer(_ =>
        {
            lock (_callbackLock)
            {
                _connector.MainThreadAction();
            }
        }, null, 0, 20); // 20ms 간격

        await Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        _callbackTimer?.Dispose();
        _callbackTimer = null;

        _connector.Disconnect();
        await Task.CompletedTask;
    }

    #region IActor 콜백 테스트 (3개)

    /// <summary>
    /// IActor.OnAuthenticate 콜백 E2E 테스트
    ///
    /// 이 테스트는 인증 플로우에서 OnAuthenticate 콜백이 호출되는지 검증합니다.
    /// </summary>
    /// <remarks>
    /// E2E 검증 방법:
    /// - 응답 검증: IsAuthenticated() == true
    /// - 콜백 호출 검증: TestActorImpl.OnAuthenticateCallCount > 0, AuthenticatedAccountIds 확인
    /// </remarks>
    [Fact(DisplayName = "Authenticate - OnAuthenticate 콜백 호출, AccountId 설정")]
    public async Task Authenticate_CallsOnAuthenticate_SetsAccountId()
    {
        // Given - 연결된 상태
        var stageId = await ConnectOnlyAsync();

        // When - AuthenticateRequest로 인증
        var authRequest = new AuthenticateRequest
        {
            UserId = "test-user-1",
            Token = "valid-token"
        };
        using var authPacket = new Packet(authRequest);
        await _connector.AuthenticateAsync(authPacket);
        await Task.Delay(100);

        // Then - E2E 검증: 응답 검증
        _connector.IsAuthenticated().Should().BeTrue("인증이 성공해야 함");

        // Then - E2E 검증: 콜백 호출 검증
        TestActorImpl.OnAuthenticateCallCount.Should().BeGreaterThan(0,
            "OnAuthenticate 콜백이 호출되어야 함");
        TestActorImpl.AuthenticatedAccountIds.Should().NotBeEmpty(
            "AccountId가 설정되어야 함");
    }

    /// <summary>
    /// IActor.OnPostAuthenticate 콜백 E2E 테스트
    ///
    /// 이 테스트는 인증 성공 후 OnPostAuthenticate 콜백이 호출되는지 검증합니다.
    /// </summary>
    /// <remarks>
    /// E2E 검증 방법:
    /// - 응답 검증: IsAuthenticated() == true
    /// - 콜백 호출 검증: TestActorImpl.Instances에서 OnPostAuthenticateCalled 확인
    /// </remarks>
    [Fact(DisplayName = "Authenticate - OnPostAuthenticate 콜백 호출 (인증 후)")]
    public async Task Authenticate_CallsOnPostAuthenticate_AfterSuccess()
    {
        // Given - 연결된 상태
        var stageId = await ConnectOnlyAsync();

        // When - 인증 성공 후
        var authRequest = new AuthenticateRequest
        {
            UserId = "test-user-2",
            Token = "valid-token"
        };
        using var authPacket = new Packet(authRequest);
        await _connector.AuthenticateAsync(authPacket);
        await Task.Delay(100);

        // Then - E2E 검증: 응답 검증
        _connector.IsAuthenticated().Should().BeTrue("인증이 성공해야 함");

        // Then - E2E 검증: 콜백 호출 검증
        TestActorImpl.Instances.Should().Contain(a => a.OnPostAuthenticateCalled,
            "OnPostAuthenticate 콜백이 호출되어야 함");
    }

    /// <summary>
    /// IActor.OnCreate 콜백 E2E 테스트
    ///
    /// 이 테스트는 Stage 가입 시 새로운 Actor가 생성되고 OnCreate 콜백이 호출되는지 검증합니다.
    /// </summary>
    /// <remarks>
    /// E2E 검증 방법:
    /// - 응답 검증: IsAuthenticated() == true
    /// - 콜백 호출 검증: TestActorImpl.Instances에서 OnCreateCalled 확인
    /// </remarks>
    [Fact(DisplayName = "JoinStage - OnCreate 콜백 호출 (새 Actor 생성)")]
    public async Task JoinStage_CallsOnCreate_ForNewActor()
    {
        // Given - 인증된 상태 (자동으로 Stage에 Join)
        var stageId = await ConnectToServerAsync();

        // Then - E2E 검증: 콜백 호출 검증
        TestActorImpl.Instances.Should().Contain(a => a.OnCreateCalled,
            "OnCreate 콜백이 호출되어야 함");
    }

    #endregion

    #region Helper Methods

    /// <summary>
    /// 서버에 연결만 수행 (인증 X).
    /// </summary>
    private async Task<long> ConnectOnlyAsync()
    {
        var stageId = Random.Shared.NextInt64(100000, long.MaxValue);
        _connector.Init(new ConnectorConfig { RequestTimeoutMs = 30000 });
        var connected = await _connector.ConnectAsync("127.0.0.1", _fixture.PlayServer!.ActualTcpPort, stageId);
        connected.Should().BeTrue("서버에 연결되어야 함");
        await Task.Delay(100);
        return stageId;
    }

    /// <summary>
    /// 서버에 연결 및 인증 수행.
    /// </summary>
    private async Task<long> ConnectToServerAsync()
    {
        var stageId = await ConnectOnlyAsync();

        // Proto 메시지로 인증
        var authRequest = new AuthenticateRequest
        {
            UserId = "test-user",
            Token = "valid-token"
        };
        using var authPacket = new Packet(authRequest);
        await _connector.AuthenticateAsync(authPacket);
        await Task.Delay(100);
        return stageId;
    }

    #endregion
}
