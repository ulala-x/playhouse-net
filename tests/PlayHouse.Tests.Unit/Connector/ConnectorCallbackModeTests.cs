#nullable enable

using FluentAssertions;
using PlayHouse.Connector;
using Xunit;
using ClientConnector = PlayHouse.Connector.Connector;

namespace PlayHouse.Tests.Unit.Connector;

/// <summary>
/// 단위 테스트: Connector 콜백 실행 모드 검증
/// </summary>
public class ConnectorCallbackModeTests
{
    [Fact(DisplayName = "ConnectorConfig - UseMainThreadCallback 기본값은 false")]
    public void ConnectorConfig_UseMainThreadCallback_DefaultIsFalse()
    {
        // Given (전제조건)
        var config = new ConnectorConfig();

        // Then (결과)
        config.UseMainThreadCallback.Should().BeFalse(
            "기본값은 false (즉시 실행 모드)로 고성능 시나리오에 최적화되어야 함");
    }

    [Fact(DisplayName = "ConnectorConfig - UseMainThreadCallback을 true로 설정 가능")]
    public void ConnectorConfig_UseMainThreadCallback_CanSetTrue()
    {
        // Given (전제조건)
        var config = new ConnectorConfig
        {
            UseMainThreadCallback = true  // Unity 모드
        };

        // Then (결과)
        config.UseMainThreadCallback.Should().BeTrue(
            "Unity 프로젝트에서는 true로 설정하여 메인 스레드 큐 사용");
    }

    [Fact(DisplayName = "Connector - UseMainThreadCallback = false 설정으로 초기화")]
    public void Connector_Init_WithUseMainThreadCallbackFalse()
    {
        // Given (전제조건)
        var connector = new ClientConnector();
        var config = new ConnectorConfig
        {
            UseMainThreadCallback = false
        };

        // When (행동)
        connector.Init(config);

        // Then (결과)
        connector.ConnectorConfig.UseMainThreadCallback.Should().BeFalse(
            "콜백 즉시 실행 모드로 설정되어야 함");
    }

    [Fact(DisplayName = "Connector - UseMainThreadCallback = true 설정으로 초기화")]
    public void Connector_Init_WithUseMainThreadCallbackTrue()
    {
        // Given (전제조건)
        var connector = new ClientConnector();
        var config = new ConnectorConfig
        {
            UseMainThreadCallback = true
        };

        // When (행동)
        connector.Init(config);

        // Then (결과)
        connector.ConnectorConfig.UseMainThreadCallback.Should().BeTrue(
            "메인 스레드 큐 모드로 설정되어야 함");
    }
}
