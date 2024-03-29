﻿using CommonLib;
using Google.Protobuf;
using NetMQ;
using NetMQ.Sockets;
using Playhouse.Protocol;
using PlayHouse.Communicator.Message;
using System.Text;
using PlayHouse.Utils;

namespace PlayHouse.Communicator.PlaySocket;
internal class NetMQPlaySocket : IPlaySocket
{
    private readonly RouterSocket _socket = new();
    private readonly string _bindEndpoint;
    private readonly PooledByteBuffer _buffer = new PooledByteBuffer(ConstOption.MaxPacketSize);
    private readonly LOG<NetMQPlaySocket> _log = new ();

    public NetMQPlaySocket(SocketConfig socketConfig,string bindEndpoint)
    {
        _bindEndpoint = bindEndpoint;

        _socket.Options.Identity = Encoding.UTF8.GetBytes(_bindEndpoint);
        _socket.Options.DelayAttachOnConnect = true; // immediate
        _socket.Options.RouterHandover = true;
        _socket.Options.Backlog = socketConfig.BackLog;
        _socket.Options.Linger = TimeSpan.FromMilliseconds(socketConfig.Linger);
        _socket.Options.TcpKeepalive = true;
        _socket.Options.SendBuffer = socketConfig.SendBufferSize;
        _socket.Options.ReceiveBuffer = socketConfig.ReceiveBufferSize;
        _socket.Options.ReceiveHighWatermark = socketConfig.ReceiveHighWatermark;
        _socket.Options.SendHighWatermark = socketConfig.SendHighWatermark;
        _socket.Options.RouterMandatory = true;

        
    }

    public void Bind()
    {
        _socket.Bind(_bindEndpoint);
        _log.Info(()=>$"socket bind {_bindEndpoint}");
    }

    public void Close()
    {
        _socket.Close();
    }

    public void Connect(string endpoint)
    {
        _socket.Connect(endpoint);
    }

    public void Disconnect(string endpoint)
    {
        _socket.Disconnect(endpoint);
    }

    public string GetBindEndpoint()
    {
        return _bindEndpoint;
    }

    public string Id()
    {
        return _bindEndpoint;
    }

    public RoutePacket? Receive()
    {
        NetMQMessage? message = new NetMQMessage();
        if(_socket.TryReceiveMultipartMessage(ref message))
        {
            if(message.Count() < 3)
            {
                _log.Error(()=>$"message size is invalid : {message.Count()}");
                return null;
            }

            String target = Encoding.UTF8.GetString(message[0].Buffer);
            RouteHeaderMsg header = RouteHeaderMsg.Parser.ParseFrom(message[1].Buffer);
            //PooledBufferPayload payload = new(new (message[2].Buffer));
            FramePayload payload = new FramePayload(message[2]); 

            var routePacket = RoutePacket.Of(new RouteHeader(header),payload);
            routePacket.RouteHeader.From = target;
            return routePacket;
        }
        return null;
    }

    public void Send(string endpoint, RoutePacket routePacket)
    {
        
        using (routePacket)
        {
            NetMQMessage message = new NetMQMessage();
            IPayload payload = routePacket.Payload;

            NetMQFrame frame;
            
            _buffer.Clear();
            if (routePacket.IsToClient())
            {
                routePacket.WriteClientPacketBytes(_buffer);
                frame = new NetMQFrame(_buffer.Buffer(), _buffer.Count);
            }
            else
            {
                if (payload is FramePayload)
                {
                    frame = ((FramePayload)payload).Frame;
                }
                else
                {
                    _buffer.Write(payload.DataSpan);    
                    frame = new NetMQFrame(_buffer.Buffer(), _buffer.Count);
                }
                
            }
                  
          
            message.Append(new NetMQFrame(Encoding.UTF8.GetBytes(endpoint)));
            var routerHeaderMsg = routePacket.RouteHeader.ToMsg();

            var headerSize = routerHeaderMsg.CalculateSize();
            var headerFrame = new NetMQFrame(headerSize);
            routerHeaderMsg.WriteTo(new MemoryStream(headerFrame.Buffer));

            message.Append(headerFrame);
            message.Append(frame);

            if (!_socket.TrySendMultipartMessage(message))
            {
                _log.Error(()=>$"PostAsync fail to {endpoint}, MsgName:{routePacket.MsgId}");
            }
        }
    }

    public void Dispose()
    {
        throw new NotImplementedException();
    }
}
