using Google.Protobuf;
using PlayHouse.Production.Shared;
using Playhouse.Protocol;
using PlayHouse.Service.Session.Network;
using PlayHouse.Service.Shared;
using PlayHouse.Utils;

namespace PlayHouse.Communicator.Message;

public class Header(ushort serviceId = 0, string msgId = "", ushort msgSeq = 0, ushort errorCode = 0, long stageId = 0)
{
    public ushort ServiceId { get; set; } = serviceId;
    public string MsgId { get; set; } = msgId;
    public ushort MsgSeq { get; set; } = msgSeq;
    public ushort ErrorCode { get; set; } = errorCode;
    public long StageId { get; set; } = stageId;

    public static Header Of(HeaderMsg headerMsg)
    {
        return new Header((ushort)headerMsg.ServiceId, headerMsg.MsgId, (ushort)headerMsg.MsgSeq,
            (ushort)headerMsg.ErrorCode, (byte)headerMsg.StageId);
    }

    public HeaderMsg ToMsg()
    {
        return new HeaderMsg
        {
            ServiceId = ServiceId,
            MsgId = MsgId,
            MsgSeq = MsgSeq,
            ErrorCode = ErrorCode,
            StageId = StageId
        };
    }

    public override string ToString()
    {
        return
            $"Header(GetServiceId={ServiceId}, MsgId={MsgId}, MsgSeq={MsgSeq}, ErrorCode={ErrorCode}, StageId={StageId})";
    }
}

public class RouteHeader(Header header)
{
    public RouteHeader(RouteHeaderMsg headerMsg)
        : this(Header.Of(headerMsg.HeaderMsg))
    {
        Sid = headerMsg.Sid;
        IsSystem = headerMsg.IsSystem;
        IsBase = headerMsg.IsBase;
        IsBackend = headerMsg.IsBackend;
        IsReply = headerMsg.IsReply;
        AccountId = headerMsg.AccountId;
        StageId = headerMsg.StageId;
    }

    public Header Header { get; } = header;
    public long Sid { get; set; }
    public bool IsSystem { get; set; }
    public bool IsBase { get; set; }
    public bool IsBackend { get; set; }
    public bool IsReply { get; set; }
    public long AccountId { get; set; }
    public long StageId { get; set; }

    public string From { get; set; } = "";

    public bool IsToClient { get; set; }

    public string MsgId => Header.MsgId;

    public byte[] ToByteArray()
    {
        return ToMsg().ToByteArray();
    }

    public RouteHeaderMsg ToMsg()
    {
        var message = new RouteHeaderMsg();
        message.HeaderMsg = Header.ToMsg();
        message.Sid = Sid;
        message.IsSystem = IsSystem;
        message.IsBase = IsBase;
        message.IsBackend = IsBackend;
        message.IsReply = IsReply;
        message.AccountId = AccountId;
        message.StageId = StageId;
        return message;
    }

    public static RouteHeader Of(HeaderMsg header)
    {
        return new RouteHeader(Header.Of(header));
    }

    public static RouteHeader Of(Header header)
    {
        return new RouteHeader(header);
    }

    public static RouteHeader TimerOf(long stageId, string msgId)
    {
        return new RouteHeader(new Header(msgId: msgId))
        {
            StageId = stageId
        };
    }

    public override string ToString()
    {
        return
            $"RouteHeader(Header={Header},AccountId={AccountId}, Sid={Sid}, IsSystem={IsSystem}, IsBase={IsBase}, IsBackend={IsBackend}, IsReply={IsReply},  StageId={StageId}, From={From}, ForClient={IsToClient})";
    }
}

internal class RoutePacket : IBasePacket
{
    public RouteHeader RouteHeader;
    public TimerCallbackTask? TimerCallback;

    public long TimerId;

    protected RoutePacket(RouteHeader routeHeader, IPayload payload)
    {
        RouteHeader = routeHeader;
        Payload = payload;
    }


    public string MsgId => RouteHeader.MsgId;


