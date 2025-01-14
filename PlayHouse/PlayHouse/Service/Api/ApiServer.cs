using PlayHouse.Communicator;
using PlayHouse.Communicator.PlaySocket;
using PlayHouse.Production.Api;
using PlayHouse.Production.Shared;

namespace PlayHouse.Service.Api;

public class ApiServer : IServer
{
    private readonly Communicator.Communicator? _communicator;

    public ApiServer(
        PlayhouseOption commonOption,
        ApiOption apiOption)
    {
        var communicatorOption = new CommunicatorOption.Builder()
            .SetIp(commonOption.Ip)
            .SetPort(commonOption.Port)
            .SetServiceProvider(commonOption.ServiceProvider)
            .SetShowQps(commonOption.ShowQps)
            .SetServiceId(commonOption.ServiceId)
            .SetServerId(commonOption.ServerId)
            .SetPacketProducer(commonOption.PacketProducer)
            .Build();


        PooledBuffer.Init(commonOption.MaxBufferPoolSize);
        ConstOption.ServerTimeLimitMs = commonOption.ServerTimeLimitsMs;

        var requestCache = new RequestCache(commonOption.RequestTimeoutMSec);
        var serverInfoCenter = new XServerInfoCenter(commonOption.DebugMode);

        var serviceId = commonOption.ServiceId;
        var serverId = commonOption.ServerId;
        var nid = communicatorOption.Nid;

        var bindEndpoint = communicatorOption.BindEndpoint;
        var playSocketConfig = commonOption.PlaySocketConfig;

        var communicateClient =
            new XClientCommunicator(
                PlaySocketFactory.CreatePlaySocket(new SocketConfig(nid, bindEndpoint, playSocketConfig)));

        var service = new ApiService(serviceId, serverId, nid, apiOption, requestCache, communicateClient,
            communicatorOption.ServiceProvider);

        _communicator = new Communicator.Communicator(
            communicatorOption,
            playSocketConfig,
            requestCache,
            serverInfoCenter,
            service,
            communicateClient
        );
    }

    public void Start()
    {
        _communicator!.Start();
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