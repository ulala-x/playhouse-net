using FluentAssertions;
using Org.Ulalax.Playhouse.Protocol;
using PlayHouse;
using PlayHouse.Communicator;
using PlayHouse.Communicator.Message;
using PlayHouse.Communicator.PlaySocket;
using Playhouse.Protocol;
using Xunit;

namespace PlayHouseTests.Communicator.Socket;

[Collection("NetMQPlaySocketTests")]
public class NetMQPlaySocketTests : IDisposable
{
    private readonly NetMqPlaySocket? _clientSocket;

    private readonly NetMqPlaySocket? _serverSocket;
    private readonly string ClientNid = "client:1";
    private readonly string ServerNid = "sever:1";

    public NetMQPlaySocketTests()
    {
        PooledBuffer.Init(1024 * 1024);

        var localIp = IpFinder.FindLocalIp();
        var serverPort = IpFinder.FindFreePort();
        var clientPort = IpFinder.FindFreePort();

        var serverBindEndpoint = $"tcp://{localIp}:{serverPort}";
        var clientBindEndpoint = $"tcp://{localIp}:{clientPort}";

        _serverSocket = new NetMqPlaySocket(new SocketConfig(ServerNid, serverBindEndpoint, new PlaySocketConfig()));
        _clientSocket = new NetMqPlaySocket(new SocketConfig(ClientNid, clientBindEndpoint, new PlaySocketConfig()));

        _serverSocket.Bind();
        _clientSocket.Bind();

        _clientSocket.Connect(serverBindEndpoint);

        Thread.Sleep(200);
    }

    public void Dispose()
    {
        _clientSocket!.Close();
        _serverSocket!.Close();
    }

    [Fact]
    public void Send_Empty_Frame()
    {
        var sendRoutePacket = RoutePacket.Of(RouteHeader.Of(new HeaderMsg()), new EmptyPayload());
        _clientSocket!.Send(ServerNid, sendRoutePacket);

        RoutePacket? recvPacket = null;
        while (recvPacket != null)
        {
            recvPacket = _serverSocket!.Receive();
        }
    }

    [Fact]
    public void Send()
    {
        var message = new TestMsg
        {
            TestMsg_ = "Hello",
            TestNumber = 27
        };

        var header = new HeaderMsg
        {
            ErrorCode = 10,
            MsgSeq = 1,
            ServiceId = (short)ServiceType.SESSION,
            MsgId = TestMsg.Descriptor.Name
        };

        var routeHeader = RouteHeader.Of(header);

        var sendRoutePacket = RoutePacket.Of(routeHeader, new ProtoPayload(message));


        _clientSocket!.Send(ServerNid, sendRoutePacket);

        RoutePacket? receiveRoutePacket = null;
        while (receiveRoutePacket == null)
        {
            receiveRoutePacket = _serverSocket!.Receive();
            Thread.Sleep(10);
        }


        receiveRoutePacket.RouteHeader.Header.ToMsg().Should().Be(header);
        receiveRoutePacket.RouteHeader.From.Should().Be(ClientNid);

        var receiveBody = TestMsg.Parser.ParseFrom(receiveRoutePacket.Span);

        receiveBody.Should().Be(message);
    }
}