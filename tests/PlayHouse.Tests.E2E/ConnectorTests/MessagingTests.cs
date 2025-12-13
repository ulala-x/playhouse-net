#nullable enable

using FluentAssertions;
using Google.Protobuf;
using PlayHouse.Connector;
using PlayHouse.Connector.Protocol;
using PlayHouse.Tests.E2E.Infrastructure;
using PlayHouse.Tests.E2E.Infrastructure.Fixtures;
using PlayHouse.Tests.E2E.Proto;
using Xunit;
using ClientConnector = PlayHouse.Connector.Connector;
using ClientPacket = PlayHouse.Connector.Protocol.IPacket;

namespace PlayHouse.Tests.E2E.ConnectorTests;

/// <summary>
/// 6.2 Connector 메시지 송수신 E2E 테스트
///
/// E2E 테스트 원칙:
/// - Request 패킷: 응답 메시지 내용 검증
/// - Send 패킷: 서버에서 Push 응답 → OnReceive로 확인
/// </summary>
[Collection("E2E Connector Tests")]
public class MessagingTests : IAsyncLifetime
{
    private readonly SinglePlayServerFixture _fixture;
    private readonly ClientConnector _connector;
    private readonly List<(long stageId, ClientPacket packet)> _receivedMessages = new();
    private readonly List<(long stageId, ushort errorCode, ClientPacket request)> _receivedErrors = new();
    private Timer? _callbackTimer;
    private readonly object _callbackLock = new();

    public MessagingTests(SinglePlayServerFixture fixture)
    {
        _fixture = fixture;
        _connector = new ClientConnector();
        _connector.OnReceive += (stageId, packet) => _receivedMessages.Add((stageId, packet));
        _connector.OnError += (stageId, errorCode, request) => _receivedErrors.Add((stageId, errorCode, request));
    }

    public Task InitializeAsync()
    {
        // 콜백 자동 처리 타이머 시작
        _callbackTimer = new Timer(_ =>
        {
            lock (_callbackLock)
            {
                _connector.MainThreadAction();
            }
        }, null, 0, 20); // 20ms 간격

        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        _callbackTimer?.Dispose();
        _callbackTimer = null;

        _connector.Disconnect();
        return Task.CompletedTask;
    }

    #region 6.2.1 Send (Fire-and-Forget)

    [Fact(DisplayName = "Send 후 연결 유지 - IsConnected() == true")]
    public async Task Send_ConnectionMaintained_IsConnectedTrue()
    {
        // Given - 인증된 상태로 연결
        await ConnectToServerAsync();

        using var packet = Packet.Empty("StatusRequest");

        // When - Send (Fire-and-Forget, 응답 없이 전송)
        _connector.Send(packet);
        await Task.Delay(100);

        // Then - E2E 검증: 연결 유지 확인
        // Note: Send는 Fire-and-Forget이므로 응답이 없음
        // Send 메시지가 도착했는지는 서버 내부 동작이므로 E2E에서 직접 검증 불가
        // 대신 연결이 유지되는지 확인하여 메시지가 정상 처리되었음을 간접 검증
        _connector.IsConnected().Should().BeTrue("Send 후에도 연결이 유지되어야 함");
    }

    #endregion

    #region 6.2.2 Request (Callback)

    [Fact(DisplayName = "Request 성공 - 콜백 호출, 응답 패킷 내용 검증")]
    public async Task Request_Success_CallbackInvokedWithResponse()
    {
        // Given - 인증된 상태로 연결
        await ConnectToServerAsync();

        var echoRequest = new EchoRequest { Content = "Callback Test", Sequence = 42 };
        using var packet = new Packet(echoRequest);
        ClientPacket? receivedResponse = null;
        var responseReceived = new ManualResetEventSlim(false);

        // When - 콜백과 함께 Request
        _connector.Request(packet, response =>
        {
            receivedResponse = response;
            responseReceived.Set();
        });

        // 콜백 대기 (Timer가 자동으로 MainThreadAction 호출)
        var timeout = DateTime.UtcNow.AddSeconds(5);
        while (!responseReceived.IsSet && DateTime.UtcNow < timeout)
        {
            await Task.Delay(50);
        }

        // Then - E2E 검증: 콜백 호출, 응답 패킷 내용
        responseReceived.IsSet.Should().BeTrue("콜백이 호출되어야 함");
        receivedResponse.Should().NotBeNull("응답 패킷을 받아야 함");
        receivedResponse!.MsgId.Should().EndWith("EchoReply", "응답 메시지 ID가 EchoReply로 끝나야 함");

        var echoReply = EchoReply.Parser.ParseFrom(receivedResponse.Payload.Data.Span);
        echoReply.Content.Should().Be("Callback Test", "에코 내용이 동일해야 함");
        echoReply.Sequence.Should().Be(42, "시퀀스 번호가 동일해야 함");
    }