    public Header Header => RouteHeader.Header;
    public ushort ErrorCode => RouteHeader.Header.ErrorCode;

    public long AccountId => RouteHeader.AccountId;

    public long StageId => RouteHeader.StageId;

    public bool IsSystem => RouteHeader.IsSystem;

    public IPayload Payload { get; private set; }

    public object? TimerObject { get; }
    public ushort MsgSeq => Header.MsgSeq;

    //public static  void WriteClientPacketBytes(ClientPacket clientPacket, PooledByteBuffer buffer)
    //{
    //    var body = clientPacket.Payload.Data;

    //    int bodySize = body.Length;

    //    if (bodySize > ConstOption.MaxPacketSize)
    //    {
    //        throw new Exception($"body size is over : {bodySize}");
    //    }

    //    int msgIdSize = clientPacket.MsgId.Length;

    //    buffer.WriteInt16((ushort)(ConstOption.MinServerHeaderSize + msgIdSize));
    //    buffer.WriteInt24(bodySize);
    //    buffer.WriteInt16(clientPacket.ServiceId);
    //    buffer.Write((byte)msgIdSize);
    //    buffer.Write(clientPacket.MsgId);
    //    buffer.WriteInt16(clientPacket.MsgSeq);
    //    buffer.WriteInt64(clientPacket.Header.StageId);
    //    buffer.WriteInt16(clientPacket.Header.ErrorCode);
    //    buffer.Write(clientPacket.Payload.DataSpan);

    //}


    public IPayload MovePayload()
    {
        var temp = Payload;
        Payload = new EmptyPayload();
        return temp;
    }

    public ReadOnlyMemory<byte> Data => Payload.Data;
    public ReadOnlySpan<byte> Span => Payload.DataSpan;

    public void Dispose()
    {
        Payload.Dispose();
    }

    public ushort ServiceId()
    {
        return RouteHeader.Header.ServiceId;
    }

    public bool IsBackend()
    {
        return RouteHeader.IsBackend;
    }

    public ClientPacket ToClientPacket()
    {
        return new ClientPacket(RouteHeader.Header, MovePayload());
    }

    //public Packet ToPacket()
    //{
    //    return new Packet(MsgId, MovePayload());
    //}

    public bool IsBase()
    {
        return RouteHeader.IsBase;
    }

    public void SetMsgSeq(ushort msgSeq)
    {
        RouteHeader.Header.MsgSeq = msgSeq;
    }

    public bool IsRequest()
    {
        return RouteHeader.Header.MsgSeq != 0;
    }

    public bool IsReply()
    {
        return RouteHeader.IsReply;
    }

    public bool IsToClient()
    {
        return RouteHeader.IsToClient;
    }

    public static RoutePacket Of(ushort errorCode)
    {
        return new RoutePacket(new RouteHeader(new Header { ErrorCode = errorCode }), new EmptyPayload());
    }

    public static RoutePacket MoveOf(RoutePacket routePacket)
    {
        var movePacket = Of(routePacket.RouteHeader, routePacket.MovePayload());
        movePacket.TimerId = routePacket.TimerId;
        movePacket.TimerCallback = routePacket.TimerCallback;
        return movePacket;
    }


    public static RoutePacket Of(RouteHeader routeHeader, IPayload payload)
    {
        return new RoutePacket(routeHeader, payload);
    }

    internal static RoutePacket Of(string msgId, IPayload payload)
    {
        return new RoutePacket(new RouteHeader(new Header { MsgId = msgId }), payload);
    }

    internal static RoutePacket Of(IMessage message)
    {
        return new RoutePacket(new RouteHeader(new Header { MsgId = message.Descriptor.Name }),
            new ProtoPayload(message));
    }

    internal static RoutePacket Of(IPacket packet)
    {
        return new RoutePacket(new RouteHeader(new Header { MsgId = packet.MsgId }), packet.Payload);
    }

