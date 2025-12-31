#nullable enable

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PlayHouse.Abstractions;
using PlayHouse.Abstractions.Play;
using PlayHouse.Abstractions.System;
using PlayHouse.Bootstrap;
using PlayHouse.Core.Play.Bootstrap;
using PlayHouse.Runtime.ServerMesh.Discovery;

namespace PlayHouse.Extensions;

internal sealed class PlayServerBuilder : IPlayServerBuilder
{
    private readonly IServiceCollection _services;
    private readonly PlayServerOption _options;
    private readonly Dictionary<string, Type> _stageTypes = new();
    private readonly Dictionary<string, Type> _actorTypes = new();
    private Type? _systemControllerType;

    public IServiceCollection Services => _services;

    internal PlayServerBuilder(IServiceCollection services, PlayServerOption options)
    {
        _services = services;
        _options = options;
    }

    public IPlayServerBuilder UseStage<TStage, TActor>(string stageType)
        where TStage : class, IStage
        where TActor : class, IActor
    {
        _stageTypes[stageType] = typeof(TStage);
        _actorTypes[stageType] = typeof(TActor);
        return this;
    }

    public IPlayServerBuilder UseSystemController<T>() where T : class, ISystemController
    {
        _systemControllerType = typeof(T);
        _services.AddSingleton<ISystemController, T>();
        return this;
    }

    internal void Build()
    {
        // IServerInfoCenter 싱글턴 등록
        _services.AddSingleton<IServerInfoCenter, XServerInfoCenter>();

        // PlayServer 싱글턴 등록
        _services.AddSingleton<PlayServer>(sp =>
        {
            // SystemController 필수 검증 (PlayServer 생성 시점에)
            if (_systemControllerType == null)
                throw new InvalidOperationException(
                    "SystemController is required. Use UseSystemController<T>() to register.");

            // ILogger 필수 검증
            var logger = sp.GetRequiredService<ILogger<PlayServer>>();

            // ISystemController 필수 검증
            var systemController = sp.GetRequiredService<ISystemController>();

            var producer = new PlayProducer(_stageTypes, _actorTypes, sp);
            return new PlayServer(_options, producer, systemController, sp, logger);
        });

        // IPlayServerControl로도 접근 가능하도록 등록
        _services.AddSingleton<IPlayServerControl>(sp => sp.GetRequiredService<PlayServer>());
    }
}
