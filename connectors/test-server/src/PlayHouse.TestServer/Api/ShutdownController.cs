#nullable enable

using Microsoft.AspNetCore.Mvc;
using PlayHouse.TestServer.Shared;

namespace PlayHouse.TestServer.Api;

/// <summary>
/// HTTP API Controller for graceful shutdown requests.
/// </summary>
[ApiController]
[Route("api/shutdown")]
public class ShutdownController : ControllerBase
{
    private readonly ShutdownSignal _shutdownSignal;
    private readonly ILogger<ShutdownController> _logger;

    public ShutdownController(ShutdownSignal shutdownSignal, ILogger<ShutdownController> logger)
    {
        _shutdownSignal = shutdownSignal;
        _logger = logger;
    }

    [HttpPost]
    public IActionResult ShutdownAll()
        => SignalShutdown("all");

    [HttpPost("play")]
    public IActionResult ShutdownPlay()
        => SignalShutdown("play");

    [HttpPost("api")]
    public IActionResult ShutdownApi()
        => SignalShutdown("api");

    private IActionResult SignalShutdown(string target)
    {
        _logger.LogInformation("Shutdown requested: Target={Target}", target);
        var signaled = _shutdownSignal.TrySignal();
        return Ok(new { success = true, target, signaled });
    }
}
