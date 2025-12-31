#nullable enable

using Microsoft.Extensions.DependencyInjection;
using PlayHouse.Abstractions.Api;
using PlayHouse.Abstractions.Play;
using PlayHouse.Abstractions.System;

namespace PlayHouse.Extensions;

/// <summary>
/// PlayServer 구성 빌더 인터페이스.
/// </summary>
public interface IPlayServerBuilder
{
    IServiceCollection Services { get; }
    IPlayServerBuilder UseStage<TStage>(string stageType) where TStage : class, IStage;
    IPlayServerBuilder UseActor<TActor>() where TActor : class, IActor;
    IPlayServerBuilder UseSystemController<T>() where T : class, ISystemController;
}

/// <summary>
/// ApiServer 구성 빌더 인터페이스.
/// </summary>
public interface IApiServerBuilder
{
    IServiceCollection Services { get; }
    IApiServerBuilder UseController<T>() where T : class, IApiController;
    IApiServerBuilder UseSystemController<T>() where T : class, ISystemController;
}
