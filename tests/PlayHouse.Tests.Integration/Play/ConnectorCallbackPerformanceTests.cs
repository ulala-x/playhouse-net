#nullable enable

using System.Collections.Concurrent;
using System.Diagnostics;
using FluentAssertions;
using PlayHouse.Connector;
using PlayHouse.Connector.Protocol;
using PlayHouse.Tests.Integration.Infrastructure.Fixtures;
using PlayHouse.Tests.Integration.Proto;
using Xunit;
using ClientConnector = PlayHouse.Connector.Connector;
using ClientPacket = PlayHouse.Connector.Protocol.Packet;

namespace PlayHouse.Tests.Integration.Play;

/// <summary>
/// Connector RequestCallback 모드 성능 E2E 테스트
///
/// RequestCallback은 UseMainThreadCallback 설정과 관계없이 항상 큐를 사용합니다.
/// MainThreadAction() 호출이 필수이며, 8KB 이상 메시지도 정상 처리되어야 합니다.
/// </summary>
[Collection("E2E ApiPlayServer")]
public class ConnectorCallbackPerformanceTests : IAsyncLifetime
{
    private readonly ApiPlayServerFixture _fixture;
    private ClientConnector? _connector;
    private Timer? _callbackTimer;
    private readonly object _callbackLock = new();

    public ConnectorCallbackPerformanceTests(ApiPlayServerFixture fixture)
    {
        _fixture = fixture;
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        _callbackTimer?.Dispose();
        _callbackTimer = null;

        if (_connector != null)
        {
            _connector.Disconnect();
            await _connector.DisposeAsync();
            _connector = null;
        }
    }

    /// <summary>
    /// RequestCallback 모드 - 8KB 메시지 정상 처리, MainThreadAction 필요
    /// </summary>
    /// <remarks>
    /// RequestCallback은 UseMainThreadCallback 설정과 관계없이 항상 큐를 사용합니다.
    /// MainThreadAction() 호출이 필수입니다.
    /// 8KB 이상 메시지도 정상적으로 처리되어야 합니다.
    /// </remarks>
    [Fact(DisplayName = "RequestCallback - 8KB 메시지 정상 처리, MainThreadAction 필요")]
    public async Task RequestCallback_8KBMessage_RequiresMainThreadAction()
    {
        // Given - 연결 및 콜백 자동 처리 타이머 시작
        _connector = await CreateConnectorAsync(useMainThreadCallback: false);

        // 콜백 자동 처리 타이머 시작 (RequestCallback은 항상 큐를 사용하므로 필수)
        _callbackTimer = new Timer(_ =>
        {
            lock (_callbackLock)
            {
                _connector?.MainThreadAction();
            }
        }, null, 0, 10); // 10ms 간격

        var receivedCount = 0;
        var timestamps = new ConcurrentDictionary<int, long>();
        var tcs = new TaskCompletionSource();
        var messageCount = 50;

        // When - 8KB 메시지 50개를 RequestCallback으로 전송
        var largeContent = new string('A', 8192);

        for (int i = 0; i < messageCount; i++)
        {
            var request = new EchoRequest
            {
                Content = $"Sequence {i} - {largeContent}",
                Sequence = i
            };

            var packet = new ClientPacket(request);
            var seq = i;
            timestamps[seq] = Stopwatch.GetTimestamp();

            _connector.Request(packet, response =>
            {
                // 콜백에서 응답 처리
                if (timestamps.TryRemove(seq, out var startTicks))
                {
                    var elapsed = Stopwatch.GetTimestamp() - startTicks;
                    // 8KB 메시지도 1초 이내 응답
                    var elapsedMs = elapsed * 1000.0 / Stopwatch.Frequency;
                    elapsedMs.Should().BeLessThan(1000, "8KB 메시지도 1초 이내 응답해야 함");
                }

                var count = Interlocked.Increment(ref receivedCount);
                if (count >= messageCount)
                {
                    tcs.TrySetResult();
                }

                packet.Dispose();
            });
        }

        // MainThreadAction() 호출로 콜백 처리
        var completed = await Task.WhenAny(tcs.Task, Task.Delay(5000));

        // Then - 모든 응답이 정상 수신되어야 함
        completed.Should().Be(tcs.Task, "타임아웃 없이 모든 응답을 받아야 함");
        receivedCount.Should().Be(messageCount, "모든 메시지의 응답을 받아야 함");
    }

