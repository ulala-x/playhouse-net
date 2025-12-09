#nullable enable

using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace PlayHouse.Infrastructure.Http;

/// <summary>
/// Extension methods for registering PlayHouse services.
/// </summary>
public static class PlayHouseServiceExtensions
{
    /// <summary>
    /// Adds PlayHouse services to the service collection.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The configuration instance.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddPlayHouse(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Register options with validation
        services.AddOptions<PlayHouseOptions>()
            .Bind(configuration.GetSection(PlayHouseOptions.SectionName))
            .ValidateOnStart();

        // Register options validator
        services.AddSingleton<IValidateOptions<PlayHouseOptions>, PlayHouseOptions>();

        // Register PlayHouse server as hosted service
        services.AddHostedService<PlayHouseServer>();
        services.AddSingleton<PlayHouseServer>(sp =>
            sp.GetServices<Microsoft.Extensions.Hosting.IHostedService>()
                .OfType<PlayHouseServer>()
                .First());

        return services;
    }

    /// <summary>
    /// Adds PlayHouse services to the service collection with custom configuration.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configureOptions">Action to configure options.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddPlayHouse(
        this IServiceCollection services,
        Action<PlayHouseOptions> configureOptions)
    {
        // Register options with validation
        services.AddOptions<PlayHouseOptions>()
            .Configure(configureOptions)
            .ValidateOnStart();

        // Register options validator
        services.AddSingleton<IValidateOptions<PlayHouseOptions>, PlayHouseOptions>();

        // Register PlayHouse server as hosted service
        services.AddHostedService<PlayHouseServer>();
        services.AddSingleton<PlayHouseServer>(sp =>
            sp.GetServices<Microsoft.Extensions.Hosting.IHostedService>()
                .OfType<PlayHouseServer>()
                .First());

        return services;
    }

    /// <summary>
    /// Configures PlayHouse middleware for WebSocket support.
    /// </summary>
    /// <param name="app">The application builder.</param>
    /// <returns>The application builder for chaining.</returns>
    public static IApplicationBuilder UsePlayHouse(this IApplicationBuilder app)
    {
        var options = app.ApplicationServices.GetRequiredService<IOptions<PlayHouseOptions>>().Value;

        if (options.EnableWebSocket)
        {
            app.UseWebSockets();

            app.Use(async (context, next) =>
            {
                if (context.Request.Path == options.WebSocketPath)
                {
                    var server = context.RequestServices.GetRequiredService<PlayHouseServer>();
                    if (server.WebSocketServer != null)
                    {
                        await server.WebSocketServer.HandleWebSocketAsync(context);
                    }
                    else
                    {
                        context.Response.StatusCode = 503;
                        await context.Response.Body.WriteAsync(
                            System.Text.Encoding.UTF8.GetBytes("WebSocket server not available"));
                    }
                }
                else
                {
                    await next();
                }
            });
        }

        return app;
    }
}
