#nullable enable

using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PlayHouse.Abstractions;
using PlayHouse.Core.Messaging;
using PlayHouse.Core.Session;
using PlayHouse.Core.Stage;
using PlayHouse.Infrastructure.Http;
using Xunit;

namespace PlayHouse.Tests.Integration.Core;

/// <summary>
/// E2E integration tests for the full Actor lifecycle:
/// Server start → Client connect → Stage create → Actor join → OnAuthenticate → Message dispatch → Reply
/// </summary>
[Collection("ActorLifecycle")] // Run tests in this class sequentially
public class ActorLifecycleTests : IAsyncLifetime
{
    private static int _portCounter = 19500;
    private readonly int _testPort;
    private const string TestIp = "127.0.0.1";

    public ActorLifecycleTests()
    {
        // Use unique port for each test instance to avoid port conflicts
        _testPort = Interlocked.Increment(ref _portCounter);
    }

    private IHost? _host;
    private StageFactory? _stageFactory;
    private StagePool? _stagePool;
    private SessionManager? _sessionManager;
    private PacketDispatcher? _dispatcher;

    public async Task InitializeAsync()
    {
        // Reset test state tracking
        TestStage.Reset();

        _host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                // Configure PlayHouse options using object initializer syntax
                services.AddOptions<PlayHouseOptions>()
                    .Configure(opts =>
                    {
                        // Use reflection to set init-only properties in test
                        typeof(PlayHouseOptions).GetProperty(nameof(PlayHouseOptions.Ip))!
                            .SetValue(opts, TestIp);
                        typeof(PlayHouseOptions).GetProperty(nameof(PlayHouseOptions.Port))!
                            .SetValue(opts, _testPort);
                        typeof(PlayHouseOptions).GetProperty(nameof(PlayHouseOptions.EnableWebSocket))!
                            .SetValue(opts, false);
                    })
                    .ValidateOnStart();

                // Register core PlayHouse services manually since we're bypassing AddPlayHouse
                services.AddSingleton<PlayHouse.Infrastructure.Serialization.PacketSerializer>();
                services.AddSingleton<SessionManager>();
                services.AddSingleton<StagePool>();
                services.AddSingleton<PacketDispatcher>();

                // Register TimerManager with proper dispatch action
                services.AddSingleton<PlayHouse.Core.Timer.TimerManager>(sp =>
                {
                    var dispatcher = sp.GetRequiredService<PacketDispatcher>();
                    var logger = sp.GetRequiredService<ILoggerFactory>()
                        .CreateLogger<PlayHouse.Core.Timer.TimerManager>();
                    return new PlayHouse.Core.Timer.TimerManager(
                        packet => dispatcher.Dispatch(packet),
                        logger);
                });

                services.AddSingleton<StageFactory>(sp =>
                {
                    var stagePool = sp.GetRequiredService<StagePool>();
                    var dispatcher = sp.GetRequiredService<PacketDispatcher>();
                    var timerManager = sp.GetRequiredService<PlayHouse.Core.Timer.TimerManager>();
                    var sessionManager = sp.GetRequiredService<SessionManager>();
                    var loggerFactory = sp.GetRequiredService<ILoggerFactory>();

                    var factory = new StageFactory(
                        stagePool,
                        dispatcher,
                        timerManager,
                        sessionManager,
                        loggerFactory);

                    // Register test stage type
                    factory.Registry.RegisterStageType<TestStage>("test-stage");

                    return factory;
                });

                // Register PlayHouseServer as hosted service
                services.AddHostedService<PlayHouseServer>();
                services.AddSingleton<PlayHouseServer>(sp =>
                    sp.GetServices<Microsoft.Extensions.Hosting.IHostedService>()
                        .OfType<PlayHouseServer>()
                        .First());
            })
            .ConfigureLogging(logging =>
            {
                logging.SetMinimumLevel(LogLevel.Debug);
                logging.AddConsole();
            })
            .Build();

        await _host.StartAsync();

        // Get service instances
        _stageFactory = _host.Services.GetRequiredService<StageFactory>();
        _stagePool = _host.Services.GetRequiredService<StagePool>();
        _sessionManager = _host.Services.GetRequiredService<SessionManager>();
        _dispatcher = _host.Services.GetRequiredService<PacketDispatcher>();
    }

    public async Task DisposeAsync()
    {
        if (_host != null)
        {
            await _host.StopAsync();
            _host.Dispose();
        }
    }

    [Fact]
    public async Task Server_ShouldStartAndAcceptTcpConnections()
    {
        // Arrange
        var server = _host!.Services.GetRequiredService<PlayHouseServer>();

        // Act - Connect a TCP client
        using var client = new TcpClient();
        await client.ConnectAsync(TestIp, _testPort);

        // Assert
        client.Connected.Should().BeTrue();
        server.TcpServer.Should().NotBeNull();
    }

    [Fact]
    public async Task StageFactory_ShouldCreateStageAndCallOnCreate()
    {
        // Arrange
        var creationPacket = CreateTestPacket("CreateStage");

        // Act
        var (stageContext, errorCode, reply) = await _stageFactory!.CreateStageAsync("test-stage", creationPacket);

        // Assert
        errorCode.Should().Be(ErrorCode.Success);
        stageContext.Should().NotBeNull();
        stageContext!.StageId.Should().BeGreaterThan(0);
        stageContext.StageType.Should().Be("test-stage");

        // Verify OnCreate was called
        TestStage.OnCreateCalled.Should().BeTrue();
        TestStage.OnPostCreateCalled.Should().BeTrue();
    }

    [Fact]
    public async Task StageContext_ShouldJoinActorAndCallOnJoinRoom()
    {
        // Arrange
        var creationPacket = CreateTestPacket("CreateStage");
        var (stageContext, _, _) = await _stageFactory!.CreateStageAsync("test-stage", creationPacket);
        stageContext.Should().NotBeNull();

        // Create a session
        var session = _sessionManager!.CreateSession(1);
        _sessionManager.MapAccountId(1, 100);

        var joinPacket = CreateTestPacket("JoinRoom");

        // Act
        var (joinError, joinReply, actorContext) = await stageContext!.JoinActorAsync(
            accountId: 100,
            sessionId: 1,
            userInfo: joinPacket);

        // Assert
        joinError.Should().Be(ErrorCode.Success);
        actorContext.Should().NotBeNull();

        // Verify stage callbacks were called
        TestStage.OnJoinRoomCalled.Should().BeTrue();
        TestStage.OnPostJoinRoomCalled.Should().BeTrue();
        TestStage.LastJoinedAccountId.Should().Be(100);
    }

    [Fact]
    public async Task PacketDispatcher_ShouldRouteMessageToActor()
    {
        // Arrange - Create stage and join actor
        var creationPacket = CreateTestPacket("CreateStage");
        var (stageContext, _, _) = await _stageFactory!.CreateStageAsync("test-stage", creationPacket);

        var session = _sessionManager!.CreateSession(1);
        _sessionManager.MapAccountId(1, 100);

        var joinPacket = CreateTestPacket("JoinRoom");
        await stageContext!.JoinActorAsync(100, 1, joinPacket);

        // Create a test message packet
        var messagePacket = CreateTestPacket("TestMessage", stageContext.StageId);

        // Act - Dispatch message to actor
        var dispatched = _dispatcher!.DispatchToActor(stageContext.StageId, 100, messagePacket);

        // Wait for message processing
        await Task.Delay(100);

        // Assert
        dispatched.Should().BeTrue();
        TestStage.OnDispatchCalled.Should().BeTrue();
        TestStage.LastDispatchedMsgId.Should().Be("TestMessage");
    }

    [Fact]
    public async Task Actor_ShouldLeaveStageAndCallOnLeaveRoom()
    {
        // Arrange - Create stage and join actor
        var creationPacket = CreateTestPacket("CreateStage");
        var (stageContext, _, _) = await _stageFactory!.CreateStageAsync("test-stage", creationPacket);

        var session = _sessionManager!.CreateSession(1);
        _sessionManager.MapAccountId(1, 100);

        var joinPacket = CreateTestPacket("JoinRoom");
        await stageContext!.JoinActorAsync(100, 1, joinPacket);

        // Act - Leave stage
        var leaveResult = await stageContext.LeaveActorAsync(100, LeaveReason.UserRequest);

        // Assert
        leaveResult.Should().BeTrue();
        TestStage.OnLeaveRoomCalled.Should().BeTrue();
        TestStage.LastLeaveReason.Should().Be(LeaveReason.UserRequest);
    }

    [Fact]
    public async Task StageFactory_ShouldDestroyStageAndCallDisposeAsync()
    {
        // Arrange - Create stage
        var creationPacket = CreateTestPacket("CreateStage");
        var (stageContext, _, _) = await _stageFactory!.CreateStageAsync("test-stage", creationPacket);
        var stageId = stageContext!.StageId;

        // Act - Destroy stage
        var destroyed = await _stageFactory!.DestroyStageAsync(stageId);

        // Assert
        destroyed.Should().BeTrue();
        _stagePool!.GetStage(stageId).Should().BeNull();
        TestStage.DisposeCalled.Should().BeTrue();
    }

    [Fact]
    public async Task MultipleActors_ShouldJoinSameStage()
    {
        // Arrange - Create stage
        var creationPacket = CreateTestPacket("CreateStage");
        var (stageContext, _, _) = await _stageFactory!.CreateStageAsync("test-stage", creationPacket);

        // Create sessions for multiple actors
        _sessionManager!.CreateSession(1);
        _sessionManager.MapAccountId(1, 100);
        _sessionManager.CreateSession(2);
        _sessionManager.MapAccountId(2, 101);
        _sessionManager.CreateSession(3);
        _sessionManager.MapAccountId(3, 102);

        var joinPacket = CreateTestPacket("JoinRoom");

        // Act - Join multiple actors
        var (error1, _, actor1) = await stageContext!.JoinActorAsync(100, 1, joinPacket);
        var (error2, _, actor2) = await stageContext.JoinActorAsync(101, 2, joinPacket);
        var (error3, _, actor3) = await stageContext.JoinActorAsync(102, 3, joinPacket);

        // Assert
        error1.Should().Be(ErrorCode.Success);
        error2.Should().Be(ErrorCode.Success);
        error3.Should().Be(ErrorCode.Success);

        stageContext.ActorPool.Count.Should().Be(3);
        stageContext.ActorPool.GetActor(100).Should().NotBeNull();
        stageContext.ActorPool.GetActor(101).Should().NotBeNull();
        stageContext.ActorPool.GetActor(102).Should().NotBeNull();
    }

    [Fact]
    public async Task DuplicateActor_ShouldReturnDuplicateLoginError()
    {
        // Arrange - Create stage and join first actor
        var creationPacket = CreateTestPacket("CreateStage");
        var (stageContext, _, _) = await _stageFactory!.CreateStageAsync("test-stage", creationPacket);

        _sessionManager!.CreateSession(1);
        _sessionManager.MapAccountId(1, 100);

        var joinPacket = CreateTestPacket("JoinRoom");
        await stageContext!.JoinActorAsync(100, 1, joinPacket);

        // Act - Try to join same actor again
        var (duplicateError, _, _) = await stageContext.JoinActorAsync(100, 1, joinPacket);

        // Assert
        duplicateError.Should().Be(ErrorCode.DuplicateLogin);
    }

    [Fact]
    public async Task DispatchToNonExistentStage_ShouldReturnFalse()
    {
        // Arrange
        var messagePacket = CreateTestPacket("TestMessage", 99999);

        // Act
        var dispatched = _dispatcher!.DispatchToActor(99999, 100, messagePacket);

        // Assert
        dispatched.Should().BeFalse();
    }

    [Fact]
    public async Task DispatchToNonExistentActor_ShouldNotCrash()
    {
        // Arrange - Create stage without joining any actor
        var creationPacket = CreateTestPacket("CreateStage");
        var (stageContext, _, _) = await _stageFactory!.CreateStageAsync("test-stage", creationPacket);

        var messagePacket = CreateTestPacket("TestMessage", stageContext!.StageId);

        // Act - Dispatch to non-existent actor
        var dispatched = _dispatcher!.DispatchToActor(stageContext.StageId, 999, messagePacket);

        // Wait for processing
        await Task.Delay(100);

        // Assert - Should dispatch to stage but actor lookup will fail gracefully
        dispatched.Should().BeTrue();
        // OnDispatch should not be called because actor doesn't exist
    }

    private static IPacket CreateTestPacket(string msgId, int stageId = 0)
    {
        return new TestPacket(msgId, stageId);
    }
}

