#nullable enable

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PlayHouse.Bootstrap;
using PlayHouse.Core.Api.Bootstrap;
using PlayHouse.Core.Play.Bootstrap;

namespace PlayHouse.Extensions;

/// <summary>
/// PlayHouse DI 등록을 위한 IServiceCollection 확장 메서드.
/// </summary>
public static class PlayHouseServiceCollectionExtensions
{
    /// <summary>
    /// PlayServer를 DI 컨테이너에 등록합니다.
    /// </summary>
    public static IPlayServerBuilder AddPlayServer(
        this IServiceCollection services,
        Action<PlayServerOption> configure)
    {
        var options = new PlayServerOption();
        configure(options);
        options.Validate();

        services.AddSingleton(options);

        // ILoggerFactory가 등록되지 않은 경우 NullLoggerFactory 등록
        services.TryAddSingleton<ILoggerFactory, NullLoggerFactory>();

        var builder = new PlayServerBuilder(services, options);

        return builder;
    }

    /// <summary>
    /// ApiServer를 DI 컨테이너에 등록합니다.
    /// </summary>
    public static IApiServerBuilder AddApiServer(
        this IServiceCollection services,
        Action<ApiServerOption> configure)
    {
        var options = new ApiServerOption();
        configure(options);
        options.Validate();

        services.AddSingleton(options);

        // ILoggerFactory가 등록되지 않은 경우 NullLoggerFactory 등록
        services.TryAddSingleton<ILoggerFactory, NullLoggerFactory>();

        var builder = new ApiServerBuilder(services, options);

        return builder;
    }
}
