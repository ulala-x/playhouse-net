#nullable enable

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PlayHouse.Abstractions;
using PlayHouse.Core.Messaging;
using PlayHouse.Core.Session;
using PlayHouse.Core.Stage;
using PlayHouse.Core.Timer;
using PlayHouse.Infrastructure.Http;

namespace PlayHouse.Core.Bootstrap;

/// <summary>
/// PlayHouse 서버 부트스트랩 빌더.
/// Fluent API로 서버 설정을 구성합니다.
/// </summary>
public sealed class PlayHouseBootstrapBuilder
{
    private Action<PlayHouseOptions>? _optionsAction;
    private Action<ILoggingBuilder>? _loggingAction;
    private readonly StageTypeRegistry _registry = new();
    private Action<IServiceCollection>? _additionalServices;

    /// <summary>
    /// PlayHouse 옵션을 설정합니다.
    /// </summary>
    public PlayHouseBootstrapBuilder WithOptions(Action<PlayHouseOptions> configure)
    {
        _optionsAction = configure;
        return this;
    }

    /// <summary>
    /// 로깅을 설정합니다.
    /// </summary>
    public PlayHouseBootstrapBuilder WithLogging(Action<ILoggingBuilder> configure)
    {
        _loggingAction = configure;
        return this;
    }

    /// <summary>
    /// Stage 타입을 등록합니다.
    /// </summary>
    public PlayHouseBootstrapBuilder WithStage<TStage>(string stageTypeName) where TStage : IStage
    {
        _registry.RegisterStageType<TStage>(stageTypeName);
        return this;
    }

    /// <summary>
    /// Actor 타입을 등록합니다.
    /// </summary>
    public PlayHouseBootstrapBuilder WithActor<TActor>(string stageTypeName) where TActor : IActor
    {
        _registry.RegisterActorType<TActor>(stageTypeName);
        return this;
    }

    /// <summary>
    /// 추가 서비스를 등록합니다.
    /// </summary>
    public PlayHouseBootstrapBuilder WithServices(Action<IServiceCollection> configure)
    {
        _additionalServices = configure;
        return this;
    }

    /// <summary>
    /// StageTypeRegistry를 반환합니다 (테스트용).
    /// </summary>
    public StageTypeRegistry Registry => _registry;

    /// <summary>
    /// IHost를 빌드합니다.
    /// </summary>
    public IHost Build()
    {
        return Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                ConfigurePlayHouseServices(services);
                _additionalServices?.Invoke(services);
            })
            .ConfigureLogging(logging =>
            {
                _loggingAction?.Invoke(logging);
            })
            .Build();
    }

    /// <summary>
    /// 서버를 빌드하고 실행합니다.
    /// </summary>
    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        using var host = Build();
        await host.RunAsync(cancellationToken);
    }

    /// <summary>
    /// 서버를 빌드하고 시작합니다 (테스트용 - 블로킹하지 않음).
    /// </summary>
    public async Task<IHost> StartAsync(CancellationToken cancellationToken = default)
    {
        var host = Build();
        await host.StartAsync(cancellationToken);
        return host;
    }

    private void ConfigurePlayHouseServices(IServiceCollection services)
    {
        // 옵션 설정
        services.AddOptions<PlayHouseOptions>()
            .Configure(opts =>
            {
                // 기본값 설정
                typeof(PlayHouseOptions).GetProperty(nameof(PlayHouseOptions.Ip))!
                    .SetValue(opts, "127.0.0.1");
                typeof(PlayHouseOptions).GetProperty(nameof(PlayHouseOptions.Port))!
                    .SetValue(opts, 5000);
                typeof(PlayHouseOptions).GetProperty(nameof(PlayHouseOptions.EnableWebSocket))!
                    .SetValue(opts, false);

                // 사용자 설정 적용
                _optionsAction?.Invoke(opts);
            })
            .ValidateOnStart();

        // Core 서비스
        services.AddSingleton<Infrastructure.Serialization.PacketSerializer>();
        services.AddSingleton<SessionManager>();
        services.AddSingleton<StagePool>();
        services.AddSingleton<PacketDispatcher>();
        services.AddSingleton<RoomTokenManager>();

        // StageTypeRegistry를 싱글톤으로 등록
        services.AddSingleton(_registry);

        // TimerManager
        services.AddSingleton<TimerManager>(sp =>
        {
            var dispatcher = sp.GetRequiredService<PacketDispatcher>();
            var logger = sp.GetRequiredService<ILoggerFactory>()
                .CreateLogger<TimerManager>();
            return new TimerManager(
                packet => dispatcher.Dispatch(packet),
                logger);
        });

        // StageFactory - Registry 주입
        services.AddSingleton<StageFactory>(sp =>
        {
            var stagePool = sp.GetRequiredService<StagePool>();
            var dispatcher = sp.GetRequiredService<PacketDispatcher>();
            var timerManager = sp.GetRequiredService<TimerManager>();
            var sessionManager = sp.GetRequiredService<SessionManager>();
            var loggerFactory = sp.GetRequiredService<ILoggerFactory>();

            var factory = new StageFactory(
                stagePool,
                dispatcher,
                timerManager,
                sessionManager,
                loggerFactory);

            // Registry에 등록된 타입들을 StageFactory Registry로 복사
            foreach (var (stageTypeName, stageType) in _registry.GetAllStageTypes())
            {
                factory.Registry.RegisterStageType(stageTypeName, stageType);
            }
            foreach (var (stageTypeName, actorType) in _registry.GetAllActorTypes())
            {
                factory.Registry.RegisterActorType(stageTypeName, actorType);
            }

            return factory;
        });

        // PlayHouseServer
        services.AddHostedService<PlayHouseServer>();
        services.AddSingleton<PlayHouseServer>(sp =>
            sp.GetServices<IHostedService>()
                .OfType<PlayHouseServer>()
                .First());
    }
}
