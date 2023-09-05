﻿using Google.Protobuf;
using Playhouse.Protocol;
using PlayHouse.Production;

namespace PlayHouse.Service.Play.Base
{
    public interface ISessionUpdater
    {
        public Task<int> UpdateStageInfo(string sessionEndpoint, int sid);
    }


    public class XSessionUpdater : ISessionUpdater
    {
        private XStageSender _stageSender;
        private string _playEndpoint;
        public XSessionUpdater(string playEndpoint,XStageSender stageSender)
        {
            _stageSender = stageSender;
            _playEndpoint = playEndpoint;
        }

        public async Task<int> UpdateStageInfo(string sessionEndpoint, int sid)
        {
            var joinStageInfoUpdateReq = new JoinStageInfoUpdateReq()
            {
                StageId = ByteString.CopyFrom(_stageSender.StageId.ToByteArray()),
                PlayEndpoint = _playEndpoint,
            };

            var res = await _stageSender.RequestToBaseSession(sessionEndpoint, sid, new Packet(joinStageInfoUpdateReq));
            var result = JoinStageInfoUpdateRes.Parser.ParseFrom(res.Data);
            return result.StageIdx;
        }
    }
}