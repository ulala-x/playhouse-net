using FluentAssertions;
using PlayHouse.Communicator;
using PlayHouse.Production.Shared;
using Xunit;

namespace PlayHouseTests.Communicator;

public class CacheTests
{
    private readonly string endpoint1 = "127.0.0.1:8081";


    //[Fact]
    //public void Test_ServerInfo_Update_And_Get()
    //{

    //    // Arrange
    //    RedisStorageClient redisClient = new RedisStorageClient(_redisContainer.Hostname, _redisContainer.GetMappedPublicPort(port));
    //    redisClient.Connect();


    //    // act
    //    redisClient.UpdateServerInfo(new XServerInfo(endpoint1,GetServiceType.SESSION,(ushort)GetServiceType.SESSION, ServerState.RUNNING,0,0));
    //    redisClient.UpdateServerInfo(new XServerInfo(endpoint2, GetServiceType.API, (ushort)GetServiceType.API, ServerState.RUNNING, 0, 0));

    //    // Assert
    //    List<XServerInfo> serverList = redisClient.GetServerList("");

    //    serverList.Count.Should().Be(2);
    //    serverList[0].GetState.Should().Be(ServerState.RUNNING);
    //    serverList.Should().Contain(s => s.GetBindEndpoint == endpoint1)
    //         .And.Contain(s => s.GetBindEndpoint == endpoint2);

    //}


    [Fact]
    public void Test_TimeOver()
    {
        ConstOption.ServerTimeLimitMs = 60000;
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        XServerInfo serverInfo = new(endpoint1, (ushort)ServiceType.SESSION, 1, $"{ServiceType.SESSION}:1",
            ServiceType.SESSION, ServerState.RUNNING,
            0, timestamp);

        serverInfo.TimeOver().Should().BeFalse();

        serverInfo.SetLastUpdate(timestamp - 59000);

        serverInfo.TimeOver().Should().BeFalse();

        serverInfo.SetLastUpdate(timestamp - 61000);

        serverInfo.TimeOver().Should().BeTrue();
    }
}