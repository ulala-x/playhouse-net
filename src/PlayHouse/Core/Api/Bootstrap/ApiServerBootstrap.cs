#nullable enable

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PlayHouse.Abstractions.Api;
using PlayHouse.Abstractions.System;
using PlayHouse.Core.Api.Bootstrap;

namespace PlayHouse.Bootstrap;

/// <summary>
/// API Server 부트스트랩 빌더.
/// </summary>
/// <remarks>
/// 사용 예시:
/// <code>
/// var apiServer = new ApiServerBootstrap()
///     .Configure(options =>
///     {
///         options.ServiceId = 2;
///         options.ServerId = 1;
///         options.BindEndpoint = "tcp://0.0.0.0:5100";
///     })
///     .UseController&lt;GameApiController&gt;()
///     .Build();
///
/// await apiServer.StartAsync();
/// </code>
/// </remarks>
public sealed class ApiServerBootstrap
{
    private readonly ApiServerOption _options = new();
    private readonly List<Type> _controllerTypes = new();
    private Type? _systemControllerType;
    private ILogger<ApiServer>? _logger;
    private IServiceProvider? _serviceProvider;

    /// <summary>
    /// 서버 옵션을 설정합니다.
    /// </summary>
    /// <param name="configure">설정 액션.</param>
    /// <returns>빌더 인스턴스.</returns>
    public ApiServerBootstrap Configure(Action<ApiServerOption> configure)
    {
        configure(_options);
        return this;
    }

    /// <summary>
    /// API Controller 타입을 등록합니다.
    /// </summary>
    /// <typeparam name="TController">IApiController 구현 타입.</typeparam>
    /// <returns>빌더 인스턴스.</returns>
    public ApiServerBootstrap UseController<TController>() where TController : class, IApiController
    {
        _controllerTypes.Add(typeof(TController));
        return this;
    }

    /// <summary>
    /// System Controller를 등록합니다.
    /// </summary>
    /// <typeparam name="TSystemController">ISystemController 구현 타입.</typeparam>
    /// <returns>빌더 인스턴스.</returns>
    public ApiServerBootstrap UseSystemController<TSystemController>() where TSystemController : class, Abstractions.System.ISystemController
    {
        _systemControllerType = typeof(TSystemController);
        return this;
    }

    /// <summary>
    /// Logger를 설정합니다.
    /// </summary>
    /// <param name="logger">ILogger&lt;ApiServer&gt; 인스턴스.</param>
    /// <returns>빌더 인스턴스.</returns>
    public ApiServerBootstrap UseLogger(ILogger<ApiServer> logger)
    {
        _logger = logger;
        return this;
    }

    /// <summary>
    /// ServiceProvider를 설정합니다.
    /// </summary>
    /// <param name="serviceProvider">IServiceProvider 인스턴스.</param>
    /// <returns>빌더 인스턴스.</returns>
    public ApiServerBootstrap UseServiceProvider(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
        return this;
    }

    /// <summary>
    /// API Server 인스턴스를 생성합니다.
    /// </summary>
    /// <returns>ApiServer 인스턴스.</returns>
    public ApiServer Build()
    {
        _options.Validate();

        // ILogger 필수 검증
        if (_logger == null)
            throw new InvalidOperationException(
                "ILogger is required. Use UseLogger(logger) to register.");

        // ServiceProvider 생성 또는 사용
        var serviceProvider = _serviceProvider;
        if (serviceProvider == null)
        {
            // 외부에서 제공되지 않았으면 내부에서 생성
            var services = new ServiceCollection();

            // Logging 설정 (Controllers가 ILogger를 주입받을 수 있도록)
            services.AddLogging(builder =>
            {
                // NullLogger 사용 (기본값)
            });

            // API Controllers 등록
            foreach (var controllerType in _controllerTypes)
            {
                services.AddTransient(typeof(IApiController), controllerType);
            }

            // SystemController 등록
            if (_systemControllerType != null)
            {
                services.AddSingleton(typeof(ISystemController), _systemControllerType);
            }

            serviceProvider = services.BuildServiceProvider();
        }

        return new ApiServer(_options, _controllerTypes, _systemControllerType, serviceProvider, _logger!);
    }
}
