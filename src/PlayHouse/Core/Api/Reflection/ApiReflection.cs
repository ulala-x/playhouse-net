#nullable enable

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PlayHouse.Abstractions;
using PlayHouse.Abstractions.Api;

namespace PlayHouse.Core.Api.Reflection;

/// <summary>
/// Manages API handler registration and invocation via reflection.
/// </summary>
/// <remarks>
/// ApiReflection discovers and registers handlers from IApiController
/// implementations through the service provider. It provides methods to invoke the appropriate
/// handler for incoming packets.
/// </remarks>
internal sealed class ApiReflection
{
    private readonly Dictionary<string, ApiHandler> _handlers = new();
    private readonly ILogger<ApiReflection> _logger;

    /// <summary>
    /// Gets the number of registered handlers.
    /// </summary>
    public int HandlerCount => _handlers.Count;

    /// <summary>
    /// Initializes a new instance of the <see cref="ApiReflection"/> class.
    /// </summary>
    /// <param name="serviceProvider">The service provider for resolving controllers.</param>
    /// <param name="logger">The logger instance.</param>
    public ApiReflection(IServiceProvider serviceProvider, ILogger<ApiReflection> logger)
    {
        _logger = logger;

        // Register handlers from IApiController implementations
        var controllers = serviceProvider.GetServices<IApiController>();
        foreach (var controller in controllers)
        {
            try
            {
                var register = new HandlerRegister(_handlers);
                controller.Handles(register);
                _logger.LogDebug("Registered API controller: {ControllerType}",
                    controller.GetType().Name);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to register API controller: {ControllerType}",
                    controller.GetType().Name);
                throw;
            }
        }

        _logger.LogInformation("ApiReflection initialized with {HandlerCount} handlers",
            _handlers.Count);
    }

    /// <summary>
    /// Invokes the handler for a request.
    /// </summary>
    /// <param name="packet">The incoming packet.</param>
    /// <param name="apiSender">The sender context.</param>
    /// <exception cref="PlayException">Thrown when no handler is registered for the message ID.</exception>
    public async Task CallMethodAsync(IPacket packet, IApiSender apiSender)
    {
        var msgId = packet.MsgId;

        if (_handlers.TryGetValue(msgId, out var handler))
        {
            _logger.LogDebug("Invoking handler for message: {MsgId}", msgId);
            await handler(packet, apiSender);
        }
        else
        {
            _logger.LogWarning("No handler registered for message: {MsgId}", msgId);
            throw new PlayException(ErrorCode.HandlerNotFound);
        }
    }

    /// <summary>
    /// Checks if a handler is registered for the given message ID.
    /// </summary>
    /// <param name="msgId">The message ID to check.</param>
    /// <returns>True if a handler is registered; otherwise, false.</returns>
    public bool HasHandler(string msgId)
    {
        return _handlers.ContainsKey(msgId);
    }

    /// <summary>
    /// Gets all registered message IDs.
    /// </summary>
    /// <returns>A collection of registered message IDs.</returns>
    public IReadOnlyCollection<string> GetRegisteredMessageIds()
    {
        return _handlers.Keys;
    }
}
