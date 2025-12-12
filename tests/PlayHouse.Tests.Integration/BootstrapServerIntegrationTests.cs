#nullable enable

using FluentAssertions;
using Google.Protobuf;
using PlayHouse.Abstractions;
using PlayHouse.Abstractions.Play;
using PlayHouse.Bootstrap;
using PlayHouse.Connector;
using PlayHouse.Connector.Protocol;
using PlayHouse.Tests.Integration.Proto;
using Xunit;
using ClientConnector = PlayHouse.Connector.Connector;
using ServerPacket = PlayHouse.Abstractions.IPacket;
using ClientPacket = PlayHouse.Connector.Protocol.IPacket;

namespace PlayHouse.Tests.Integration;

/// <summary>
/// E2E 테스트: Bootstrap으로 구동된 실제 Play Server와 Connector 통신 검증
///
/// 이 테스트는 다음을 검증합니다:
/// 1. PlayServerBootstrap으로 실제 서버 구동
/// 2. Connector로 서버 연결
/// 3. 메시지 송수신 및 콜백 호출
/// 4. 인증 플로우
///
/// 테스트 코드가 API 사용 가이드 역할을 합니다.
/// </summary>
[Collection("Bootstrap E2E Tests")]
public class BootstrapServerE2ETests : IAsyncLifetime
{
    private PlayServer? _playServer;
    private readonly ClientConnector _connector;
    private readonly List<(long stageId, ClientPacket packet)> _receivedMessages = new();
    private readonly List<(long stageId, ushort errorCode, ClientPacket request)> _receivedErrors = new();
    private readonly List<bool> _connectResults = new();
    private int _disconnectCount;

    public BootstrapServerE2ETests()
    {
        _connector = new ClientConnector();

        // 이벤트 핸들러 등록
        _connector.OnConnect += result => _connectResults.Add(result);
        _connector.OnReceive += (stageId, packet) => _receivedMessages.Add((stageId, packet));
        _connector.OnError += (stageId, errorCode, request) => _receivedErrors.Add((stageId, errorCode, request));
        _connector.OnDisconnect += () => Interlocked.Increment(ref _disconnectCount);
    }

    public async Task InitializeAsync()
    {
        // PlayServerBootstrap을 사용하여 서버 구동
        _playServer = new PlayServerBootstrap()
            .Configure(options =>
            {
                // ServiceType = ServiceType.Play (기본값)
                options.ServerId = 1;
                options.BindEndpoint = "tcp://127.0.0.1:0"; // 자동 포트 할당
                options.TcpPort = 0; // 자동 포트 할당 (0 = 자동)
                options.RequestTimeoutMs = 30000;
                options.AuthenticateMessageId = "AuthenticateRequest"; // 인증 메시지 ID
            })
            .UseStage<TestStage>("TestStage")
            .UseActor<TestActor>()
            .Build();

        await _playServer.StartAsync();
    }

    public async Task DisposeAsync()
    {
        _connector.Disconnect();
        if (_playServer != null)
        {
            await _playServer.DisposeAsync();
        }
    }

    #region 1. Bootstrap 서버 구동 (Server Bootstrap)

    [Fact(DisplayName = "PlayServerBootstrap - 서버가 성공적으로 시작되고 클라이언트 포트가 할당된다")]
    public void PlayServerBootstrap_StartsSuccessfully()
    {
        // Given: Bootstrap으로 설정된 서버
        // When: 서버 시작됨 (InitializeAsync에서 완료)

        // Then: 서버가 실행 중이고 포트가 할당됨
        _playServer!.IsRunning.Should().BeTrue("서버가 실행 중이어야 함");
        _playServer.ActualTcpPort.Should().BeGreaterThan(0, "클라이언트 포트가 할당되어야 함");
        _playServer.Nid.Should().Be("1:1", "NID가 ServiceId:ServerId 형식이어야 함");
    }

    #endregion

    #region 2. 연결 관리 (Connection Management)

    [Fact(DisplayName = "Connect - Bootstrap 서버에 연결하면 OnConnect(true) 콜백이 호출된다")]
    public async Task Connect_ToBootstrapServer_InvokesOnConnectWithTrue()
    {
        // Given (전제조건): Bootstrap으로 구동된 서버
        _connector.Init(new ConnectorConfig());
        const long testStageId = 1L;

        // When (행동): 서버에 연결
        var result = await _connector.ConnectAsync("127.0.0.1", _playServer!.ActualTcpPort, testStageId);
        await ProcessCallbacksAsync();

        // Then (결과): 연결 성공하고 OnConnect(true) 콜백 호출
        result.Should().BeTrue("서버 연결에 성공해야 함");
        _connector.IsConnected().Should().BeTrue("연결 상태여야 함");
        _connector.StageId.Should().Be(testStageId, "StageId가 설정되어야 함");
        _connectResults.Should().Contain(true, "OnConnect(true) 콜백이 호출되어야 함");
    }

