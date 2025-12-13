#nullable enable

using FluentAssertions;
using PlayHouse.Connector;
using PlayHouse.Connector.Protocol;
using PlayHouse.Tests.E2E.Infrastructure;
using PlayHouse.Tests.E2E.Infrastructure.Fixtures;
using PlayHouse.Tests.E2E.Proto;
using Xunit;
using ClientConnector = PlayHouse.Connector.Connector;
using ClientPacket = PlayHouse.Connector.Protocol.IPacket;

namespace PlayHouse.Tests.E2E.Play;

/// <summary>
/// AsyncBlock E2E 테스트
///
/// 이 테스트는 PlayHouse의 IStageSender.AsyncBlock 사용법을 보여줍니다.
/// AsyncBlock은 외부 I/O를 처리할 때 Stage 스레드를 블로킹하지 않도록 합니다.
///
/// E2E 테스트 원칙:
/// - 응답 검증: AsyncBlockAccepted 즉시 응답 수신
/// - Push 메시지 검증: OnReceive 콜백으로 AsyncBlockReply 수신
/// </summary>
[Collection("E2E Connector Tests")]
public class AsyncBlockTests : IAsyncLifetime
{
    private readonly SinglePlayServerFixture _fixture;
    private readonly ClientConnector _connector;
    private readonly List<(long stageId, ClientPacket packet)> _receivedMessages = new();
    private Timer? _callbackTimer;
    private readonly object _callbackLock = new();

    public AsyncBlockTests(SinglePlayServerFixture fixture)
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

    #region AsyncBlock 테스트

    /// <summary>
    /// IStageSender.AsyncBlock E2E 테스트
    ///
    /// 이 테스트는 AsyncBlock의 전체 플로우를 검증합니다:
    /// 1. AsyncBlockRequest 전송
    /// 2. 즉시 AsyncBlockAccepted 응답 수신
    /// 3. Pre 콜백 실행 (외부 I/O)
    /// 4. Post 콜백 실행 (결과 처리)
    /// 5. Push 메시지로 AsyncBlockReply 수신
    /// </summary>
    /// <remarks>
    /// E2E 검증 방법:
    /// - 응답 검증: AsyncBlockAccepted 즉시 응답
    /// - Push 메시지 검증: OnReceive 콜백으로 AsyncBlockReply 수신
    /// - 내부 검증: TestStageImpl.AsyncPreCallbackCount, AsyncPostCallbackCount 확인
    /// </remarks>
    [Fact(DisplayName = "AsyncBlock - Pre/Post 콜백 실행, Push 메시지로 결과 수신")]
    public async Task AsyncBlock_ExecutesPreAndPost_ReceivesResultViaPush()
    {
        // Given - 서버에 연결 및 인증
        var stageId = await ConnectToServerAsync();
        _receivedMessages.Clear();

        var initialPreCount = TestStageImpl.AsyncPreCallbackCount;
        var initialPostCount = TestStageImpl.AsyncPostCallbackCount;

        // When - AsyncBlockRequest 전송 (200ms 대기)
        var request = new AsyncBlockRequest
        {
            Operation = "test_operation",
            DelayMs = 200
        };
        using var packet = new Packet(request);
        var response = await _connector.RequestAsync(packet);

        // Then - E2E 검증: 즉시 응답 검증
        response.MsgId.Should().EndWith("AsyncBlockAccepted", "즉시 수락 응답을 받아야 함");

        // AsyncBlock 완료 대기
        await Task.Delay(500);

        // Then - E2E 검증: Push 메시지 검증
        var asyncReplies = _receivedMessages
            .Where(m => m.packet.MsgId.EndsWith("AsyncBlockReply"))
            .Select(m => AsyncBlockReply.Parser.ParseFrom(m.packet.Payload.Data.Span))
            .ToList();

        asyncReplies.Should().HaveCount(1, "AsyncBlockReply를 1개 받아야 함");
        var reply = asyncReplies[0];

        reply.PreResult.Should().Be("pre_completed_test_operation", "Pre 콜백 결과가 정확해야 함");
        reply.PostResult.Should().Be("post_completed_pre_completed_test_operation",
            "Post 콜백 결과가 정확해야 함");
        reply.PreThreadId.Should().NotBe(0, "Pre 콜백이 실행되어야 함");
        reply.PostThreadId.Should().NotBe(0, "Post 콜백이 실행되어야 함");

        // Then - 내부 검증: 콜백 호출 횟수 확인
        TestStageImpl.AsyncPreCallbackCount.Should().Be(initialPreCount + 1,
            "Pre 콜백이 1회 호출되어야 함");
        TestStageImpl.AsyncPostCallbackCount.Should().Be(initialPostCount + 1,
            "Post 콜백이 1회 호출되어야 함");
    }

    /// <summary>
    /// AsyncBlock 여러 개 동시 실행 테스트
    ///
    /// 이 테스트는 여러 AsyncBlock 요청을 동시에 보내고 모두 정상 처리되는지 검증합니다.
    /// </summary>
    /// <remarks>
    /// E2E 검증 방법:
    /// - 응답 검증: 모든 요청에 대해 AsyncBlockAccepted 즉시 응답
    /// - Push 메시지 검증: OnReceive 콜백으로 모든 AsyncBlockReply 수신
    /// </remarks>
    [Fact(DisplayName = "AsyncBlock - 여러 요청 동시 처리, 모든 결과 수신")]
    public async Task AsyncBlock_MultipleRequests_AllProcessedCorrectly()
    {
        // Given - 서버에 연결 및 인증
        var stageId = await ConnectToServerAsync();
        _receivedMessages.Clear();

        var requestCount = 5;
        var tasks = new List<Task<IPacket>>();

        // When - 5개의 AsyncBlockRequest 동시 전송
        for (var i = 0; i < requestCount; i++)
        {
            var request = new AsyncBlockRequest
            {
                Operation = $"operation_{i}",
                DelayMs = 100
            };
            using var packet = new Packet(request);
            tasks.Add(_connector.RequestAsync(packet));
        }

        var responses = await Task.WhenAll(tasks);

        // Then - E2E 검증: 즉시 응답 검증
        responses.Should().HaveCount(requestCount, "모든 요청에 대해 응답을 받아야 함");
        responses.Should().OnlyContain(r => r.MsgId.EndsWith("AsyncBlockAccepted"),
            "모든 응답이 AsyncBlockAccepted여야 함");

        // AsyncBlock 완료 대기
        await Task.Delay(500);

        // Then - E2E 검증: Push 메시지 검증
        var asyncReplies = _receivedMessages
            .Where(m => m.packet.MsgId.EndsWith("AsyncBlockReply"))
            .Select(m => AsyncBlockReply.Parser.ParseFrom(m.packet.Payload.Data.Span))
            .ToList();

        asyncReplies.Should().HaveCount(requestCount, $"AsyncBlockReply를 {requestCount}개 받아야 함");

        // 모든 작업의 결과가 정확한지 확인
        for (var i = 0; i < requestCount; i++)
        {
            asyncReplies.Should().Contain(
                r => r.PreResult == $"pre_completed_operation_{i}",
                $"operation_{i}의 결과를 받아야 함");
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
