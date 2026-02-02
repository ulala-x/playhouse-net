using FluentAssertions;
using Xunit;

namespace PlayHouse.Connector.IntegrationTests.CoreTests;

/// <summary>
/// C-01: Stage 생성 테스트
/// </summary>
/// <remarks>
/// HTTP API를 통해 테스트 서버에 Stage를 생성할 수 있는지 검증합니다.
/// </remarks>
public class C01_StageCreationTests : BaseIntegrationTest
{
    public C01_StageCreationTests(TestServerFixture testServer) : base(testServer)
    {
    }

    [Fact(DisplayName = "C-01-01: TestStage 타입으로 Stage를 생성할 수 있다")]
    public async Task CreateStage_WithTestStageType_ReturnsValidStageInfo()
    {
        // When: TestStage 타입으로 Stage 생성
        var stageInfo = await TestServer.CreateTestStageAsync();

        // Then: Stage 정보가 올바르게 반환되어야 함
        stageInfo.Should().NotBeNull("Stage 생성 응답이 반환되어야 함");
        stageInfo.Success.Should().BeTrue("Stage 생성이 성공해야 함");
        stageInfo.StageId.Should().BeGreaterThan(0, "Stage ID는 양수여야 함");
        stageInfo.StageType.Should().Be("TestStage", "요청한 Stage 타입이어야 함");
        stageInfo.ReplyPayloadId.Should().NotBeNullOrWhiteSpace("응답 Payload가 있어야 함");
    }

    [Fact(DisplayName = "C-01-02: 커스텀 페이로드로 Stage를 생성할 수 있다")]
    public async Task CreateStage_WithCustomPayload_ReturnsValidStageInfo()
    {
        // When: 최대 플레이어 수를 지정하여 Stage 생성
        var stageInfo = await TestServer.CreateStageAsync("TestStage", maxPlayers: 10);

        // Then: Stage가 성공적으로 생성되어야 함
        stageInfo.Should().NotBeNull();
        stageInfo.StageId.Should().BeGreaterThan(0);
        stageInfo.StageType.Should().Be("TestStage");
    }

    [Fact(DisplayName = "C-01-03: 여러 개의 Stage를 생성할 수 있다")]
    public async Task CreateStage_MultipleTimes_ReturnsUniqueStageIds()
    {
        // When: 3개의 Stage 생성
        var stage1 = await TestServer.CreateTestStageAsync();
        var stage2 = await TestServer.CreateTestStageAsync();
        var stage3 = await TestServer.CreateTestStageAsync();

        // Then: 각 Stage는 고유한 ID를 가져야 함
        stage1.StageId.Should().NotBe(stage2.StageId, "첫 번째와 두 번째 Stage ID는 달라야 함");
        stage2.StageId.Should().NotBe(stage3.StageId, "두 번째와 세 번째 Stage ID는 달라야 함");
        stage1.StageId.Should().NotBe(stage3.StageId, "첫 번째와 세 번째 Stage ID는 달라야 함");

        // 모든 Stage 타입은 동일해야 함
        stage1.StageType.Should().Be("TestStage");
        stage2.StageType.Should().Be("TestStage");
        stage3.StageType.Should().Be("TestStage");
    }

    public override async Task InitializeAsync()
    {
        // 이 테스트는 Connector 초기화가 필요 없음 (HTTP API만 테스트)
        await Task.CompletedTask;
    }

    public override async Task DisposeAsync()
    {
        // 정리할 리소스 없음
        await Task.CompletedTask;
    }
}
