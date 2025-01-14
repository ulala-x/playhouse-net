using FluentAssertions;
using Moq;
using PlayHouse;
using PlayHouse.Communicator;
using PlayHouse.Communicator.Message;
using PlayHouse.Production.Shared;
using Playhouse.Protocol;
using PlayHouse.Service.Session;
using PlayHouse.Service.Session.Network;
using Xunit;

namespace PlayHouseTests.Service.Session;

public class SessionClientTest : IDisposable
{
    private readonly IClientCommunicator _clientCommunicator;

    private readonly ushort _idApi = 2;
    private readonly ushort _idSession = 1;

    private readonly RequestCache _reqCache;
    private readonly int _serverId = 1;
    private readonly IServerInfoCenter _serviceCenter;
    private readonly ISession _session;
    private readonly int _sid = 1;
    private readonly List<string> _urls;


    public SessionClientTest()
    {
        PooledBuffer.Init();

        _serviceCenter = new XServerInfoCenter(false);

        var apiNid = $"{_idApi}:{_serverId}";

        _serviceCenter.Update(new List<XServerInfo>
        {
            XServerInfo.Of("tcp://127.0.0.1:0021", _idApi, _serverId, apiNid, ServiceType.API, ServerState.RUNNING, 21,
                DateTimeOffset.Now.ToUnixTimeMilliseconds())
        });

        _session = Mock.Of<ISession>();
        _reqCache = new RequestCache(0);
        _clientCommunicator = Mock.Of<IClientCommunicator>();
        _urls = new List<string>();
    }

    public void Dispose()
    {
    }

    [Fact]
    public void WithoutAuthenticate_SendPacket_SocketShouldBeDisconnected()
    {
        var sessionClient = new SessionActor(_idSession, _sid, _serviceCenter, _session, _clientCommunicator, _urls,
            _reqCache, string.Empty, null, new SessionServerDispatcher());
        var clientPacket = new ClientPacket(new Header(_idApi), new EmptyPayload());
        sessionClient.Dispatch(clientPacket);
        Mock.Get(_session).Verify(s => s.ClientDisconnect(), Times.Once());
    }

    [Fact]
    public void PacketOnTheAuthList_ShouldBeDelivered()
    {
        var messageId = "AuthenticateReq";
        //var messageId = "1";
        _urls.Add($"{_idApi}:{messageId}");

        var sessionClient = new SessionActor(_idSession, _sid, _serviceCenter, _session, _clientCommunicator, _urls,
            _reqCache, string.Empty, null, new SessionServerDispatcher());
        var clientPacket = new ClientPacket(new Header(_idApi, messageId), new EmptyPayload());
        sessionClient.Dispatch(clientPacket);

        Mock.Get(_clientCommunicator).Verify(c => c.Send(It.IsAny<string>(), It.IsAny<RoutePacket>()), Times.Once());
    }

    [Fact]
    public async Task ReceiveAuthenticatePacket_SessionClientShouldBeAuthenticated()
    {
        // api 서버로부터 authenticate 패킷을 받을 경우 인증 확인 및 session info 정보 확인
        var accountId = 1000L;

        var message = new AuthenticateMsg
        {
            ServiceId = _idApi,
            AccountId = accountId
        };
        var routePacket = RoutePacket.SessionOf(_sid, RoutePacket.Of(message), true, true);

        var sessionClient = new SessionActor(_idSession, _sid, _serviceCenter, _session, _clientCommunicator, _urls,
            _reqCache, string.Empty, null, new SessionServerDispatcher());
        await sessionClient.DispatchAsync(routePacket);

        sessionClient.IsAuthenticated.Should().BeTrue();
    }
}