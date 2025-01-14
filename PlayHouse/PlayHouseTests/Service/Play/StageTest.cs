using System.Runtime.CompilerServices;
using FluentAssertions;
using Google.Protobuf;
using Moq;
using Org.Ulalax.Playhouse.Protocol;
using PlayHouse.Communicator;
using PlayHouse.Communicator.Message;
using PlayHouse.Production.Play;
using PlayHouse.Production.Shared;
using Playhouse.Protocol;
using PlayHouse.Service.Play;
using PlayHouse.Service.Play.Base;
using PlayHouse.Service.Shared;
using Xunit;

[assembly: InternalsVisibleTo("DynamicProxyGenAssembly2")]

namespace PlayHouseTests.Service.Play;

public class StageTest
{
    private readonly long _accountId = 0;
    private readonly Mock<IClientCommunicator> _clientCommunicator;
    private readonly IStage _contentStage = Mock.Of<IStage>();
    private readonly string _nid = "2:0";

    private readonly List<RoutePacket> _resultList = [];

    //private readonly long _testStageId = 0;
    private readonly string _sessionNid = "0:0";
    private readonly BaseStage _stage;
    private readonly long _stageId = 0;
    private readonly string _stageType = "dungeon";


    public StageTest()
    {
        _clientCommunicator = new Mock<IClientCommunicator>();
        var reqCache = new RequestCache(0);
        var playOption = new PlayOption();

        playOption.PlayProducer.Register(
            _stageType,
            stageSender => _contentStage,
            actorSender => Mock.Of<IActor>()
        );
        var serverInfoCenter = Mock.Of<IServerInfoCenter>();

        //playProcessor = new PlayService(
        //    2,
        //    _bindEndpoint,
        //    playOption,
        //    _clientCommunicator.Object,
        //    reqCache,
        //    Mock.Of<IServerInfoCenter>()
        //);
        var playDispatcher = new PlayDispatcher(2, _clientCommunicator.Object, reqCache, serverInfoCenter, _nid,
            playOption);
        playDispatcher.Start();
        var xStageSender = new XStageSender(2, _stageId, playDispatcher, _clientCommunicator.Object, reqCache);

        var sessionUpdater = new Mock<ISessionUpdater>();

        sessionUpdater.Setup(updater => updater.UpdateStageInfo(It.IsAny<string>(), It.IsAny<int>()))
            .Returns(Task.FromResult(1));


        _stage = new BaseStage(
            _stageId,
            playDispatcher,
            _clientCommunicator.Object,
            reqCache,
            serverInfoCenter,
            sessionUpdater.Object,
            xStageSender
        );

        PacketProducer.Init((msgId, payload, msgSeq) => new TestPacket(msgId, payload, msgSeq));


        Mock.Get(_contentStage)
            .Setup(stage => stage.OnCreate(It.IsAny<IPacket>()))
            .Returns(Task.FromResult(((ushort)0, CPacket.Of(new TestMsg { TestMsg_ = "onCreate" }))));

        Mock.Get(_contentStage)
            .Setup(stage => stage.OnJoinStage(It.IsAny<IActor>(), It.IsAny<IPacket>()))
            .Returns(Task.FromResult(((ushort)0, CPacket.Of(new TestMsg { TestMsg_ = "onJoinStage" }))));
    }

    [Fact]
    public void CreateRoom_ShouldSucceed()
    {
        // given
        PacketContext.AsyncCore.Init();
        //PacketProducer.Init((msgId, payload, msgSeq) => new TestPacket(msgId, payload, msgSeq));

        var result = new List<RoutePacket>();
        _clientCommunicator.Setup(x => x.Send(It.IsAny<string>(), It.IsAny<RoutePacket>()))
            .Callback<string, RoutePacket>((sid, packet) => result.Add(packet));

        // when
        _stage.Post(CreateRoomPacket(_stageType));
        Thread.Sleep(100);

        // then
        result[0].RouteHeader.Header.ErrorCode.Should().Be((ushort)BaseErrorCode.Success);

        result[0].MsgId.Should().Be(CreateStageRes.Descriptor.Name);
        var createStageRes = CreateStageRes.Parser.ParseFrom(result[0].Span);

        createStageRes.PayloadId.Should().Be(TestMsg.Descriptor.Name);

        TestMsg.Parser.ParseFrom(createStageRes.Payload).TestMsg_.Should().Be("onCreate");
    }

    [Fact]
    public async Task CreateRoom_WithInvalidType_ShouldReturnInvalidError()
    {
        // given
        PacketContext.AsyncCore.Init();

        var result = new List<RoutePacket>();
        _clientCommunicator.Setup(x => x.Send(It.IsAny<string>(), It.IsAny<RoutePacket>()))
            .Callback<string, RoutePacket>((sid, packet) => result.Add(packet));

        // when
        _stage.Post(CreateRoomPacket("invalid type"));
        Thread.Sleep(100);

        // then
        result[0].RouteHeader.Header.ErrorCode.Should().Be((ushort)BaseErrorCode.StageTypeIsInvalid);
        await Task.CompletedTask;
    }

