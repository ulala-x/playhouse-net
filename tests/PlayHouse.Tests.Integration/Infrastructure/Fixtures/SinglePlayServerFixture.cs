#nullable enable

using Microsoft.Extensions.Logging;
using PlayHouse.Bootstrap;
using Xunit;

namespace PlayHouse.Tests.Integration.Infrastructure.Fixtures;

/// <summary>
/// PlayServer 1대를 공유하는 Collection Fixture.
/// 테스트 클래스 간 서버 인스턴스를 공유하여 초기화 비용을 절감합니다.
/// </summary>
public class SinglePlayServerFixture : IAsyncLifetime
{
    public PlayServer? PlayServer { get; private set; }

    public async Task InitializeAsync()
    {
        // Note: Static 리셋 하지 않음
        // - 각 테스트가 고유한 StageId를 사용하므로 테스트 간 격리됨
        // - TestStageImpl.GetByStageId()로 해당 Stage만 검증
        // - 서버가 Collection 전체에서 공유되므로 리셋 시 다른 테스트에 영향

        var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Warning));

        PlayServer = new PlayServerBootstrap()
            .Configure(options =>
            {
                options.ServerId = "1";
                options.BindEndpoint = "tcp://127.0.0.1:0";
                options.TcpPort = 0;
                options.RequestTimeoutMs = 30000;
                options.AuthenticateMessageId = "AuthenticateRequest";
                options.DefaultStageType = "TestStage";
            })
            .UseLogger(loggerFactory.CreateLogger<PlayServer>())
            .UseStage<TestStageImpl, TestActorImpl>("TestStage")
            .UseSystemController<TestSystemController>()
            .Build();

        await PlayServer.StartAsync();
    }

    public async Task DisposeAsync()
    {
        if (PlayServer != null)
        {
            await PlayServer.DisposeAsync();
        }
    }
}
