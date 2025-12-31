#nullable enable

using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using PlayHouse.Abstractions;
using PlayHouse.Connector;
using PlayHouse.Connector.Protocol;
using PlayHouse.Tests.Integration.Infrastructure;
using PlayHouse.Tests.Integration.Infrastructure.Fixtures;
using PlayHouse.Tests.Integration.Proto;
using Xunit;
using ClientConnector = PlayHouse.Connector.Connector;

namespace PlayHouse.Tests.Integration.Extensions;

/// <summary>
/// DI 통합 E2E 테스트.
///
/// 이 테스트는 PlayHouse의 DI 통합 기능 사용법을 보여줍니다.
/// IServiceCollection을 통해 등록한 사용자 서비스가 Stage/Actor에 정상적으로 주입되는지 검증합니다.
/// </summary>
/// <remarks>
/// E2E 검증 방법:
/// - Stage/Actor 생성 시 DI 컨테이너에서 서비스를 주입받음
/// - Static 필드에 주입받은 인스턴스를 기록하여 검증
/// - IPlayServerControl, IServerInfoCenter 등 PlayHouse 인터페이스도 DI로 해결 가능
/// </remarks>
[Collection("E2E DI Integration")]
public class DIIntegrationTests : IAsyncLifetime
{
    private readonly DIPlayServerFixture _fixture;
    private ClientConnector? _connector;
    private Timer? _callbackTimer;
    private readonly object _callbackLock = new();

    public DIIntegrationTests(DIPlayServerFixture fixture)
    {
        _fixture = fixture;
    }

    public async Task InitializeAsync()
    {
        _connector = new ClientConnector();

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

        _connector?.Disconnect();
        await Task.CompletedTask;
    }

    /// <summary>
    /// Stage 생성 시 ITestService가 주입되는지 검증합니다.
    /// </summary>
    /// <remarks>
    /// E2E 검증 방법:
    /// 1. CreateStageRequest로 Stage 생성
    /// 2. DITestStage.InjectedServices에 ITestService가 기록됨
    /// 3. 주입받은 서비스의 GetValue() 메서드로 값 확인
    /// </remarks>
    [Fact(DisplayName = "Stage 생성 시 ITestService가 주입됨")]
    public async Task Stage_ReceivesInjectedService_WhenCreatedViaPlayProducer()
    {
        // Given - DI가 구성된 서버 (Fixture 초기화 시 이미 1개 생성됨)
        // DefaultStageType이 설정되어 있어 연결 시 자동으로 Stage가 생성됨
        var stageId = await CreateStageAsync();

        // Then - ITestService가 주입됨 (최소 1개 이상)
        DITestStage.InjectedServices.Should().HaveCountGreaterOrEqualTo(1,
            "Stage 생성 시 ITestService가 주입되어야 함");

        var injectedService = DITestStage.InjectedServices.Last();
        injectedService.Should().NotBeNull("주입된 ITestService가 null이 아니어야 함");
        injectedService.GetValue().Should().Be("DI-Injected-Value",
            "주입된 서비스의 GetValue()가 올바른 값을 반환해야 함");

        // E2E 검증: Stage에서 주입받은 값을 응답으로 받을 수 있음
        await AuthenticateAsync(stageId);
        using var request = Packet.Empty("GetDIValueRequest");
        var response = await _connector!.RequestAsync(request);

        response.MsgId.Should().Contain("EchoReply", "GetDIValueRequest에 대한 EchoReply 응답이어야 함");
        var reply = EchoReply.Parser.ParseFrom(response.Payload.DataSpan);
        reply.Content.Should().Be("DI-Injected-Value",
            "Stage에서 DI로 주입받은 값을 클라이언트가 받을 수 있어야 함");
    }

    /// <summary>
    /// Actor 생성 시 ITestService가 주입되는지 검증합니다.
    /// </summary>
    /// <remarks>
    /// E2E 검증 방법:
    /// 1. AuthenticateRequest로 Actor 생성 및 인증
    /// 2. DITestActor.InjectedServices에 ITestService가 기록됨
    /// 3. 주입받은 서비스의 GetValue() 메서드로 값 확인
    /// </remarks>
    [Fact(DisplayName = "Actor 생성 시 ITestService가 주입됨")]
    public async Task Actor_ReceivesInjectedService_WhenAuthenticated()
    {
        // Given - DI가 구성된 서버 및 Stage 생성
        var stageId = await CreateStageAsync();

        // When - Actor 생성 (인증)
        await AuthenticateAsync(stageId);

        // Then - ITestService가 주입됨 (최소 1개 이상)
        DITestActor.InjectedServices.Should().HaveCountGreaterOrEqualTo(1,
            "Actor 생성 시 ITestService가 주입되어야 함");

        var injectedService = DITestActor.InjectedServices.Last();
        injectedService.Should().NotBeNull("주입된 ITestService가 null이 아니어야 함");
        injectedService.GetValue().Should().Be("DI-Injected-Value",
            "주입된 서비스의 GetValue()가 올바른 값을 반환해야 함");

        // E2E 검증: 인증 성공 응답 확인
        _connector!.IsAuthenticated().Should().BeTrue("Actor가 정상적으로 생성되고 인증되어야 함");
    }

