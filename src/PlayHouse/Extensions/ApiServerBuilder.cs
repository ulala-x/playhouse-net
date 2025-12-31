#nullable enable

using Microsoft.Extensions.DependencyInjection;
using PlayHouse.Abstractions;
using PlayHouse.Abstractions.Api;
using PlayHouse.Abstractions.System;
using PlayHouse.Bootstrap;
using PlayHouse.Core.Api.Bootstrap;

namespace PlayHouse.Extensions;

internal sealed class ApiServerBuilder : IApiServerBuilder
{
    private readonly IServiceCollection _services;
    private readonly ApiServerOption _options;
    private readonly List<Type> _controllerTypes = new();
    private Type? _systemControllerType;

    public IServiceCollection Services => _services;

    internal ApiServerBuilder(IServiceCollection services, ApiServerOption options)
    {
        _services = services;
        _options = options;
    }

    public IApiServerBuilder UseController<T>() where T : class, IApiController
    {
        _controllerTypes.Add(typeof(T));
        _services.AddTransient<IApiController, T>();
        return this;
    }

    public IApiServerBuilder UseSystemController<T>() where T : class, ISystemController
    {
        _systemControllerType = typeof(T);
        _services.AddSingleton<ISystemController, T>();
        return this;
    }

    internal void Build()
    {
        // ApiServer 싱글턴 등록
        _services.AddSingleton<ApiServer>(sp =>
        {
            return new ApiServer(_options, _controllerTypes, _systemControllerType, sp);
        });

        // IApiServerControl로도 접근 가능하도록 등록
        _services.AddSingleton<IApiServerControl>(sp => sp.GetRequiredService<ApiServer>());
    }
}
