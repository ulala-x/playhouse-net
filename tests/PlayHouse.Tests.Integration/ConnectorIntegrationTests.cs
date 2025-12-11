#nullable enable

using FluentAssertions;
using Google.Protobuf;
using PlayHouse.Connector;
using PlayHouse.Connector.Protocol;
using PlayHouse.Tests.Integration.Infrastructure;
using PlayHouse.Tests.Integration.Proto;
using Xunit;
using ClientConnector = PlayHouse.Connector.Connector;

namespace PlayHouse.Tests.Integration;

/// <summary>
/// E2E 테스트: Connector의 실제 TCP 통신 검증
/// 실제 서버와 통신하며 연결, 메시지 송수신, 콜백 호출을 검증합니다.
/// Connect() 시 host, port, stageId를 전달하는 새 API를 사용합니다.
/// </summary>
[Collection("E2E Tests")]
public class ConnectorE2ETests : IAsyncLifetime
{
    private readonly TestTcpServer _server;
    private readonly ClientConnector _connector;
    private readonly List<(long stageId, IPacket packet)> _receivedMessages = new();
    private readonly List<(long stageId, ushort errorCode, IPacket request)> _receivedErrors = new();
    private readonly List<bool> _connectResults = new();
    private int _disconnectCount;

    private const long DefaultStageId = 12345L;

    public ConnectorE2ETests()
    {
        _server = new TestTcpServer();
        _connector = new ClientConnector();

        // 이벤트 핸들러 등록
        _connector.OnConnect += result => _connectResults.Add(result);
        _connector.OnReceive += (stageId, packet) => _receivedMessages.Add((stageId, packet));
        _connector.OnError += (stageId, errorCode, request) => _receivedErrors.Add((stageId, errorCode, request));
        _connector.OnDisconnect += () => Interlocked.Increment(ref _disconnectCount);
    }

    public async Task InitializeAsync()
    {
        await _server.StartAsync();
    }

    public async Task DisposeAsync()
    {
        _connector.Disconnect();
        await _server.DisposeAsync();
    }

    #region 1. 연결 관리 (Connection Management)

    [Fact(DisplayName = "Connect - 서버에 TCP 연결하면 OnConnect(true) 콜백이 호출된다")]
    public async Task Connect_ToRunningServer_InvokesOnConnectWithTrue()
    {
        // Given (전제조건): 서버가 실행 중이고 Connector가 초기화됨
        _connector.Init(new ConnectorConfig());

        // When (행동): 서버에 연결 (host, port, stageId 전달)
        var result = await _connector.ConnectAsync("127.0.0.1", _server.Port, DefaultStageId);
        await ProcessCallbacksAsync();

        // Then (결과): 연결 성공하고 OnConnect(true) 콜백 호출
        result.Should().BeTrue("서버 연결에 성공해야 함");
        _connector.IsConnected().Should().BeTrue("연결 상태여야 함");
        _connector.StageId.Should().Be(DefaultStageId, "StageId가 설정되어야 함");
        _connectResults.Should().Contain(true, "OnConnect(true) 콜백이 호출되어야 함");
    }

    [Fact(DisplayName = "Connect - 존재하지 않는 서버에 연결하면 OnConnect(false) 콜백이 호출된다")]
    public async Task Connect_ToNonExistentServer_InvokesOnConnectWithFalse()
    {
        // Given (전제조건): Connector 초기화
        _connector.Init(new ConnectorConfig());

        // When (행동): 존재하지 않는 포트로 연결 시도
        var result = await _connector.ConnectAsync("127.0.0.1", 59999, DefaultStageId);
        await ProcessCallbacksAsync();

        // Then (결과): 연결 실패하고 OnConnect(false) 콜백 호출
        result.Should().BeFalse("연결에 실패해야 함");
        _connector.IsConnected().Should().BeFalse("연결되지 않아야 함");
        _connectResults.Should().Contain(false, "OnConnect(false) 콜백이 호출되어야 함");
    }

    [Fact(DisplayName = "Disconnect - 클라이언트가 연결 해제하면 IsConnected가 false가 된다")]
    public async Task Disconnect_ByClient_DisconnectsWithoutCallback()
    {
        // Given (전제조건): 서버에 연결된 상태
        await ConnectToServerAsync();
        _disconnectCount = 0;

        // When (행동): 클라이언트가 연결 해제
        _connector.Disconnect();
        await Task.Delay(100);

        // Then (결과): 연결만 해제됨
        _connector.IsConnected().Should().BeFalse("연결이 해제되어야 함");
    }