    [Fact]
    public void CreateJoinRoomInCreateState_ShouldBeSuccess()
    {
        PacketContext.AsyncCore.Init();
        //PacketProducer.Init((msgId, payload, msgSeq) => new TestPacket(msgId, payload, msgSeq));

        var result = new List<RoutePacket>();
        _clientCommunicator.Setup(x => x.Send(It.IsAny<string>(), It.IsAny<RoutePacket>()))
            .Callback<string, RoutePacket>((sid, packet) => result.Add(packet));

        var createJoinRoom = CreateJoinRoomPacket(_stageType, _stageId, _accountId);
        _stage.Post(createJoinRoom);

        Thread.Sleep(100);

        result[0].MsgId.Should().Be(CreateJoinStageRes.Descriptor.Name);
        var createJoinStageRes = CreateJoinStageRes.Parser.ParseFrom(result[0].Span);

        createJoinStageRes.IsCreated.Should().BeTrue();
        createJoinStageRes.CreatePayloadId.Should().Be(TestMsg.Descriptor.Name);
        createJoinStageRes.JoinPayloadId.Should().Be(TestMsg.Descriptor.Name);

        TestMsg.Parser.ParseFrom(createJoinStageRes.CreatePayload).TestMsg_.Should().Be("onCreate");
        TestMsg.Parser.ParseFrom(createJoinStageRes.JoinPayload).TestMsg_.Should().Be("onJoinStage");
    }

    [Fact]
    public void TestCreateJoinRoomInJoinState()
    {
        // Arrange
        PacketContext.AsyncCore.Init();
        //PacketProducer.Init((msgId, payload, msgSeq) => new TestPacket(msgId, payload, msgSeq));

        CreateRoomWithSuccess();

        var result = new List<RoutePacket>();
        _clientCommunicator.Setup(x => x.Send(It.IsAny<string>(), It.IsAny<RoutePacket>()))
            .Callback<string, RoutePacket>((sid, packet) => result.Add(packet));

        var createJoinRoom = CreateJoinRoomPacket(_stageType, _stageId, _accountId);
        // Act
        _stage.Post(createJoinRoom);

        Thread.Sleep(100);

        // Assert
        CreateJoinStageRes.Descriptor.Name.Should().Be(result[0].MsgId);
        var createJoinStageRes = CreateJoinStageRes.Parser.ParseFrom(result[0].Span);

        createJoinStageRes.IsCreated.Should().BeFalse();
        createJoinStageRes.CreatePayloadId.Should().Be(string.Empty);
        createJoinStageRes.JoinPayloadId.Should().Be(TestMsg.Descriptor.Name);
    }

    [Fact]
    public void AsyncBlock_ShouldRunBlocking()
    {
        var result = "";
        _stage.Post(AsyncBlockPacket.Of(_stageId, async arg =>
        {
            result = (string)arg;
            await Task.CompletedTask;
        }, "test async block"));
        Thread.Sleep(100);
        Assert.Equal("test async block", result);
    }

    private RoutePacket CreateRoomPacket(string stageType)
    {
        var packet = RoutePacket.Of(new CreateStageReq
        {
            StageType = stageType
        });

        var result = RoutePacket.StageOf(0, 0, packet, true, true);
        result.SetMsgSeq(1);
        return result;
    }

    private RoutePacket JoinRoomPacket(long stageId, long accountId)
    {
        var packet = RoutePacket.Of(new JoinStageReq
        {
            SessionNid = _sessionNid,
            Sid = 1,
            PayloadId = "1",
            Payload = ByteString.Empty
        });
        var result = RoutePacket.StageOf(stageId, accountId, packet, true, true);
        result.SetMsgSeq(2);
        return result;
    }

    private RoutePacket CreateJoinRoomPacket(string stageType, long stageId, long accountId)
    {
        var req = new CreateJoinStageReq
        {
            StageType = stageType,
            SessionNid = _sessionNid,
            Sid = 1,
            CreatePayloadId = "1",
            CreatePayload = ByteString.Empty,
            JoinPayloadId = "2",
            JoinPayload = ByteString.Empty
        };
        var packet = RoutePacket.Of(req);
        var result = RoutePacket.StageOf(stageId, accountId, packet, true, true);
        result.SetMsgSeq(3);
        return result;
    }

    private void CreateRoomWithSuccess()
    {
        var result = new List<RoutePacket>();
        _clientCommunicator.Setup(c => c.Send(It.IsAny<string>(), It.IsAny<RoutePacket>()))
            .Callback<string, RoutePacket>((sid, packet) => result.Add(packet));

        _stage.Post(CreateRoomPacket(_stageType));
        Thread.Sleep(100);


        result[0].RouteHeader.Header.ErrorCode.Should().Be((ushort)BaseErrorCode.Success);

        var createStageRes = CreateStageRes.Parser.ParseFrom(result[0].Span);
    }
}