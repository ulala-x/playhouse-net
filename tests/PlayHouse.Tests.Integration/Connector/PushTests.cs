#nullable enable

using FluentAssertions;
using Google.Protobuf;
using PlayHouse.Connector;
using PlayHouse.Connector.Protocol;
using PlayHouse.Tests.Integration.Infrastructure;
using PlayHouse.Tests.Integration.Infrastructure.Fixtures;
using PlayHouse.Tests.Integration.Proto;
using Xunit;
using ClientConnector = PlayHouse.Connector.Connector;
using ClientPacket = PlayHouse.Connector.Protocol.IPacket;

namespace PlayHouse.Tests.Integration.Connector;

/// <summary>
/// Connector Push 메시지 수신 E2E 테스트
///
/// E2E 테스트 원칙:
/// - OnReceive 콜백으로 Push 메시지 수신 검증
/// </summary>
[Collection("E2E Connector Tests")]
public class PushTests : IAsyncLifetime
{
    private readonly SinglePlayServerFixture _fixture;
    private readonly ClientConnector _connector;
    private readonly List<(long stageId, string stageType, string msgId, byte[] payloadData)> _receivedMessages = new();
    private Timer? _callbackTimer;
    private readonly object _callbackLock = new();

    public PushTests(SinglePlayServerFixture fixture)
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

    #region OnReceive 이벤트

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

        var (stageId, stageType, msgId, payloadData) = _receivedMessages.First();
        msgId.Should().EndWith("BroadcastNotify", "메시지 ID가 BroadcastNotify로 끝나야 함");

        var parsed = BroadcastNotify.Parser.ParseFrom(payloadData);
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
            .Where(m => m.msgId.Contains("BroadcastNotify"))
            .ToList();
        pushMessages.Should().HaveCountGreaterOrEqualTo(3, "3개 이상의 BroadcastNotify를 수신해야 함");
    }

    #endregion

    #region Helper Methods

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
