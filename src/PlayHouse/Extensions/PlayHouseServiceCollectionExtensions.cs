#nullable enable

using Microsoft.Extensions.DependencyInjection;
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

        var builder = new PlayServerBuilder(services, options);

        // Build를 즉시 호출하여 서비스 등록 완료
        builder.Build();

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

        var builder = new ApiServerBuilder(services, options);

        // Build를 즉시 호출하여 서비스 등록 완료
        builder.Build();

        return builder;
    }
}