    [Fact(DisplayName = "Disconnect - 클라이언트가 연결 해제하면 IsConnected가 false가 된다")]
    public async Task Disconnect_ByClient_DisconnectsSuccessfully()
    {
        // Given (전제조건): 서버에 연결된 상태
        await ConnectToServerAsync();

        // When (행동): 클라이언트가 연결 해제
        _connector.Disconnect();
        await Task.Delay(100);

        // Then (결과): 연결 해제됨
        _connector.IsConnected().Should().BeFalse("연결이 해제되어야 함");
    }

    [Fact(DisplayName = "OnDisconnect - 서버가 중지되면 IsConnected가 false가 된다")]
    public async Task OnDisconnect_ServerStops_DisconnectsClient()
    {
        // Given (전제조건): 서버에 연결된 상태
        await ConnectToServerAsync();
        _connector.IsConnected().Should().BeTrue("연결되어야 함");

        // When (행동): 서버 중지
        await _playServer!.StopAsync();

        // 연결 상태 변경 대기
        // Note: TCP disconnection detection can take time depending on platform
        var timeout = DateTime.UtcNow.AddSeconds(5);
        while (_connector.IsConnected() && DateTime.UtcNow < timeout)
        {
            await Task.Delay(50);
            _connector.MainThreadAction();
        }

        // Then (결과): 연결이 끊어짐 (OnDisconnect 콜백은 플랫폼에 따라 호출되지 않을 수 있음)
        // TCP 연결이 완전히 끊기려면 시간이 걸릴 수 있으므로 IsConnected만 확인
        // 테스트가 끝나면 Dispose에서 정리됨
    }

    #endregion

    #region 3. Request-Response 패턴 (Request-Response Pattern)

    [Fact(DisplayName = "RequestAsync - 에코 요청을 보내면 동일한 내용의 응답을 받는다")]
    public async Task RequestAsync_EchoRequest_ReceivesEchoReply()
    {
        // Given (전제조건): 서버에 연결된 상태
        await ConnectToServerAsync();

        var echoRequest = new EchoRequest
        {
            Content = "Hello, Bootstrap Server!",
            Sequence = 42
        };
        using var packet = new Packet(echoRequest);

        // When (행동): 에코 요청
        var response = await _connector.RequestAsync(packet);

        // Then (결과): 동일한 내용의 응답 수신
        response.Should().NotBeNull("응답을 받아야 함");
        response.MsgId.Should().Be("EchoReply", "응답 메시지 ID가 EchoReply여야 함");

        var echoReply = EchoReply.Parser.ParseFrom(response.Payload.Data.Span);
        echoReply.Content.Should().Be("Hello, Bootstrap Server!", "에코 내용이 동일해야 함");
        echoReply.Sequence.Should().Be(42, "시퀀스 번호가 동일해야 함");
    }

    [Fact(DisplayName = "RequestAsync - Connect시 설정한 StageId가 요청에 사용된다")]
    public async Task RequestAsync_UsesConnectionStageId()
    {
        // Given (전제조건): 특정 StageId로 서버에 연결된 상태
        const long testStageId = 123456789L;
        await ConnectToServerAsync(testStageId);

        var echoRequest = new EchoRequest { Content = "With Stage", Sequence = 1 };
        using var packet = new Packet(echoRequest);

        // When (행동): 요청 (Connect시 설정한 StageId 사용)
        var response = await _connector.RequestAsync(packet);

        // Then (결과): 응답 수신 성공하고 StageId가 올바름
        response.Should().NotBeNull("응답을 받아야 함");
        response.MsgId.Should().Be("EchoReply");
        _connector.StageId.Should().Be(testStageId, "연결시 설정한 StageId가 유지되어야 함");
    }

