using Microsoft.Extensions.DependencyInjection;
using PlayHouse.Communicator.Message;
using PlayHouse.Communicator.PlaySocket;
using PlayHouse.Production.Shared;
using Playhouse.Protocol;
using PlayHouse.Service.Shared;
using PlayHouse.Utils;

namespace PlayHouse.Communicator;

public class CommunicatorOption(
    string bindEndpoint,
    IServiceProvider serviceProvider,
    bool showQps,
    int serverId,
    string nid,
    Func<string, IPayload, ushort, IPacket> packetProducer)
{
    public string BindEndpoint { get; } = bindEndpoint;
    public bool ShowQps { get; } = showQps;
    public int ServerId { get; } = serverId;
    public string Nid { get; } = nid;


    public IServiceProvider ServiceProvider { get; } = serviceProvider;
    public Func<string, IPayload, ushort, IPacket>? PacketProducer { get; } = packetProducer;


    public class Builder
    {
        private string _ip = string.Empty;
        private Func<string, IPayload, ushort, IPacket>? _packetProducer;
        private int _port;
        private int _serverId;
        private ushort _serviceId;
        private IServiceProvider? _serviceProvider;
        private bool _showQps;

        public Builder SetIp(string ip)
        {
            _ip = ip;
            return this;
        }

        public Builder SetPort(int port)
        {
            _port = port;
            return this;
        }


        public Builder SetShowQps(bool showQps)
        {
            _showQps = showQps;
            return this;
        }

        public Builder SetServiceProvider(IServiceProvider? serviceProvider)
        {
            _serviceProvider = serviceProvider;
            return this;
        }


        public CommunicatorOption Build()
        {
            var localIp = IpFinder.FindLocalIp();
            if (_ip != string.Empty)
            {
                localIp = _ip;
            }


            var bindEndpoint = $"tcp://{localIp}:{_port}";

            if (_serviceProvider == null)
            {
                throw new Exception("serviceProvider is not registered");
            }

            if (_packetProducer == null)
            {
                throw new Exception("packetProducer is not registered");
            }

            var nid = ISystemPanel.MakeNid(_serviceId, _serverId);

            return new CommunicatorOption(
                bindEndpoint,
                _serviceProvider!,
                _showQps,
                _serverId,
                nid,
                _packetProducer
            );
        }

        public Builder SetServerId(int serverId)
        {
            if (serverId is >= 0 and < 4096)
            {
                _serverId = serverId;
            }
            else
            {
                throw new Exception($"invalid serverId (serverId should be 0 ~ 4095 ) - [serverId:{serverId}] ");
            }

            return this;
        }

        public Builder SetPacketProducer(Func<string, IPayload, ushort, IPacket>? producer)
        {
            _packetProducer = producer;
            return this;
        }

        public Builder SetServiceId(ushort serviceId)
        {
            _serviceId = serviceId;
            return this;
        }
    }
}

internal class Communicator : ICommunicateListener
{
    private readonly ServerAddressResolver _addressResolver;
    private readonly XClientCommunicator _clientCommunicator;
    private readonly LOG<Communicator> _log = new();
    private readonly MessageLoop _messageLoop;
    private readonly CommunicatorOption _option;
    private readonly PerformanceTester _performanceTester;
    private readonly RequestCache _requestCache;

    private readonly XServerCommunicator _serverCommunicator;
    private readonly IService _service;
    private readonly ushort _serviceId;
    private readonly SystemDispatcher _systemDispatcher;
    private readonly XSystemPanel _systemPanel;

    public Communicator(
        CommunicatorOption option,
        PlaySocketConfig config,
        RequestCache requestCache,
        XServerInfoCenter serverInfoCenter,
        IService service,
        XClientCommunicator clientCommunicator
    )
    {
        _option = option;
        _requestCache = requestCache;
        _service = service;
        _clientCommunicator = clientCommunicator;
        _serviceId = _service.ServiceId;

        _serverCommunicator =
            new XServerCommunicator(
                PlaySocketFactory.CreatePlaySocket(new SocketConfig(option.Nid, option.BindEndpoint, config)));
        _performanceTester = new PerformanceTester(_option.ShowQps);
        _messageLoop = new MessageLoop(_serverCommunicator, _clientCommunicator);
        var sender = new XSender(_serviceId, _clientCommunicator, _requestCache);
        _systemPanel = new XSystemPanel(serverInfoCenter, _clientCommunicator, _option.ServerId, _option.Nid);

        var systemController = _option.ServiceProvider.GetRequiredService<ISystemController>();

        _addressResolver = new ServerAddressResolver(
            _option.BindEndpoint,
            serverInfoCenter,
            _clientCommunicator,
            _service,
            systemController
        );
        _systemDispatcher = new SystemDispatcher(_serviceId, _requestCache, _clientCommunicator, _systemPanel,
            option.ServiceProvider);

        ControlContext.Init(sender, _systemPanel);
        PacketProducer.Init(_option.PacketProducer!);
    }

