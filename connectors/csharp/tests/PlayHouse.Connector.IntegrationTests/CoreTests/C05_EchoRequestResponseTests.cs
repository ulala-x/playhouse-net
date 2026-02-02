using FluentAssertions;
using PlayHouse.Connector.Protocol;
using PlayHouse.TestServer.Proto;
using Xunit;

namespace PlayHouse.Connector.IntegrationTests.CoreTests;

/// <summary>
/// C-05: Echo Request-Response 테스트
/// </summary>
/// <remarks>
/// 기본적인 Request-Response 패턴이 정상 동작하는지 검증합니다.
/// EchoRequest를 보내고 EchoReply를 받아 내용이 일치하는지 확인합니다.
/// </remarks>
public class C05_EchoRequestResponseTests : BaseIntegrationTest
{
    public C05_EchoRequestResponseTests(TestServerFixture testServer) : base(testServer)
    {
    }

    [Fact(DisplayName = "C-05-01: Echo Request-Response가 정상 동작한다")]
    public async Task EchoRequest_SendAndReceive_ReturnsMatchingReply()
    {
        // Given: 연결 및 인증 완료
        await CreateStageAndConnectAsync();
        await AuthenticateAsync("echoUser");

        var testContent = "Hello PlayHouse!";
        var testSequence = 42;

        // When: Echo 요청
        var echoReply = await EchoAsync(testContent, testSequence);

        // Then: 응답이 요청과 일치해야 함
        echoReply.Should().NotBeNull("Echo 응답이 반환되어야 함");
        echoReply.Content.Should().Be(testContent, "내용이 에코되어야 함");
        echoReply.Sequence.Should().Be(testSequence, "시퀀스가 에코되어야 함");
        echoReply.ProcessedAt.Should().BeGreaterThan(0, "처리 시간이 기록되어야 함");
    }

    [Fact(DisplayName = "C-05-02: RequestAsync로 Echo 요청할 수 있다")]
    public async Task RequestAsync_WithEchoRequest_ReturnsEchoReply()
    {
        // Given: 연결 및 인증 완료
        await CreateStageAndConnectAsync();
        await AuthenticateAsync("asyncUser");

        var echoRequest = new EchoRequest
        {
            Content = "Async Echo Test",
            Sequence = 1
        };

        // When: RequestAsync 호출
        using var requestPacket = new Packet(echoRequest);
        var responsePacket = await Connector!.RequestAsync(requestPacket);

        // Then: 올바른 응답을 받아야 함
        responsePacket.Should().NotBeNull();
        responsePacket.MsgId.Should().Be("EchoReply", "응답 메시지 타입이 일치해야 함");

        var echoReply = ParsePayload<EchoReply>(responsePacket.Payload);
        echoReply.Content.Should().Be("Async Echo Test");
        echoReply.Sequence.Should().Be(1);
    }

    [Fact(DisplayName = "C-05-03: Request 콜백 방식으로 Echo 요청할 수 있다")]
    public async Task Request_WithCallback_ReceivesEchoReply()
    {
        // Given: 연결 및 인증 완료
        await CreateStageAndConnectAsync();
        await AuthenticateAsync("callbackUser");

        var echoRequest = new EchoRequest
        {
            Content = "Callback Echo Test",
            Sequence = 100
        };

        var tcs = new TaskCompletionSource<EchoReply>();

        // When: Request 콜백 방식 호출
        using var requestPacket = new Packet(echoRequest);
        Connector!.Request(requestPacket, responsePacket =>
        {
            var echoReply = ParsePayload<EchoReply>(responsePacket.Payload);
            tcs.TrySetResult(echoReply);
        });

        // 콜백 대기 (MainThreadAction 호출하면서 최대 5초)
        EchoReply echoReply;
        try
        {
            echoReply = await WaitWithMainThreadActionAsync(tcs, 5000);
        }
        catch (TimeoutException)
        {
            true.Should().BeFalse("콜백이 5초 이내에 호출되어야 함");
            return;
        }

        // Then: 콜백이 호출되고 올바른 응답을 받아야 함
        echoReply.Content.Should().Be("Callback Echo Test");
        echoReply.Sequence.Should().Be(100);
    }

