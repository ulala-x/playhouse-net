using FluentAssertions;
using Org.Ulalax.Playhouse.Protocol;
using PlayHouse;
using PlayHouse.Communicator;
using PlayHouse.Communicator.Message;
using PlayHouse.Communicator.PlaySocket;
using Playhouse.Protocol;
using Xunit;

namespace PlayHouseTests.Communicator;

internal class TestListener : ICommunicateListener
{
    public List<RoutePacket> Results = new();

    public void OnReceive(RoutePacket routePacket)
    {
        Results.Add(routePacket);
    }
}

[Collection("ZSocketCommunicateTest")]
public class CommunicatorTest
{
    private const string SessionNid = "session:1";
    private const string ApiNid = "api:1";

    public CommunicatorTest()
    {
        PooledBuffer.Init();
    }

    [Fact]
    public void Should_communicate_between_Session_and_Api()
    {
        var localIp = IpFinder.FindLocalIp();

        var sessionPort = IpFinder.FindFreePort();
        var sessionEndpoint = $"tcp://{localIp}:{sessionPort}";

        var sessionServer =
            new XServerCommunicator(
                new NetMqPlaySocket(new SocketConfig(SessionNid, sessionEndpoint, new PlaySocketConfig())));
        var sessionClient =
            new XClientCommunicator(
                new NetMqPlaySocket(new SocketConfig(SessionNid, sessionEndpoint, new PlaySocketConfig())));

        var sessionListener = new TestListener();
        sessionServer.Bind(sessionListener);

        var apiPort = IpFinder.FindFreePort();
        var apiEndpoint = $"tcp://{localIp}:{apiPort}";
        var apiServer =
            new XServerCommunicator(new NetMqPlaySocket(new SocketConfig(ApiNid, apiEndpoint, new PlaySocketConfig())));
        var apiClient =
            new XClientCommunicator(new NetMqPlaySocket(new SocketConfig(ApiNid, apiEndpoint, new PlaySocketConfig())));

        var apiListener = new TestListener();
        apiServer.Bind(apiListener);

        var sessionServerThread = new Thread(() => { sessionServer.Communicate(); });

        var sessionClientThread = new Thread(() => { sessionClient.Communicate(); });

        var apiServerThread = new Thread(() => { apiServer.Communicate(); });

        var apiClientThread = new Thread(() => { apiClient.Communicate(); });

        sessionServerThread.Start();
        sessionClientThread.Start();
        apiServerThread.Start();
        apiClientThread.Start();

        ///////// session to api ///////////

        sessionClient.Connect(ApiNid, apiEndpoint);
        apiListener.Results.Clear();

        Thread.Sleep(100);

        var message = new HeaderMsg();
        sessionClient.Send(ApiNid, RoutePacket.ClientOf((ushort)ServiceType.SESSION, 0, new TestPacket(message)));

        Thread.Sleep(200);

        apiListener.Results.Count.Should().Be(1);
        apiListener.Results[0].MsgId.Should().Be(HeaderMsg.Descriptor.Name);

        ////////// api to session ///////////////

        apiClient.Connect(SessionNid, sessionEndpoint);
        sessionListener.Results.Clear();

        Thread.Sleep(100);

        //string messageId = "TestMsgId";
        var messageId = TestMsg.Descriptor.Name;
        apiClient.Send(SessionNid, RoutePacket.ClientOf((ushort)ServiceType.API, 0, new TestPacket(messageId)));

        Thread.Sleep(200);

        sessionListener.Results.Count.Should().Be(1);
        sessionListener.Results[0].MsgId.Should().Be(messageId);
    }
}