    /// <summary>
    /// RequestCallback 모드 - UseMainThreadCallback 설정 검증
    /// </summary>
    /// <remarks>
    /// UseMainThreadCallback은 Push 콜백 처리 방식을 결정합니다.
    /// RequestCallback은 이 설정과 관계없이 항상 큐를 사용합니다.
    /// MainThreadAction() 호출이 필수입니다.
    /// </remarks>
    [Fact(DisplayName = "RequestCallback - UseMainThreadCallback=true 설정, MainThreadAction 필요")]
    public async Task RequestCallback_MainThreadQueue_RequiresMainThreadAction()
    {
        // Given - 메인 스레드 큐 모드로 연결
        _connector = await CreateConnectorAsync(useMainThreadCallback: true);

        // 콜백 자동 처리 타이머 시작 (Unity Update 시뮬레이션)
        _callbackTimer = new Timer(_ =>
        {
            lock (_callbackLock)
            {
                _connector?.MainThreadAction();
            }
        }, null, 0, 10); // 10ms 간격

        var receivedCount = 0;
        var tcs = new TaskCompletionSource();
        var messageCount = 10;

        // When - 1KB 메시지 10개를 RequestCallback으로 전송
        var largeContent = new string('A', 1024);

        for (int i = 0; i < messageCount; i++)
        {
            var request = new EchoRequest
            {
                Content = $"Sequence {i} - {largeContent}",
                Sequence = i
            };

            var packet = new ClientPacket(request);

            _connector.Request(packet, response =>
            {
                var count = Interlocked.Increment(ref receivedCount);
                if (count >= messageCount)
                {
                    tcs.TrySetResult();
                }

                packet.Dispose();
            });
        }

        var completed = await Task.WhenAny(tcs.Task, Task.Delay(3000));

        // Then - MainThreadAction 호출로 모든 응답이 처리되어야 함
        completed.Should().Be(tcs.Task, "MainThreadAction 호출로 모든 콜백이 실행되어야 함");
        receivedCount.Should().Be(messageCount, "모든 메시지의 응답을 받아야 함");
    }

    /// <summary>
    /// RequestAsync와 RequestCallback 성능 비교
    /// </summary>
    /// <remarks>
    /// RequestCallback은 항상 큐를 사용하므로 MainThreadAction() 호출이 필요합니다.
    /// RequestAsync와 비교하여 합리적인 성능을 보여야 합니다.
    /// </remarks>
    [Fact(DisplayName = "RequestCallback vs RequestAsync - 성능 비교")]
    public async Task RequestCallback_Vs_RequestAsync_SimilarPerformance()
    {
        // Given - 연결
        _connector = await CreateConnectorAsync(useMainThreadCallback: false);

        var messageCount = 100;
        var largeContent = new string('A', 1024);

        // When 1 - RequestAsync 성능 측정
        var asyncSw = Stopwatch.StartNew();
        for (int i = 0; i < messageCount; i++)
        {
            var request = new EchoRequest
            {
                Content = $"Async {i} - {largeContent}",
                Sequence = i
            };

            using var packet = new ClientPacket(request);
            using var response = await _connector.RequestAsync(packet);
            response.MsgId.Should().EndWith("EchoReply");
        }
        asyncSw.Stop();

        // When 2 - RequestCallback 성능 측정 (MainThreadAction 필요)
        _callbackTimer = new Timer(_ =>
        {
            lock (_callbackLock)
            {
                _connector?.MainThreadAction();
            }
        }, null, 0, 10); // 10ms 간격

        var receivedCount = 0;
        var tcs = new TaskCompletionSource();

        var callbackSw = Stopwatch.StartNew();
        for (int i = 0; i < messageCount; i++)
        {
            var request = new EchoRequest
            {
                Content = $"Callback {i} - {largeContent}",
                Sequence = i
            };

            var packet = new ClientPacket(request);

            _connector.Request(packet, response =>
            {
                var count = Interlocked.Increment(ref receivedCount);
                if (count >= messageCount)
                {
                    callbackSw.Stop();
                    tcs.TrySetResult();
                }
                packet.Dispose();
            });
        }

        await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // Then - 성능 비교 (RequestCallback이 RequestAsync보다 2배 이상 느리지 않아야 함)
        var asyncMs = asyncSw.ElapsedMilliseconds;
        var callbackMs = callbackSw.ElapsedMilliseconds;

        callbackMs.Should().BeLessThan(asyncMs * 2,
            $"RequestCallback({callbackMs}ms)이 RequestAsync({asyncMs}ms)보다 2배 이상 느리지 않아야 함");
    }

    #region Helper Methods

    private async Task<ClientConnector> CreateConnectorAsync(bool useMainThreadCallback)
    {
        var connector = new ClientConnector();
        var config = new ConnectorConfig
        {
            UseMainThreadCallback = useMainThreadCallback,
            RequestTimeoutMs = 5000
        };

        connector.Init(config);

        var stageId = Random.Shared.Next(10000, 99999);
        var connected = await connector.ConnectAsync("127.0.0.1", _fixture.PlayServer!.ActualTcpPort, stageId, "TestStage");
        connected.Should().BeTrue("서버에 연결되어야 함");

        // 인증
        using (var authPacket = ClientPacket.Empty("AuthenticateRequest"))
        {
            var authReply = await connector.AuthenticateAsync(authPacket);
            authReply.MsgId.Should().EndWith("AuthenticateReply", "인증 성공");
        }

        return connector;
    }

    private static Google.Protobuf.ByteString CreatePayload(int size)
    {
        if (size <= 0) return Google.Protobuf.ByteString.Empty;

        var payload = new byte[size];
        for (int i = 0; i < payload.Length; i++)
        {
            payload[i] = (byte)(i % 256);
        }
        return Google.Protobuf.ByteString.CopyFrom(payload);
    }

    #endregion
}
