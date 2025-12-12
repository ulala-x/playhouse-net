using System;
using System.Collections.Generic;
using PlayHouse.Connector.Infrastructure.Buffers;
using PlayHouse.Connector.Compression;
using PlayHouse.Connector.Protocol;

namespace PlayHouse.Connector.Network;

/*
 *  4byte  body size
 *  1byte  msgId size
 *  n byte msgId string
 *  2byte  msgSeq
 *  8byte  stageId
 *  2byte  errorCode
 *  4byte  original body size (if 0 not compressed)
 *  Header Size = 4+1+2+8+2+4+N = 21 + n (ServiceId 없음)
 */

public sealed class PacketParser
{
    private readonly Lz4 _lz4 = new();

    public List<ClientPacket> Parse(RingBuffer buffer)
    {
        var packets = new List<ClientPacket>();

        while (buffer.Count >= PacketConst.MinHeaderSize)
        {
            int bodySize = buffer.PeekInt32(buffer.ReaderIndex);

            if (bodySize > PacketConst.MaxBodySize)
            {
                Console.WriteLine($"Body size over : {bodySize}");
                throw new IndexOutOfRangeException("BodySizeOver");
            }

            // ServiceId 제거: 4 + 2 → 4
            int checkSizeOfMsg = buffer.PeekByte(buffer.MoveIndex(buffer.ReaderIndex, 4));

            // If the remaining buffer is smaller than the expected packet size, wait for more data
            if (buffer.Count < bodySize + checkSizeOfMsg + PacketConst.MinHeaderSize)
            {
                break;
            }

            buffer.Clear(sizeof(int));

            // ServiceId 읽기 제거
            var sizeOfMsgId = buffer.ReadByte();
            var msgName = buffer.ReadString(sizeOfMsgId);

            var msgSeq = buffer.ReadInt16();
            var stageId = buffer.ReadInt64();
            var errorCode = buffer.ReadInt16();
            var originalSize = buffer.ReadInt32();

            PooledByteBuffer body;

            if (originalSize > 0) // compressed
            {
                body = new PooledByteBuffer(originalSize);
                buffer.Read(body, bodySize);

                var source = new ReadOnlySpan<byte>(body.Buffer(), 0, bodySize);
                var decompressed = _lz4.Decompress(source, originalSize);

                body.Clear();
                body.Write(decompressed);

                // TODO: Add logging
                Console.WriteLine($"decompressed - [msgId:{msgName},originalSize:{originalSize},compressedSize:{bodySize}]");
            }
            else
            {
                body = new PooledByteBuffer(bodySize);
                buffer.Read(body, bodySize);
            }

            // ServiceId 제거: Header 생성자에서 serviceId 제거
            var clientPacket = new ClientPacket(new Header(msgName, msgSeq, errorCode, stageId), new PooledByteBufferPayload(body));
            packets.Add(clientPacket);
        }

        return packets;
    }
}
