namespace PlayHouse.Connector.Extensions;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

/// <summary>
/// Extension methods for registering PlayHouse.Connector services with dependency injection.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers IPlayHouseClient as a singleton service with default options.
    /// </summary>
    /// <param name="services">The service collection</param>
    /// <returns>The service collection for chaining</returns>
    public static IServiceCollection AddPlayHouseClient(this IServiceCollection services)
    {
        return AddPlayHouseClient(services, options => { });
    }

    /// <summary>
    /// Registers IPlayHouseClient as a singleton service with custom options.
    /// </summary>
    /// <param name="services">The service collection</param>
    /// <param name="configureOptions">Action to configure client options</param>
    /// <returns>The service collection for chaining</returns>
    public static IServiceCollection AddPlayHouseClient(
        this IServiceCollection services,
        Action<PlayHouseClientOptions> configureOptions)
    {
        if (services == null)
        {
            throw new ArgumentNullException(nameof(services));
        }

        if (configureOptions == null)
        {
            throw new ArgumentNullException(nameof(configureOptions));
        }

        // Configure options
        var options = new PlayHouseClientOptions();
        configureOptions(options);
        options.Validate();

        // Register options as singleton
        services.TryAddSingleton(options);

        // Register client as singleton
        services.TryAddSingleton<IPlayHouseClient>(sp =>
        {
            var clientOptions = sp.GetRequiredService<PlayHouseClientOptions>();
            var logger = sp.GetService<Microsoft.Extensions.Logging.ILogger<PlayHouseClient>>();
            return new PlayHouseClient(clientOptions, logger);
        });

        return services;
    }

    /// <summary>
    /// Registers IPlayHouseClient as a transient service (new instance per injection).
    /// </summary>
    /// <param name="services">The service collection</param>
    /// <param name="configureOptions">Action to configure client options</param>
    /// <returns>The service collection for chaining</returns>
    public static IServiceCollection AddPlayHouseClientTransient(
        this IServiceCollection services,
        Action<PlayHouseClientOptions>? configureOptions = null)
    {
        if (services == null)
        {
            throw new ArgumentNullException(nameof(services));
        }

        // Configure options
        var options = new PlayHouseClientOptions();
        configureOptions?.Invoke(options);
        options.Validate();

        // Register options as singleton (shared across instances)
        services.TryAddSingleton(options);

        // Register client as transient
        services.AddTransient<IPlayHouseClient>(sp =>
        {
            var clientOptions = sp.GetRequiredService<PlayHouseClientOptions>();
            var logger = sp.GetService<Microsoft.Extensions.Logging.ILogger<PlayHouseClient>>();
            return new PlayHouseClient(clientOptions, logger);
        });

        return services;
    }

    /// <summary>
    /// Registers IPlayHouseClient as a scoped service (one instance per scope).
    /// </summary>
    /// <param name="services">The service collection</param>
    /// <param name="configureOptions">Action to configure client options</param>
    /// <returns>The service collection for chaining</returns>
    public static IServiceCollection AddPlayHouseClientScoped(
        this IServiceCollection services,
        Action<PlayHouseClientOptions>? configureOptions = null)
    {
        if (services == null)
        {
            throw new ArgumentNullException(nameof(services));
        }

        // Configure options
        var options = new PlayHouseClientOptions();
        configureOptions?.Invoke(options);
        options.Validate();

        // Register options as singleton (shared across instances)
        services.TryAddSingleton(options);

        // Register client as scoped
        services.AddScoped<IPlayHouseClient>(sp =>
        {
            var clientOptions = sp.GetRequiredService<PlayHouseClientOptions>();
            var logger = sp.GetService<Microsoft.Extensions.Logging.ILogger<PlayHouseClient>>();
            return new PlayHouseClient(clientOptions, logger);
        });

        return services;
    }

    /// <summary>
    /// Registers MockPlayHouseClient for testing purposes.
    /// </summary>
    /// <param name="services">The service collection</param>
    /// <returns>The service collection for chaining</returns>
    public static IServiceCollection AddMockPlayHouseClient(this IServiceCollection services)
    {
        if (services == null)
        {
            throw new ArgumentNullException(nameof(services));
        }

        services.TryAddSingleton<IPlayHouseClient, MockPlayHouseClient>();

        return services;
    }

    /// <summary>
    /// Registers a factory for creating IPlayHouseClient instances.
    /// </summary>
    /// <param name="services">The service collection</param>
    /// <param name="factory">Factory function to create client instances</param>
    /// <returns>The service collection for chaining</returns>
    public static IServiceCollection AddPlayHouseClientFactory(
        this IServiceCollection services,
        Func<IServiceProvider, IPlayHouseClient> factory)
    {
        if (services == null)
        {
            throw new ArgumentNullException(nameof(services));
        }

        if (factory == null)
        {
            throw new ArgumentNullException(nameof(factory));
        }

        services.TryAddSingleton<IPlayHouseClient>(factory);

        return services;
    }
}
