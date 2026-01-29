using FluentAssertions;
using PlayHouse.Abstractions;
using PlayHouse.Runtime.ServerMesh.Discovery;
using Xunit;

namespace PlayHouse.Unit.Runtime;

/// <summary>
/// XServerInfoCenter 단위 테스트 - 서버 선택 정책 검증
/// </summary>
public class XServerInfoCenterTests
{
    #region RoundRobin 정책 테스트

    [Fact(DisplayName = "GetServerByService_RoundRobin은 서버를 순환하며 선택한다")]
    public void GetServerByService_RoundRobin_ShouldRotateServers()
    {
        // Given
        var center = new XServerInfoCenter();
        center.Update(new[]
        {
            new XServerInfo(ServerType.Play, 1, "server-1", "tcp://127.0.0.1:5001", ServerState.Running, 100),
            new XServerInfo(ServerType.Play, 1, "server-2", "tcp://127.0.0.1:5002", ServerState.Running, 100),
            new XServerInfo(ServerType.Play, 1, "server-3", "tcp://127.0.0.1:5003", ServerState.Running, 100),
        });

        // When - 4번 호출
        var first = center.GetServerByService(ServerType.Play, 1, ServerSelectionPolicy.RoundRobin);
        var second = center.GetServerByService(ServerType.Play, 1, ServerSelectionPolicy.RoundRobin);
        var third = center.GetServerByService(ServerType.Play, 1, ServerSelectionPolicy.RoundRobin);
        var fourth = center.GetServerByService(ServerType.Play, 1, ServerSelectionPolicy.RoundRobin);

        // Then - 순환 확인 (4번째는 1번째와 같아야 함)
        first.Should().NotBeNull();
        second.Should().NotBeNull();
        third.Should().NotBeNull();
        fourth.Should().NotBeNull();

        // 서로 다른 서버가 선택되어야 함
        var selectedIds = new[] { first!.ServerId, second!.ServerId, third!.ServerId };
        selectedIds.Distinct().Count().Should().Be(3, "3개의 서로 다른 서버가 선택되어야 함");

        // 4번째는 다시 처음으로 돌아옴
        fourth!.ServerId.Should().Be(first.ServerId, "4번째 호출은 다시 처음 서버로 돌아가야 함");
    }

    [Fact(DisplayName = "GetServerByService 기본값은 RoundRobin이다")]
    public void GetServerByService_Default_ShouldUseRoundRobin()
    {
        // Given
        var center = new XServerInfoCenter();
        center.Update(new[]
        {
            new XServerInfo(ServerType.Play, 1, "server-1", "tcp://127.0.0.1:5001", ServerState.Running, 100),
            new XServerInfo(ServerType.Play, 1, "server-2", "tcp://127.0.0.1:5002", ServerState.Running, 100),
        });

        // When - 기본 메서드 사용 (policy 없이)
        var first = center.GetServerByService(ServerType.Play, 1);
        var second = center.GetServerByService(ServerType.Play, 1);

        // Then - RoundRobin 동작 (서로 다른 서버 선택)
        first.Should().NotBeNull();
        second.Should().NotBeNull();
        first!.ServerId.Should().NotBe(second!.ServerId, "RoundRobin은 서로 다른 서버를 순환 선택해야 함");
    }

    [Fact(DisplayName = "RoundRobin은 서비스별로 독립적으로 동작한다")]
    public void GetServerByService_RoundRobin_ShouldBeIndependentPerService()
    {
        // Given
        var center = new XServerInfoCenter();
        center.Update(new[]
        {
            new XServerInfo(ServerType.Play, 1, "play-1", "tcp://127.0.0.1:5001", ServerState.Running, 100),
            new XServerInfo(ServerType.Play, 1, "play-2", "tcp://127.0.0.1:5002", ServerState.Running, 100),
            new XServerInfo(ServerType.Api, 1, "api-1", "tcp://127.0.0.1:5003", ServerState.Running, 100),
            new XServerInfo(ServerType.Api, 1, "api-2", "tcp://127.0.0.1:5004", ServerState.Running, 100),
        });

        // When - Play 서비스에서 2번 호출
        var play1 = center.GetServerByService(ServerType.Play, 1, ServerSelectionPolicy.RoundRobin);
        var play2 = center.GetServerByService(ServerType.Play, 1, ServerSelectionPolicy.RoundRobin);

        // Api 서비스에서 2번 호출 (Play와 독립적이어야 함)
        var api1 = center.GetServerByService(ServerType.Api, 1, ServerSelectionPolicy.RoundRobin);
        var api2 = center.GetServerByService(ServerType.Api, 1, ServerSelectionPolicy.RoundRobin);

        // Then
        play1!.ServerType.Should().Be(ServerType.Play);
        play2!.ServerType.Should().Be(ServerType.Play);
        api1!.ServerType.Should().Be(ServerType.Api);
        api2!.ServerType.Should().Be(ServerType.Api);

        // 각 서비스 내에서 서로 다른 서버가 선택되어야 함
        play1.ServerId.Should().NotBe(play2.ServerId);
        api1.ServerId.Should().NotBe(api2.ServerId);
    }

