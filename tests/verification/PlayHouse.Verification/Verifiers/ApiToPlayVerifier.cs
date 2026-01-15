using System.Net.Http.Json;
using PlayHouse.Verification.Shared.Infrastructure;

namespace PlayHouse.Verification.Verifiers;

/// <summary>
/// API-to-Play 통신 검증 (HTTP API 기반 E2E)
///
/// E2E 원칙:
/// - HTTP 클라이언트만 사용 (HttpClient.PostAsJsonAsync)
/// - HTTP 응답만으로 검증 (CreateStageResponse, GetOrCreateStageResponse)
/// - 서버 내부 상태 접근 금지 (ApiServer.ApiSender 직접 호출 X)
/// - 클라이언트 연결 불필요 (ApiToPlay는 HTTP API만 사용)
/// </summary>
public class ApiToPlayVerifier : VerifierBase
{
    public override string CategoryName => "ApiToPlay";

    private HttpClient? _httpClient;
    private string _httpBaseUrl = null!;

    public ApiToPlayVerifier(ServerContext serverContext) : base(serverContext)
    {
    }

    public override int GetTestCount() => 3;

    protected override async Task SetupAsync()
    {
        // HTTP 클라이언트 초기화
        _httpBaseUrl = $"http://127.0.0.1:{ServerContext.ApiServer1HttpPort}";
        _httpClient = new HttpClient { BaseAddress = new Uri(_httpBaseUrl) };

        // HTTP 서버 안정화 대기
        await Task.Delay(1000);
    }

    protected override Task TeardownAsync()
    {
        // HttpClient 정리
        _httpClient?.Dispose();
        _httpClient = null;

        return Task.CompletedTask;
    }

    protected override async Task RunTestsAsync()
    {
        await RunTest("CreateStage_Success", Test_CreateStage_Success);
        await RunTest("GetOrCreateStage_NewStage", Test_GetOrCreateStage_NewStage);
        await RunTest("GetOrCreateStage_ExistingStage", Test_GetOrCreateStage_ExistingStage);
    }

    /// <summary>
    /// CreateStage - HTTP POST /api/stages 성공
    /// </summary>
    private async Task Test_CreateStage_Success()
    {
        // Given
        var stageId = GenerateUniqueStageId(10000);
        var request = new CreateStageRequest
        {
            StageType = "TestStage",
            StageId = (ushort)stageId
        };

        // When - HTTP POST /api/stages
        var response = await _httpClient!.PostAsJsonAsync("/api/stages", request);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<CreateStageResponse>();

        // Then - E2E 검증 (HTTP 응답만)
        Assert.IsTrue(result!.Success, "CreateStage should succeed");
        Assert.Equals((ushort)stageId, result.StageId);
    }

    /// <summary>
    /// GetOrCreateStage - 새 Stage 생성 (IsCreated=true)
    /// </summary>
    private async Task Test_GetOrCreateStage_NewStage()
    {
        // Given
        var stageId = GenerateUniqueStageId(11000);
        var request = new GetOrCreateStageRequest
        {
            StageType = "TestStage",
            StageId = (ushort)stageId
        };

        // When - HTTP POST /api/stages/get-or-create
        var response = await _httpClient!.PostAsJsonAsync("/api/stages/get-or-create", request);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<GetOrCreateStageResponse>();

        // Then - 새로운 Stage 생성 확인
        Assert.IsTrue(result!.Success, "GetOrCreateStage should succeed");
        Assert.IsTrue(result.IsCreated, "Stage should be newly created");
        Assert.Equals((ushort)stageId, result.StageId);
    }

    /// <summary>
    /// GetOrCreateStage - 기존 Stage 조회 (IsCreated=false)
    /// </summary>
    private async Task Test_GetOrCreateStage_ExistingStage()
    {
        // Given - 먼저 Stage 생성
        var stageId = GenerateUniqueStageId(12000);
        var createRequest = new CreateStageRequest
        {
            StageType = "TestStage",
            StageId = (ushort)stageId
        };
        var createResponse = await _httpClient!.PostAsJsonAsync("/api/stages", createRequest);
        createResponse.EnsureSuccessStatusCode();

        await Task.Delay(100); // Stage 생성 완료 대기

        // When - 동일한 StageId로 GetOrCreateStage 재호출
        var getOrCreateRequest = new GetOrCreateStageRequest
        {
            StageType = "TestStage",
            StageId = (ushort)stageId
        };
        var response = await _httpClient!.PostAsJsonAsync("/api/stages/get-or-create", getOrCreateRequest);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<GetOrCreateStageResponse>();

        // Then - 기존 Stage 반환 확인
        Assert.IsTrue(result!.Success, "GetOrCreateStage should succeed");
        Assert.IsFalse(result.IsCreated, "Stage should already exist (IsCreated=false)");
        Assert.Equals((ushort)stageId, result.StageId);
    }
}
