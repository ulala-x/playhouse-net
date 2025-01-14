using PlayHouse.Communicator;
using PlayHouse.Communicator.PlaySocket;
using PlayHouse.Production.Session;
using PlayHouse.Production.Shared;
using PlayHouse.Service.Shared;

namespace PlayHouse.Service.Session;

public class SessionServer : IServer
{
    private readonly Communicator.Communicator _communicator;

    public SessionServer(PlayhouseOption commonOption, SessionOption sessionOption)
    {
        if (commonOption.PacketProducer == null)
        {
            commonOption.PacketProducer = (msgId, payload, msgSeq) => new EmptyPacket();
        }


        var communicatorOption = new CommunicatorOption.Builder()
            .SetIp(commonOption.Ip)
            .SetPort(commonOption.Port)
            .SetServiceProvider(commonOption.ServiceProvider)
            .SetShowQps(commonOption.ShowQps)
            .SetServerId(commonOption.ServerId)
            .SetPacketProducer(commonOption.PacketProducer)
            .Build();

        PooledBuffer.Init(commonOption.MaxBufferPoolSize);
        ConstOption.ServerTimeLimitMs = commonOption.ServerTimeLimitsMs;

        var nid = communicatorOption.Nid;
        var serviceId = commonOption.ServiceId;
        var serverId = communicatorOption.ServerId;

        var bindEndpoint = communicatorOption.BindEndpoint;
        var playSocketOption = commonOption.PlaySocketConfig;
        var communicateClient =
            new XClientCommunicator(
                PlaySocketFactory.CreatePlaySocket(new SocketConfig(nid, bindEndpoint, commonOption.PlaySocketConfig)));
        var requestCache = new RequestCache(commonOption.RequestTimeoutMSec);

        var serverInfoCenter = new XServerInfoCenter(commonOption.DebugMode);

        var sessionService = new SessionService(
            serviceId,
            serverId,
            nid,
            sessionOption,
            serverInfoCenter,
            communicateClient,
            requestCache,
            commonOption.ShowQps
        );

        _communicator = new Communicator.Communicator(
            communicatorOption,
            playSocketOption,
            requestCache,
            serverInfoCenter,
            sessionService,
            communicateClient
        );
    }

    public void Start()
    {
        _communicator.Start();
    }

    public async Task StopAsync()
    {
        await _communicator!.StopAsync();
    }

    public void AwaitTermination()
    {
        _communicator!.AwaitTermination();
    }
}