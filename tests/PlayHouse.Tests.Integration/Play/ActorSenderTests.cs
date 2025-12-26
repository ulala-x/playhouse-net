#nullable enable

using FluentAssertions;
using PlayHouse.Connector;
using PlayHouse.Connector.Protocol;
using PlayHouse.Tests.Integration.Infrastructure;
using PlayHouse.Tests.Integration.Infrastructure.Fixtures;
using PlayHouse.Tests.Integration.Proto;
using Xunit;
using ClientConnector = PlayHouse.Connector.Connector;

namespace PlayHouse.Tests.Integration.Play;

/// <summary>
/// IActorSender API E2E 테스트
///
/// 이 테스트는 PlayHouse의 IActorSender API 사용법을 보여줍니다:
/// - AccountId: 인증 후 설정되는 고유 식별자
/// - Reply: 클라이언트에게 응답 전송
/// - SendToClient: 클라이언트에게 Push 메시지 전송
/// - LeaveStage: Actor가 Stage 떠나기
///
/// E2E 테스트 원칙:
/// - 응답 검증: Connector 공개 API로 확인
/// - 콜백 호출 검증: 테스트 구현체의 검증
/// </summary>
[Collection("E2E Connector Tests")]
public class ActorSenderTests : IAsyncLifetime
{
    private readonly SinglePlayServerFixture _fixture;
    private readonly ClientConnector _connector;
    private Timer? _callbackTimer;
    private readonly object _callbackLock = new();

    public ActorSenderTests(SinglePlayServerFixture fixture)
    {
        _fixture = fixture;
        _connector = new ClientConnector();
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

    #region IActorSender.AccountId 테스트

    /// <summary>
    /// IActorSender.AccountId E2E 테스트
    ///
    /// 이 테스트는 인증 후 AccountId가 설정되고 Reply 메시지에 포함되는지 검증합니다.
    /// </summary>
    /// <remarks>
    /// E2E 검증 방법:
    /// - 응답 검증: GetAccountIdReply 메시지 수신, AccountId 확인
    /// - 값 검증: AccountId가 비어있지 않음 (서버가 자동으로 할당)
    /// </remarks>
    [Fact(DisplayName = "AccountId - 인증 후 설정, Reply에 포함")]
    public async Task AccountId_SetAfterAuthentication_IncludedInReply()
    {
        // Given - 서버에 연결 및 인증
        var stageId = await ConnectToServerAsync();

        // When - AccountId 조회 요청
        var request = new GetAccountIdRequest();
        using var packet = new Packet(request);
        var response = await _connector.RequestAsync(packet);

        // Then - E2E 검증: 응답 검증
        response.MsgId.Should().EndWith("GetAccountIdReply", "응답 메시지를 받아야 함");
        var reply = GetAccountIdReply.Parser.ParseFrom(response.Payload.DataSpan);

        // AccountId는 인증 후 서버가 자동으로 할당 (TestActorImpl에서는 자동 증가 카운터 사용)
        reply.AccountId.Should().NotBeNullOrEmpty("AccountId가 설정되어야 함");
    }

    #endregion

    #region IActorSender.LeaveStage 테스트

    /// <summary>
    /// IActorSender.LeaveStage E2E 테스트
    ///
    /// 이 테스트는 LeaveStage 호출 시 성공 응답을 받는지 검증합니다.
    /// </summary>
    /// <remarks>
    /// E2E 검증 방법:
    /// - 응답 검증: LeaveStageReply 메시지 수신, Success == true
    /// Note: LeaveStage 후 OnDestroy는 서버 내부 동작이므로 E2E에서 직접 검증 불가
    /// </remarks>
    [Fact(DisplayName = "LeaveStage - Actor가 Stage 떠남, 성공 응답")]
    public async Task LeaveStage_ActorLeavesStage_SuccessResponse()
    {
        // Given - 서버에 연결 및 인증
        var stageId = await ConnectToServerAsync();

        // When - LeaveStageRequest 전송
        var request = new LeaveStageRequest { Reason = "Test" };
        using var packet = new Packet(request);
        var response = await _connector.RequestAsync(packet);

        // Then - E2E 검증: 응답 검증
        response.MsgId.Should().EndWith("LeaveStageReply", "응답 메시지를 받아야 함");
        var reply = LeaveStageReply.Parser.ParseFrom(response.Payload.DataSpan);
        reply.Success.Should().BeTrue("LeaveStage가 성공해야 함");
    }

    #endregion

    #region IActorSender.Reply 테스트

    /// <summary>
    /// IActorSender.Reply E2E 테스트
    ///
    /// 이 테스트는 Reply 메서드로 클라이언트에게 응답을 전송하는지 검증합니다.
    /// </summary>
    /// <remarks>
    /// E2E 검증 방법:
    /// - 응답 검증: EchoReply 메시지 수신, 내용 확인
    /// </remarks>
    [Fact(DisplayName = "Reply - 클라이언트에게 응답 전송")]
    public async Task Reply_SendsResponseToClient()
    {
        // Given - 서버에 연결 및 인증
        var stageId = await ConnectToServerAsync();

        // When - EchoRequest 전송
        var request = new EchoRequest { Content = "Reply Test", Sequence = 99 };
        using var packet = new Packet(request);
        var response = await _connector.RequestAsync(packet);

        // Then - E2E 검증: 응답 검증
        response.MsgId.Should().EndWith("EchoReply", "응답 메시지를 받아야 함");
        var reply = EchoReply.Parser.ParseFrom(response.Payload.DataSpan);
        reply.Content.Should().Be("Reply Test", "에코 내용이 동일해야 함");
        reply.Sequence.Should().Be(99, "시퀀스 번호가 동일해야 함");
    }

    /// <summary>
    /// IActorSender.Reply(errorCode) E2E 테스트
    ///
    /// 이 테스트는 Reply 메서드로 에러 응답을 전송하는지 검증합니다.
    /// </summary>
    /// <remarks>
    /// E2E 검증 방법:
    /// - 응답 검증: ConnectorException 발생, ErrorCode 확인
    /// </remarks>
    [Fact(DisplayName = "Reply(errorCode) - 에러 응답 전송, ConnectorException 발생")]
    public async Task Reply_WithErrorCode_ThrowsConnectorException()
    {
        // Given - 서버에 연결 및 인증
        var stageId = await ConnectToServerAsync();

        // When - FailRequest 전송 (에러 응답)
        using var failRequest = Packet.Empty("FailRequest");

        var act = async () => await _connector.RequestAsync(failRequest);

        // Then - E2E 검증: 에러 응답 검증
        var exception = await act.Should().ThrowAsync<ConnectorException>("에러 응답이 예외로 변환되어야 함");
        exception.Which.ErrorCode.Should().Be(500, "에러코드가 500이어야 함");
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