/// <summary>
/// Test implementation of IStage for integration testing.
/// Tracks all lifecycle callbacks for verification.
/// </summary>
public class TestStage : IStage
{
    // Static tracking for test verification
    private static readonly ConcurrentDictionary<string, bool> _callbackTracking = new();
    private static long _lastJoinedAccountId;
    private static string? _lastDispatchedMsgId;
    private static LeaveReason? _lastLeaveReason;

    public static bool OnCreateCalled => _callbackTracking.GetValueOrDefault("OnCreate");
    public static bool OnPostCreateCalled => _callbackTracking.GetValueOrDefault("OnPostCreate");
    public static bool OnJoinRoomCalled => _callbackTracking.GetValueOrDefault("OnJoinRoom");
    public static bool OnPostJoinRoomCalled => _callbackTracking.GetValueOrDefault("OnPostJoinRoom");
    public static bool OnLeaveRoomCalled => _callbackTracking.GetValueOrDefault("OnLeaveRoom");
    public static bool OnDispatchCalled => _callbackTracking.GetValueOrDefault("OnDispatch");
    public static bool DisposeCalled => _callbackTracking.GetValueOrDefault("Dispose");
    public static long LastJoinedAccountId => _lastJoinedAccountId;
    public static string? LastDispatchedMsgId => _lastDispatchedMsgId;
    public static LeaveReason? LastLeaveReason => _lastLeaveReason;

