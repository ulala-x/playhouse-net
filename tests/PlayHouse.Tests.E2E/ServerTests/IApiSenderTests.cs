#nullable enable

using FluentAssertions;
using PlayHouse.Bootstrap;
using PlayHouse.Tests.E2E.Infrastructure;
using PlayHouse.Tests.E2E.Proto;
using Xunit;

namespace PlayHouse.Tests.E2E.ServerTests;

/// <summary>
/// IApiSender E2E 테스트
///
/// 이 테스트는 PlayHouse의 API 서버와 Play 서버 간 통신을 검증합니다.
/// - CreateStage: API 서버에서 Play 서버로 Stage 생성 요청
/// - GetOrCreateStage: API 서버에서 Play 서버로 Stage 조회/생성 요청
///
/// Note: 이 테스트는 PlayServer와 ApiServer 간 통신을 테스트합니다.
/// 현재 테스트 환경에서는 서버간 Discovery/Connection이 설정되지 않아
/// 실제 서버간 통신 테스트를 위해서는 별도의 서버 클러스터 설정이 필요합니다.
/// </summary>
[Collection("E2E IApiSender Tests")]
public class IApiSenderTests : IAsyncLifetime
{
    private PlayServer? _playServer;
    private ApiServer? _apiServer;

    public async Task InitializeAsync()
    {
        TestActorImpl.ResetAll();
        TestStageImpl.ResetAll();
        TestApiController.ResetAll();

        // PlayServer (ServiceId=1, ServerId=1)
        _playServer = new PlayServerBootstrap()
            .Configure(options =>
            {
                options.ServerId = 1;
                options.BindEndpoint = "tcp://127.0.0.1:0";
                options.TcpPort = 0;
                options.RequestTimeoutMs = 30000;
                options.AuthenticateMessageId = "AuthenticateRequest";
                options.DefaultStageType = "TestStage";
            })
            .UseStage<TestStageImpl>("TestStage")
            .UseActor<TestActorImpl>()
            .Build();

        // ApiServer (ServiceType.Api=2, ServerId=1)
        _apiServer = new ApiServerBootstrap()
            .Configure(options =>
            {
                options.ServerId = 1;
                options.BindEndpoint = "tcp://127.0.0.1:0";
                options.RequestTimeoutMs = 30000;
            })
            .UseController<TestApiController>()
            .Build();

        await _playServer.StartAsync();
        await _apiServer.StartAsync();
        await Task.Delay(200); // 서버 초기화 대기
    }

    public async Task DisposeAsync()
    {
        if (_apiServer != null)
        {
            await _apiServer.DisposeAsync();
        }
        if (_playServer != null)
        {
            await _playServer.DisposeAsync();
        }
    }

    #region CreateStage 테스트

    /// <summary>
    /// CreateStage E2E 테스트
    /// API 서버에서 PlayServer로 Stage 생성 요청을 전송합니다.
    ///
    /// Note: 이 테스트는 ApiServer → PlayServer 통신을 테스트합니다.
    /// PlayHouse 아키텍처에서 서버간 통신은 NetMQ를 통해 라우팅되며,
    /// 현재 테스트 환경에서는 서버간 Discovery/Connection이 설정되지 않아
    /// 실제 서버간 통신 테스트를 위해서는 별도의 서버 클러스터 설정이 필요합니다.
    /// </summary>
    [Fact(DisplayName = "CreateStage - API에서 Stage 생성 요청 성공", Skip = "서버간 통신은 별도 클러스터 설정 필요")]
    public async Task CreateStage_Success_StageCreated()
    {
        // Given - API 서버에 CreateStage 요청 준비
        var request = new TriggerCreateStageRequest
        {
            StageType = "TestStage",
            StageId = 12345L
        };

        // When - API 핸들러에서 CreateStage 호출
        // Note: 실제로는 HTTP/Client 요청을 통해 API 서버로 전달되어야 하지만,
        // 서버간 통신이 설정되지 않아 Skip 처리

        // Then - 검증할 사항:
        // 1. CreateStage 호출 성공
        // 2. TestStageImpl.OnCreate 콜백 호출됨
        // 3. 응답에서 success=true 확인
        await Task.CompletedTask;
    }

    #endregion

    #region GetOrCreateStage 테스트

    /// <summary>
    /// GetOrCreateStage E2E 테스트
    /// API 서버에서 PlayServer로 Stage 조회/생성 요청을 전송합니다.
    ///
    /// Note: CreateStage와 동일한 제약사항이 적용됩니다.
    /// 실제 서버간 통신 테스트를 위해서는 별도의 서버 클러스터 설정이 필요합니다.
    /// </summary>
    [Fact(DisplayName = "GetOrCreateStage - API에서 Stage 조회/생성 요청 성공", Skip = "서버간 통신은 별도 클러스터 설정 필요")]
    public async Task GetOrCreateStage_Success_StageReturned()
    {
        // Given - API 서버에 GetOrCreateStage 요청 준비
        var request = new TriggerGetOrCreateStageRequest
        {
            StageType = "TestStage",
            StageId = 12345L
        };

        // When - API 핸들러에서 GetOrCreateStage 호출
        // Note: 실제로는 HTTP/Client 요청을 통해 API 서버로 전달되어야 하지만,
        // 서버간 통신이 설정되지 않아 Skip 처리

        // Then - 검증할 사항:
        // 1. GetOrCreateStage 호출 성공
        // 2. 새 Stage인 경우: OnCreate 콜백 호출됨, is_created=true
        // 3. 기존 Stage인 경우: is_created=false
        // 4. 응답에서 success=true 확인
        await Task.CompletedTask;
    }

    #endregion
}
