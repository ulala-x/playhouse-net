#nullable enable

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PlayHouse.Core.Play.Bootstrap;

namespace PlayHouse.Extensions;

/// <summary>
/// IHostedService implementation for PlayServer.
/// Manages PlayServer lifecycle within ASP.NET Core host.
/// </summary>
public sealed class PlayServerHostedService(
    PlayServer playServer,
    ILogger<PlayServerHostedService> logger)
    : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("Starting PlayServer...");
        await playServer.StartAsync();
        logger.LogInformation("PlayServer started on port {Port}", playServer.ActualTcpPort);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("Stopping PlayServer...");
        await playServer.StopAsync(cancellationToken);
        logger.LogInformation("PlayServer stopped");
    }
}
