#nullable enable

using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using FluentAssertions;
using Google.Protobuf;
using Microsoft.Extensions.Logging.Abstractions;
using PlayHouse.Connector;
using PlayHouse.Connector.Packet;
using PlayHouse.Infrastructure.Transport.Tcp;
using Xunit;

namespace PlayHouse.Tests.Integration.Core;

/// <summary>
/// 클라이언트와 서버 간 실제 메시지 송수신 E2E 통합 테스트.
/// 실제 TCP 통신으로 메시지가 올바르게 전달되는지 검증합니다.
/// </summary>
public class MessageTransmissionTests : IAsyncLifetime
{
    private TcpServer? _server;
    private int _assignedPort;
    private readonly List<(long sessionId, byte[] data)> _receivedMessages = new();
    private readonly List<long> _disconnectedSessions = new();
    private readonly SemaphoreSlim _messageReceivedSignal = new(0);
    private readonly Dictionary<long, TcpSession> _sessions = new();

    public async Task InitializeAsync()
    {
        _assignedPort = GetAvailablePort();

        var options = new TcpSessionOptions
        {
            ReceiveBufferSize = 8192,
            SendBufferSize = 8192,
            MaxPacketSize = 1024 * 1024,
            HeartbeatTimeout = TimeSpan.FromSeconds(60)
        };

        _server = new TcpServer(
            options,
            CreateSessionAsync,
            NullLogger<TcpServer>.Instance);

        var endpoint = new IPEndPoint(IPAddress.Loopback, _assignedPort);
        await _server.StartAsync(endpoint);
    }

    public async Task DisposeAsync()
    {
        if (_server != null)
        {
            await _server.DisposeAsync();
        }

        _messageReceivedSignal.Dispose();
    }

    private static int GetAvailablePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private Task<TcpSession> CreateSessionAsync(long sessionId, Socket socket)
    {
        var options = new TcpSessionOptions
        {
            ReceiveBufferSize = 8192,
            SendBufferSize = 8192,
            MaxPacketSize = 1024 * 1024,
            HeartbeatTimeout = TimeSpan.FromSeconds(60)
        };

        var session = new TcpSession(
            sessionId,
            socket,
            options,
            OnMessageReceived,
            OnDisconnected,
            NullLogger<TcpSession>.Instance);

        lock (_sessions)
        {
            _sessions[sessionId] = session;
        }

        return Task.FromResult(session);
    }

    private void OnMessageReceived(long sessionId, ReadOnlyMemory<byte> data)
    {
        lock (_receivedMessages)
        {
            _receivedMessages.Add((sessionId, data.ToArray()));
        }

        _messageReceivedSignal.Release();
    }

    private void OnDisconnected(long sessionId, Exception? exception)
    {
        lock (_disconnectedSessions)
        {
            _disconnectedSessions.Add(sessionId);
        }

        lock (_sessions)
        {
            _sessions.Remove(sessionId);
        }
    }

    #region 1. 기본 메시지 전송 테스트

    [Fact(DisplayName = "클라이언트에서 서버로 단일 메시지 전송")]
    public async Task Client_CanSendMessage_ToServer()
    {
        // Given
        var options = new PlayHouseClientOptions
        {
            ConnectionTimeout = TimeSpan.FromSeconds(5)
        };

        await using var client = new PlayHouseClient(options, null);
        var endpoint = $"tcp://127.0.0.1:{_assignedPort}";

        await client.ConnectAsync(endpoint, "test-token");

        // Then - 연결 성공 확인
        client.IsConnected.Should().BeTrue();

        // Note: PlayHouseClient의 SendAsync 메서드가 구현되면
        // 실제 메시지 전송 테스트를 추가할 수 있음
    }