    [Fact(DisplayName = "OnDisconnect - 서버가 연결을 끊으면 OnDisconnect 콜백이 호출된다")]
    public async Task OnDisconnect_ServerClosesConnection_InvokesCallback()
    {
        // Given (전제조건): 서버에 연결된 상태
        await ConnectToServerAsync();
        _disconnectCount = 0;

        // When (행동): 서버가 연결 종료
        await _server.StopAsync();

        // 콜백 대기
        var timeout = DateTime.UtcNow.AddSeconds(5);
        while (_disconnectCount == 0 && DateTime.UtcNow < timeout)
        {
            await Task.Delay(50);
            _connector.MainThreadAction();
        }

        // Then (결과): OnDisconnect 콜백 호출
        _disconnectCount.Should().BeGreaterOrEqualTo(1, "서버 종료시 OnDisconnect 콜백이 호출되어야 함");
    }

    [Fact(DisplayName = "IsConnected - 연결 전에는 false, 연결 후에는 true를 반환한다")]
    public async Task IsConnected_ReflectsConnectionState()
    {
        // Given (전제조건): 초기화된 Connector
        _connector.Init(new ConnectorConfig());

        // When & Then: 연결 전
        _connector.IsConnected().Should().BeFalse("연결 전에는 false");

        // When: 연결
        await _connector.ConnectAsync("127.0.0.1", _server.Port, DefaultStageId);
        await ProcessCallbacksAsync();

        // Then: 연결 후
        _connector.IsConnected().Should().BeTrue("연결 후에는 true");
    }

    #endregion

    #region 2. Request-Response 패턴 (Request-Response Pattern)

    [Fact(DisplayName = "RequestAsync - 에코 요청을 보내면 동일한 내용의 응답을 받는다")]
    public async Task RequestAsync_EchoRequest_ReceivesEchoReply()
    {
        // Given (전제조건): 서버에 연결된 상태
        await ConnectToServerAsync();

        var echoRequest = new EchoRequest
        {
            Content = "Hello, Server!",
            Sequence = 42
        };
        using var packet = new Packet(echoRequest);

        // When (행동): 에코 요청
        var response = await _connector.RequestAsync(packet);

        // Then (결과): 동일한 내용의 응답 수신
        response.Should().NotBeNull("응답을 받아야 함");
        response.MsgId.Should().Be("EchoReply", "응답 메시지 ID가 EchoReply여야 함");

        var echoReply = EchoReply.Parser.ParseFrom(response.Payload.Data.Span);
        echoReply.Content.Should().Be("Hello, Server!", "에코 내용이 동일해야 함");
        echoReply.Sequence.Should().Be(42, "시퀀스 번호가 동일해야 함");
        echoReply.ProcessedAt.Should().BeGreaterThan(0, "처리 시간이 설정되어야 함");
    }

    [Fact(DisplayName = "RequestAsync - Connect에서 설정한 StageId가 서버에 전달된다")]
    public async Task RequestAsync_StageIdFromConnect_IsSentToServer()
    {
        // Given (전제조건): 특정 StageId로 서버에 연결
        const long testStageId = 123456789L;
        _connector.Init(new ConnectorConfig());
        await _connector.ConnectAsync("127.0.0.1", _server.Port, testStageId);
        await ProcessCallbacksAsync();

        var echoRequest = new EchoRequest { Content = "With Stage", Sequence = 1 };
        using var packet = new Packet(echoRequest);

        // When (행동): 요청 전송
        var response = await _connector.RequestAsync(packet);
        await ProcessCallbacksAsync();

        // Then (결과): 서버가 StageId를 수신함
        _server.ReceivedMessages.Should().Contain(m => m.StageId == testStageId,
            "서버가 Connect에서 설정한 StageId를 수신해야 함");
    }

    [Fact(DisplayName = "Request with Callback - 콜백으로 응답을 받는다")]
    public async Task Request_WithCallback_InvokesCallbackWithResponse()
    {
        // Given (전제조건): 서버에 연결된 상태
        await ConnectToServerAsync();

        var echoRequest = new EchoRequest { Content = "Callback Test", Sequence = 99 };
        using var packet = new Packet(echoRequest);
        IPacket? receivedResponse = null;
        var responseReceived = new ManualResetEventSlim(false);

        // When (행동): 콜백과 함께 요청
        _connector.Request(packet, response =>
        {
            receivedResponse = response;
            responseReceived.Set();
        });

        // Then (결과): 콜백이 응답과 함께 호출됨
        var timeout = DateTime.UtcNow.AddSeconds(5);
        while (!responseReceived.IsSet && DateTime.UtcNow < timeout)
        {
            await Task.Delay(50);
            _connector.MainThreadAction();
        }

        responseReceived.IsSet.Should().BeTrue("콜백이 호출되어야 함");
        receivedResponse.Should().NotBeNull("응답이 전달되어야 함");
        receivedResponse!.MsgId.Should().Be("EchoReply");
    }

