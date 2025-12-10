using FluentAssertions;
using PlayHouse.Runtime;
using Xunit;

namespace PlayHouse.Tests.Unit.Runtime;

/// <summary>
/// ServerConfig 단위 테스트
/// </summary>
public class ServerConfigTests
{
    #region NID 생성

    [Fact(DisplayName = "NID는 ServiceId:ServerId 형식으로 생성된다")]
    public void ServerConfig_WhenCreated_GeneratesCorrectNidFormat()
    {
        // Given
        const ushort serviceId = 1;
        const ushort serverId = 2;

        // When
        var config = new ServerConfig(serviceId, serverId, "tcp://*:5555");

        // Then
        config.Nid.Should().Be("1:2");
    }

    [Fact(DisplayName = "Play 서버 NID는 1:N 형식이다")]
    public void ServerConfig_ForPlayServer_GeneratesPlayNidFormat()
    {
        // Given & When
        var config = new ServerConfig(ServiceIds.Play, 1, "tcp://*:5555");

        // Then
        config.Nid.Should().Be("1:1");
        config.ServiceId.Should().Be(ServiceIds.Play);
    }

    [Fact(DisplayName = "API 서버 NID는 2:N 형식이다")]
    public void ServerConfig_ForApiServer_GeneratesApiNidFormat()
    {
        // Given & When
        var config = new ServerConfig(ServiceIds.Api, 3, "tcp://*:5556");

        // Then
        config.Nid.Should().Be("2:3");
        config.ServiceId.Should().Be(ServiceIds.Api);
    }

    #endregion

    #region 기본값 검증

    [Fact(DisplayName = "기본 타임아웃은 30초이다")]
    public void ServerConfig_WithDefaults_HasThirtySecondTimeout()
    {
        // Given & When
        var config = new ServerConfig(1, 1, "tcp://*:5555");

        // Then
        config.RequestTimeoutMs.Should().Be(30000);
    }

    [Fact(DisplayName = "기본 High Water Mark는 1000이다")]
    public void ServerConfig_WithDefaults_HasDefaultHighWatermarks()
    {
        // Given & When
        var config = new ServerConfig(1, 1, "tcp://*:5555");

        // Then
        config.SendHighWatermark.Should().Be(1000);
        config.ReceiveHighWatermark.Should().Be(1000);
    }

    [Fact(DisplayName = "TCP Keepalive는 기본적으로 활성화된다")]
    public void ServerConfig_WithDefaults_EnablesTcpKeepalive()
    {
        // Given & When
        var config = new ServerConfig(1, 1, "tcp://*:5555");

        // Then
        config.TcpKeepalive.Should().BeTrue();
    }

    #endregion

    #region Factory 메서드

    [Fact(DisplayName = "Create는 포트 번호로 바인드 주소를 생성한다")]
    public void ServerConfig_Create_GeneratesBindAddressFromPort()
    {
        // Given
        const int port = 5555;

        // When
        var config = ServerConfig.Create(ServiceIds.Play, 1, port);

        // Then
        config.BindAddress.Should().Be("tcp://*:5555");
    }

    [Fact(DisplayName = "GetConnectAddress는 호스트와 포트로 연결 주소를 생성한다")]
    public void ServerConfig_GetConnectAddress_GeneratesCorrectAddress()
    {
        // Given
        const string host = "192.168.1.100";
        const int port = 5555;

        // When
        var address = ServerConfig.GetConnectAddress(host, port);

        // Then
        address.Should().Be("tcp://192.168.1.100:5555");
    }

    #endregion

    #region 커스텀 설정

    [Fact(DisplayName = "커스텀 타임아웃을 설정할 수 있다")]
    public void ServerConfig_WithCustomTimeout_UsesProvidedValue()
    {
        // Given
        const int customTimeout = 60000;

        // When
        var config = new ServerConfig(1, 1, "tcp://*:5555", requestTimeoutMs: customTimeout);

        // Then
        config.RequestTimeoutMs.Should().Be(customTimeout);
    }

    [Fact(DisplayName = "커스텀 High Water Mark를 설정할 수 있다")]
    public void ServerConfig_WithCustomHighWatermarks_UsesProvidedValues()
    {
        // Given
        const int sendHwm = 5000;
        const int recvHwm = 3000;

        // When
        var config = new ServerConfig(1, 1, "tcp://*:5555",
            sendHighWatermark: sendHwm,
            receiveHighWatermark: recvHwm);

        // Then
        config.SendHighWatermark.Should().Be(sendHwm);
        config.ReceiveHighWatermark.Should().Be(recvHwm);
    }

    #endregion
}

/// <summary>
/// ServiceIds 상수 테스트
/// </summary>
public class ServiceIdsTests
{
    [Fact(DisplayName = "Play 서비스 ID는 1이다")]
    public void ServiceIds_Play_IsOne()
    {
        ServiceIds.Play.Should().Be(1);
    }

    [Fact(DisplayName = "API 서비스 ID는 2이다")]
    public void ServiceIds_Api_IsTwo()
    {
        ServiceIds.Api.Should().Be(2);
    }
}
