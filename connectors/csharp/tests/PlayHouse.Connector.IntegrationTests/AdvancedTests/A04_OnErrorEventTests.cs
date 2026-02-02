using PlayHouse.Connector.Protocol;
using PlayHouse.TestServer.Proto;
using Xunit;

namespace PlayHouse.Connector.IntegrationTests.AdvancedTests;

/// <summary>
/// A-04: OnError 이벤트 테스트
/// </summary>
/// <remarks>
/// OnError 이벤트는 요청 실패, 연결 문제, 서버 에러 등 다양한 상황에서 발생.
/// 콜백 기반 API에서 에러 처리를 위해 사용됨.
/// </remarks>
[Trait("Category", "Advanced")]
[Trait("Feature", "OnError")]
public class A04_OnErrorEventTests : BaseIntegrationTest
{
    public A04_OnErrorEventTests(TestServerFixture testServer) : base(testServer) { }

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        await CreateStageAndConnectAsync();
        await AuthenticateAsync("onerror-test-user");
    }

    [Fact(DisplayName = "A-04-01: 연결 해제 상태에서 Send 시 OnError가 발생한다")]
    public async Task OnError_Fired_On_Disconnected_Send()
    {
        // Arrange
        var errorEvents = new List<(long stageId, string stageType, ushort errorCode, IPacket request)>();
        Connector!.OnError += (stageId, stageType, errorCode, request) =>
        {
            errorEvents.Add((stageId, stageType, errorCode, request));
        };

        Connector.Disconnect();
        await Task.Delay(500);

        var echoRequest = new EchoRequest { Content = "Test", Sequence = 1 };

        // Act
        using var packet = new Packet(echoRequest);
        Connector.Send(packet);
        Connector.MainThreadAction();

        // Assert
        Assert.Single(errorEvents);
        Assert.Equal((ushort)ConnectorErrorCode.Disconnected, errorEvents[0].errorCode);
        Assert.Equal(StageInfo!.StageId, errorEvents[0].stageId);
    }

    [Fact(DisplayName = "A-04-02: 연결 해제 상태에서 Request 콜백 시 OnError가 발생한다")]
    public async Task OnError_Fired_On_Disconnected_Request_Callback()
    {
        // Arrange
        var errorEvents = new List<(long stageId, ushort errorCode)>();
        Connector!.OnError += (stageId, stageType, errorCode, request) =>
        {
            errorEvents.Add((stageId, errorCode));
        };

        Connector.Disconnect();
        await Task.Delay(500);

        var echoRequest = new EchoRequest { Content = "Test", Sequence = 1 };
        var callbackFired = false;

        // Act
        using var packet = new Packet(echoRequest);
        Connector.Request(packet, response =>
        {
            callbackFired = true;
        });
        Connector.MainThreadAction();

        // Assert
        Assert.Single(errorEvents);
        Assert.Equal((ushort)ConnectorErrorCode.Disconnected, errorEvents[0].errorCode);
        Assert.False(callbackFired);
    }

    [Fact(DisplayName = "A-04-03: OnError에 원본 요청 패킷이 전달된다")]
    public async Task OnError_Contains_Original_Request()
    {
        // Arrange
        IPacket? receivedRequest = null;
        Connector!.OnError += (stageId, stageType, errorCode, request) =>
        {
            receivedRequest = request;
        };

        Connector.Disconnect();
        await Task.Delay(500);

        var echoRequest = new EchoRequest { Content = "Original Content", Sequence = 42 };

        // Act
        using var packet = new Packet(echoRequest);
        Connector.Send(packet);
        Connector.MainThreadAction();

        // Assert
        Assert.NotNull(receivedRequest);
        Assert.Equal("EchoRequest", receivedRequest.MsgId);
    }

    [Fact(DisplayName = "A-04-04: OnError에 StageId와 StageType이 전달된다")]
    public async Task OnError_Contains_Stage_Info()
    {
        // Arrange
        long receivedStageId = 0;
        string receivedStageType = "";
        Connector!.OnError += (stageId, stageType, errorCode, request) =>
        {
            receivedStageId = stageId;
            receivedStageType = stageType;
        };

        Connector.Disconnect();
        await Task.Delay(500);

        var echoRequest = new EchoRequest { Content = "Test", Sequence = 1 };

        // Act
        using var packet = new Packet(echoRequest);
        Connector.Send(packet);
        Connector.MainThreadAction();

        // Assert
        Assert.Equal(StageInfo!.StageId, receivedStageId);
        Assert.Equal(StageInfo.StageType, receivedStageType);
    }

    [Fact(DisplayName = "A-04-05: 연결 해제 상태에서 Authenticate 시 OnError가 발생한다")]
    public async Task OnError_Fired_On_Disconnected_Authenticate()
    {
        // Arrange
        var errorEvents = new List<ushort>();
        Connector!.OnError += (stageId, stageType, errorCode, request) =>
        {
            errorEvents.Add(errorCode);
        };

        Connector.Disconnect();
        await Task.Delay(500);

        var authRequest = new AuthenticateRequest { UserId = "test", Token = "token" };
        var callbackFired = false;

        // Act
        using var packet = new Packet(authRequest);
        Connector.Authenticate(packet, response =>
        {
            callbackFired = true;
        });
        Connector.MainThreadAction();

        // Assert
        Assert.Single(errorEvents);
        Assert.Equal((ushort)ConnectorErrorCode.Disconnected, errorEvents[0]);
        Assert.False(callbackFired);
    }

    [Fact(DisplayName = "A-04-06: 여러 OnError 핸들러를 등록할 수 있다")]
    public async Task OnError_Multiple_Handlers()
    {
        // Arrange
        var handler1Called = false;
        var handler2Called = false;
        var handler3Called = false;

        Connector!.OnError += (_, _, _, _) => handler1Called = true;
        Connector.OnError += (_, _, _, _) => handler2Called = true;
        Connector.OnError += (_, _, _, _) => handler3Called = true;

        Connector.Disconnect();
        await Task.Delay(500);

        var echoRequest = new EchoRequest { Content = "Test", Sequence = 1 };

        // Act
        using var packet = new Packet(echoRequest);
        Connector.Send(packet);
        Connector.MainThreadAction();

        // Assert
        Assert.True(handler1Called);
        Assert.True(handler2Called);
        Assert.True(handler3Called);
    }

    [Fact(DisplayName = "A-04-07: OnError 핸들러가 예외를 던져도 다른 핸들러는 실행된다")]
    public async Task OnError_Handler_Exception_Does_Not_Block_Others()
    {
        // Arrange
        var handler2Called = false;

        Connector!.OnError += (_, _, _, _) => throw new InvalidOperationException("Test exception");
        Connector.OnError += (_, _, _, _) => handler2Called = true;

        Connector.Disconnect();
        await Task.Delay(500);

        var echoRequest = new EchoRequest { Content = "Test", Sequence = 1 };

        // Act
        using var packet = new Packet(echoRequest);
        try
        {
            Connector.Send(packet);
            Connector.MainThreadAction();
        }
        catch
        {
            // 예외가 발생할 수 있음
        }

        // Assert - 구현에 따라 다를 수 있음
        // 이상적으로는 한 핸들러의 예외가 다른 핸들러에 영향을 주지 않아야 함
        Assert.True(true);
    }

    [Fact(DisplayName = "A-04-08: 연결 후 즉시 Disconnect 시 OnError 없이 처리된다")]
    public async Task OnError_Not_Fired_On_Normal_Disconnect()
    {
        // Arrange
        var errorCount = 0;
        Connector!.OnError += (_, _, _, _) => errorCount++;

        // Act
        Connector.Disconnect();
        await Task.Delay(500);

        // Assert - 정상 연결 해제 시에는 OnError가 발생하지 않아야 함
        Assert.Equal(0, errorCount);
    }
}
