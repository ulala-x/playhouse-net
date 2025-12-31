#nullable enable

using FluentAssertions;
using Microsoft.Extensions.Logging;
using PlayHouse.Bootstrap;
using PlayHouse.Core.Api.Bootstrap;
using PlayHouse.Core.Shared;
using PlayHouse.Tests.Integration.Infrastructure;
using PlayHouse.Tests.Integration.Proto;
using Xunit;

namespace PlayHouse.Tests.Integration.Api;

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
        TestSystemController.Reset();

        var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Warning));

        // PlayServer (ServiceId=1, ServerId=play-1)
        _playServer = new PlayServerBootstrap()
            .Configure(options =>
            {
                options.ServerId = "play-1";
                options.BindEndpoint = "tcp://127.0.0.1:15102"; // Fixed port for PlayServer
                options.TcpPort = 0;
                options.RequestTimeoutMs = 30000;
                options.AuthenticateMessageId = "AuthenticateRequest";
                options.DefaultStageType = "TestStage";
            })
            .UseLogger(loggerFactory.CreateLogger<PlayServer>())
            .UseStage<TestStageImpl, TestActorImpl>("TestStage")
            .UseSystemController<TestSystemController>()
            .Build();

        // ApiServer (ServiceType.Api=2, ServerId=api-1)
        _apiServer = new ApiServerBootstrap()
            .Configure(options =>
            {
                options.ServerId = "api-1";
                options.BindEndpoint = "tcp://127.0.0.1:15103"; // Fixed port for ApiServer
                options.RequestTimeoutMs = 30000;
            })
            .UseController<TestApiController>()
            .UseSystemController<TestSystemController>()
            .Build();

        await _playServer.StartAsync();
        await _apiServer.StartAsync();

        // ServerAddressResolver가 서버를 자동으로 연결할 시간을 줌
        await Task.Delay(5000);
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
    /// 테스트 플로우:
    /// 1. ApiServer.ApiSender.CreateStage() 호출
    /// 2. ApiServer → PlayServer로 ZMQ 메시지 전송
    /// 3. PlayServer에서 Stage 생성 및 OnCreate 콜백 호출
    /// 4. 응답 검증
    /// </summary>
    [Fact(DisplayName = "CreateStage - API에서 Stage 생성 요청 성공")]
    public async Task CreateStage_Success_StageCreated()
    {
        // Given
        const long stageId = 12345L;
        const string stageType = "TestStage";
        var initialInstanceCount = TestStageImpl.Instances.Count;

        // When - ApiServer에서 PlayServer로 CreateStage 요청
        var result = await _apiServer!.ApiSender!.CreateStage(
            "play-1", // PlayServer ServerId
            stageType,
            stageId,
            CPacket.Empty("CreateStagePayload"));

        // Then
        // 1. CreateStage 호출 성공
        result.Result.Should().BeTrue($"CreateStage should succeed, but got Result={result.Result}");

        // 2. TestStageImpl.OnCreate 콜백 호출됨 (새 인스턴스 생성됨)
        TestStageImpl.Instances.Count.Should().Be(initialInstanceCount + 1,
            "a new TestStageImpl instance should be created");

        var createdStage = TestStageImpl.Instances.Last();
        createdStage.OnCreateCalled.Should().BeTrue(
            "OnCreate callback should be called");

        // 3. OnPostCreate도 호출되어야 함
        createdStage.OnPostCreateCalled.Should().BeTrue(
            "OnPostCreate callback should be called");
    }

    #endregion

    #region GetOrCreateStage 테스트

    /// <summary>
    /// GetOrCreateStage E2E 테스트 - 새 Stage 생성
    /// API 서버에서 PlayServer로 Stage 조회/생성 요청을 전송합니다.
    ///
    /// 테스트 플로우:
    /// 1. 존재하지 않는 StageId로 GetOrCreateStage 호출
    /// 2. ApiServer → PlayServer로 ZMQ 메시지 전송
    /// 3. PlayServer에서 Stage 생성 및 OnCreate 콜백 호출
    /// 4. IsCreated=true 응답 검증
    /// </summary>
    [Fact(DisplayName = "GetOrCreateStage - 새 Stage 생성 성공")]
    public async Task GetOrCreateStage_NewStage_Created()
    {
        // Given
        const long stageId = 99999L;
        const string stageType = "TestStage";
        var initialInstanceCount = TestStageImpl.Instances.Count;

        // When - 존재하지 않는 Stage 조회/생성
        var result = await _apiServer!.ApiSender!.GetOrCreateStage(
            "play-1", // PlayServer ServerId
            stageType,
            stageId,
            CPacket.Empty("CreatePayload"),
            CPacket.Empty("JoinPayload"));

        // Then
        // 1. GetOrCreateStage 호출 성공
        result.Result.Should().BeTrue("GetOrCreateStage should succeed");

        // 2. 새 Stage가 생성됨
        result.IsCreated.Should().BeTrue("Stage should be newly created");

        // 3. TestStageImpl.OnCreate 콜백 호출됨
        TestStageImpl.Instances.Count.Should().Be(initialInstanceCount + 1,
            "a new TestStageImpl instance should be created");

        var createdStage = TestStageImpl.Instances.Last();
        createdStage.OnCreateCalled.Should().BeTrue(
            "OnCreate callback should be called");
    }

    /// <summary>
    /// GetOrCreateStage E2E 테스트 - 기존 Stage 조회
    /// API 서버에서 PlayServer로 기존 Stage 조회 요청을 전송합니다.
    ///
    /// 테스트 플로우:
    /// 1. CreateStage로 Stage 미리 생성
    /// 2. 동일 StageId로 GetOrCreateStage 호출
    /// 3. IsCreated=false 응답 검증 (기존 Stage 반환됨)
    /// </summary>
    [Fact(DisplayName = "GetOrCreateStage - 기존 Stage 조회 성공")]
    public async Task GetOrCreateStage_ExistingStage_Returned()
    {
        // Given - Stage 미리 생성
        const long stageId = 88888L;
        const string stageType = "TestStage";

        var createResult = await _apiServer!.ApiSender!.CreateStage(
            "play-1",
            stageType,
            stageId,
            CPacket.Empty("CreatePayload"));

        createResult.Result.Should().BeTrue("CreateStage should succeed first");

        var instanceCountAfterCreate = TestStageImpl.Instances.Count;

        // When - 동일 StageId로 GetOrCreateStage 호출
        var result = await _apiServer.ApiSender.GetOrCreateStage(
            "play-1",
            stageType,
            stageId,
            CPacket.Empty("CreatePayload2"),
            CPacket.Empty("JoinPayload"));

        // Then
        // 1. GetOrCreateStage 호출 성공
        result.Result.Should().BeTrue("GetOrCreateStage should succeed");

        // 2. 기존 Stage 반환됨 (새로 생성되지 않음)
        result.IsCreated.Should().BeFalse("Stage should already exist");

        // 3. 새 인스턴스가 생성되지 않음
        TestStageImpl.Instances.Count.Should().Be(instanceCountAfterCreate,
            "no new instance should be created");
    }

    #endregion
}
