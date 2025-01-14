using NetMQ;
using PlayHouse.Communicator.Message;
using PlayHouse.Utils;

namespace PlayHouse.Service.Session.Network;

/*
 *  4byte  body size
 *  2byte  serviceId
 *  1byte  msgId size
 *  n byte msgId string
 *  2byte  msgSeq
 *  8byte  stageId
 *  From Header Size = 2+3+2+1+2+8+2+N = 17 + n
 * */
internal sealed class PacketParser
{
    private readonly LOG<PacketParser> _log = new();

    public List<ClientPacket> Parse(RingBuffer buffer)
    {
        var packets = new List<ClientPacket>();
        while (buffer.Count >= PacketConst.MinPacketSize)
        {
            var bodySize = buffer.PeekInt32(buffer.ReaderIndex);

            if (bodySize > PacketConst.MaxPacketSize)
            {
                _log.Error(() => $"Body size over : {bodySize}");
                throw new Exception("BodySizeOver");
            }

            int checkSizeOfMsg = buffer.PeekByte(buffer.MoveIndex(buffer.ReaderIndex, 4 + 2));

            // If the remaining buffer is smaller than the expected packet size, wait for more data
            if (buffer.Count < bodySize + checkSizeOfMsg + PacketConst.MinPacketSize)
            {
                break;
            }

            buffer.Clear(4);

            var serviceId = buffer.ReadInt16();
            var sizeOfMsgId = buffer.ReadByte();
            var msgId = buffer.ReadString(sizeOfMsgId);

            var msgSeq = buffer.ReadInt16();
            var stageId = buffer.ReadInt64();
            var body = new NetMQFrame(bodySize);

            buffer.Read(body.Buffer, 0, bodySize);

            var clientPacket =
                new ClientPacket(new Header(serviceId, msgId, msgSeq, 0, stageId), new FramePayload(body));
            packets.Add(clientPacket);
        }

        return packets;
    }
}