    public static RoutePacket SystemOf(RoutePacket packet, bool isBase)
    {
        var header = new Header(msgId: packet.MsgId);
        var routeHeader = RouteHeader.Of(header);
        routeHeader.IsSystem = true;
        routeHeader.IsBase = isBase;
        return new RoutePacket(routeHeader, packet.MovePayload());
    }

    public static RoutePacket ApiOf(RoutePacket packet, bool isBase, bool isBackend)
    {
        var header = new Header(msgId: packet.MsgId);
        var routeHeader = RouteHeader.Of(header);
        routeHeader.IsBase = isBase;
        routeHeader.IsBackend = isBackend;
        return new RoutePacket(routeHeader, packet.MovePayload());
    }

    public static RoutePacket SessionOf(long sid, RoutePacket packet, bool isBase, bool isBackend)
    {
        var header = new Header(msgId: packet.MsgId);
        var routeHeader = RouteHeader.Of(header);
        routeHeader.Sid = sid;
        routeHeader.IsBase = isBase;
        routeHeader.IsBackend = isBackend;

        return new RoutePacket(routeHeader, packet.MovePayload());
    }

    public static RoutePacket AddTimerOf(TimerMsg.Types.Type type, long stageId, long timerId,
        TimerCallbackTask timerCallback, TimeSpan initialDelay, TimeSpan period, int count = 0)
    {
        var header = new Header(msgId: TimerMsg.Descriptor.Name);
        var routeHeader = RouteHeader.Of(header);
        routeHeader.StageId = stageId;
        routeHeader.IsBase = true;

        var message = new TimerMsg
        {
            Type = type,
            Count = count,
            InitialDelay = (long)initialDelay.TotalMilliseconds,
            Period = (long)period.TotalMilliseconds
        };

        return new RoutePacket(routeHeader, new ProtoPayload(message))
        {
            TimerCallback = timerCallback,
            TimerId = timerId
        };
    }

    public static RoutePacket StageTimerOf(long stageId, long timerId, TimerCallbackTask timerCallback,
        object? timerState)
    {
        var header = new Header(msgId: StageTimer.Descriptor.Name);
        var routeHeader = RouteHeader.Of(header);

        return new RoutePacket(routeHeader, new EmptyPayload())
        {
            RouteHeader = { StageId = stageId, IsBase = true },
            TimerId = timerId,
            TimerCallback = timerCallback
        };
    }

    public static RoutePacket StageOf(long stageId, long accountId, RoutePacket packet, bool isBase, bool isBackend)
    {
        var header = new Header(msgId: packet.MsgId);
        var routeHeader = RouteHeader.Of(header);
        routeHeader.StageId = stageId;
        routeHeader.AccountId = accountId;
        routeHeader.IsBase = isBase;
        routeHeader.IsBackend = isBackend;
        return new RoutePacket(routeHeader, packet.MovePayload());
    }

    //public static RoutePacket ReplyOf(ushort serviceId, ushort msgSeq, int sid,bool forClient, ReplyPacket reply)
    //{
    //    Header header = new(msgId:reply.MsgId)
    //    {
    //        GetServiceId = serviceId,
    //        MsgSeq = msgSeq
    //    };

    //    RouteHeader routeHeader = RouteHeader.Of(header);
    //    routeHeader.IsReply = true;
    //    routeHeader.IsToClient = forClient;
    //    routeHeader.Sid = sid;

    //    var routePacket = new RoutePacket(routeHeader, reply.MovePayload());
    //    routePacket.RouteHeader.Header.ErrorCode = reply.ErrorCode;
    //    return routePacket;
    //}
    public static RoutePacket ReplyOf(ushort serviceId, RouteHeader sourceHeader, ushort errorCode, IPacket? reply)
    {
        Header header = new(msgId: reply != null ? reply.MsgId : "")
        {
            ServiceId = serviceId,
            MsgSeq = sourceHeader.Header.MsgSeq
        };

        var routeHeader = RouteHeader.Of(header);
        routeHeader.IsReply = true;
        routeHeader.IsToClient = !sourceHeader.IsBackend;
        routeHeader.Sid = sourceHeader.Sid;
        routeHeader.IsBackend = sourceHeader.IsBackend;
        routeHeader.IsBase = sourceHeader.IsBase;
        routeHeader.AccountId = sourceHeader.AccountId;

        var routePacket = reply != null
            ? new RoutePacket(routeHeader, reply.Payload)
            : new RoutePacket(routeHeader, new EmptyPayload());
        routePacket.RouteHeader.Header.ErrorCode = errorCode;
        return routePacket;
    }

