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
/// implementations through the service provider. It creates a new controller
/// instance per request for proper Scoped dependency injection support.
/// </remarks>
internal sealed class ApiReflection
{
    private readonly Dictionary<string, HandlerDescriptor> _handlers = new();
    private readonly IServiceProvider _serviceProvider;
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
        _serviceProvider = serviceProvider;
        _logger = logger;

        // Register handlers from IApiController implementations
        // Note: Controllers are instantiated here only for handler registration,
        // NOT for handling requests. New instances are created per request.
        var controllers = serviceProvider.GetServices<IApiController>();
        foreach (var controller in controllers)
        {
            try
            {
                var controllerType = controller.GetType();
                var register = new HandlerRegister(_handlers, controllerType);
                controller.Handles(register);
                _logger.LogDebug("Registered API controller: {ControllerType}",
                    controllerType.Name);
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
    /// Invokes the handler for a request with per-request controller instantiation.
    /// </summary>
    /// <param name="packet">The incoming packet.</param>
    /// <param name="apiLink">The sender context.</param>
    /// <exception cref="PlayException">Thrown when no handler is registered for the message ID.</exception>
    /// <remarks>
    /// Creates a new IServiceScope and controller instance for each request,
    /// enabling proper Scoped dependency injection.
    /// </remarks>
    public async Task CallMethodAsync(IPacket packet, IApiLink apiLink)
    {
        var msgId = packet.MsgId;

        if (_handlers.TryGetValue(msgId, out var descriptor))
        {
            _logger.LogDebug("Invoking handler for message: {MsgId}", msgId);

            // Create scope for Scoped dependencies
            using var scope = _serviceProvider.CreateScope();

            // Create new controller instance within the scope
            var controller = ActivatorUtilities.CreateInstance(
                scope.ServiceProvider,
                descriptor.ControllerType);

            try
            {
                await descriptor.CompiledHandler(controller, packet, apiLink);
            }
            finally
            {
                // Dispose controller if it implements IDisposable/IAsyncDisposable
                if (controller is IAsyncDisposable asyncDisposable)
                {
                    await asyncDisposable.DisposeAsync();
                }
                else if (controller is IDisposable disposable)
                {
                    disposable.Dispose();
                }
            }
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
