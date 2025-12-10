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
/// ApiReflection discovers and registers handlers from IApiController and IApiBackendController
/// implementations through the service provider. It provides methods to invoke the appropriate
/// handler for incoming packets.
/// </remarks>
internal sealed class ApiReflection
{
    private readonly Dictionary<string, ApiHandler> _handlers = new();
    private readonly Dictionary<string, ApiHandler> _backendHandlers = new();
    private readonly ILogger<ApiReflection>? _logger;

    /// <summary>
    /// Gets the number of registered frontend handlers.
    /// </summary>
    public int HandlerCount => _handlers.Count;

    /// <summary>
    /// Gets the number of registered backend handlers.
    /// </summary>
    public int BackendHandlerCount => _backendHandlers.Count;

    /// <summary>
    /// Initializes a new instance of the <see cref="ApiReflection"/> class.
    /// </summary>
    /// <param name="serviceProvider">The service provider for resolving controllers.</param>
    /// <param name="logger">The logger instance (optional).</param>
    public ApiReflection(IServiceProvider serviceProvider, ILogger<ApiReflection>? logger = null)
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
                _logger?.LogDebug("Registered API controller: {ControllerType}",
                    controller.GetType().Name);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to register API controller: {ControllerType}",
                    controller.GetType().Name);
                throw;
            }
        }

        // Register handlers from IApiBackendController implementations
        var backendControllers = serviceProvider.GetServices<IApiBackendController>();
        foreach (var controller in backendControllers)
        {
            try
            {
                var register = new HandlerRegister(_backendHandlers);
                controller.Handles(register);
                _logger?.LogDebug("Registered API backend controller: {ControllerType}",
                    controller.GetType().Name);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to register API backend controller: {ControllerType}",
                    controller.GetType().Name);
                throw;
            }
        }

        _logger?.LogInformation("ApiReflection initialized with {HandlerCount} handlers and {BackendHandlerCount} backend handlers",
            _handlers.Count, _backendHandlers.Count);
    }

    /// <summary>
    /// Invokes the handler for a client request.
    /// </summary>
    /// <param name="packet">The incoming packet.</param>
    /// <param name="apiSender">The sender context.</param>
    /// <exception cref="ServiceException.NotRegisterMethod">Thrown when no handler is registered for the message ID.</exception>
    public async Task CallMethodAsync(IPacket packet, IApiSender apiSender)
    {
        var msgId = packet.MsgId;

        if (_handlers.TryGetValue(msgId, out var handler))
        {
            _logger?.LogDebug("Invoking handler for message: {MsgId}", msgId);
            await handler(packet, apiSender);
        }
        else
        {
            _logger?.LogWarning("No handler registered for message: {MsgId}", msgId);
            throw new ServiceException.NotRegisterMethod($"Not registered handler: {msgId}");
        }
    }

    /// <summary>
    /// Invokes the handler for a backend (server-to-server) request.
    /// </summary>
    /// <param name="packet">The incoming packet.</param>
    /// <param name="apiSender">The sender context.</param>
    /// <exception cref="ServiceException.NotRegisterMethod">Thrown when no handler is registered for the message ID.</exception>
    public async Task CallBackendMethodAsync(IPacket packet, IApiSender apiSender)
    {
        var msgId = packet.MsgId;

        if (_backendHandlers.TryGetValue(msgId, out var handler))
        {
            _logger?.LogDebug("Invoking backend handler for message: {MsgId}", msgId);
            await handler(packet, apiSender);
        }
        else
        {
            _logger?.LogWarning("No backend handler registered for message: {MsgId}", msgId);
            throw new ServiceException.NotRegisterMethod($"Not registered backend handler: {msgId}");
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
    /// Checks if a backend handler is registered for the given message ID.
    /// </summary>
    /// <param name="msgId">The message ID to check.</param>
    /// <returns>True if a backend handler is registered; otherwise, false.</returns>
    public bool HasBackendHandler(string msgId)
    {
        return _backendHandlers.ContainsKey(msgId);
    }

    /// <summary>
    /// Gets all registered message IDs.
    /// </summary>
    /// <returns>A collection of registered message IDs.</returns>
    public IReadOnlyCollection<string> GetRegisteredMessageIds()
    {
        return _handlers.Keys;
    }

    /// <summary>
    /// Gets all registered backend message IDs.
    /// </summary>
    /// <returns>A collection of registered backend message IDs.</returns>
    public IReadOnlyCollection<string> GetRegisteredBackendMessageIds()
    {
        return _backendHandlers.Keys;
    }
}
