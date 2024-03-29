﻿using PlayHouse.Service.Shared;

namespace PlayHouse.Production.Shared
{
    public delegate Task TimerCallbackTask();
    public interface ISystemPanel
    {
        IServerInfo GetServerInfoBy(ushort serviceId);
        IServerInfo GetServerInfoBy(ushort serviceId,string accountId);
        IServerInfo GetServerInfoByEndpoint(string endpoint);
        IList<IServerInfo> GetServers();
        void Pause();
        void Resume();
        Task ShutdownASync();
        ServerState GetServerState();
        long GenerateUUID();
    }

    public interface ISender
    {
        ushort ServiceId { get; }
        void Reply(ushort errorCode);
        void Reply(IPacket reply);

        void SendToClient(string sessionEndpoint, int sid, IPacket packet);
        void SendToApi(string apiEndpoint, IPacket packet);
        void SendToStage(string playEndpoint, string stageId, string accountId, IPacket packet);

        void RequestToApi(string apiEndpoint, IPacket packet, ReplyCallback replyCallback);
        void RequestToStage(string playEndpoint, string stageId, string accountId, IPacket packet, ReplyCallback replyCallback);
        Task<IPacket> RequestToApi(string apiEndpoint, IPacket packet);
        Task<IPacket> RequestToApi(string apiEndpoint,string accountId, IPacket packet);
        Task<IPacket> RequestToStage(string playEndpoint, string stageId, string accountId, IPacket packet);


        void SendToSystem(string endpoint, IPacket packet);
        Task<IPacket> RequestToSystem(string endpoint, IPacket packet);

        void SessionClose(string sessionEndpoint, int sid);
    }

    public interface IApiCommonSender : ISender
    {

        string AccountId { get; }
        Task<CreateStageResult> CreateStage(string playEndpoint, string stageType, string stageId, IPacket packet);


    }
    public interface IApiSender : IApiCommonSender
    {
        void Authenticate(string accountId);
        string SessionEndpoint { get; }
        int Sid { get; }

        Task<JoinStageResult> JoinStage(string playEndpoint,
                    string stageId,
                    IPacket packet
      );
        Task<CreateJoinStageResult> CreateJoinStage(string playEndpoint,
                            string stageType,
                            string stageId,
                            IPacket createPacket,
                            IPacket joinPacket
        );

        void SendToClient(IPacket packet)
        {
            SendToClient(SessionEndpoint, Sid, packet);
        }
        void SessionClose()
        {
            SessionClose(SessionEndpoint, Sid);
        }

    }

    public delegate Task<object> AsyncPreCallback();
    public delegate Task AsyncPostCallback(object result);

    public interface IStageSender : ISender
    {
        public string StageId { get; }
        public string StageType { get; }

        long AddRepeatTimer(TimeSpan initialDelay, TimeSpan period, TimerCallbackTask timerCallback);
        long AddCountTimer(TimeSpan initialDelay, int count, TimeSpan period, TimerCallbackTask timerCallback);
        void CancelTimer(long timerId);
        void CloseStage();

        void AsyncBlock(AsyncPreCallback preCallback, AsyncPostCallback? postCallback = null);
    }

    public interface IApiBackendSender : IApiCommonSender
    {
        string GetFromEndpoint();
    }

    public interface ISessionSender : ISender { }

}