    [Fact(DisplayName = "C-05-04: 연속된 Echo 요청을 처리할 수 있다")]
    public async Task EchoRequest_Sequential_AllSucceed()
    {
        // Given: 연결 및 인증 완료
        await CreateStageAndConnectAsync();
        await AuthenticateAsync("sequentialUser");

        // When: 5개의 연속된 Echo 요청
        var replies = new List<EchoReply>();
        for (int i = 1; i <= 5; i++)
        {
            var reply = await EchoAsync($"Message {i}", i);
            replies.Add(reply);
        }

        // Then: 모든 응답이 순서대로 올바르게 반환되어야 함
        replies.Should().HaveCount(5, "5개의 응답을 받아야 함");
        for (int i = 0; i < 5; i++)
        {
            replies[i].Content.Should().Be($"Message {i + 1}", $"{i + 1}번째 내용이 일치해야 함");
            replies[i].Sequence.Should().Be(i + 1, $"{i + 1}번째 시퀀스가 일치해야 함");
        }
    }

    [Fact(DisplayName = "C-05-05: 병렬 Echo 요청을 처리할 수 있다")]
    public async Task EchoRequest_Parallel_AllSucceed()
    {
        // Given: 연결 및 인증 완료
        await CreateStageAndConnectAsync();
        await AuthenticateAsync("parallelUser");

        // When: 10개의 병렬 Echo 요청
        var tasks = Enumerable.Range(1, 10)
            .Select(i => EchoAsync($"Parallel {i}", i))
            .ToArray();

        var replies = await Task.WhenAll(tasks);

        // Then: 모든 응답이 올바르게 반환되어야 함
        replies.Should().HaveCount(10, "10개의 응답을 받아야 함");
        for (int i = 0; i < 10; i++)
        {
            var expectedSequence = i + 1;
            var reply = replies.First(r => r.Sequence == expectedSequence);
            reply.Content.Should().Be($"Parallel {expectedSequence}");
        }
    }

    [Fact(DisplayName = "C-05-06: 빈 문자열도 에코할 수 있다")]
    public async Task EchoRequest_WithEmptyString_ReturnsEmptyString()
    {
        // Given: 연결 및 인증 완료
        await CreateStageAndConnectAsync();
        await AuthenticateAsync("emptyUser");

        // When: 빈 문자열로 Echo 요청
        var echoReply = await EchoAsync("", 999);

        // Then: 빈 문자열이 에코되어야 함
        echoReply.Content.Should().BeEmpty("빈 문자열이 에코되어야 함");
        echoReply.Sequence.Should().Be(999);
    }

    [Fact(DisplayName = "C-05-07: 긴 문자열도 에코할 수 있다")]
    public async Task EchoRequest_WithLongString_ReturnsFullString()
    {
        // Given: 연결 및 인증 완료
        await CreateStageAndConnectAsync();
        await AuthenticateAsync("longUser");

        // 1KB 문자열 생성
        var longContent = new string('A', 1024);

        // When: 긴 문자열로 Echo 요청
        var echoReply = await EchoAsync(longContent, 1);

        // Then: 전체 문자열이 에코되어야 함
        echoReply.Content.Should().Be(longContent, "긴 문자열이 완전히 에코되어야 함");
        echoReply.Content.Length.Should().Be(1024, "문자열 길이가 유지되어야 함");
    }

    [Fact(DisplayName = "C-05-08: 유니코드 문자열도 에코할 수 있다")]
    public async Task EchoRequest_WithUnicodeString_ReturnsCorrectString()
    {
        // Given: 연결 및 인증 완료
        await CreateStageAndConnectAsync();
        await AuthenticateAsync("unicodeUser");

        var unicodeContent = "안녕하세요 🎮 こんにちは 你好";

        // When: 유니코드 문자열로 Echo 요청
        var echoReply = await EchoAsync(unicodeContent, 1);

        // Then: 유니코드 문자열이 올바르게 에코되어야 함
        echoReply.Content.Should().Be(unicodeContent, "유니코드 문자열이 정확히 에코되어야 함");
    }

    [Fact(DisplayName = "C-05-09: 응답 메시지 타입이 올바르다")]
    public async Task EchoRequest_ResponseMessageType_IsCorrect()
    {
        // Given: 연결 및 인증 완료
        await CreateStageAndConnectAsync();
        await AuthenticateAsync("typeCheckUser");

        var echoRequest = new EchoRequest
        {
            Content = "Type Check",
            Sequence = 1
        };

        // When: Echo 요청
        using var requestPacket = new Packet(echoRequest);
        var responsePacket = await Connector!.RequestAsync(requestPacket);

        // Then: 응답 메시지 타입이 EchoReply여야 함
        responsePacket.MsgId.Should().Be("EchoReply", "응답 메시지 타입이 EchoReply여야 함");
    }
}