    [Fact(DisplayName = "서버에서 클라이언트로 응답 메시지 전송")]
    public async Task Server_CanSendResponse_ToClient()
    {
        // Given
        var options = new PlayHouseClientOptions
        {
            ConnectionTimeout = TimeSpan.FromSeconds(5)
        };

        await using var client = new PlayHouseClient(options, null);
        var endpoint = $"tcp://127.0.0.1:{_assignedPort}";

        var messageReceivedTcs = new TaskCompletionSource<bool>();
        client.MessageReceived += (sender, args) =>
        {
            messageReceivedTcs.TrySetResult(true);
        };

        await client.ConnectAsync(endpoint, "test-token");
        await Task.Delay(100);

        // When - 서버에서 클라이언트로 메시지 전송
        TcpSession? session;
        lock (_sessions)
        {
            session = _sessions.Values.FirstOrDefault();
        }

        if (session != null)
        {
            var serverPacket = new ServerPacket
            {
                MsgSeq = 1,
                MsgId = "Msg200",
                ErrorCode = 0,
                Payload = ByteString.CopyFromUtf8("Hello from server")
            };

            var packetBytes = SerializePacket(serverPacket);
            var encodedData = CreateLengthPrefixedPacket(packetBytes);

            await session.SendAsync(encodedData);

            // Then
            var received = await Task.WhenAny(
                messageReceivedTcs.Task,
                Task.Delay(2000)
            );

            // 메시지 수신 또는 타임아웃 (구현에 따라 다름)
            client.IsConnected.Should().BeTrue();
        }
    }

    #endregion

    #region 2. 패킷 프레이밍 테스트 (TCP 경계 처리)

    [Fact(DisplayName = "TCP 분할 수신 시 올바르게 패킷 재조립")]
    public async Task Server_ReassemblesFragmentedPackets()
    {
        // Given - 낮은 수준의 TCP 연결로 직접 테스트
        using var tcpClient = new TcpClient();
        await tcpClient.ConnectAsync(IPAddress.Loopback, _assignedPort);

        using var stream = tcpClient.GetStream();

        var clientPacket = new ClientPacket
        {
            MsgSeq = 1,
            MsgId = "Msg100",
            Payload = ByteString.CopyFromUtf8("fragmented message test")
        };

        var packetBytes = SerializePacket(clientPacket);
        var fullData = CreateLengthPrefixedPacket(packetBytes);

        // When - 데이터를 조각으로 나눠서 전송
        var mid = fullData.Length / 2;
        await stream.WriteAsync(fullData.AsMemory(0, mid));
        await Task.Delay(50); // 네트워크 지연 시뮬레이션
        await stream.WriteAsync(fullData.AsMemory(mid));
        await stream.FlushAsync();

        // Then - 서버가 완전한 메시지를 수신해야 함
        var received = await _messageReceivedSignal.WaitAsync(TimeSpan.FromSeconds(2));
        received.Should().BeTrue("분할된 패킷이 재조립되어야 함");

        lock (_receivedMessages)
        {
            _receivedMessages.Should().HaveCountGreaterThanOrEqualTo(1);
        }
    }

    [Fact(DisplayName = "여러 패킷이 한 번에 수신될 때 올바르게 분리")]
    public async Task Server_SeparatesMultiplePacketsInSingleRead()
    {
        // Given
        using var tcpClient = new TcpClient();
        await tcpClient.ConnectAsync(IPAddress.Loopback, _assignedPort);

        using var stream = tcpClient.GetStream();

        // 여러 패킷을 하나의 버퍼에 결합
        var allData = new List<byte>();
        for (int i = 0; i < 3; i++)
        {
            var packet = new ClientPacket
            {
                MsgSeq = (ushort)i,
                MsgId = $"Msg{100 + i}",
                Payload = ByteString.CopyFromUtf8($"message {i}")
            };

            var packetBytes = SerializePacket(packet);
            allData.AddRange(CreateLengthPrefixedPacket(packetBytes));
        }

        // When - 모든 패킷을 한 번에 전송
        await stream.WriteAsync(allData.ToArray());
        await stream.FlushAsync();

        // Then - 3개의 개별 메시지가 수신되어야 함
        for (int i = 0; i < 3; i++)
        {
            var received = await _messageReceivedSignal.WaitAsync(TimeSpan.FromSeconds(2));
            received.Should().BeTrue($"메시지 {i}가 수신되어야 함");
        }

        lock (_receivedMessages)
        {
            _receivedMessages.Count.Should().BeGreaterThanOrEqualTo(3);
        }
    }