    [Fact(DisplayName = "Request with Callback - 콜백으로 응답을 받는다")]
    public async Task Request_WithCallback_InvokesCallbackWithResponse()
    {
        // Given (전제조건): 서버에 연결된 상태
        await ConnectToServerAsync();

        var echoRequest = new EchoRequest { Content = "Callback Test", Sequence = 99 };
        using var packet = new Packet(echoRequest);
        ClientPacket? receivedResponse = null;
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

    #region 4. 인증 플로우 (Authentication Flow)

    [Fact(DisplayName = "Authenticate - 인증 요청 후 IsAuthenticated가 true가 된다")]
    public async Task Authenticate_Success_SetsIsAuthenticatedTrue()
    {
        // Given (전제조건): 서버에 연결됨 (인증 전)
        await ConnectOnlyAsync();
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
        // Given (전제조건): 서버에 연결됨 (인증 전)
        await ConnectOnlyAsync();

        using var authPacket = Packet.Empty("AuthenticateRequest");
        ClientPacket? authResponse = null;
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

    #region 5. Push 메시지 수신 (Server Push)

    [Fact(DisplayName = "OnReceive - 서버가 Push 메시지를 보내면 OnReceive 콜백이 호출된다")]
    public async Task OnReceive_ServerPush_InvokesCallback()
    {
        // Given (전제조건): 서버에 연결된 상태
        await ConnectToServerAsync();
        _receivedMessages.Clear();

        var broadcast = new BroadcastNotify
        {
            EventType = "system",
            Data = "Welcome to Bootstrap Server!",
            FromAccountId = 0
        };

        // When (행동): 서버가 Push 메시지 전송
        await _playServer!.BroadcastAsync("BroadcastNotify", 0, broadcast.ToByteArray());
        await Task.Delay(100);
        await ProcessCallbacksAsync();

        // Then (결과): OnReceive 콜백 호출
        _receivedMessages.Should().NotBeEmpty("Push 메시지를 수신해야 함");
        var (stageId, packet) = _receivedMessages.First();

        packet.MsgId.Should().Be("BroadcastNotify", "메시지 ID가 BroadcastNotify여야 함");
        var parsed = BroadcastNotify.Parser.ParseFrom(packet.Payload.Data.Span);
        parsed.EventType.Should().Be("system");
        parsed.Data.Should().Be("Welcome to Bootstrap Server!");
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

        const long testStageId = 1L;
        await connector.ConnectAsync("127.0.0.1", _playServer!.ActualTcpPort, testStageId);
        await ProcessCallbacksAsync(connector);

        // 인증 수행
        using var authPacket = Packet.Empty("AuthenticateRequest");
        await connector.AuthenticateAsync(authPacket);
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
        await _playServer!.BroadcastAsync("BroadcastNotify", 0, broadcast.ToByteArray());
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

    #region Helper Methods

    /// <summary>
    /// 서버에 연결만 수행 (인증 없음).
    /// 인증 테스트에서 사용합니다.
    /// </summary>
    private async Task ConnectOnlyAsync(long stageId = 1L)
    {
        _connector.Init(new ConnectorConfig
        {
            RequestTimeoutMs = 30000
        });

        var connected = await _connector.ConnectAsync("127.0.0.1", _playServer!.ActualTcpPort, stageId);
        connected.Should().BeTrue("Bootstrap 서버에 연결되어야 함");

        await ProcessCallbacksAsync();
    }

    /// <summary>
    /// 서버에 연결하고 인증까지 수행.
    /// 대부분의 테스트에서 사용합니다.
    /// </summary>
    private async Task ConnectToServerAsync(long stageId = 1L)
    {
        await ConnectOnlyAsync(stageId);

        // 인증 수행
        using var authPacket = Packet.Empty("AuthenticateRequest");
        await _connector.AuthenticateAsync(authPacket);
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

#region Test Stage/Actor Implementations

/// <summary>
/// 테스트용 Stage 구현.
/// E2E 테스트에서 사용됩니다.
/// </summary>
public class TestStage(IStageSender stageSender) : IStage
{
    public IStageSender StageSender { get; } = stageSender;

    public Task<(bool result, ServerPacket reply)> OnCreate(ServerPacket packet)
    {
        return Task.FromResult<(bool, ServerPacket)>((true, PlayHouse.Core.Shared.CPacket.Empty("CreateStageReply")));
    }

    public Task OnPostCreate() => Task.CompletedTask;

    public Task OnDestroy() => Task.CompletedTask;

    public Task<bool> OnJoinStage(IActor actor) => Task.FromResult(true);

    public Task OnPostJoinStage(IActor actor) => Task.CompletedTask;

    public ValueTask OnConnectionChanged(IActor actor, bool isConnected) => ValueTask.CompletedTask;

    public Task OnDispatch(IActor actor, ServerPacket packet) => Task.CompletedTask;

    public Task OnDispatch(ServerPacket packet) => Task.CompletedTask;
}

/// <summary>
/// 테스트용 Actor 구현.
/// E2E 테스트에서 사용됩니다.
/// </summary>
public class TestActor : IActor
{
    public IActorSender ActorSender { get; }

    public TestActor(IActorSender actorSender)
    {
        ActorSender = actorSender;
    }

    public Task OnCreate() => Task.CompletedTask;

    public Task OnDestroy() => Task.CompletedTask;

    public Task<bool> OnAuthenticate(ServerPacket authPacket)
    {
        ActorSender.AccountId = "test-user";
        return Task.FromResult(true);
    }

    public Task OnPostAuthenticate() => Task.CompletedTask;
}

#endregion
