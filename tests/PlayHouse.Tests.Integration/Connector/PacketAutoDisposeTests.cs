#nullable enable

using FluentAssertions;
using PlayHouse.Connector;
using PlayHouse.Connector.Protocol;
using PlayHouse.Tests.Integration.Infrastructure.Fixtures;
using PlayHouse.Tests.Integration.Proto;
using Xunit;
using ClientConnector = PlayHouse.Connector.Connector;
using ClientPacket = PlayHouse.Connector.Protocol.IPacket;

namespace PlayHouse.Tests.Integration.Connector;

/// <summary>
/// Connector Reply 패킷 자동 Dispose E2E 테스트
///
/// E2E 테스트 원칙:
/// - Connector의 패킷 자동 dispose 기능을 검증
/// - Callback 패턴: 콜백 완료 후 자동 dispose
/// - Push 메시지: OnReceive 콜백 완료 후 자동 dispose
/// - Async 패턴: 호출자가 dispose 책임 확인
/// </summary>
[Collection("E2E Connector Tests")]
public class PacketAutoDisposeTests : IAsyncLifetime
{
    private readonly SinglePlayServerFixture _fixture;
    private readonly ClientConnector _connector;
    private readonly List<(long stageId, string stageType, string msgId, byte[] payloadData)> _receivedMessages = new();
    private Timer? _callbackTimer;
    private readonly object _callbackLock = new();

