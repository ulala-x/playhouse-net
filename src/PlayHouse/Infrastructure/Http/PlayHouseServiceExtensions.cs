#nullable enable

using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PlayHouse.Abstractions;
using PlayHouse.Core.Messaging;
using PlayHouse.Core.Session;
using PlayHouse.Core.Stage;
using PlayHouse.Core.Timer;
using PlayHouse.Infrastructure.Serialization;

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

        // Register core services
        RegisterCoreServices(services);

        // Register PlayHouse server as hosted service
        services.AddHostedService<PlayHouseServer>();
        services.AddSingleton<PlayHouseServer>(sp =>
            sp.GetServices<Microsoft.Extensions.Hosting.IHostedService>()
                .OfType<PlayHouseServer>()
                .First());

        // Register controllers
        services.AddControllers()
            .AddApplicationPart(typeof(RoomController).Assembly);

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

        // Register core services
        RegisterCoreServices(services);

        // Register PlayHouse server as hosted service
        services.AddHostedService<PlayHouseServer>();
        services.AddSingleton<PlayHouseServer>(sp =>
            sp.GetServices<Microsoft.Extensions.Hosting.IHostedService>()
                .OfType<PlayHouseServer>()
                .First());

        // Register controllers
        services.AddControllers()
            .AddApplicationPart(typeof(RoomController).Assembly);

        return services;
    }

    /// <summary>
    /// Registers a stage type for creation via HTTP API.
    /// </summary>
    /// <typeparam name="TStage">The stage type to register.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <param name="stageTypeName">The stage type name (identifier).</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddStageType<TStage>(
        this IServiceCollection services,
        string stageTypeName)
        where TStage : IStage
    {
        // Register a configuration action to be applied when StageFactory is resolved
        services.Configure<StageRegistrationOptions>(options =>
        {
            options.StageTypes.Add((stageTypeName, typeof(TStage)));
        });

        return services;
    }

    /// <summary>
    /// Registers an actor type for future use.
    /// </summary>
    /// <typeparam name="TActor">The actor type to register.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <remarks>
    /// Currently stores registration for future use. Actor instantiation
    /// is handled by user stage implementations.
    /// </remarks>
    public static IServiceCollection AddActorType<TActor>(
        this IServiceCollection services)
        where TActor : IActor
    {
        // Register a configuration action for future use
        services.Configure<ActorRegistrationOptions>(options =>
        {
            options.ActorTypes.Add(typeof(TActor));
        });

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

    /// <summary>
    /// Registers core PlayHouse services.
    /// </summary>
    private static void RegisterCoreServices(IServiceCollection services)
    {
        // Register serialization
        services.AddSingleton<PacketSerializer>();

        // Register session management
        services.AddSingleton<SessionManager>();

        // Register stage management
        services.AddSingleton<StagePool>();

        // Register messaging (depends on StagePool)
        services.AddSingleton<PacketDispatcher>();

        // Register timer management (depends on PacketDispatcher)
        services.AddSingleton<TimerManager>(sp =>
        {
            var dispatcher = sp.GetRequiredService<PacketDispatcher>();
            var logger = sp.GetRequiredService<Microsoft.Extensions.Logging.ILoggerFactory>()
                .CreateLogger<TimerManager>();
            return new TimerManager(packet => dispatcher.Dispatch(packet), logger);
        });

        // Register stage factory (depends on all core services)
        services.AddSingleton<StageFactory>(sp =>
        {
            var stagePool = sp.GetRequiredService<StagePool>();
            var dispatcher = sp.GetRequiredService<PacketDispatcher>();
            var timerManager = sp.GetRequiredService<TimerManager>();
            var sessionManager = sp.GetRequiredService<SessionManager>();
            var loggerFactory = sp.GetRequiredService<Microsoft.Extensions.Logging.ILoggerFactory>();

            var factory = new StageFactory(
                stagePool,
                dispatcher,
                timerManager,
                sessionManager,
                loggerFactory);

            // Register stage types from configuration
            var stageRegistrations = sp.GetService<IOptions<StageRegistrationOptions>>()?.Value;
            if (stageRegistrations != null)
            {
                foreach (var (stageTypeName, stageType) in stageRegistrations.StageTypes)
                {
                    factory.Registry.RegisterStageType(stageTypeName, stageType);
                }
            }

            return factory;
        });

        // Register options for stage and actor registration
        services.AddOptions<StageRegistrationOptions>();
        services.AddOptions<ActorRegistrationOptions>();
    }
}

/// <summary>
/// Options for stage type registration.
/// </summary>
internal sealed class StageRegistrationOptions
{
    /// <summary>
    /// Gets the list of registered stage types.
    /// </summary>
    public List<(string StageTypeName, Type StageType)> StageTypes { get; } = new();
}

/// <summary>
/// Options for actor type registration.
/// </summary>
internal sealed class ActorRegistrationOptions
{
    /// <summary>
    /// Gets the list of registered actor types.
    /// </summary>
    public List<Type> ActorTypes { get; } = new();
}