    public static void Reset()
    {
        _callbackTracking.Clear();
        _lastJoinedAccountId = 0;
        _lastDispatchedMsgId = null;
        _lastLeaveReason = null;
    }

    public IStageSender StageSender { get; set; } = null!;

    public Task<(ushort errorCode, IPacket? reply)> OnCreate(IPacket packet)
    {
        _callbackTracking["OnCreate"] = true;
        return Task.FromResult<(ushort, IPacket?)>((ErrorCode.Success, null));
    }

    public Task OnPostCreate()
    {
        _callbackTracking["OnPostCreate"] = true;
        return Task.CompletedTask;
    }

    public Task<(ushort errorCode, IPacket? reply)> OnJoinRoom(IActor actor, IPacket userInfo)
    {
        _callbackTracking["OnJoinRoom"] = true;
        _lastJoinedAccountId = actor.ActorSender.AccountId;
        return Task.FromResult<(ushort, IPacket?)>((ErrorCode.Success, null));
    }

    public Task OnPostJoinRoom(IActor actor)
    {
        _callbackTracking["OnPostJoinRoom"] = true;
        return Task.CompletedTask;
    }

    public ValueTask OnLeaveRoom(IActor actor, LeaveReason reason)
    {
        _callbackTracking["OnLeaveRoom"] = true;
        _lastLeaveReason = reason;
        return ValueTask.CompletedTask;
    }

    public ValueTask OnActorConnectionChanged(IActor actor, bool isConnected, DisconnectReason? reason)
    {
        return ValueTask.CompletedTask;
    }

    public ValueTask OnDispatch(IActor actor, IPacket packet)
    {
        _callbackTracking["OnDispatch"] = true;
        _lastDispatchedMsgId = packet.MsgId;
        return ValueTask.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        _callbackTracking["Dispose"] = true;
        return ValueTask.CompletedTask;
    }
}

/// <summary>
/// Simple test packet implementation.
/// </summary>
internal sealed class TestPacket : IPacket
{
    public string MsgId { get; }
    public ushort MsgSeq { get; }
    public int StageId { get; }
    public ushort ErrorCode { get; }
    public IPayload Payload { get; }

    public TestPacket(string msgId, int stageId = 0, ushort msgSeq = 0, ushort errorCode = 0)
    {
        MsgId = msgId;
        StageId = stageId;
        MsgSeq = msgSeq;
        ErrorCode = errorCode;
        Payload = EmptyPayload.Instance;
    }

    public void Dispose() { }
}
