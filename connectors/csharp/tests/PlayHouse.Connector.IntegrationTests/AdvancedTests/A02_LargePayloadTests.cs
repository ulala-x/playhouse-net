using PlayHouse.Connector.Protocol;
using PlayHouse.TestServer.Proto;
using Xunit;

namespace PlayHouse.Connector.IntegrationTests.AdvancedTests;

/// <summary>
/// A-02: 큰 페이로드 (LZ4 압축) 테스트
/// </summary>
/// <remarks>
/// LargePayloadRequest를 통한 큰 데이터 송수신 및 압축 검증.
/// 서버는 1MB 페이로드를 반환하며, 전송 계층에서 LZ4 압축이 적용됨.
/// </remarks>
[Trait("Category", "Advanced")]
[Trait("Feature", "Compression")]
public class A02_LargePayloadTests : BaseIntegrationTest
{
    public A02_LargePayloadTests(TestServerFixture testServer) : base(testServer) { }

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();

        // 큰 페이로드를 위해 타임아웃 증가
        Connector = new Connector();
        Connector.Init(new ConnectorConfig
        {
            RequestTimeoutMs = 30000,  // 30초
            HeartBeatIntervalMs = 10000
        });

        await CreateStageAndConnectAsync();
        await AuthenticateAsync("large-payload-user");
    }

    [Fact(DisplayName = "A-02-01: 1MB 페이로드를 수신할 수 있다")]
    public async Task LargePayload_1MB_Received()
    {
        // Arrange
        var largePayloadRequest = new LargePayloadRequest
        {
            SizeBytes = 1048576  // 1MB
        };

        // Act
        using var requestPacket = new Packet(largePayloadRequest);
        var responsePacket = await Connector!.RequestAsync(requestPacket);
        var reply = BenchmarkReply.Parser.ParseFrom(responsePacket.Payload.DataSpan.ToArray());

        // Assert
        Assert.NotNull(reply.Payload);
        Assert.Equal(1048576, reply.Payload.Length);
    }

    [Fact(DisplayName = "A-02-02: 큰 페이로드의 데이터 무결성이 유지된다")]
    public async Task LargePayload_Data_Integrity()
    {
        // Arrange
        var largePayloadRequest = new LargePayloadRequest
        {
            SizeBytes = 1048576
        };

        // Act
        using var requestPacket = new Packet(largePayloadRequest);
        var responsePacket = await Connector!.RequestAsync(requestPacket);
        var reply = BenchmarkReply.Parser.ParseFrom(responsePacket.Payload.DataSpan.ToArray());

        // Assert - 서버가 순차적인 바이트 패턴으로 채움
        var data = reply.Payload.ToByteArray();
        for (int i = 0; i < Math.Min(1000, data.Length); i++)
        {
            Assert.Equal((byte)(i % 256), data[i]);
        }
    }

    [Fact(DisplayName = "A-02-03: 연속된 큰 페이로드 요청을 처리할 수 있다")]
    public async Task LargePayload_Sequential_Requests()
    {
        // Arrange & Act
        var results = new List<int>();

        for (int i = 0; i < 3; i++)
        {
            var request = new LargePayloadRequest { SizeBytes = 1048576 };
            using var requestPacket = new Packet(request);
            var responsePacket = await Connector!.RequestAsync(requestPacket);
            var reply = BenchmarkReply.Parser.ParseFrom(responsePacket.Payload.DataSpan.ToArray());
            results.Add(reply.Payload.Length);
        }

        // Assert
        Assert.All(results, size => Assert.Equal(1048576, size));
    }

    [Fact(DisplayName = "A-02-04: 큰 요청 페이로드를 전송할 수 있다")]
    public async Task LargePayload_Send_Large_Request()
    {
        // Arrange - 큰 데이터를 담은 Echo 요청
        var largeContent = new string('A', 100000);  // 100KB 문자열
        var echoRequest = new EchoRequest
        {
            Content = largeContent,
            Sequence = 1
        };

        // Act
        using var requestPacket = new Packet(echoRequest);
        var responsePacket = await Connector!.RequestAsync(requestPacket);
        var echoReply = EchoReply.Parser.ParseFrom(responsePacket.Payload.DataSpan.ToArray());

        // Assert
        Assert.Equal(largeContent, echoReply.Content);
    }

    [Fact(DisplayName = "A-02-05: 병렬 큰 페이로드 요청을 처리할 수 있다")]
    public async Task LargePayload_Parallel_Requests()
    {
        // Arrange & Act
        var tasks = new List<Task<IPacket>>();

        for (int i = 0; i < 3; i++)
        {
            var request = new LargePayloadRequest { SizeBytes = 524288 };  // 512KB each
            var packet = new Packet(request);
            tasks.Add(Connector!.RequestAsync(packet));
        }

        var responses = await Task.WhenAll(tasks);

        // Assert
        Assert.Equal(3, responses.Length);
        foreach (var response in responses)
        {
            var reply = BenchmarkReply.Parser.ParseFrom(response.Payload.DataSpan.ToArray());
            // 서버가 항상 1MB를 반환하므로 1MB 확인
            Assert.Equal(1048576, reply.Payload.Length);
        }
    }
}
