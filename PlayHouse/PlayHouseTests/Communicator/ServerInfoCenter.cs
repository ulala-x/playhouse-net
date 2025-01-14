using FluentAssertions;
using PlayHouse.Communicator;
using PlayHouse.Production.Shared;
using Xunit;

namespace PlayHouseTests.Communicator;

public class ServerInfoCenterFuncSpecTest
{
    private readonly long _curTime;
    private readonly XServerInfoCenter _serverInfoCenter;
    private readonly List<XServerInfo> _serverList;


    public ServerInfoCenterFuncSpecTest()
    {
        _serverInfoCenter = new XServerInfoCenter(false);
        _curTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        _serverList = new List<XServerInfo>
        {
            XServerInfo.Of("tcp://127.0.0.1:0001",
                (ushort)ServiceType.API, 1, $"{ServiceType.API}:{1}", ServiceType.API,
                ServerState.RUNNING, 1, _curTime),

            XServerInfo.Of("tcp://127.0.0.1:0002",
                (ushort)ServiceType.Play, 2, $"{ServiceType.Play}:{2}", ServiceType.Play,
                ServerState.RUNNING, 1, _curTime),

            XServerInfo.Of("tcp://127.0.0.1:0003",
                (ushort)ServiceType.SESSION, 3, $"{ServiceType.SESSION}:{3}", ServiceType.SESSION,
                ServerState.RUNNING, 1, _curTime),


            XServerInfo.Of("tcp://127.0.0.1:0011",
                (ushort)ServiceType.API, 11, $"{ServiceType.API}:{11}", ServiceType.API,
                ServerState.RUNNING, 11, _curTime),

            XServerInfo.Of("tcp://127.0.0.1:0012",
                (ushort)ServiceType.Play, 12, $"{ServiceType.Play}:{12}", ServiceType.Play,
                ServerState.RUNNING, 11, _curTime),
            XServerInfo.Of("tcp://127.0.0.1:0013",
                (ushort)ServiceType.SESSION, 13, $"{ServiceType.SESSION}:{13}", ServiceType.SESSION,
                ServerState.RUNNING, 11, _curTime),


            XServerInfo.Of("tcp://127.0.0.1:0021",
                (ushort)ServiceType.API, 21, $"{ServiceType.API}:{21}", ServiceType.API,
                ServerState.RUNNING, 1, _curTime),

            XServerInfo.Of("tcp://127.0.0.1:0022",
                (ushort)ServiceType.Play, 22, $"{ServiceType.Play}:{22}", ServiceType.Play,
                ServerState.RUNNING, 1, _curTime),

            XServerInfo.Of("tcp://127.0.0.1:0023",
                (ushort)ServiceType.SESSION, 23, $"{ServiceType.SESSION}:{23}", ServiceType.SESSION,
                ServerState.RUNNING, 1, _curTime)
        };
    }

    [Fact]
    public void RemoveInvalidServerInfoFromTheList()
    {
        var updatedList = _serverInfoCenter.Update(_serverList);

        updatedList.Should().HaveCount(_serverList.Count);

        var update = new List<XServerInfo>
        {
            XServerInfo.Of("tcp://127.0.0.1:0001",
                (ushort)ServiceType.API, 1, $"{ServiceType.API}:{1}", ServiceType.API,
                ServerState.DISABLE, 1, _curTime),

            XServerInfo.Of("tcp://127.0.0.1:0011",
                (ushort)ServiceType.API, 11, $"{ServiceType.API}:{11}", ServiceType.API,
                ServerState.RUNNING, 11, _curTime)
        };

        updatedList = _serverInfoCenter.Update(update);

        updatedList.Should().HaveCount(9);
    }

    [Fact]
    public void RemoveTimedOutServerInfoFromTheList()
    {
        ConstOption.ServerTimeLimitMs = 60000;
        _serverInfoCenter.Update(_serverList);

        var update = new List<XServerInfo>
        {
            XServerInfo.Of("tcp://127.0.0.1:0011",
                (ushort)ServiceType.API, 11, $"{ServiceType.API}:{11}", ServiceType.API,
                ServerState.RUNNING, 11, _curTime - 61000)
        };

        var updatedList = _serverInfoCenter.Update(update);

        updatedList.Should().HaveCount(9);
        updatedList.First(e => e.GetBindEndpoint() == "tcp://127.0.0.1:0011").GetState().Should()
            .Be(ServerState.DISABLE);
    }

    [Fact]
    public void ReturnTheCorrectServerInfoWhenSearchingForAnExistingServer()
    {
        _serverInfoCenter.Update(_serverList);

        var findServerNid = $"{ServiceType.API}:{21}";
        var serverInfo = _serverInfoCenter.FindServer(findServerNid);

        serverInfo.GetNid().Should().Be(findServerNid);
        serverInfo.GetState().Should().Be(ServerState.RUNNING);

        Action act = () => _serverInfoCenter.FindServer($"{ServiceType.API}:{0}");
        act.Should().Throw<CommunicatorException.NotExistServerInfo>();
    }


    [Fact]
    public void ReturnTheCorrectRoundRobinServerInfo()
    {
        _serverInfoCenter.Update(_serverList);

        // Play service should return servers in order 0012 -> 0022 -> 0002
        _serverInfoCenter.FindRoundRobinServer((ushort)ServiceType.Play).GetNid().Should()
            .Be("Play:2");
        _serverInfoCenter.FindRoundRobinServer((ushort)ServiceType.Play).GetNid().Should()
            .Be("Play:22");
        _serverInfoCenter.FindRoundRobinServer((ushort)ServiceType.Play).GetNid().Should()
            .Be("Play:12");

        // Session service should return servers in order 0013 -> 0023 -> 0003
        _serverInfoCenter.FindRoundRobinServer((ushort)ServiceType.SESSION).GetNid().Should()
            .Be("SESSION:23");
        _serverInfoCenter.FindRoundRobinServer((ushort)ServiceType.SESSION).GetNid().Should()
            .Be("SESSION:3");
        _serverInfoCenter.FindRoundRobinServer((ushort)ServiceType.SESSION).GetNid().Should()
            .Be("SESSION:13");
    }

    [Fact]
    public void ReturnTheFullListOfServerInfo()
    {
        _serverInfoCenter.Update(_serverList);
        _serverInfoCenter.GetServerList().Should().HaveCount(9);
    }
}