    #endregion

    #region Weighted 정책 테스트

    [Fact(DisplayName = "GetServerByService_Weighted는 가장 높은 가중치를 선택한다")]
    public void GetServerByService_Weighted_ShouldSelectHighestWeight()
    {
        // Given
        var center = new XServerInfoCenter();
        center.Update(new[]
        {
            new XServerInfo(ServerType.Play, 1, "low", "tcp://127.0.0.1:5001", ServerState.Running, 10),
            new XServerInfo(ServerType.Play, 1, "high", "tcp://127.0.0.1:5002", ServerState.Running, 100),
            new XServerInfo(ServerType.Play, 1, "medium", "tcp://127.0.0.1:5003", ServerState.Running, 50),
        });

        // When
        var selected = center.GetServerByService(ServerType.Play, 1, ServerSelectionPolicy.Weighted);

        // Then - 항상 가장 높은 가중치 서버 선택
        selected.Should().NotBeNull();
        selected!.ServerId.Should().Be("high");
        selected.Weight.Should().Be(100);
    }

    [Fact(DisplayName = "GetServerByService_Weighted는 여러번 호출해도 같은 서버를 선택한다")]
    public void GetServerByService_Weighted_ShouldSelectHighestWeight_MultipleCallsConsistent()
    {
        // Given
        var center = new XServerInfoCenter();
        center.Update(new[]
        {
            new XServerInfo(ServerType.Play, 1, "server-a", "tcp://127.0.0.1:5001", ServerState.Running, 30),
            new XServerInfo(ServerType.Play, 1, "server-b", "tcp://127.0.0.1:5002", ServerState.Running, 80),
        });

        // When - 여러 번 호출
        var results = Enumerable.Range(0, 10)
            .Select(_ => center.GetServerByService(ServerType.Play, 1, ServerSelectionPolicy.Weighted))
            .ToList();

        // Then - 항상 같은 서버 (가장 높은 가중치)
        results.Should().AllSatisfy(s =>
        {
            s.Should().NotBeNull();
            s!.ServerId.Should().Be("server-b");
        });
    }

    [Fact(DisplayName = "Weighted는 동일 가중치일 경우 일관된 결과를 반환한다")]
    public void GetServerByService_Weighted_WithEqualWeights_ShouldBeConsistent()
    {
        // Given
        var center = new XServerInfoCenter();
        center.Update(new[]
        {
            new XServerInfo(ServerType.Play, 1, "server-1", "tcp://127.0.0.1:5001", ServerState.Running, 100),
            new XServerInfo(ServerType.Play, 1, "server-2", "tcp://127.0.0.1:5002", ServerState.Running, 100),
        });

        // When - 여러 번 호출
        var first = center.GetServerByService(ServerType.Play, 1, ServerSelectionPolicy.Weighted);
        var second = center.GetServerByService(ServerType.Play, 1, ServerSelectionPolicy.Weighted);
        var third = center.GetServerByService(ServerType.Play, 1, ServerSelectionPolicy.Weighted);

        // Then - 동일 가중치이므로 MaxBy 결과가 일관되어야 함 (구현에 따라 첫 번째 또는 마지막)
        first.Should().NotBeNull();
        second.Should().NotBeNull();
        third.Should().NotBeNull();
        first!.ServerId.Should().Be(second!.ServerId).And.Be(third!.ServerId);
    }

    #endregion

    #region Disabled 서버 필터링 테스트