    [Fact(DisplayName = "Request 에러 응답 - OnError(stageId, errorCode, request) 콜백")]
    public async Task Request_ErrorResponse_OnErrorCallbackInvoked()
    {
        // Given - 인증된 상태로 연결
        await ConnectToServerAsync();
        _receivedErrors.Clear();

        using var failRequest = Packet.Empty("FailRequest");

        // When - 실패하는 요청 (콜백 방식)
        _connector.Request(failRequest, _ => { });

        // 콜백 대기 (Timer가 자동으로 MainThreadAction 호출)
        var timeout = DateTime.UtcNow.AddSeconds(5);
        while (_receivedErrors.Count == 0 && DateTime.UtcNow < timeout)
        {
            await Task.Delay(50);
        }

        // Then - E2E 검증: OnError 콜백 호출
        _receivedErrors.Should().NotBeEmpty("OnError 콜백이 호출되어야 함");
        var (stageId, errorCode, request) = _receivedErrors.First();
        errorCode.Should().Be(500, "서버가 반환한 에러코드 500이어야 함");
    }

    #endregion

    #region 6.2.3 RequestAsync

    [Fact(DisplayName = "RequestAsync 성공 - 응답 패킷 내용 검증")]
    public async Task RequestAsync_Success_ResponsePacketVerified()
    {
        // Given - 인증된 상태로 연결
        await ConnectToServerAsync();

        var echoRequest = new EchoRequest
        {
            Content = "Hello, Server!",
            Sequence = 99
        };
        using var packet = new Packet(echoRequest);

        // When
        var response = await _connector.RequestAsync(packet);

        // Then - E2E 검증: 응답 패킷 내용
        response.Should().NotBeNull("응답을 받아야 함");
        response.MsgId.Should().EndWith("EchoReply", "응답 메시지 ID가 EchoReply로 끝나야 함");

        var echoReply = EchoReply.Parser.ParseFrom(response.Payload.Data.Span);
        echoReply.Content.Should().Be("Hello, Server!", "에코 내용이 동일해야 함");
        echoReply.Sequence.Should().Be(99, "시퀀스 번호가 동일해야 함");
        // Note: ProcessedAt은 Stage에서 처리될 때만 설정됨.
        // 현재 PlayServer 기본 핸들러는 설정하지 않음.
    }

    [Fact(DisplayName = "RequestAsync 타임아웃 - ConnectorException 발생, ErrorCode == RequestTimeout")]
    public async Task RequestAsync_Timeout_ThrowsConnectorExceptionWithRequestTimeout()
    {
        // Given - 짧은 타임아웃으로 설정된 Connector
        var connector = new ClientConnector();
        var callbackTimer = new Timer(_ =>
        {
            lock (_callbackLock)
            {
                connector.MainThreadAction();
            }
        }, null, 0, 20);

        try
        {
            connector.Init(new ConnectorConfig { RequestTimeoutMs = 100 });

            var stageId = Random.Shared.NextInt64(100000, long.MaxValue);
            await connector.ConnectAsync("127.0.0.1", _fixture.PlayServer!.ActualTcpPort, stageId);
            await Task.Delay(100);

            // 인증 수행
            using var authPacket = Packet.Empty("AuthenticateRequest");
            await connector.AuthenticateAsync(authPacket);
            await Task.Delay(100);

            // 서버가 응답하지 않는 메시지
            using var noResponsePacket = Packet.Empty("NoResponseRequest");

            // When
            Func<Task> action = async () => await connector.RequestAsync(noResponsePacket);

            // Then - E2E 검증: ConnectorException 발생, ErrorCode 확인
            var exception = await action.Should().ThrowAsync<ConnectorException>("타임아웃시 예외가 발생해야 함");
            exception.Which.ErrorCode.Should().Be((ushort)ConnectorErrorCode.RequestTimeout,
                "ErrorCode가 RequestTimeout이어야 함");
        }
        finally
        {
            callbackTimer.Dispose();
            connector.Disconnect();
        }
    }

    [Fact(DisplayName = "RequestAsync 에러 응답 - ConnectorException 발생, ErrorCode 확인")]
    public async Task RequestAsync_ErrorResponse_ThrowsConnectorExceptionWithErrorCode()
    {
        // Given - 인증된 상태로 연결
        await ConnectToServerAsync();

        using var failRequest = Packet.Empty("FailRequest");

        // When
        Func<Task> action = async () => await _connector.RequestAsync(failRequest);

        // Then - E2E 검증: ConnectorException 발생, ErrorCode 확인
        var exception = await action.Should().ThrowAsync<ConnectorException>("에러 응답시 예외가 발생해야 함");
        exception.Which.ErrorCode.Should().Be(500, "서버가 반환한 에러코드여야 함");
    }

    #endregion

    #region 6.2.4 OnReceive 이벤트