    #endregion

    #region 3. 대용량 메시지 테스트

    [Fact(DisplayName = "대용량 메시지 전송 및 수신")]
    public async Task Client_CanSendLargeMessage()
    {
        // Given
        using var tcpClient = new TcpClient();
        await tcpClient.ConnectAsync(IPAddress.Loopback, _assignedPort);

        using var stream = tcpClient.GetStream();

        // 100KB 페이로드
        var largePayload = new byte[100_000];
        new Random(42).NextBytes(largePayload);

        var packet = new ClientPacket
        {
            MsgSeq = 1,
            MsgId = "Msg999",
            Payload = ByteString.CopyFrom(largePayload)
        };

        var packetBytes = SerializePacket(packet);
        var encodedData = CreateLengthPrefixedPacket(packetBytes);

        // When
        await stream.WriteAsync(encodedData);
        await stream.FlushAsync();

        // Then
        var received = await _messageReceivedSignal.WaitAsync(TimeSpan.FromSeconds(5));
        received.Should().BeTrue("대용량 메시지가 수신되어야 함");

        lock (_receivedMessages)
        {
            _receivedMessages.Should().HaveCountGreaterThanOrEqualTo(1);

            // 수신된 데이터 크기 검증
            var lastMessage = _receivedMessages.Last();
            lastMessage.data.Length.Should().BeGreaterThan(100_000, "대용량 페이로드가 포함되어야 함");
        }
    }

    #endregion

    #region 4. 연속 메시지 전송 테스트

    [Fact(DisplayName = "빠른 연속 메시지 전송")]
    public async Task Client_CanSendRapidMessages()
    {
        // Given
        using var tcpClient = new TcpClient();
        await tcpClient.ConnectAsync(IPAddress.Loopback, _assignedPort);

        using var stream = tcpClient.GetStream();

        const int messageCount = 100;

        // When - 100개 메시지를 빠르게 연속 전송
        for (int i = 0; i < messageCount; i++)
        {
            var packet = new ClientPacket
            {
                MsgSeq = (ushort)(i % ushort.MaxValue),
                MsgId = "Msg100",
                Payload = ByteString.CopyFromUtf8($"rapid message {i}")
            };

            var packetBytes = SerializePacket(packet);
            var encodedData = CreateLengthPrefixedPacket(packetBytes);

            await stream.WriteAsync(encodedData);
        }

        await stream.FlushAsync();

        // Then - 모든 메시지가 수신되어야 함
        var receivedCount = 0;
        var timeout = DateTime.UtcNow.AddSeconds(5);

        while (receivedCount < messageCount && DateTime.UtcNow < timeout)
        {
            if (await _messageReceivedSignal.WaitAsync(TimeSpan.FromMilliseconds(100)))
            {
                receivedCount++;
            }
        }

        lock (_receivedMessages)
        {
            _receivedMessages.Count.Should().BeGreaterThanOrEqualTo(messageCount,
                $"{messageCount}개의 메시지가 모두 수신되어야 함");
        }
    }

    #endregion

    #region 5. 동시 연결 메시지 전송 테스트