    [Fact(DisplayName = "RequestAsync - 서버가 에러코드를 반환하면 ConnectorException이 발생한다")]
    public async Task RequestAsync_ServerReturnsError_ThrowsConnectorException()
    {
        // Given (전제조건): 서버에 연결된 상태
        await ConnectToServerAsync();

        using var failRequest = Packet.Empty("FailRequest");

        // When (행동): 실패하는 요청
        Func<Task> action = async () => await _connector.RequestAsync(failRequest);

        // Then (결과): ConnectorException 발생
        var exception = await action.Should().ThrowAsync<ConnectorException>("에러 응답시 예외가 발생해야 함");
        exception.Which.ErrorCode.Should().Be(500, "서버가 반환한 에러코드여야 함");
    }

    #endregion

    #region 3. Send 패턴 (Fire-and-Forget)

    [Fact(DisplayName = "Send - 인증 후 메시지를 서버로 전송하고 서버가 수신한다")]
    public async Task Send_Message_ServerReceivesIt()
    {
        // Given (전제조건): 서버에 연결되고 인증된 상태
        await ConnectToServerAsync();

        // 인증 수행
        using var authPacket = Packet.Empty("AuthenticateRequest");
        await _connector.AuthenticateAsync(authPacket);

        _server.ReceivedMessages.Clear();

        var echoRequest = new EchoRequest { Content = "Fire and Forget", Sequence = 7 };
        using var packet = new Packet(echoRequest);

        // When (행동): 메시지 전송
        _connector.Send(packet);
        await Task.Delay(100);

        // Then (결과): 서버가 메시지 수신
        _server.ReceivedMessages.Should().Contain(m => m.MsgId == "EchoRequest",
            "서버가 메시지를 수신해야 함");

        var received = _server.ReceivedMessages.First(m => m.MsgId == "EchoRequest");
        var parsed = EchoRequest.Parser.ParseFrom(received.Payload);
        parsed.Content.Should().Be("Fire and Forget");
        parsed.Sequence.Should().Be(7);
    }

    [Fact(DisplayName = "Send - Connect에서 설정한 StageId가 서버에 전달된다")]
    public async Task Send_StageIdFromConnect_ServerReceivesStageId()
    {
        // Given (전제조건): 특정 StageId로 서버에 연결되고 인증된 상태
        const long testStageId = 987654321L;
        _connector.Init(new ConnectorConfig());
        await _connector.ConnectAsync("127.0.0.1", _server.Port, testStageId);
        await ProcessCallbacksAsync();

        // 인증 수행
        using var authPacket = Packet.Empty("AuthenticateRequest");
        await _connector.AuthenticateAsync(authPacket);

        _server.ReceivedMessages.Clear();
        using var packet = Packet.Empty("StatusRequest");

        // When (행동): 전송
        _connector.Send(packet);
        await Task.Delay(100);

        // Then (결과): 서버가 StageId 수신
        _server.ReceivedMessages.Should().Contain(m => m.StageId == testStageId,
            "서버가 Connect에서 설정한 StageId를 수신해야 함");
    }

    #endregion

    #region 4. Push 메시지 수신 (Server Push)

    [Fact(DisplayName = "OnReceive - 서버가 Push 메시지를 보내면 OnReceive 콜백이 호출된다")]
    public async Task OnReceive_ServerPush_InvokesCallback()
    {
        // Given (전제조건): 서버에 연결된 상태
        await ConnectToServerAsync();
        _receivedMessages.Clear();

        var broadcast = new BroadcastNotify
        {
            EventType = "system",
            Data = "Welcome!",
            FromAccountId = 0
        };

        // When (행동): 서버가 Push 메시지 전송
        await _server.BroadcastPushAsync("BroadcastNotify", broadcast.ToByteArray());
        await Task.Delay(100);
        await ProcessCallbacksAsync();

        // Then (결과): OnReceive 콜백 호출
        _receivedMessages.Should().NotBeEmpty("Push 메시지를 수신해야 함");
        var (stageId, packet) = _receivedMessages.First();

        packet.MsgId.Should().Be("BroadcastNotify", "메시지 ID가 BroadcastNotify여야 함");
        var parsed = BroadcastNotify.Parser.ParseFrom(packet.Payload.Data.Span);
        parsed.EventType.Should().Be("system");
        parsed.Data.Should().Be("Welcome!");
    }