    /// <summary>
    /// IPlayServerControl을 DI로 해결하고 서버를 중지할 수 있는지 검증합니다.
    /// </summary>
    /// <remarks>
    /// E2E 검증 방법:
    /// 1. ServiceProvider에서 IPlayServerControl 해결
    /// 2. StopAsync() 호출 가능 여부 확인
    /// 참고: 실제 중지는 하지 않음 (Fixture에 영향)
    /// </remarks>
    [Fact(DisplayName = "IPlayServerControl로 서버 중지 가능")]
    public async Task IPlayServerControl_CanBeResolvedAndUsed()
    {
        // Given - DI가 구성된 서버
        var serviceProvider = _fixture.ServiceProvider;
        serviceProvider.Should().NotBeNull("ServiceProvider가 초기화되어야 함");

        // When - IPlayServerControl 해결
        var playServerControl = serviceProvider!.GetService<IPlayServerControl>();

        // Then - 해결 성공
        playServerControl.Should().NotBeNull("IPlayServerControl이 DI 컨테이너에 등록되어야 함");
        playServerControl.Should().BeSameAs(_fixture.PlayServer,
            "IPlayServerControl이 PlayServer와 같은 인스턴스여야 함");

        // 참고: 실제 StopAsync()는 호출하지 않음 (Fixture가 관리)
        await Task.CompletedTask;
    }

    /// <summary>
    /// IServerInfoCenter를 DI로 해결할 수 있는지 검증합니다.
    /// </summary>
    /// <remarks>
    /// E2E 검증 방법:
    /// 1. ServiceProvider에서 IServerInfoCenter 해결
    /// 2. 인터페이스가 정상적으로 등록되었는지 확인
    /// 참고: Standalone 모드에서는 ServerMesh에 등록되지 않을 수 있음
    /// </remarks>
    [Fact(DisplayName = "IServerInfoCenter를 DI로 해결 가능")]
    public async Task IServerInfoCenter_CanBeResolved()
    {
        // Given - DI가 구성된 서버
        var serviceProvider = _fixture.ServiceProvider;
        serviceProvider.Should().NotBeNull("ServiceProvider가 초기화되어야 함");

        // When - IServerInfoCenter 해결
        var serverInfoCenter = serviceProvider!.GetService<IServerInfoCenter>();

        // Then - 해결 성공
        serverInfoCenter.Should().NotBeNull("IServerInfoCenter가 DI 컨테이너에 등록되어야 함");

        // Count 속성에 접근 가능한지 확인
        var count = serverInfoCenter!.Count;
        count.Should().BeGreaterOrEqualTo(0, "Count 속성이 정상적으로 작동해야 함");

        await Task.CompletedTask;
    }

    /// <summary>
    /// 사용자 정의 서비스를 DI로 등록하고 Stage/Actor에서 사용할 수 있는지 종합 검증합니다.
    /// </summary>
    /// <remarks>
    /// E2E 검증 방법:
    /// 1. Stage 생성 → ITestService 주입 확인
    /// 2. Actor 인증 → ITestService 주입 확인
    /// 3. Stage와 Actor가 같은 싱글톤 인스턴스를 받는지 확인
    /// </remarks>
    [Fact(DisplayName = "Stage와 Actor가 동일한 싱글톤 서비스를 주입받음")]
    public async Task StageAndActor_ReceiveSameSingletonService()
    {
        // Given - DI가 구성된 서버
        DITestStage.ResetAll();
        DITestActor.ResetAll();

        // When - Stage 생성 및 Actor 인증
        var stageId = await CreateStageAsync();
        await AuthenticateAsync(stageId);

        // Then - Stage와 Actor 모두 ITestService를 주입받음
        DITestStage.InjectedServices.Should().HaveCount(1, "Stage가 ITestService를 주입받아야 함");
        DITestActor.InjectedServices.Should().HaveCount(1, "Actor가 ITestService를 주입받아야 함");

        var stageService = DITestStage.InjectedServices.First();
        var actorService = DITestActor.InjectedServices.First();

        // 싱글톤으로 등록했으므로 같은 인스턴스여야 함
        stageService.Should().BeSameAs(actorService,
            "Stage와 Actor가 같은 싱글톤 ITestService 인스턴스를 받아야 함");

        // 둘 다 같은 값을 반환해야 함
        stageService.GetValue().Should().Be("DI-Injected-Value");
        actorService.GetValue().Should().Be("DI-Injected-Value");
    }

    #region Helper Methods

    /// <summary>
    /// 서버에 연결하고 StageId를 반환합니다.
    /// </summary>
    private async Task<long> ConnectAsync()
    {
        var stageId = Random.Shared.NextInt64(100000, long.MaxValue);
        _connector!.Init(new ConnectorConfig { RequestTimeoutMs = 30000 });
        var connected = await _connector.ConnectAsync("127.0.0.1", _fixture.TcpPort, stageId);
        connected.Should().BeTrue("서버에 연결되어야 함");
        await Task.Delay(100);
        return stageId;
    }

    /// <summary>
    /// Stage를 생성하고 StageId를 반환합니다.
    /// </summary>
    private async Task<long> CreateStageAsync()
    {
        var stageId = await ConnectAsync();
        return stageId;
    }

    /// <summary>
    /// 인증을 수행합니다.
    /// </summary>
    private async Task AuthenticateAsync(long stageId)
    {
        var authRequest = new AuthenticateRequest
        {
            UserId = $"di-user-{Guid.NewGuid()}",
            Token = "test-token"
        };

        using var authPacket = new Packet(authRequest);
        await _connector!.AuthenticateAsync(authPacket);
        await Task.Delay(100);

        _connector.IsAuthenticated().Should().BeTrue("인증 후 IsAuthenticated()가 true여야 함");
    }

    #endregion
}
