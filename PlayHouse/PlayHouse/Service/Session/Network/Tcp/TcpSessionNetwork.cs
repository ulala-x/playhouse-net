using System.Net.Sockets;
using NetCoreServer;
using PlayHouse.Communicator.Message;
using PlayHouse.Production.Session;
using PlayHouse.Utils;

namespace PlayHouse.Service.Session.Network.Tcp;

internal class XTcpSession(TcpServer server, ISessionDispatcher sessionDispatcher) : TcpSession(server), ISession
{
    private readonly RingBuffer _buffer = new(1024 * 4, PacketConst.MaxPacketSize);
    private readonly LOG<XTcpSession> _log = new();
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
            base.SendAsync(packet.Span);
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
            _log.Debug(() => $"TCP session OnConnected - [Sid:{GetSid()}]");
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
            _log.Debug(() => $"TCP session OnDisConnected - [Sid:{GetSid()}]");
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
                _log.Trace(() => $"OnReceive from:client - [sid:{GetSid()},packetInfo:{packet.Header}]");
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

internal class TcpSessionServer : TcpServer
{
    private readonly LOG<TcpSessionServer> _log = new();

    private readonly ISessionDispatcher _sessionDispatcher;

    public TcpSessionServer(string address, int port, ISessionDispatcher sessionDispatcher) : base(address, port)
    {
        _sessionDispatcher = sessionDispatcher;

        OptionNoDelay = true;
        OptionReuseAddress = true;
        OptionKeepAlive = true;

        OptionReceiveBufferSize = 1024 * 64;
        OptionSendBufferSize = 1024 * 256;
        OptionAcceptorBacklog = 4096 * 3;
    }

    protected override TcpSession CreateSession()
    {
        return new XTcpSession(this, _sessionDispatcher);
    }

    protected override void OnStarted()
    {
        _log.Info(() => $"Server Started");
    }
}

internal class TcpSessionNetwork(SessionOption sessionOption, ISessionDispatcher sessionDispatcher)
    : ISessionNetwork
{
    private readonly LOG<TcpSessionNetwork> _log = new();
    private readonly TcpSessionServer _tcpSessionServer = new("0.0.0.0", sessionOption.SessionPort, sessionDispatcher);

    public void Start()
    {
        if (_tcpSessionServer.Start())
        {
            _log.Info(() => $"TcpSessionNetwork Start");
        }
        else
        {
            _log.Fatal(() => $"Session Server Start Fail");
            Environment.Exit(0);
        }
    }

    public void Stop()
    {
        _log.Info(() => $"TcpSessionNetwork StopAsync");
        _tcpSessionServer.Stop();
    }
}