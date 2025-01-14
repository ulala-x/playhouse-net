using FluentAssertions;
using NetMQ;
using Org.Ulalax.Playhouse.Protocol;
using PlayHouse;
using PlayHouse.Communicator;
using PlayHouse.Communicator.Message;
using PlayHouse.Production.Session;
using PlayHouse.Service.Session.Network;
using PlayHouseConnector;
using Xunit;

namespace PlayHouseTests.Service.Session;

internal class SessionServerDispatcher : ISessionDispatcher
{
    private ISession? _session;
    public bool UseWebSocket { get; set; }
    public PooledByteBuffer Buffer { get; } = new(ConstOption.MaxPacketSize);

    public string ResultValue { get; set; } = "";

    public void OnConnect(long sid, ISession session, string remoteIp)
    {
        ResultValue = "onConnect";
        _session = session;
    }

    public void OnReceive(long sid, ClientPacket clientPacket)
    {
        Console.WriteLine($"OnReceive sid:{sid},packetInfo:{clientPacket.Header}");
        var testMsg = TestMsg.Parser.ParseFrom(clientPacket.Span);

        if (testMsg.TestMsg_ == "request")
        {
            Buffer.Clear();
            clientPacket.Header.ErrorCode = 0;
            RoutePacket.WriteClientPacketBytes(clientPacket, Buffer);

            var frame = new NetMQFrame(Buffer.Buffer(), Buffer.Count);
            clientPacket.Payload = new FramePayload(frame);


            _session!.Send(clientPacket);
        }

        ResultValue = testMsg.TestMsg_;
    }

    public void OnDisconnect(long sid)
    {
        ResultValue = "onDisconnect";
    }

    public void SendToClient(ISession session, ClientPacket packet)
    {
    }
}

[Collection("SessionNetworkTest")]
public class SessionNetworkTest
{
    public SessionNetworkTest()
    {
        PooledBuffer.Init(1024 * 1024);
    }

    [Fact]
    public async Task ClientAndSessionCommunicate()
    {
        const ushort SESSION = 1;
        const ushort API = 2;


        var useWebSocketArray = new[] { false, true };

        foreach (var useWebSocket in useWebSocketArray)
        {
            SessionServerDispatcher serverDispatcher = new() { UseWebSocket = useWebSocket };
            var port = IpFinder.FindFreePort();

            var sessionNetwork =
                new SessionNetwork(new SessionOption { UseWebSocket = useWebSocket, SessionPort = port },
                    serverDispatcher);

            var serverThread = new Thread(() =>
            {
                sessionNetwork.Start();
                sessionNetwork.Await();
            });
            serverThread.Start();

            await Task.Delay(100);

            var localIp = IpFinder.FindLocalIp();
            var connector = new Connector();
            connector.Init(new ConnectorConfig { RequestTimeoutMs = 0, Host = localIp, Port = port });


            var timer = new Timer(task => { connector.MainThreadAction(); }, null, 0, 10);


            connector.Connect();

            await Task.Delay(100);
            serverDispatcher.ResultValue.Should().Be("onConnect");


            var replyPacket =
                await connector.AuthenticateAsync(SESSION, new Packet(new TestMsg { TestMsg_ = "request" }));

            using (replyPacket)
            {
                TestMsg.Parser.ParseFrom(replyPacket.DataSpan).TestMsg_.Should().Be("request");
            }

            connector.Send(API, new Packet(new TestMsg { TestMsg_ = "test" }));
            await Task.Delay(100);


            replyPacket = await connector.RequestAsync(SESSION, new Packet(new TestMsg { TestMsg_ = "request" }));

            using (replyPacket)
            {
                TestMsg.Parser.ParseFrom(replyPacket.DataSpan).TestMsg_.Should().Be("request");
            }

            connector.Disconnect();

            await Task.Delay(100);
            serverDispatcher.ResultValue.Should().Be("onDisconnect");

            sessionNetwork.Stop();
            await timer.DisposeAsync();
        }
    }
}