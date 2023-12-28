﻿using Google.Protobuf;
using PlayHouse.Communicator.Message;
using PlayHouse.Production;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PlayHouseTests
{
    internal class TestPacket : IPacket
    {
        private int _msgId;
        private IPayload _payload;
        private int _msgSeq;
        public int MsgSeq { get => _msgSeq; set => _msgSeq = value; }
        public TestPacket(IMessage message)
        {
            _msgId = message.Descriptor.Index;
            _payload = new ProtoPayload(message);
            _msgSeq = 0;
        }

        public TestPacket(int msgId)
        {
            _msgId = msgId;
            _payload = new EmptyPayload();
        }

        public TestPacket(int msgId, IPayload payload,int msgSeq) : this(msgId)
        {
            _payload = payload;
            _msgSeq = msgSeq;
        }

        public int MsgId => _msgId;

        public IPayload Payload => _payload;

        public IPacket Copy()
        {
            throw new NotImplementedException();
        }

        public T Parse<T>()
        {
            throw new NotImplementedException();
        }
    }
}