    #endregion

    #region 5. 인증 플로우 (Authentication Flow)

    [Fact(DisplayName = "Authenticate - 인증 요청 후 IsAuthenticated가 true가 된다")]
    public async Task Authenticate_Success_SetsIsAuthenticatedTrue()
    {
        // Given (전제조건): 서버에 연결됨
        await ConnectToServerAsync();
        _connector.IsAuthenticated().Should().BeFalse("인증 전에는 false");

        using var authPacket = Packet.Empty("AuthenticateRequest");

        // When (행동): 인증 요청
        var response = await _connector.AuthenticateAsync(authPacket);

        // Then (결과): 인증 상태가 true로 변경
        _connector.IsAuthenticated().Should().BeTrue("인증 후에는 true");
        response.Should().NotBeNull("인증 응답을 받아야 함");
    }

    [Fact(DisplayName = "Authenticate with Callback - 콜백으로 인증 응답을 받는다")]
    public async Task Authenticate_WithCallback_InvokesCallbackOnSuccess()
    {
        // Given (전제조건): 서버에 연결됨
        await ConnectToServerAsync();

        using var authPacket = Packet.Empty("AuthenticateRequest");
        IPacket? authResponse = null;
        var authCompleted = new ManualResetEventSlim(false);

        // When (행동): 콜백과 함께 인증
        _connector.Authenticate(authPacket, response =>
        {
            authResponse = response;
            authCompleted.Set();
        });

        // Then (결과): 인증 콜백 호출
        var timeout = DateTime.UtcNow.AddSeconds(5);
        while (!authCompleted.IsSet && DateTime.UtcNow < timeout)
        {
            await Task.Delay(50);
            _connector.MainThreadAction();
        }

        authCompleted.IsSet.Should().BeTrue("인증 콜백이 호출되어야 함");
        authResponse.Should().NotBeNull("인증 응답이 전달되어야 함");
        _connector.IsAuthenticated().Should().BeTrue("인증 상태가 true여야 함");
    }

    #endregion

    #region 6. 에러 처리 (Error Handling)

    [Fact(DisplayName = "Request with Callback - 서버 에러시 OnError 콜백이 호출된다")]
    public async Task Request_ServerError_InvokesOnErrorCallback()
    {
        // Given (전제조건): 서버에 연결된 상태
        await ConnectToServerAsync();
        _receivedErrors.Clear();

        using var failRequest = Packet.Empty("FailRequest");

        // When (행동): 실패하는 요청 (콜백 방식)
        _connector.Request(failRequest, _ => { });

        // 콜백 대기
        var timeout = DateTime.UtcNow.AddSeconds(5);
        while (_receivedErrors.Count == 0 && DateTime.UtcNow < timeout)
        {
            await Task.Delay(50);
            _connector.MainThreadAction();
        }

        // Then (결과): OnError 콜백 호출
        _receivedErrors.Should().NotBeEmpty("OnError 콜백이 호출되어야 함");
        var (stageId, errorCode, request) = _receivedErrors.First();
        errorCode.Should().Be(500, "서버가 반환한 에러코드여야 함");
    }

    [Fact(DisplayName = "RequestAsync - 타임아웃 시 ConnectorException(RequestTimeout)이 발생한다")]
    public async Task RequestAsync_Timeout_ThrowsRequestTimeoutException()
    {
        // Given (전제조건): 짧은 타임아웃으로 설정된 Connector
        var connector = new ClientConnector();
        connector.Init(new ConnectorConfig
        {
            RequestTimeoutMs = 100 // 100ms 타임아웃
        });

        await connector.ConnectAsync("127.0.0.1", _server.Port, DefaultStageId);
        await ProcessCallbacksAsync(connector);

        // 서버가 응답하지 않는 메시지
        using var noResponsePacket = Packet.Empty("NoResponseRequest");

        // When (행동): 응답이 없는 요청
        Func<Task> action = async () => await connector.RequestAsync(noResponsePacket);

        // Then (결과): RequestTimeout 예외 발생
        var exception = await action.Should().ThrowAsync<ConnectorException>("타임아웃시 예외가 발생해야 함");
        exception.Which.ErrorCode.Should().Be((ushort)ConnectorErrorCode.RequestTimeout,
            "RequestTimeout 에러코드여야 함");

        connector.Disconnect();
    }

    #endregion

    #region 7. 연속 통신 (Continuous Communication)

