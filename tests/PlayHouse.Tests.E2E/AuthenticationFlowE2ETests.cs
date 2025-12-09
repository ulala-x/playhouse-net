#nullable enable

using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using PlayHouse.Connector;
using PlayHouse.Infrastructure.Http;
using PlayHouse.Proto;
using PlayHouse.Tests.E2E.Proto;
using PlayHouse.Tests.E2E.TestFixtures;
using PlayHouse.Tests.Shared;
using Xunit;

namespace PlayHouse.Tests.E2E;

/// <summary>
/// PlayHouse 인증 플로우 E2E 테스트 (스펙 준수).
///
/// 플로우:
/// 1. HTTP API로 방 생성 및 roomToken 발급
/// 2. TCP 연결
/// 3. AuthenticateRequest(roomToken) 전송
/// 4. AuthenticateReply 수신 및 검증
/// </summary>
public class AuthenticationFlowE2ETests : IAsyncLifetime
{
    private TestServerFixture _fixture = null!;
    private TestServer _server = null!;
    private HttpClient _httpClient = null!;

    public async Task InitializeAsync()
    {
        ChatStage.Reset();

        _fixture = new TestServerFixture()
            .RegisterStage<ChatStage>("chat-stage")
            .RegisterActor<ChatActor>("chat-stage");

        _server = await _fixture.StartServerAsync();
        await Task.Delay(100); // 서버 초기화 대기

        // HTTP 클라이언트 생성 (HTTP 포트 사용)
        _httpClient = _server.CreateHttpClient();
    }

    public async Task DisposeAsync()
    {
        _httpClient?.Dispose();
        await _fixture.DisposeAsync();
    }

    [Fact(DisplayName = "[스펙준수] HTTP API로 roomToken 발급 → TCP 연결 → AuthenticateRequest → AuthenticateReply")]
    public async Task FullAuthenticationFlow_FollowsSpec()
    {
        // ============================================
        // 1단계: HTTP API로 방 생성 및 토큰 발급
        // ============================================
        var createRequest = new PlayHouse.Infrastructure.Http.CreateRoomRequest
        {
            RoomType = "chat-stage",
            Nickname = "TestUser"
        };

        var httpResponse = await _httpClient.PostAsJsonAsync("/api/rooms/create", createRequest);
        httpResponse.IsSuccessStatusCode.Should().BeTrue("HTTP API 호출이 성공해야 함");

        var roomInfo = await httpResponse.Content.ReadFromJsonAsync<PlayHouse.Infrastructure.Http.CreateRoomResponse>();

        roomInfo.Should().NotBeNull();
        roomInfo!.RoomToken.Should().NotBeNullOrWhiteSpace();
        roomInfo.Endpoint.Should().NotBeNullOrWhiteSpace();
        roomInfo.StageId.Should().BeGreaterThan(0);

        // ============================================
        // 2단계: TCP 연결
        // ============================================
        await using var client = new PlayHouseClient(null, null);
        var connectResult = await client.ConnectAsync(roomInfo.Endpoint, "temp-token");

        connectResult.Success.Should().BeTrue("TCP 연결이 성공해야 함");
        client.IsConnected.Should().BeTrue();

        // ============================================
        // 3단계: AuthenticateRequest 전송 (일방향 + On<> 수신)
        // ============================================
        var authReplyReceived = new TaskCompletionSource<AuthenticateReply>();
        client.On<AuthenticateReply>(reply =>
        {
            authReplyReceived.TrySetResult(reply);
        });

        var authRequest = new AuthenticateRequest
        {
            RoomToken = roomInfo.RoomToken
        };

        await client.SendAsync(authRequest);

        // ============================================
        // 4단계: AuthenticateReply 수신 및 검증
        // ============================================
        var authReply = await authReplyReceived.Task.WaitAsync(TimeSpan.FromSeconds(3));

        authReply.Should().NotBeNull();
        authReply.Authenticated.Should().BeTrue();
        authReply.AccountId.Should().BeGreaterThan(0);
        authReply.StageId.Should().Be(roomInfo.StageId);

        // 추가: 클라이언트 상태 확인 (TODO: 자동 설정 기능 추가 필요)
        // client.AccountId.Should().Be(authReply.AccountId, "클라이언트의 AccountId가 설정되어야 함");
        // client.StageId.Should().Be(authReply.StageId, "클라이언트의 StageId가 설정되어야 함");
    }