    public PacketAutoDisposeTests(SinglePlayServerFixture fixture)
    {
        _fixture = fixture;
        _connector = new ClientConnector();
        _connector.OnReceive += (stageId, stageType, packet) =>
        {
            // 콜백 내에서 데이터를 복사하여 저장 (콜백 외부에서 패킷 접근 불가)
            var msgId = packet.MsgId;
            var payloadData = packet.Payload.DataSpan.ToArray();
            _receivedMessages.Add((stageId, stageType, msgId, payloadData));
        };
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

    #region Callback 패턴 - 콜백 완료 후 자동 dispose

    /// <summary>
    /// Callback 패턴에서 응답 패킷이 자동으로 dispose되는지 검증
    ///
    /// ClientNetwork.OnPacketReceived (line 398-412):
    /// - pending.Callback(packet) 실행 후
    /// - finally 블록에서 packet.Dispose() 자동 호출
    /// </summary>
    /// <remarks>
    /// E2E 검증 방법:
    /// - 콜백에서 응답 받은 후, 정상적으로 처리되는지 확인
    /// - 자동 dispose로 인한 에러가 발생하지 않는지 확인
    /// - 연결이 정상적으로 유지되는지 확인
    /// </remarks>
    [Fact(DisplayName = "Callback 패턴 - 콜백 완료 후 패킷 자동 dispose, 연결 유지")]
    public async Task Request_CallbackPattern_AutoDisposesPacketAfterCallback()
    {
        // Given - 서버에 연결 및 인증
        await ConnectToServerAsync();

        // When - Callback 패턴으로 요청
        var echoRequest = new EchoRequest { Content = "Callback Test", Sequence = 1 };
        using var packet = new Packet(echoRequest);

        ClientPacket? receivedResponse = null;
        var responseReceived = new ManualResetEventSlim(false);

        _connector.Request(packet, response =>
        {
            // 콜백 내에서 응답 패킷 사용
            receivedResponse = response;
            var echoReply = EchoReply.Parser.ParseFrom(response.Payload.DataSpan);
            echoReply.Content.Should().Be("Callback Test", "응답 내용이 일치해야 함");
            responseReceived.Set();

            // 콜백이 끝나면 ClientNetwork가 자동으로 packet.Dispose() 호출
        });

        // 콜백 대기 (Timer가 자동으로 MainThreadAction 호출)
        var timeout = DateTime.UtcNow.AddSeconds(5);
        while (!responseReceived.IsSet && DateTime.UtcNow < timeout)
        {
            await Task.Delay(50);
        }

        // Then - E2E 검증: 응답을 정상적으로 받았고, 연결이 유지됨
        responseReceived.IsSet.Should().BeTrue("콜백이 호출되어야 함");
        receivedResponse.Should().NotBeNull("응답 패킷을 받아야 함");
        _connector.IsConnected().Should().BeTrue("자동 dispose 후에도 연결이 유지되어야 함");

        // 추가 요청이 정상적으로 동작하는지 확인
        var echoRequest2 = new EchoRequest { Content = "Second Request", Sequence = 2 };
        using var packet2 = new Packet(echoRequest2);
        var response2 = await _connector.RequestAsync(packet2);

        response2.Should().NotBeNull("자동 dispose 후에도 추가 요청이 가능해야 함");
        response2.MsgId.Should().EndWith("EchoReply");
    }

    /// <summary>
    /// 여러 Callback 요청이 모두 자동 dispose되는지 검증
    /// </summary>
    [Fact(DisplayName = "여러 Callback 요청 - 모든 응답 패킷 자동 dispose, 연결 유지")]
    public async Task MultipleRequests_CallbackPattern_AllPacketsAutoDisposed()
    {
        // Given - 서버에 연결 및 인증
        await ConnectToServerAsync();

        // When - 여러 Callback 요청
        var responseCount = 0;
        var allReceived = new ManualResetEventSlim(false);
        var expectedCount = 5;

        for (int i = 0; i < expectedCount; i++)
        {
            var request = new EchoRequest { Content = $"Message {i}", Sequence = i };
            using var packet = new Packet(request);

            _connector.Request(packet, response =>
            {
                var reply = EchoReply.Parser.ParseFrom(response.Payload.DataSpan);
                reply.Content.Should().StartWith("Message", "응답 내용이 일치해야 함");

                if (Interlocked.Increment(ref responseCount) == expectedCount)
                {
                    allReceived.Set();
                }
            });
        }

        // 모든 콜백 대기
        var timeout = DateTime.UtcNow.AddSeconds(5);
        while (!allReceived.IsSet && DateTime.UtcNow < timeout)
        {
            await Task.Delay(50);
        }

        // Then - E2E 검증
        responseCount.Should().Be(expectedCount, $"{expectedCount}개의 응답을 모두 받아야 함");
        _connector.IsConnected().Should().BeTrue("모든 패킷 자동 dispose 후에도 연결이 유지되어야 함");
    }

    #endregion

    #region Push 메시지 - OnReceive 콜백 완료 후 자동 dispose

    /// <summary>
    /// Push 메시지가 OnReceive 콜백 완료 후 자동 dispose되는지 검증
    ///
    /// ClientNetwork.OnPacketReceived (line 418-429):
    /// - _callback.ReceiveCallback(stageId, packet) 실행 후
    /// - finally 블록에서 packet.Dispose() 자동 호출
    /// </summary>
    /// <remarks>
    /// E2E 검증 방법:
    /// - OnReceive 콜백에서 수신한 메시지가 정상적으로 처리되는지 확인
    /// - 자동 dispose로 인한 에러가 발생하지 않는지 확인
    /// - 연결이 정상적으로 유지되는지 확인
    ///
    /// Note: Push 메시지는 PushTests.cs에서 상세히 테스트하므로
    /// 여기서는 OnReceive 이벤트 핸들러에 추가된 메시지가 dispose되는지만 확인
    /// </remarks>
    [Fact(DisplayName = "OnReceive 이벤트 - 메시지 수신 후 정상 동작, 연결 유지")]
    public async Task OnReceive_MessageReceived_NormalOperation()
    {
        // Given - 서버에 연결 및 인증
        await ConnectToServerAsync();
        _receivedMessages.Clear();

        // When - 여러 요청 전송
        for (int i = 0; i < 3; i++)
        {
            var echoRequest = new EchoRequest { Content = $"Test {i}", Sequence = i };
            using var packet = new Packet(echoRequest);
            await _connector.RequestAsync(packet);
        }

        await Task.Delay(200);

        // Then - E2E 검증: 연결 유지 및 추가 요청 가능
        _connector.IsConnected().Should().BeTrue("메시지 수신 후에도 연결이 유지되어야 함");

        // 추가 요청이 정상적으로 동작하는지 확인
        var finalRequest = new EchoRequest { Content = "After Messages", Sequence = 99 };
        using var finalPacket = new Packet(finalRequest);
        var echoResponse = await _connector.RequestAsync(finalPacket);

        echoResponse.Should().NotBeNull("추가 요청이 정상적으로 동작해야 함");
        echoResponse.MsgId.Should().EndWith("EchoReply");
    }

    #endregion

    #region Async 패턴 - 호출자가 dispose 책임

    /// <summary>
    /// Async 패턴에서 호출자가 패킷 dispose 책임을 가지는지 검증
    ///
    /// ClientNetwork.OnPacketReceived (line 393-396):
    /// - pending.Tcs.TrySetResult(packet) 호출
    /// - 주석: "Async pattern - caller owns the packet and is responsible for disposal"
    /// - 자동 dispose 하지 않음 (호출자가 using으로 감싸야 함)
    /// </summary>
    /// <remarks>
    /// E2E 검증 방법:
    /// - RequestAsync로 받은 패킷을 using으로 감싸서 사용하는 패턴 확인
    /// - 호출자가 명시적으로 dispose 해야 함
    /// </remarks>
    [Fact(DisplayName = "Async 패턴 - 호출자가 using으로 패킷 dispose 책임, 정상 동작")]
    public async Task RequestAsync_CallerResponsibleForDisposal()
    {
        // Given - 서버에 연결 및 인증
        await ConnectToServerAsync();

        // When - Async 패턴으로 요청 (호출자가 using으로 감싸서 dispose)
        var echoRequest = new EchoRequest { Content = "Async Test", Sequence = 10 };
        using var requestPacket = new Packet(echoRequest);

        // Async 패턴 - 응답 패킷을 using으로 감싸서 호출자가 dispose
        using var response = await _connector.RequestAsync(requestPacket);

        // Then - E2E 검증: 응답 패킷 사용
        response.Should().NotBeNull("응답 패킷을 받아야 함");
        response.MsgId.Should().EndWith("EchoReply");

        var echoReply = EchoReply.Parser.ParseFrom(response.Payload.DataSpan);
        echoReply.Content.Should().Be("Async Test", "응답 내용이 일치해야 함");
        echoReply.Sequence.Should().Be(10, "시퀀스가 일치해야 함");

        _connector.IsConnected().Should().BeTrue("연결이 유지되어야 함");

        // using 블록이 끝나면 자동으로 response.Dispose() 호출됨
    }

    /// <summary>
    /// Async 패턴에서 여러 요청이 정상적으로 처리되는지 검증
    /// </summary>
    [Fact(DisplayName = "여러 Async 요청 - 호출자가 각 패킷 dispose, 정상 동작")]
    public async Task MultipleRequestAsync_CallerDisposesAllPackets()
    {
        // Given - 서버에 연결 및 인증
        await ConnectToServerAsync();

        // When & Then - 여러 Async 요청, 각각 using으로 감싸서 dispose
        for (int i = 0; i < 5; i++)
        {
            var request = new EchoRequest { Content = $"Async {i}", Sequence = i };
            using var requestPacket = new Packet(request);

            // 호출자가 using으로 감싸서 dispose
            using var response = await _connector.RequestAsync(requestPacket);

            response.Should().NotBeNull($"응답 {i}를 받아야 함");
            response.MsgId.Should().EndWith("EchoReply");

            var reply = EchoReply.Parser.ParseFrom(response.Payload.DataSpan);
            reply.Content.Should().Be($"Async {i}", $"응답 {i} 내용이 일치해야 함");
            reply.Sequence.Should().Be(i, $"시퀀스 {i}가 일치해야 함");
        }

        // Then - E2E 검증: 모든 요청 후에도 연결 유지
        _connector.IsConnected().Should().BeTrue("모든 요청 처리 후에도 연결이 유지되어야 함");
    }

    /// <summary>
    /// Async 패턴과 Callback 패턴을 혼용해도 정상 동작하는지 검증
    /// </summary>
    [Fact(DisplayName = "Async + Callback 혼용 - 각 패턴의 dispose 규칙이 정상 동작")]
    public async Task MixedPattern_AsyncAndCallback_BothDisposeCorrectly()
    {
        // Given - 서버에 연결 및 인증
        await ConnectToServerAsync();

        // When - Async 패턴
        var asyncRequest = new EchoRequest { Content = "Async Mixed", Sequence = 100 };
        using var asyncPacket = new Packet(asyncRequest);
        using var asyncResponse = await _connector.RequestAsync(asyncPacket);

        asyncResponse.MsgId.Should().EndWith("EchoReply", "Async 응답을 받아야 함");

        // When - Callback 패턴
        var callbackRequest = new EchoRequest { Content = "Callback Mixed", Sequence = 200 };
        using var callbackPacket = new Packet(callbackRequest);

        var callbackReceived = new ManualResetEventSlim(false);
        _connector.Request(callbackPacket, response =>
        {
            response.MsgId.Should().EndWith("EchoReply", "Callback 응답을 받아야 함");
            callbackReceived.Set();
        });

        var timeout = DateTime.UtcNow.AddSeconds(5);
        while (!callbackReceived.IsSet && DateTime.UtcNow < timeout)
        {
            await Task.Delay(50);
        }

        // Then - E2E 검증
        callbackReceived.IsSet.Should().BeTrue("Callback이 호출되어야 함");
        _connector.IsConnected().Should().BeTrue("혼용 패턴 사용 후에도 연결이 유지되어야 함");
    }

    #endregion

    #region Helper Methods

    /// <summary>
    /// 서버에 연결 및 인증 수행.
    /// </summary>
    private async Task ConnectToServerAsync()
    {
        var stageId = Random.Shared.NextInt64(100000, long.MaxValue);
        _connector.Init(new ConnectorConfig { RequestTimeoutMs = 30000 });
        var connected = await _connector.ConnectAsync("127.0.0.1", _fixture.PlayServer!.ActualTcpPort, stageId, "TestStage");
        connected.Should().BeTrue("서버에 연결되어야 함");
        await Task.Delay(100);

        // 인증 수행
        using var authPacket = Packet.Empty("AuthenticateRequest");
        await _connector.AuthenticateAsync(authPacket);
        await Task.Delay(100);
    }

    #endregion
}
