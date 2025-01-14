using System.Text;
using Google.Protobuf;
using NetMQ;
using NetMQ.Sockets;
using PlayHouse.Communicator.Message;
using Playhouse.Protocol;
using PlayHouse.Utils;

namespace PlayHouse.Communicator.PlaySocket;

internal class NetMqPlaySocket : IPlaySocket
{
    private readonly string _bindEndpoint;
    private readonly PooledByteBuffer _buffer = new(ConstOption.MaxPacketSize);
    private readonly LOG<NetMqPlaySocket> _log = new();
    private readonly string _nid;
    private readonly RouterSocket _socket = new();

    public NetMqPlaySocket(SocketConfig socketConfig)
    {
        _bindEndpoint = socketConfig.BindEndpoint;
        _nid = socketConfig.Nid;
        _socket.Options.Identity = Encoding.UTF8.GetBytes(_nid);
        _socket.Options.DelayAttachOnConnect = true; // immediate
        _socket.Options.RouterHandover = true;
        _socket.Options.Backlog = socketConfig.PlaySocketConfig.BackLog;
        _socket.Options.Linger = TimeSpan.FromMilliseconds(socketConfig.PlaySocketConfig.Linger);
        _socket.Options.TcpKeepalive = true;
        _socket.Options.SendBuffer = socketConfig.PlaySocketConfig.SendBufferSize;
        _socket.Options.ReceiveBuffer = socketConfig.PlaySocketConfig.ReceiveBufferSize;
        _socket.Options.ReceiveHighWatermark = socketConfig.PlaySocketConfig.ReceiveHighWatermark;
        _socket.Options.SendHighWatermark = socketConfig.PlaySocketConfig.SendHighWatermark;
        _socket.Options.RouterMandatory = true;
    }

    public void Bind()
    {
        if (TestOption.IsUnitTest)
        {
            return;
        }

        _socket.Bind(_bindEndpoint);
        _log.Info(() => $"socket bind - [bindEndpoint:{_bindEndpoint},nid:{_nid}]");
    }


    public void Close()
    {
        _socket.Close();
    }

    public void Connect(string endPoint)
    {
        if (TestOption.IsUnitTest)
        {
            return;
        }

        _socket.Connect(endPoint);
    }

    public void Disconnect(string endPoint)
    {
        if (TestOption.IsUnitTest)
        {
            return;
        }

        _socket.Disconnect(endPoint);
    }

    public string GetBindEndpoint()
    {
        return _bindEndpoint;
    }

    public string EndPoint()
    {
        return _bindEndpoint;
    }

    public string Nid()
    {
        return _nid;
    }

    public RoutePacket? Receive()
    {
        var message = new NetMQMessage();
        if (_socket.TryReceiveMultipartMessage(TimeSpan.FromSeconds(1), ref message))
        {
            if (message.Count() < 3)
            {
                _log.Error(() => $"message size is invalid : {message.Count()}");
                return null;
            }

            var target = Encoding.UTF8.GetString(message[0].Buffer);
            var header = RouteHeaderMsg.Parser.ParseFrom(message[1].Buffer);
            //PooledBufferPayload payload = new(new (message[2].Buffer));
            var payload = new FramePayload(message[2]);

            var routePacket = RoutePacket.Of(new RouteHeader(header), payload);
            routePacket.RouteHeader.From = target;
            return routePacket;
        }

        return null;
    }

    public void Send(string nid, RoutePacket routePacket)
    {
        if (TestOption.IsUnitTest)
        {
            return;
        }

        using (routePacket)
        {
            var message = new NetMQMessage();
            var payload = routePacket.Payload;

            NetMQFrame frame;

            _buffer.Clear();
            if (routePacket.IsToClient())
            {
                routePacket.WriteClientPacketBytes(_buffer);
                frame = new NetMQFrame(_buffer.Buffer(), _buffer.Count);
            }
            else
            {
                if (payload is FramePayload framePayload)
                {
                    frame = framePayload.Frame;
                }
                else
                {
                    _buffer.Write(payload.DataSpan);
                    frame = new NetMQFrame(_buffer.Buffer(), _buffer.Count);
                }
            }

            message.Append(new NetMQFrame(Encoding.UTF8.GetBytes(nid)));
            var routerHeaderMsg = routePacket.RouteHeader.ToMsg();

            var headerSize = routerHeaderMsg.CalculateSize();
            var headerFrame = new NetMQFrame(headerSize);
            routerHeaderMsg.WriteTo(new MemoryStream(headerFrame.Buffer));

            message.Append(headerFrame);
            message.Append(frame);

            if (!_socket.TrySendMultipartMessage(message))
            {
                _log.Error(() => $"PostAsync fail to -  [nid:{nid}, MsgName:{routePacket.MsgId}]");
            }
        }
    }
}