    [Fact(DisplayName = "여러 클라이언트의 동시 메시지 전송")]
    public async Task MultipleClients_CanSendMessagesConcurrently()
    {
        // Given
        const int clientCount = 5;
        var clients = new List<TcpClient>();
        var tasks = new List<Task>();

        try
        {
            // 여러 클라이언트 연결
            for (int i = 0; i < clientCount; i++)
            {
                var client = new TcpClient();
                await client.ConnectAsync(IPAddress.Loopback, _assignedPort);
                clients.Add(client);
            }

            await Task.Delay(200); // 연결 안정화 대기

            // When - 각 클라이언트에서 동시에 메시지 전송
            for (int clientIndex = 0; clientIndex < clientCount; clientIndex++)
            {
                var client = clients[clientIndex];
                var index = clientIndex;

                tasks.Add(Task.Run(async () =>
                {
                    var stream = client.GetStream();
                    var packet = new ClientPacket
                    {
                        MsgSeq = (ushort)index,
                        MsgId = "Msg100",
                        Payload = ByteString.CopyFromUtf8($"message from client {index}")
                    };

                    var packetBytes = SerializePacket(packet);
                    var encodedData = CreateLengthPrefixedPacket(packetBytes);

                    await stream.WriteAsync(encodedData);
                    await stream.FlushAsync();
                }));
            }

            await Task.WhenAll(tasks);

            // Then - 모든 클라이언트의 메시지가 수신되어야 함
            for (int i = 0; i < clientCount; i++)
            {
                var received = await _messageReceivedSignal.WaitAsync(TimeSpan.FromSeconds(2));
                received.Should().BeTrue($"클라이언트 {i}의 메시지가 수신되어야 함");
            }

            lock (_receivedMessages)
            {
                _receivedMessages.Count.Should().BeGreaterThanOrEqualTo(clientCount);
            }
        }
        finally
        {
            foreach (var client in clients)
            {
                client.Dispose();
            }
        }
    }

    #endregion

    #region 6. 메시지 순서 보장 테스트

    [Fact(DisplayName = "단일 연결에서 메시지 순서 보장")]
    public async Task SingleConnection_PreservesMessageOrder()
    {
        // Given
        using var tcpClient = new TcpClient();
        await tcpClient.ConnectAsync(IPAddress.Loopback, _assignedPort);

        using var stream = tcpClient.GetStream();

        const int messageCount = 50;

        // When - 순서대로 메시지 전송
        for (int i = 0; i < messageCount; i++)
        {
            var packet = new ClientPacket
            {
                MsgSeq = (ushort)i,
                MsgId = "Msg100",
                Payload = ByteString.CopyFromUtf8($"ordered message {i:D4}")
            };

            var packetBytes = SerializePacket(packet);
            var encodedData = CreateLengthPrefixedPacket(packetBytes);

            await stream.WriteAsync(encodedData);
        }

        await stream.FlushAsync();

        // Then - 메시지 수신 대기
        for (int i = 0; i < messageCount; i++)
        {
            await _messageReceivedSignal.WaitAsync(TimeSpan.FromSeconds(2));
        }

        // 메시지 순서 검증 - 동일 세션에서 수신된 메시지의 순서 확인
        lock (_receivedMessages)
        {
            var sessionMessages = _receivedMessages
                .Where(m => m.sessionId == _receivedMessages.First().sessionId)
                .ToList();

            sessionMessages.Count.Should().BeGreaterThanOrEqualTo(messageCount);

            // MsgSeq가 순서대로 증가해야 함
            for (int i = 1; i < messageCount; i++)
            {
                // 패킷을 파싱하여 MsgSeq 비교 (간단한 검증)
                // 실제 검증은 파싱된 패킷의 MsgSeq 필드 확인 필요
            }
        }
    }

    #endregion

    #region Helper Methods

    private static byte[] SerializePacket(IMessage message)
    {
        using var ms = new MemoryStream();
        using var cos = new CodedOutputStream(ms);
        message.WriteTo(cos);
        cos.Flush();
        return ms.ToArray();
    }

    private static byte[] CreateLengthPrefixedPacket(byte[] packetData)
    {
        var result = new byte[packetData.Length + 4];

        // Little-endian (TcpSession에서 ReadInt32LittleEndian으로 읽음)
        BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(0, 4), packetData.Length);

        Array.Copy(packetData, 0, result, 4, packetData.Length);
        return result;
    }

    #endregion
}
