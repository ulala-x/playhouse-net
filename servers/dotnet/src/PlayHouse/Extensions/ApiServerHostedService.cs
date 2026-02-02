#nullable enable

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PlayHouse.Core.Api.Bootstrap;

namespace PlayHouse.Extensions;

/// <summary>
/// IHostedService implementation for ApiServer.
/// Manages ApiServer lifecycle within ASP.NET Core host.
/// </summary>
public sealed class ApiServerHostedService : IHostedService
{
    private readonly ApiServer _apiServer;
    private readonly ILogger<ApiServerHostedService>? _logger;

    public ApiServerHostedService(
        ApiServer apiServer,
        ILogger<ApiServerHostedService>? logger = null)
    {
        _apiServer = apiServer;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger?.LogInformation("Starting ApiServer...");
        await _apiServer.StartAsync();
        _logger?.LogInformation("ApiServer started");
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger?.LogInformation("Stopping ApiServer...");
        await _apiServer.StopAsync(cancellationToken);
        _logger?.LogInformation("ApiServer stopped");
    }
}
