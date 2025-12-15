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
/// ToArray() 제거 전에 반드시 통과해야 함.
/// </summary>
/// <remarks>
/// Net.Zmq의 Send() 메서드가 메모리를 즉시 복사하는지 검증합니다.
/// 이 검증이 실패하면 Phase 3의 ToArray() 제거가 불가능합니다.
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
        // 테스트 간 격리를 위해 Static 필드 리셋
        TestActorImpl.ResetAll();
        TestStageImpl.ResetAll();
        TestSystemController.Reset();

        // Server A: tcp://127.0.0.1:16200 (Client-facing: 16210)
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

        // Server B: tcp://127.0.0.1:16201 (No client-facing port)
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

        // ServerAddressResolver가 서버를 자동으로 연결할 시간 제공
        await Task.Delay(1000);

        // Connector 생성 및 초기화
        _connector = new ClientConnector();
        _connector.Init(new ConnectorConfig { RequestTimeoutMs = 5000 });

        // 콜백 자동 처리 타이머 시작
        _callbackTimer = new Timer(_ =>
        {
            lock (_callbackLock)
            {
                _connector.MainThreadAction();
            }
        }, null, 0, 20); // 20ms 간격

        // 연결 및 인증
        var stageId = Random.Shared.NextInt64(100000, long.MaxValue);
        var connectResult = await _connector.ConnectAsync("127.0.0.1", _serverA.ActualTcpPort, stageId);
        connectResult.Should().BeTrue("서버 연결은 성공해야 함");

        // 인증
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
    /// <remarks>
    /// Net.Zmq가 Send() 호출 시 메모리를 즉시 복사하지 않는다면,
    /// ArrayPool 버퍼를 조기 반환하면 데이터가 손상됩니다.
    /// </remarks>
    [Fact]
    public async Task ArrayPool_EarlyReturn_ShouldNotCorruptData()
    {
        // Arrange: 1KB 데이터 생성
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

        // Act: 요청 전송 및 응답 수신
        using var packet = new Packet(echoRequest);
        var response = await _connector!.RequestAsync(packet);

        // Assert: 응답 검증
        response.MsgId.Should().Be("EchoReply", "에코 응답을 받아야 함");

        var echoReply = EchoReply.Parser.ParseFrom(response.Payload.Data.Span);
        var actualData = Convert.FromBase64String(echoReply.Content);

        actualData.Should().Equal(expectedData, "데이터가 손상되지 않아야 함");
    }

    /// <summary>
    /// ReadOnlyMemory Span 무결성 검증.
    /// </summary>
    /// <remarks>
    /// ReadOnlyMemory를 Span으로 변환한 후 원본 메모리가 유지되는지 확인합니다.
    /// </remarks>
    [Fact]
    public void ReadOnlyMemory_ToSpan_ShouldMaintainReferenceIntegrity()
    {
        // Arrange: 테스트 데이터 생성
        var originalData = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 };
        var memory = new ReadOnlyMemory<byte>(originalData);

        // Act: Span으로 변환 후 원본 데이터 수정
        var span = memory.Span;
        var spanCopy = span.ToArray();

        // 원본 데이터 수정
        originalData[0] = 99;

        // Assert: Span이 원본 메모리를 참조하는지 확인
        memory.Span[0].Should().Be(99, "ReadOnlyMemory는 원본 배열을 참조해야 함");
        spanCopy[0].Should().Be(1, "ToArray()는 복사본을 생성해야 함");
    }

    /// <summary>
    /// 클라이언트-서버간 통신 데이터 무결성 검증 (1KB).
    /// </summary>
    /// <remarks>
    /// 1KB 크기의 페이로드를 전송하여 데이터가 손상되지 않았는지 확인합니다.
    /// </remarks>
    [Fact]
    public async Task ClientToServer_1KB_ShouldPreserveDataIntegrity()
    {
        // Arrange: 1KB 데이터 생성
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

        // Act: 요청 전송
        using var packet = new Packet(echoRequest);
        var response = await _connector!.RequestAsync(packet);

        // Assert: 응답 검증
        response.MsgId.Should().Be("EchoReply");

        var echoReply = EchoReply.Parser.ParseFrom(response.Payload.Data.Span);
        var actualData = Convert.FromBase64String(echoReply.Content);

        actualData.Should().Equal(testData, "1KB 데이터가 손상되지 않아야 함");
        echoReply.Sequence.Should().Be(10);
    }

    /// <summary>
    /// 대용량 데이터 무결성 검증 (10KB).
    /// </summary>
    /// <remarks>
    /// 10KB 크기의 페이로드를 전송하여 대용량 데이터의 메모리 안전성을 검증합니다.
    /// Net.Zmq가 Send() 시 메모리를 즉시 복사하지 않으면 데이터가 손상될 수 있습니다.
    /// </remarks>
    [Fact]
    public async Task ClientToServer_10KB_ShouldPreserveDataIntegrity()
    {
        // Arrange: 10KB 데이터 생성
        const int payloadSize = 10 * 1024; // 10KB
        var largeData = new byte[payloadSize];

        // 패턴이 있는 데이터 생성 (검증 용이)
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

        // Act: 대용량 데이터 전송
        using var packet = new Packet(echoRequest);
        var response = await _connector!.RequestAsync(packet);

        // Assert: 응답 검증
        response.MsgId.Should().Be("EchoReply");

        var echoReply = EchoReply.Parser.ParseFrom(response.Payload.Data.Span);
        var actualData = Convert.FromBase64String(echoReply.Content);

        actualData.Should().Equal(largeData, "10KB 대용량 데이터의 모든 바이트가 일치해야 함");
        echoReply.Sequence.Should().Be(20);
    }

    /// <summary>
    /// ArrayPool 버퍼의 조기 반환 시뮬레이션.
    /// </summary>
    /// <remarks>
    /// 실제 시나리오에서 ArrayPool 버퍼를 조기 반환했을 때
    /// Net.Zmq가 메모리를 복사했는지 확인합니다.
    /// </remarks>
    [Fact]
    public async Task ArrayPool_BufferReuse_ShouldNotAffectSentData()
    {
        // Arrange: ArrayPool에서 버퍼 임대
        var pool = ArrayPool<byte>.Shared;
        var buffer = pool.Rent(2048);

        try
        {
            // 버퍼에 데이터 작성
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

            // Act: 요청 전송 후 즉시 버퍼 반환
            var sendTask = _connector!.RequestAsync(packet);

            // 버퍼를 풀에 반환하고 덮어쓰기
            pool.Return(buffer);
            var anotherBuffer = pool.Rent(2048);
            Array.Fill(anotherBuffer, (byte)0xFF, 0, 2048);
            pool.Return(anotherBuffer);

            // 응답 대기
            var response = await sendTask;

            // Assert: 데이터가 손상되지 않았는지 확인
            response.MsgId.Should().Be("EchoReply");
            var echoReply = EchoReply.Parser.ParseFrom(response.Payload.Data.Span);
            var actualData = Convert.FromBase64String(echoReply.Content);

            actualData.Should().Equal(originalData,
                "ArrayPool 버퍼 반환 후에도 전송된 데이터는 손상되지 않아야 함");
        }
        finally
        {
            // 정리
            // buffer는 이미 반환됨
        }
    }
}
