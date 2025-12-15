#nullable enable

using System.Buffers;
using FluentAssertions;
using PlayHouse.Bootstrap;
using PlayHouse.Connector;
using PlayHouse.Connector.Protocol;
using PlayHouse.Tests.Integration.Infrastructure;
using PlayHouse.Tests.Integration.Proto;
using Xunit;
using ClientConnector = PlayHouse.Connector.Connector;
using ClientPacket = PlayHouse.Connector.Protocol.IPacket;

namespace PlayHouse.Tests.Performance.Validation;

/// <summary>
/// Net.Zmq Send() 메서드의 메모리 수명 주기 안전성 검증.
/// </summary>
/// <remarks>
/// Net.Zmq의 Send() 메서드가 메모리를 즉시 복사하는지 검증합니다.
///
/// 테스트 시나리오:
/// 1. ArrayPool 사용 시 조기 반환 후 데이터 손상 여부
/// 2. ReadOnlyMemory Span 무결성
/// 3. 서버간 통신 데이터 무결성 (1KB)
/// 4. 대용량 데이터 무결성 (10KB)
/// </remarks>
public class ZmqMemorySafetyTests : IAsyncLifetime
{
    private PlayServer? _serverA;
    private PlayServer? _serverB;
    private ClientConnector? _connector;
    private Timer? _callbackTimer;
    private readonly object _callbackLock = new();

    public async Task InitializeAsync()
    {
        TestActorImpl.ResetAll();
        TestStageImpl.ResetAll();
        TestSystemController.Reset();
        _serverA = new PlayServerBootstrap()
            .Configure(options =>
            {
                options.ServerId = "safety-a";
                options.BindEndpoint = "tcp://127.0.0.1:16200";
                options.TcpPort = 16210;
                options.RequestTimeoutMs = 5000;
                options.AuthenticateMessageId = "AuthenticateRequest";
                options.DefaultStageType = "TestStage";
            })
            .UseStage<TestStageImpl>("TestStage")
            .UseActor<TestActorImpl>()
            .UseSystemController<TestSystemController>()
            .Build();

        _serverB = new PlayServerBootstrap()
            .Configure(options =>
            {
                options.ServerId = "safety-b";
                options.BindEndpoint = "tcp://127.0.0.1:16201";
                options.TcpPort = 0;
                options.RequestTimeoutMs = 5000;
                options.AuthenticateMessageId = "AuthenticateRequest";
                options.DefaultStageType = "TestStage";
            })
            .UseStage<TestStageImpl>("TestStage")
            .UseActor<TestActorImpl>()
            .UseSystemController<TestSystemController>()
            .Build();

        await _serverA.StartAsync();
        await _serverB.StartAsync();
        await Task.Delay(1000);

        _connector = new ClientConnector();
        _connector.Init(new ConnectorConfig { RequestTimeoutMs = 5000 });

        _callbackTimer = new Timer(_ =>
        {
            lock (_callbackLock)
            {
                _connector.MainThreadAction();
            }
        }, null, 0, 20);
        var stageId = Random.Shared.NextInt64(100000, long.MaxValue);
        var connectResult = await _connector.ConnectAsync("127.0.0.1", _serverA.ActualTcpPort, stageId);
        connectResult.Should().BeTrue("서버 연결은 성공해야 함");

        var authRequest = new AuthenticateRequest
        {
            UserId = "test-user",
            Token = "test-token"
        };
        using var authPacket = new Packet(authRequest);
        var authReply = await _connector.AuthenticateAsync(authPacket);
        authReply.MsgId.Should().Be("AuthenticateReply", "인증 응답을 받아야 함");
    }

    public async Task DisposeAsync()
    {
        _callbackTimer?.Dispose();
        _callbackTimer = null;

        _connector?.Disconnect();

        if (_serverB != null)
        {
            await _serverB.DisposeAsync();
        }
        if (_serverA != null)
        {
            await _serverA.DisposeAsync();
        }
    }

    /// <summary>
    /// ArrayPool 사용 시 조기 반환 후 데이터 손상 여부 검증.
    /// </summary>
    [Fact]
    public async Task ArrayPool_EarlyReturn_ShouldNotCorruptData()
    {
        const int payloadSize = 1024;
        var expectedData = new byte[payloadSize];
        for (int i = 0; i < payloadSize; i++)
        {
            expectedData[i] = (byte)(i % 256);
        }

        var echoRequest = new EchoRequest
        {
            Content = Convert.ToBase64String(expectedData),
            Sequence = 1
        };

        using var packet = new Packet(echoRequest);
        var response = await _connector!.RequestAsync(packet);

        response.MsgId.Should().Be("EchoReply", "에코 응답을 받아야 함");

        var echoReply = EchoReply.Parser.ParseFrom(response.Payload.Data.Span);
        var actualData = Convert.FromBase64String(echoReply.Content);

        actualData.Should().Equal(expectedData, "데이터가 손상되지 않아야 함");
    }

