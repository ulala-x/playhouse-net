#nullable enable

using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.DependencyInjection;
using PlayHouse.Abstractions;
using PlayHouse.Bootstrap;
using PlayHouse.Extensions;
using Xunit;

namespace PlayHouse.Tests.Integration.Infrastructure.Fixtures;

/// <summary>
/// DI 통합 테스트용 PlayServer Fixture.
/// IServiceCollection을 통해 사용자 서비스를 등록하고 Stage/Actor에 주입합니다.
/// </summary>
public class DIPlayServerFixture : IAsyncLifetime
{
    public IServiceProvider? ServiceProvider { get; private set; }
    public PlayServer? PlayServer { get; private set; }
    public int TcpPort { get; private set; }
    public string BindEndpoint { get; private set; } = string.Empty;

    public async Task InitializeAsync()
    {
        var services = new ServiceCollection();

        // 사용자 서비스 등록 (DI 검증용)
        services.AddSingleton<ITestService, TestService>();

        // 포트 할당
        TcpPort = GetAvailablePort();
        var zmqPort = GetAvailablePort();
        BindEndpoint = $"tcp://127.0.0.1:{zmqPort}";

        // PlayServer 등록 및 구성
        services.AddPlayServer(options =>
        {
            options.ServiceType = ServiceType.Play;
            options.ServerId = "di-test-1";
            options.BindEndpoint = BindEndpoint;
            options.TcpPort = TcpPort;
            options.RequestTimeoutMs = 30000;
            options.AuthenticateMessageId = "AuthenticateRequest";
            options.DefaultStageType = "DITest";
        })
        .UseStage<DITestStage>("DITest")
        .UseActor<DITestActor>();

        ServiceProvider = services.BuildServiceProvider();
        PlayServer = ServiceProvider.GetRequiredService<PlayServer>();

        await PlayServer.StartAsync();
    }

    public async Task DisposeAsync()
    {
        // Static 필드 정리
        DITestStage.ResetAll();
        DITestActor.ResetAll();

        if (PlayServer != null)
        {
            await PlayServer.DisposeAsync();
        }

        // ServiceProvider는 IAsyncDisposable을 구현한 경우 DisposeAsync 사용
        if (ServiceProvider is IAsyncDisposable asyncDisposable)
        {
            await asyncDisposable.DisposeAsync();
        }
        else if (ServiceProvider is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }

    /// <summary>
    /// 사용 가능한 TCP 포트를 찾습니다.
    /// </summary>
    private static int GetAvailablePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}
