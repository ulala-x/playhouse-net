#nullable enable

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PlayHouse.Abstractions;
using PlayHouse.Abstractions.Play;
using PlayHouse.Abstractions.System;
using PlayHouse.Bootstrap;
using PlayHouse.Runtime.ServerMesh.Discovery;

namespace PlayHouse.Extensions;

internal sealed class PlayServerBuilder : IPlayServerBuilder
{
    private readonly IServiceCollection _services;
    private readonly PlayServerOption _options;
    private readonly Dictionary<string, Type> _stageTypes = new();
    private Type? _actorType;
    private Type? _systemControllerType;

    public IServiceCollection Services => _services;

    internal PlayServerBuilder(IServiceCollection services, PlayServerOption options)
    {
        _services = services;
        _options = options;
    }

    public IPlayServerBuilder UseStage<TStage>(string stageType) where TStage : class, IStage
    {
        _stageTypes[stageType] = typeof(TStage);
        return this;
    }

    public IPlayServerBuilder UseActor<TActor>() where TActor : class, IActor
    {
        _actorType = typeof(TActor);
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
            var producer = new PlayProducer(_stageTypes, _actorType, sp);
            var logger = sp.GetService<ILogger<PlayServer>>();
            return new PlayServer(_options, producer, _systemControllerType, sp, logger);
        });

        // IPlayServerControl로도 접근 가능하도록 등록
        _services.AddSingleton<IPlayServerControl>(sp => sp.GetRequiredService<PlayServer>());
    }
}
