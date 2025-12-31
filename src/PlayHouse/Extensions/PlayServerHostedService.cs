#nullable enable

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PlayHouse.Bootstrap;

namespace PlayHouse.Extensions;

/// <summary>
/// IHostedService implementation for PlayServer.
/// Manages PlayServer lifecycle within ASP.NET Core host.
/// </summary>
public sealed class PlayServerHostedService : IHostedService
{
    private readonly PlayServer _playServer;
    private readonly ILogger<PlayServerHostedService>? _logger;

    public PlayServerHostedService(
        PlayServer playServer,
        ILogger<PlayServerHostedService>? logger = null)
    {
        _playServer = playServer;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger?.LogInformation("Starting PlayServer...");
        await _playServer.StartAsync();
        _logger?.LogInformation("PlayServer started on port {Port}", _playServer.ActualTcpPort);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger?.LogInformation("Stopping PlayServer...");
        await _playServer.StopAsync(cancellationToken);
        _logger?.LogInformation("PlayServer stopped");
    }
}
