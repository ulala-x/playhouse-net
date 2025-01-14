using System.Net.Sockets;
using NetCoreServer;
using PlayHouse.Communicator.Message;
using PlayHouse.Production.Session;
using PlayHouse.Utils;

namespace PlayHouse.Service.Session.Network.Websocket;

internal class XWsSession(WsSessionServer server, ISessionDispatcher sessionDispatcher) : WsSession(server), ISession
{
    private readonly RingBuffer _buffer = new(1024 * 4, PacketConst.MaxPacketSize);
    private readonly LOG<XWsSession> _log = new();
    private readonly PacketParser _packetParser = new();
    private readonly long _sid = SocketIdGenerator.IdGenerator.NextId();

    public void ClientDisconnect()
    {
        base.Disconnect();
    }

    public void Send(ClientPacket packet)
    {
        using (packet)
        {
            base.Send(packet.Span);
        }
    }

    private long GetSid()
    {
        return _sid;
    }

    protected override void OnConnecting()
    {
        try
        {
            _log.Debug(() => $"WS session OnConnected - [Sid:{GetSid()}]");
            var remoteEndpoint = Socket.RemoteEndPoint?.ToString() ?? string.Empty;
            sessionDispatcher.OnConnect(GetSid(), this, remoteEndpoint);
        }
        catch (Exception e)
        {
            _log.Error(() => $"{e}");
        }
    }

    protected override void OnDisconnected()
    {
        try
        {
            _log.Debug(() => $"WS session OnDisConnected - [Sid:{GetSid()}]");
            sessionDispatcher.OnDisconnect(GetSid());
        }
        catch (Exception e)
        {
            _log.Error(() => $"{e}");
        }
    }

    protected override void OnReceived(byte[] buffer, long offset, long size)
    {
        try
        {
            _buffer.Write(buffer, offset, size);
            var packets = _packetParser.Parse(_buffer);

            foreach (var packet in packets)
            {
                _log.Trace(() => $"OnReceive from:client - [packetInfo:{packet.Header}]");
                sessionDispatcher.OnReceive(GetSid(), packet);
            }
        }
        catch (Exception e)
        {
            _log.Error(() => $"{e}");
            Disconnect();
        }
    }

    protected override void OnError(SocketError error)
    {
        try
        {
            _log.Error(() => $"socket caught an error - [codeCode:{error}]");
            Disconnect();
        }
        catch (Exception e)
        {
            _log.Error(() => $"{e}");
        }
    }
}

internal class WsSessionServer : WsServer
{
    private readonly ISessionDispatcher _sessionDispatcher;

    public WsSessionServer(string address, int port, ISessionDispatcher sessionDispatcher) : base(address, port)
    {
        _sessionDispatcher = sessionDispatcher;

        OptionNoDelay = true;
        OptionReuseAddress = true;
        OptionKeepAlive = true;

        OptionReceiveBufferSize = 1024 * 64;
        OptionSendBufferSize = 1024 * 256;
        OptionAcceptorBacklog = 4096 * 3;
    }

    protected override WsSession CreateSession()
    {
        return new XWsSession(this, _sessionDispatcher);
    }
}

internal class WsSessionNetwork(SessionOption sessionOption, ISessionDispatcher sessionDispatcher)
    : ISessionNetwork
{
    private readonly LOG<WsSessionNetwork> _log = new();
    private readonly WsSessionServer _wsSessionServer = new("0.0.0.0", sessionOption.SessionPort, sessionDispatcher);

    public void Start()
    {
        if (_wsSessionServer.Start())
        {
            _log.Info(() => $"WsSessionNetwork Start");
        }
        else
        {
            _log.Fatal(() => $"WsSessionNetwork Start Fail");
            Environment.Exit(0);
        }
    }

    public void Stop()
    {
        _log.Info(() => $"WsSessionNetwork StopAsync");
        _wsSessionServer.Stop();
    }
}