    [Fact(DisplayName = "Disabled 서버는 선택에서 제외된다")]
    public void GetServerByService_ShouldExcludeDisabledServers()
    {
        // Given
        var center = new XServerInfoCenter();
        center.Update(new[]
        {
            new XServerInfo(ServerType.Play, 1, "disabled", "tcp://127.0.0.1:5001", ServerState.Disabled, 100),
            new XServerInfo(ServerType.Play, 1, "running", "tcp://127.0.0.1:5002", ServerState.Running, 10),
        });

        // When
        var selected = center.GetServerByService(ServerType.Play, 1, ServerSelectionPolicy.Weighted);

        // Then - Disabled 서버 제외, Running 서버 선택
        selected.Should().NotBeNull();
        selected!.ServerId.Should().Be("running");
    }

    [Fact(DisplayName = "모든 서버가 Disabled면 null을 반환한다")]
    public void GetServerByService_AllDisabled_ShouldReturnNull()
    {
        // Given
        var center = new XServerInfoCenter();
        center.Update(new[]
        {
            new XServerInfo(ServerType.Play, 1, "disabled-1", "tcp://127.0.0.1:5001", ServerState.Disabled, 100),
            new XServerInfo(ServerType.Play, 1, "disabled-2", "tcp://127.0.0.1:5002", ServerState.Disabled, 50),
        });

        // When
        var selectedRR = center.GetServerByService(ServerType.Play, 1, ServerSelectionPolicy.RoundRobin);
        var selectedWeighted = center.GetServerByService(ServerType.Play, 1, ServerSelectionPolicy.Weighted);

        // Then
        selectedRR.Should().BeNull();
        selectedWeighted.Should().BeNull();
    }

    #endregion

    #region 서버 없음 테스트

    [Fact(DisplayName = "해당 서비스에 서버가 없으면 null을 반환한다")]
    public void GetServerByService_NoAvailableServer_ShouldReturnNull()
    {
        // Given
        var center = new XServerInfoCenter();
        center.Update(new[]
        {
            new XServerInfo(ServerType.Api, 1, "api-server", "tcp://127.0.0.1:5001", ServerState.Running, 100),
        });

        // When - Play 서버 조회 (존재하지 않음)
        var selected = center.GetServerByService(ServerType.Play, 1, ServerSelectionPolicy.RoundRobin);

        // Then
        selected.Should().BeNull();
    }

    [Fact(DisplayName = "빈 서버 목록에서는 null을 반환한다")]
    public void GetServerByService_EmptyServerList_ShouldReturnNull()
    {
        // Given
        var center = new XServerInfoCenter();
        // 서버 업데이트 없음

        // When
        var selected = center.GetServerByService(ServerType.Play, 1);

        // Then
        selected.Should().BeNull();
    }

    #endregion

    #region Thread Safety 테스트

    [Fact(DisplayName = "RoundRobin은 동시 호출에서도 Thread-safe하다")]
    public void GetServerByService_RoundRobin_ShouldBeThreadSafe()
    {
        // Given
        var center = new XServerInfoCenter();
        center.Update(new[]
        {
            new XServerInfo(ServerType.Play, 1, "server-1", "tcp://127.0.0.1:5001", ServerState.Running, 100),
            new XServerInfo(ServerType.Play, 1, "server-2", "tcp://127.0.0.1:5002", ServerState.Running, 100),
            new XServerInfo(ServerType.Play, 1, "server-3", "tcp://127.0.0.1:5003", ServerState.Running, 100),
        });

        var results = new System.Collections.Concurrent.ConcurrentBag<XServerInfo?>();
        const int threadCount = 100;

        // When - 100개의 동시 호출
        Parallel.For(0, threadCount, _ =>
        {
            var server = center.GetServerByService(ServerType.Play, 1, ServerSelectionPolicy.RoundRobin);
            results.Add(server);
        });

        // Then - 모든 호출이 유효한 서버를 반환해야 함
        results.Should().HaveCount(threadCount);
        results.Should().AllSatisfy(s =>
        {
            s.Should().NotBeNull();
            s!.ServiceId.Should().Be(1);
        });

        // 대략적으로 균등 분배 확인 (완벽한 균등은 아닐 수 있음)
        var distribution = results
            .GroupBy(s => s!.ServerId)
            .ToDictionary(g => g.Key, g => g.Count());

        distribution.Should().HaveCount(3, "3개의 서버 모두 선택되어야 함");
        distribution.Values.Should().AllSatisfy(count =>
            count.Should().BeGreaterThan(10, "각 서버가 최소 10번 이상 선택되어야 함"));
    }

    #endregion
}
