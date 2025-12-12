using System;
using PlayHouse.Connector.Infrastructure.Buffers;

namespace PlayHouse.Connector.Protocol;

/// <summary>
/// PlayHouse는 ServiceId가 없음 (Kairos와 다른 점)
/// </summary>
public class TargetId
{
    public TargetId(long stageId = 0)
    {
        StageId = stageId;
    }

    public long StageId { get; }
}

public class Header
{
    public Header(string msgId = "", ushort msgSeq = 0, ushort errorCode = 0, long stageId = 0)
    {
        MsgId = msgId;
        ErrorCode = errorCode;
        MsgSeq = msgSeq;
        StageId = stageId;
    }

    public string MsgId { get; set; }
    public ushort MsgSeq { get; set; }
    public ushort ErrorCode { get; set; }
    public long StageId { get; set; }

    public override string ToString()
    {
        return $"MsgId: {MsgId}, MsgSeq: {MsgSeq}, ErrorCode: {ErrorCode}, StageId: {StageId}";
    }
}

public class PooledByteBufferPayload : IPayload
{
    private readonly PooledByteBuffer _buffer;

    public PooledByteBufferPayload(PooledByteBuffer buffer)
    {
        _buffer = buffer;
    }

    public void Dispose()
    {
        _buffer.Dispose();
    }

    public ReadOnlyMemory<byte> Data => _buffer.AsMemory();

    public PooledByteBuffer GetBuffer()
    {
        return _buffer;
    }
}

public class ClientPacket : IBasePacket
{
    public IPayload Payload;

    public ClientPacket(Header header, IPayload payload)
    {
        Header = header;
        Payload = payload;
    }

    public Header Header { get; set; }
    public long StageId => Header.StageId;
    public int MsgSeq => Header.MsgSeq;
    public string MsgId => Header.MsgId;

    public void Dispose()
    {
        Payload.Dispose();
    }

    public IPayload MovePayload()
    {
        var temp = Payload;
        Payload = EmptyPayload.Instance;
        return temp;
    }

    public IPacket ToPacket()
    {
        return new Packet(Header.MsgId, MovePayload());
    }

    internal static ClientPacket ToServerOf(TargetId targetId, IPacket packet)
    {
        var header = new Header(packet.MsgId, stageId: targetId.StageId);
        return new ClientPacket(header, packet.Payload);
    }

    /// <summary>
    /// PlayHouse 패킷 포맷 (ServiceId 없음)
    /// 4byte  body size
    /// 1byte  msgId size
    /// n byte msgId string
    /// 2byte  msgSeq
    /// 8byte  stageId
    /// ToServer Header Size = 4+1+2+8+N = 15 + n
    /// </summary>
    internal void GetBytes(PooledByteBuffer buffer)
    {
        int msgIdLength = Header.MsgId.Length;
        if (msgIdLength > Network.PacketConst.MsgIdLimit)
        {
            throw new Exception($"MsgId size is over : {msgIdLength}");
        }

        var body = Payload.Data;
        int bodySize = body.Length;

        if (bodySize > Network.PacketConst.MaxBodySize)
        {
            throw new Exception($"body size is over : {bodySize}");
        }

        buffer.WriteInt32(bodySize); // body size 4byte
        // ServiceId 제거
        buffer.Write(Header.MsgId); // msgId string
        buffer.WriteInt16(Header.MsgSeq); // msgseq
        buffer.WriteInt64(Header.StageId);

        buffer.Write(Payload.DataSpan);
    }

    internal void SetMsgSeq(ushort seq)
    {
        Header.MsgSeq = seq;
    }

    internal bool IsHeartBeat()
    {
        return MsgId == Network.PacketConst.HeartBeat;
    }
}

/// <summary>
/// IBasePacket 인터페이스 (IDisposable 상속)
/// </summary>
public interface IBasePacket : IDisposable
{
}