    public static RoutePacket ClientOf(ushort serviceId, long sid, IPacket packet, long stageId = 0)
    {
        Header header = new(msgId: packet.MsgId)
        {
            ServiceId = serviceId
        };

        var routeHeader = RouteHeader.Of(header);
        routeHeader.Sid = sid;
        routeHeader.IsToClient = true;
        routeHeader.StageId = stageId;

        return new RoutePacket(routeHeader, packet.Payload);
    }

    public void WriteClientPacketBytes(PooledByteBuffer buffer)
    {
        var clientPacket = ToClientPacket();
        WriteClientPacketBytes(clientPacket, buffer);
    }


    public static void WriteClientPacketBytes(ClientPacket clientPacket, PooledByteBuffer buffer)
    {
        var msgIdLength = clientPacket.MsgId.Length;
        if (msgIdLength > PacketConst.MsgIdLimit)
        {
            throw new Exception($"MsgId size is over : {msgIdLength}");
        }

        var body = clientPacket.Payload.Data;
        var bodySize = body.Length;
        if (bodySize > ConstOption.MaxPacketSize)
        {
            throw new Exception($"body size is over : {bodySize}");
        }


        if (bodySize < PacketConst.MinCompressionSize)
        {
            buffer.WriteInt32(bodySize);
            buffer.WriteInt16(clientPacket.ServiceId);
            buffer.Write(clientPacket.MsgId);
            buffer.WriteInt16(clientPacket.MsgSeq);
            buffer.WriteInt64(clientPacket.Header.StageId);
            buffer.WriteInt16(clientPacket.Header.ErrorCode);
            buffer.WriteInt32(0); //압축안함
            buffer.Write(clientPacket.Payload.DataSpan);
        }
        else
        {
            var originalSize = bodySize;
            var compressed = Lz4Holder.Instance.Compress(body.Span);
            bodySize = compressed.Length;

            buffer.WriteInt32(bodySize);
            buffer.WriteInt16(clientPacket.ServiceId);
            buffer.Write(clientPacket.MsgId);
            buffer.WriteInt16(clientPacket.MsgSeq);
            buffer.WriteInt64(clientPacket.Header.StageId);
            buffer.WriteInt16(clientPacket.Header.ErrorCode);


            buffer.WriteInt32(originalSize); // 압축했을경우 원래 사이즈
            buffer.Write(compressed);
        }
    }


    internal IPacket ToContentsPacket()
    {
        return PacketProducer.CreatePacket(MsgId, Payload, MsgSeq);
    }
}

internal class AsyncBlockPacket : RoutePacket
{
    private AsyncBlockPacket(AsyncPostCallback asyncPostCallback, object result, RouteHeader routeHeader) : base(
        routeHeader, new EmptyPayload())
    {
        AsyncPostCallback = asyncPostCallback;
        Result = result;
    }

    public AsyncPostCallback AsyncPostCallback { get; }
    public object Result { get; }

    public static RoutePacket Of(long stageId, AsyncPostCallback asyncPostCallback, object result)
    {
        var header = new Header(msgId: AsyncBlock.Descriptor.Name);
        var routeHeader = RouteHeader.Of(header);
        var packet = new AsyncBlockPacket(asyncPostCallback, result, routeHeader);
        packet.RouteHeader.StageId = stageId;
        packet.RouteHeader.IsBase = true;
        return packet;
    }
}