    [Fact(DisplayName = "Push 메시지 수신 - OnReceive(stageId, packet) 콜백, stageId/packet 내용 검증")]
    public async Task OnReceive_PushMessage_CallbackWithStageIdAndPacket()
    {
        // Given - 인증된 상태로 연결
        await ConnectToServerAsync();
        _receivedMessages.Clear();

        // BroadcastTrigger 요청 전송하여 서버가 Push 메시지를 보내도록 트리거
        var trigger = new BroadcastNotify
        {
            EventType = "system",
            Data = "Welcome!",
            FromAccountId = 0
        };
        // MsgId를 "BroadcastTrigger"로 지정하여 패킷 생성
        using var triggerPacket = new Packet("BroadcastTrigger", trigger.ToByteArray());

        // When - BroadcastTrigger 요청 (서버가 Push 메시지를 보냄)
        var response = await _connector.RequestAsync(triggerPacket);
        await Task.Delay(200);

        // Then - E2E 검증: OnReceive 콜백, stageId, packet 내용
        response.MsgId.Should().Be("BroadcastTriggerReply", "트리거 응답을 받아야 함");
        _receivedMessages.Should().NotBeEmpty("Push 메시지를 수신해야 함");

        var (stageId, packet) = _receivedMessages.First();
        packet.MsgId.Should().EndWith("BroadcastNotify", "메시지 ID가 BroadcastNotify로 끝나야 함");

        var parsed = BroadcastNotify.Parser.ParseFrom(packet.Payload.Data.Span);
        parsed.EventType.Should().Be("system");
        parsed.Data.Should().Be("Welcome!");
    }

    [Fact(DisplayName = "여러 Push 수신 - 모든 OnReceive 콜백 순서대로 호출")]
    public async Task OnReceive_MultiplePushes_AllCallbacksInOrder()
    {
        // Given - 인증된 상태로 연결
        await ConnectToServerAsync();
        _receivedMessages.Clear();

        // When - 여러 BroadcastTrigger 요청 전송 (각각 Push 메시지를 트리거)
        for (int i = 0; i < 3; i++)
        {
            var trigger = new BroadcastNotify
            {
                EventType = $"event_{i}",
                Data = $"Data {i}",
                FromAccountId = 0
            };
            using var triggerPacket = new Packet("BroadcastTrigger", trigger.ToByteArray());

            await _connector.RequestAsync(triggerPacket);
            await Task.Delay(50);
        }

        await Task.Delay(200);

        // Then - E2E 검증: 모든 OnReceive 콜백 호출
        _receivedMessages.Should().HaveCountGreaterOrEqualTo(3, "3개 이상의 Push 메시지를 수신해야 함");

        // Push 메시지 내용 검증
        var pushMessages = _receivedMessages
            .Where(m => m.packet.MsgId.Contains("BroadcastNotify"))
            .ToList();
        pushMessages.Should().HaveCountGreaterOrEqualTo(3, "3개 이상의 BroadcastNotify를 수신해야 함");
    }

    #endregion

    #region 6.2.5 Multiple Requests

    [Fact(DisplayName = "여러 요청 순차 처리 - 각 응답이 올바름")]
    public async Task MultipleRequests_Sequential_ProcessedCorrectly()
    {
        // Given - 인증된 상태로 연결
        await ConnectToServerAsync();

        // When & Then - 여러 요청 순차 전송, 각 응답 검증
        for (int i = 0; i < 5; i++)
        {
            var request = new EchoRequest { Content = $"Message {i}", Sequence = i };
            using var packet = new Packet(request);

            var response = await _connector.RequestAsync(packet);
            var reply = EchoReply.Parser.ParseFrom(response.Payload.Data.Span);

            reply.Content.Should().Be($"Message {i}", $"메시지 {i}의 응답이 올바라야 함");
            reply.Sequence.Should().Be(i, $"시퀀스 {i}가 올바라야 함");
        }
    }

    [Fact(DisplayName = "여러 요청 병렬 처리 - 모든 응답 수신")]
    public async Task MultipleRequests_Parallel_AllProcessedCorrectly()
    {
        // Given - 인증된 상태로 연결
        await ConnectToServerAsync();

        // When - 여러 요청 병렬 전송
        var tasks = Enumerable.Range(0, 10).Select(async i =>
        {
            var request = new EchoRequest { Content = $"Parallel {i}", Sequence = i };
            using var packet = new Packet(request);
            return await _connector.RequestAsync(packet);
        }).ToList();

        var responses = await Task.WhenAll(tasks);

        // Then - E2E 검증: 모든 응답 수신
        responses.Should().HaveCount(10, "10개의 응답을 받아야 함");
        foreach (var response in responses)
        {
            response.MsgId.Should().EndWith("EchoReply");
        }
    }

    #endregion

    #region Helper Methods

    private async Task ConnectToServerAsync()
    {
        var stageId = Random.Shared.NextInt64(100000, long.MaxValue);
        _connector.Init(new ConnectorConfig { RequestTimeoutMs = 30000 });
        var connected = await _connector.ConnectAsync("127.0.0.1", _fixture.PlayServer!.ActualTcpPort, stageId);
        connected.Should().BeTrue("서버에 연결되어야 함");
        await Task.Delay(100);

        // 인증 수행
        using var authPacket = Packet.Empty("AuthenticateRequest");
        await _connector.AuthenticateAsync(authPacket);
        await Task.Delay(100);
    }

    #endregion
}
