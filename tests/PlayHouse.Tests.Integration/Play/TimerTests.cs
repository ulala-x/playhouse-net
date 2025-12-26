#nullable enable

using FluentAssertions;
using PlayHouse.Connector;
using PlayHouse.Connector.Protocol;
using PlayHouse.Tests.Integration.Infrastructure;
using PlayHouse.Tests.Integration.Infrastructure.Fixtures;
using PlayHouse.Tests.Integration.Proto;
using Xunit;
using ClientConnector = PlayHouse.Connector.Connector;
using ClientPacket = PlayHouse.Connector.Protocol.IPacket;

namespace PlayHouse.Tests.Integration.Play;

/// <summary>
/// Timer E2E 테스트
///
/// 이 테스트는 PlayHouse의 IStageSender.AddRepeatTimer, AddCountTimer 사용법을 보여줍니다.
/// E2E 테스트 원칙:
/// - 응답 검증: StartTimerReply 메시지 수신, TimerId 확인
/// - Push 메시지 검증: OnReceive 콜백으로 TimerTickNotify 수신
/// </summary>
[Collection("E2E Connector Tests")]
public class TimerTests : IAsyncLifetime
{
    private readonly SinglePlayServerFixture _fixture;
    private readonly ClientConnector _connector;
    private readonly List<(long stageId, ClientPacket packet)> _receivedMessages = new();
    private Timer? _callbackTimer;
    private readonly object _callbackLock = new();

    public TimerTests(SinglePlayServerFixture fixture)
    {
        _fixture = fixture;
        _connector = new ClientConnector();
        _connector.OnReceive += (stageId, packet) => _receivedMessages.Add((stageId, packet));
    }

    public async Task InitializeAsync()
    {
        // 콜백 자동 처리 타이머 시작
        _callbackTimer = new Timer(_ =>
        {
            lock (_callbackLock)
            {
                _connector.MainThreadAction();
            }
        }, null, 0, 20); // 20ms 간격

        await Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        _callbackTimer?.Dispose();
        _callbackTimer = null;

        _connector.Disconnect();
        await Task.CompletedTask;
    }

    #region AddRepeatTimer 테스트

    /// <summary>
    /// IStageSender.AddRepeatTimer E2E 테스트
    ///
    /// 이 테스트는 반복 타이머가 설정한 주기로 실행되는지 검증합니다.
    /// </summary>
    /// <remarks>
    /// E2E 검증 방법:
    /// - 응답 검증: StartTimerReply 메시지 수신, TimerId 확인
    /// - Push 메시지 검증: OnReceive 콜백으로 여러 TimerTickNotify 수신
    /// </remarks>
    [Fact(DisplayName = "AddRepeatTimer - 반복 타이머 실행, 여러 Tick 수신")]
    public async Task AddRepeatTimer_ExecutesRepeatedly_ReceivesMultipleTicks()
    {
        // Given - 서버에 연결 및 인증
        var stageId = await ConnectToServerAsync();
        _receivedMessages.Clear();

        // When - RepeatTimer 시작 요청 (100ms 초기 대기, 100ms 간격)
        var request = new StartRepeatTimerRequest
        {
            InitialDelayMs = 100,
            IntervalMs = 100
        };
        using var packet = new Packet(request);
        var response = await _connector.RequestAsync(packet);

        // Then - E2E 검증: 응답 검증
        response.MsgId.Should().EndWith("StartTimerReply", "응답 메시지를 받아야 함");
        var reply = StartTimerReply.Parser.ParseFrom(response.Payload.DataSpan);
        reply.TimerId.Should().BeGreaterThan(0, "TimerId가 할당되어야 함");

        // Timer Tick 대기 (최소 3회 이상 실행되도록)
        await Task.Delay(500);

        // Then - E2E 검증: Push 메시지 검증
        var timerTicks = _receivedMessages
            .Where(m => m.packet.MsgId.EndsWith("TimerTickNotify"))
            .Select(m => TimerTickNotify.Parser.ParseFrom(m.packet.Payload.DataSpan))
            .ToList();

        timerTicks.Should().HaveCountGreaterOrEqualTo(3, "타이머가 최소 3회 이상 실행되어야 함");
        timerTicks.Should().OnlyContain(t => t.TimerType == "repeat", "TimerType이 'repeat'이어야 함");

        // TickNumber가 순차적으로 증가하는지 확인
        for (var i = 0; i < timerTicks.Count - 1; i++)
        {
            timerTicks[i + 1].TickNumber.Should().Be(timerTicks[i].TickNumber + 1,
                "TickNumber가 순차적으로 증가해야 함");
        }
    }