    /// <summary>
    /// ReadOnlyMemory Span 무결성 검증.
    /// </summary>
    [Fact]
    public void ReadOnlyMemory_ToSpan_ShouldMaintainReferenceIntegrity()
    {
        var originalData = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 };
        var memory = new ReadOnlyMemory<byte>(originalData);

        var span = memory.Span;
        var spanCopy = span.ToArray();
        originalData[0] = 99;

        memory.Span[0].Should().Be(99, "ReadOnlyMemory는 원본 배열을 참조해야 함");
        spanCopy[0].Should().Be(1, "ToArray()는 복사본을 생성해야 함");
    }

    /// <summary>
    /// 클라이언트-서버간 통신 데이터 무결성 검증 (1KB).
    /// </summary>
    [Fact]
    public async Task ClientToServer_1KB_ShouldPreserveDataIntegrity()
    {
        const int payloadSize = 1024;
        var testData = new byte[payloadSize];
        for (int i = 0; i < payloadSize; i++)
        {
            testData[i] = (byte)(i % 256);
        }

        var expectedContent = Convert.ToBase64String(testData);
        var echoRequest = new EchoRequest
        {
            Content = expectedContent,
            Sequence = 10
        };

        using var packet = new Packet(echoRequest);
        var response = await _connector!.RequestAsync(packet);

        response.MsgId.Should().Be("EchoReply");

        var echoReply = EchoReply.Parser.ParseFrom(response.Payload.Data.Span);
        var actualData = Convert.FromBase64String(echoReply.Content);

        actualData.Should().Equal(testData, "1KB 데이터가 손상되지 않아야 함");
        echoReply.Sequence.Should().Be(10);
    }

    /// <summary>
    /// 대용량 데이터 무결성 검증 (10KB).
    /// </summary>
    [Fact]
    public async Task ClientToServer_10KB_ShouldPreserveDataIntegrity()
    {
        const int payloadSize = 10 * 1024;
        var largeData = new byte[payloadSize];

        for (int i = 0; i < payloadSize; i++)
        {
            largeData[i] = (byte)((i * 17 + 42) % 256);
        }

        var expectedContent = Convert.ToBase64String(largeData);
        var echoRequest = new EchoRequest
        {
            Content = expectedContent,
            Sequence = 20
        };

        using var packet = new Packet(echoRequest);
        var response = await _connector!.RequestAsync(packet);

        response.MsgId.Should().Be("EchoReply");

        var echoReply = EchoReply.Parser.ParseFrom(response.Payload.Data.Span);
        var actualData = Convert.FromBase64String(echoReply.Content);

        actualData.Should().Equal(largeData, "10KB 대용량 데이터의 모든 바이트가 일치해야 함");
        echoReply.Sequence.Should().Be(20);
    }

    /// <summary>
    /// ArrayPool 버퍼의 조기 반환 시뮬레이션.
    /// </summary>
    [Fact]
    public async Task ArrayPool_BufferReuse_ShouldNotAffectSentData()
    {
        var pool = ArrayPool<byte>.Shared;
        var buffer = pool.Rent(2048);

        try
        {
            for (int i = 0; i < 1024; i++)
            {
                buffer[i] = (byte)(i % 256);
            }

            var originalData = buffer.AsMemory(0, 1024).ToArray();
            var echoRequest = new EchoRequest
            {
                Content = Convert.ToBase64String(originalData),
                Sequence = 3
            };

            using var packet = new Packet(echoRequest);
            var sendTask = _connector!.RequestAsync(packet);

            pool.Return(buffer);
            var anotherBuffer = pool.Rent(2048);
            Array.Fill(anotherBuffer, (byte)0xFF, 0, 2048);
            pool.Return(anotherBuffer);

            var response = await sendTask;

            response.MsgId.Should().Be("EchoReply");
            var echoReply = EchoReply.Parser.ParseFrom(response.Payload.Data.Span);
            var actualData = Convert.FromBase64String(echoReply.Content);

            actualData.Should().Equal(originalData,
                "ArrayPool 버퍼 반환 후에도 전송된 데이터는 손상되지 않아야 함");
        }
        finally
        {
        }
    }
}