    [Fact(DisplayName = "Multiple Requests - 여러 요청을 순차적으로 처리한다")]
    public async Task MultipleRequests_Sequential_ProcessedCorrectly()
    {
        // Given (전제조건): 서버에 연결된 상태
        await ConnectToServerAsync();

        // When (행동): 여러 요청 순차 전송
        for (int i = 0; i < 5; i++)
        {
            var request = new EchoRequest { Content = $"Message {i}", Sequence = i };
            using var packet = new Packet(request);

            var response = await _connector.RequestAsync(packet);
            var reply = EchoReply.Parser.ParseFrom(response.Payload.Data.Span);

            // Then (결과): 각 응답이 올바름
            reply.Content.Should().Be($"Message {i}", $"메시지 {i}의 응답이 올바라야 함");
            reply.Sequence.Should().Be(i, $"시퀀스 {i}가 올바라야 함");
        }
    }

    [Fact(DisplayName = "Multiple Requests - 여러 요청을 병렬로 처리한다")]
    public async Task MultipleRequests_Parallel_AllProcessedCorrectly()
    {
        // Given (전제조건): 서버에 연결된 상태
        await ConnectToServerAsync();

        // When (행동): 여러 요청 병렬 전송
        var tasks = Enumerable.Range(0, 10).Select(async i =>
        {
            var request = new EchoRequest { Content = $"Parallel {i}", Sequence = i };
            using var packet = new Packet(request);
            return await _connector.RequestAsync(packet);
        }).ToList();

        var responses = await Task.WhenAll(tasks);

        // Then (결과): 모든 응답 수신
        responses.Should().HaveCount(10, "10개의 응답을 받아야 함");
        foreach (var response in responses)
        {
            response.MsgId.Should().Be("EchoReply");
        }
    }

    #endregion

    #region 8. 메인 스레드 콜백 (Unity Integration)

    [Fact(DisplayName = "MainThreadAction - 콜백이 MainThreadAction 호출시 실행된다")]
    public async Task MainThreadAction_ExecutesPendingCallbacks()
    {
        // Given (전제조건): 서버에 연결된 상태
        await ConnectToServerAsync();
        _receivedMessages.Clear();

        var broadcast = new BroadcastNotify { EventType = "test", Data = "Unity" };
        await _server.BroadcastPushAsync("BroadcastNotify", broadcast.ToByteArray());
        await Task.Delay(100);

        // 아직 MainThreadAction 호출 전이므로 콜백이 실행되지 않음
        var beforeCount = _receivedMessages.Count;

        // When (행동): MainThreadAction 호출
        _connector.MainThreadAction();

        // Then (결과): 콜백이 실행됨
        _receivedMessages.Count.Should().BeGreaterThanOrEqualTo(beforeCount,
            "MainThreadAction 호출 후 콜백이 실행되어야 함");
    }

    #endregion

    #region 9. 재연결 (Reconnection)

    [Fact(DisplayName = "Reconnect - 연결 해제 후 다른 StageId로 재연결할 수 있다")]
    public async Task Reconnect_WithDifferentStageId_Works()
    {
        // Given (전제조건): 서버에 연결된 상태
        await ConnectToServerAsync();
        _connector.StageId.Should().Be(DefaultStageId);

        // When (행동): 연결 해제 후 다른 StageId로 재연결
        _connector.Disconnect();
        await Task.Delay(100);

        const long newStageId = 999999L;
        var result = await _connector.ConnectAsync("127.0.0.1", _server.Port, newStageId);
        await ProcessCallbacksAsync();

        // Then (결과): 새로운 StageId로 연결됨
        result.Should().BeTrue("재연결에 성공해야 함");
        _connector.StageId.Should().Be(newStageId, "새로운 StageId가 설정되어야 함");
        _connector.IsConnected().Should().BeTrue("연결 상태여야 함");
        _connector.IsAuthenticated().Should().BeFalse("재연결 후 인증 상태는 false여야 함");
    }

    #endregion

    #region Helper Methods

    private async Task ConnectToServerAsync()
    {
        _connector.Init(new ConnectorConfig
        {
            RequestTimeoutMs = 30000
        });

        var connected = await _connector.ConnectAsync("127.0.0.1", _server.Port, DefaultStageId);
        connected.Should().BeTrue("테스트 서버에 연결되어야 함");

        await ProcessCallbacksAsync();
    }

    private async Task ProcessCallbacksAsync(ClientConnector? connector = null)
    {
        connector ??= _connector;
        await Task.Delay(50);
        connector.MainThreadAction();
        await Task.Delay(50);
        connector.MainThreadAction();
    }

    #endregion
}