    public void OnReceive(RoutePacket routePacket)
    {
        _performanceTester.IncCounter();

        Dispatch(routePacket);
        //Task.Run(async () =>  { await Dispatch(routePacket); });
    }

    public void Start()
    {
        var nid = _option.Nid;
        var bindEndpoint = _option.BindEndpoint;
        _systemPanel.Communicator = this;

        _serverCommunicator.Bind(this);

        _messageLoop.Start();


        _clientCommunicator.Connect(nid, bindEndpoint);


        _addressResolver.Start();

        _service.OnStart();
        _performanceTester.Start();
        _systemDispatcher.Start();

        _requestCache.Start();

        _log.Info(() => $"============== server start ==============");
        _log.Info(() => $"Ready for nid:{nid},bind:{bindEndpoint}");
    }

    public async Task StopAsync()
    {
        _service.OnStop();

        await Task.Delay(ConstOption.StopDelayMs);

        _performanceTester.Stop();
        _addressResolver.Stop();
        _messageLoop.Stop();
        _systemDispatcher.Stop();
        _requestCache.Stop();

        _log.Info(() => $"============== server stop ==============");
    }

    public void AwaitTermination()
    {
        _messageLoop.AwaitTermination();
    }

    private void Dispatch(RoutePacket routePacket)
    {
        try
        {
            //PacketContext.AsyncCore.Init();
            //ServiceAsyncContext.Init();

            if (routePacket.IsBackend() && routePacket.IsReply())
            {
                _requestCache.OnReply(routePacket);
                return;
            }

            if (routePacket.IsSystem)
            {
                _systemDispatcher.OnPost(routePacket);
            }
            else
            {
                _service.OnPost(routePacket);
            }
        }
        catch (ServiceException.NotRegisterMethod e)
        {
            var sender = new XSender(_serviceId, _clientCommunicator, _requestCache);
            sender.SetCurrentPacketHeader(routePacket.RouteHeader);

            if (routePacket.Header.MsgSeq > 0)
            {
                sender.Reply((ushort)BaseErrorCode.NotRegisteredMessage);
            }

            _log.Error(() => $"{e.Message}");
        }
        catch (ServiceException.NotRegisterInstance e)
        {
            var sender = new XSender(_serviceId, _clientCommunicator, _requestCache);
            sender.SetCurrentPacketHeader(routePacket.RouteHeader);

            if (routePacket.Header.MsgSeq > 0)
            {
                sender.Reply((ushort)BaseErrorCode.SystemError);
            }

            _log.Error(() => $"{e.Message}");
        }
        catch (Exception e)
        {
            var sender = new XSender(_serviceId, _clientCommunicator, _requestCache);
            sender.SetCurrentPacketHeader(routePacket.RouteHeader);
            // Use this error code when it's set in the content.
            // Use the default content error code if it's not set in the content.
            if (routePacket.Header.MsgSeq > 0)
            {
                sender.Reply((ushort)BaseErrorCode.UncheckedContentsError);
            }

            _log.Error(() => $"Packet processing failed due to an unexpected error. - [msgId:{routePacket.MsgId}]");
            _log.Error(() => $"[exception message:{e.Message}]");
            _log.Error(() => $"[exception message:{e.StackTrace}]");

            if (e.InnerException != null)
            {
                _log.Error(() => $"[internal exception message:{e.InnerException.Message}");
                _log.Error(() => $"[internal exception trace:{e.InnerException.StackTrace}");
            }
        }
    }

    public void Pause()
    {
        _service.OnPause();
    }

    public void Resume()
    {
        _service.OnResume();
    }

    public ServerState GetServerState()
    {
        return _service.GetServerState();
    }
}