#nullable enable

using Microsoft.Extensions.Logging;
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
    private Type _systemControllerType = null!;
    private ILogger<ApiServer> _logger = null!;
    private IServiceProvider _serviceProvider = null!;

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
    /// System Controller를 등록합니다.
    /// </summary>
    /// <typeparam name="TSystemController">ISystemController 구현 타입.</typeparam>
    /// <returns>빌더 인스턴스.</returns>
    public ApiServerBootstrap UseSystemController<TSystemController>() where TSystemController : class, ISystemController
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

        // 필수 필드 검증
        var systemControllerType = _systemControllerType
            ?? throw new InvalidOperationException("SystemController is required. Use UseSystemController<T>() to register.");
        var logger = _logger
            ?? throw new InvalidOperationException("Logger is required. Use UseLogger() to register.");
        var serviceProvider = _serviceProvider
            ?? throw new InvalidOperationException("ServiceProvider is required. Use UseServiceProvider() to register.");

        // SystemController 인스턴스 생성
        var systemController = Activator.CreateInstance(systemControllerType) as ISystemController
            ?? throw new InvalidOperationException($"Failed to create SystemController instance: {systemControllerType.Name}");

        return new ApiServer(_options, systemController, serviceProvider, logger);
    }
}