    [Fact(DisplayName = "잘못된 roomToken으로 인증 시도 시 실패")]
    public async Task InvalidToken_AuthenticationFails()
    {
        // Given: 잘못된 토큰
        var invalidToken = "invalid-token-12345";

        // When: TCP 연결 후 잘못된 토큰으로 인증
        await using var client = new PlayHouseClient(null, null);
        await client.ConnectAsync(_server.Endpoint, invalidToken);

        var authRequest = new AuthenticateRequest
        {
            RoomToken = invalidToken
        };

        var authResponse = await client.RequestAsync<AuthenticateRequest, AuthenticateReply>(
            authRequest,
            timeout: TimeSpan.FromSeconds(3));

        // Then: 인증 실패
        authResponse.Data.Should().NotBeNull();
        authResponse.Data!.Authenticated.Should().BeFalse();
        authResponse.Data.ErrorMessage.Should().NotBeNullOrWhiteSpace();
    }

    [Fact(DisplayName = "동일한 roomToken을 2번 사용 시 2번째는 실패 (1회용 토큰)")]
    public async Task SameToken_UsedTwice_SecondFails()
    {
        // Given: HTTP API로 roomToken 발급
        var createRequest = new PlayHouse.Infrastructure.Http.CreateRoomRequest
        {
            RoomType = "chat-stage",
            Nickname = "TestUser"
        };

        var httpResponse = await _httpClient.PostAsJsonAsync("/api/rooms/create", createRequest);
        var roomInfo = await httpResponse.Content.ReadFromJsonAsync<PlayHouse.Infrastructure.Http.CreateRoomResponse>();

        // When: 첫 번째 클라이언트가 토큰 사용
        await using var client1 = new PlayHouseClient(null, null);
        await client1.ConnectAsync(roomInfo!.Endpoint, roomInfo.RoomToken);

        var authRequest1 = new AuthenticateRequest { RoomToken = roomInfo.RoomToken };
        var authResponse1 = await client1.RequestAsync<AuthenticateRequest, AuthenticateReply>(
            authRequest1,
            timeout: TimeSpan.FromSeconds(3));

        authResponse1.Data!.Authenticated.Should().BeTrue("첫 번째 인증은 성공해야 함");

        // When: 두 번째 클라이언트가 동일한 토큰 사용
        await using var client2 = new PlayHouseClient(null, null);
        await client2.ConnectAsync(roomInfo.Endpoint, roomInfo.RoomToken);

        var authRequest2 = new AuthenticateRequest { RoomToken = roomInfo.RoomToken };
        var authResponse2 = await client2.RequestAsync<AuthenticateRequest, AuthenticateReply>(
            authRequest2,
            timeout: TimeSpan.FromSeconds(3));

        // Then: 두 번째 인증은 실패해야 함 (1회용 토큰)
        authResponse2.Data!.Authenticated.Should().BeFalse("토큰은 1회용이므로 두 번째 사용 시 실패해야 함");
    }

    [Fact(DisplayName = "인증 후 메시지 송수신 가능")]
    public async Task AfterAuthentication_CanSendAndReceiveMessages()
    {
        // Given: 인증 완료된 클라이언트
        var createRequest = new PlayHouse.Infrastructure.Http.CreateRoomRequest
        {
            RoomType = "chat-stage",
            Nickname = "TestUser"
        };

        var httpResponse = await _httpClient.PostAsJsonAsync("/api/rooms/create", createRequest);
        var roomInfo = await httpResponse.Content.ReadFromJsonAsync<PlayHouse.Infrastructure.Http.CreateRoomResponse>();

        await using var client = new PlayHouseClient(null, null);
        await client.ConnectAsync(roomInfo!.Endpoint, roomInfo.RoomToken);

        var authRequest = new AuthenticateRequest { RoomToken = roomInfo.RoomToken };
        var authResponse = await client.RequestAsync<AuthenticateRequest, AuthenticateReply>(
            authRequest,
            timeout: TimeSpan.FromSeconds(3));

        authResponse.Data!.Authenticated.Should().BeTrue();

        // When: EchoRequest 전송
        var echoRequest = new EchoRequest
        {
            Content = "Hello after authentication!",
            Sequence = 1
        };

        var echoResponse = await client.RequestAsync<EchoRequest, EchoReply>(
            echoRequest,
            timeout: TimeSpan.FromSeconds(3));

        // Then: EchoReply 수신
        echoResponse.Success.Should().BeTrue();
        echoResponse.Data.Should().NotBeNull();
        echoResponse.Data!.Content.Should().Be("Hello after authentication!");
    }
}