    #endregion

    #region AddCountTimer 테스트

    /// <summary>
    /// IStageSender.AddCountTimer E2E 테스트
    ///
    /// 이 테스트는 카운트 타이머가 지정한 횟수만큼만 실행되는지 검증합니다.
    /// </summary>
    /// <remarks>
    /// E2E 검증 방법:
    /// - 응답 검증: StartTimerReply 메시지 수신, TimerId 확인
    /// - Push 메시지 검증: OnReceive 콜백으로 정확히 Count개의 TimerTickNotify 수신
    /// </remarks>
    [Fact(DisplayName = "AddCountTimer - 지정 횟수만큼만 실행, Count개의 Tick 수신")]
    public async Task AddCountTimer_ExecutesExactCount_ReceivesExactTicks()
    {
        // Given - 서버에 연결 및 인증
        var stageId = await ConnectToServerAsync();
        _receivedMessages.Clear();

        // When - CountTimer 시작 요청 (50ms 초기 대기, 50ms 간격, 5회 실행)
        var request = new StartCountTimerRequest
        {
            InitialDelayMs = 50,
            IntervalMs = 50,
            Count = 5
        };
        using var packet = new Packet(request);
        var response = await _connector.RequestAsync(packet);

        // Then - E2E 검증: 응답 검증
        response.MsgId.Should().EndWith("StartTimerReply", "응답 메시지를 받아야 함");
        var reply = StartTimerReply.Parser.ParseFrom(response.Payload.DataSpan);
        reply.TimerId.Should().BeGreaterThan(0, "TimerId가 할당되어야 함");

        // Timer Tick 대기 (5회 실행 + 여유 시간)
        await Task.Delay(500);

        // Then - E2E 검증: Push 메시지 검증
        var timerTicks = _receivedMessages
            .Where(m => m.packet.MsgId.EndsWith("TimerTickNotify"))
            .Select(m => TimerTickNotify.Parser.ParseFrom(m.packet.Payload.DataSpan))
            .ToList();

        timerTicks.Should().HaveCount(5, "타이머가 정확히 5회만 실행되어야 함");
        timerTicks.Should().OnlyContain(t => t.TimerType == "count", "TimerType이 'count'이어야 함");

        // TickNumber가 1부터 5까지 순차적으로 증가하는지 확인
        for (var i = 0; i < timerTicks.Count; i++)
        {
            timerTicks[i].TickNumber.Should().Be(i + 1, $"TickNumber가 {i + 1}이어야 함");
        }
    }

    #endregion

    #region Helper Methods

    /// <summary>
    /// 서버에 연결만 수행 (인증 X).
    /// </summary>
    private async Task<long> ConnectOnlyAsync()
    {
        var stageId = Random.Shared.NextInt64(100000, long.MaxValue);
        _connector.Init(new ConnectorConfig { RequestTimeoutMs = 30000 });
        var connected = await _connector.ConnectAsync("127.0.0.1", _fixture.PlayServer!.ActualTcpPort, stageId);
        connected.Should().BeTrue("서버에 연결되어야 함");
        await Task.Delay(100);
        return stageId;
    }

    /// <summary>
    /// 서버에 연결 및 인증 수행.
    /// </summary>
    private async Task<long> ConnectToServerAsync()
    {
        var stageId = await ConnectOnlyAsync();

        // Proto 메시지로 인증
        var authRequest = new AuthenticateRequest
        {
            UserId = "test-user",
            Token = "valid-token"
        };
        using var authPacket = new Packet(authRequest);
        await _connector.AuthenticateAsync(authPacket);
        await Task.Delay(100);
        return stageId;
    }

    #endregion
}
