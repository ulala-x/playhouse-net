#nullable enable

using System.Reflection;
using PlayHouse.Abstractions.Api;

namespace PlayHouse.Core.Api.Reflection;

/// <summary>
/// Implementation of IHandlerRegister for registering API message handlers.
/// Supports both delegate-based and method-name-based registration for per-request instantiation.
/// </summary>
internal sealed class HandlerRegister : IHandlerRegister
{
    private readonly Dictionary<string, HandlerDescriptor> _descriptors;
    private readonly Type _controllerType;

    /// <summary>
    /// Initializes a new instance of the <see cref="HandlerRegister"/> class.
    /// </summary>
    /// <param name="descriptors">The dictionary to register handler descriptors into.</param>
    /// <param name="controllerType">The controller type for method resolution.</param>
    public HandlerRegister(Dictionary<string, HandlerDescriptor> descriptors, Type controllerType)
    {
        _descriptors = descriptors;
        _controllerType = controllerType;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Extracts method info from the delegate for per-request controller instantiation.
    /// The delegate's target instance is NOT used at runtime - a new instance is created per request.
    /// </remarks>
    public void Add(string msgId, ApiHandler handler)
    {
        if (string.IsNullOrEmpty(msgId))
        {
            throw new ArgumentException("Message ID cannot be null or empty", nameof(msgId));
        }

        if (handler == null)
        {
            throw new ArgumentNullException(nameof(handler));
        }

        // Extract method info from delegate
        var method = handler.Method;
        var controllerType = handler.Target?.GetType() ?? _controllerType;

        // Validate that it's an instance method (not static)
        if (method.IsStatic)
        {
            throw new InvalidOperationException(
                $"Handler '{method.Name}' must be an instance method, not static.");
        }

        var descriptor = new HandlerDescriptor(controllerType, method);

        if (!_descriptors.TryAdd(msgId, descriptor))
        {
            throw new InvalidOperationException($"Handler already registered for message: {msgId}");
        }
    }

    /// <inheritdoc/>
    public void Add<TPacket>(ApiHandler handler) where TPacket : class
    {
        Add(typeof(TPacket).Name!, handler);
    }

    /// <inheritdoc/>
    public void Add(string msgId, string methodName)
    {
        if (string.IsNullOrEmpty(msgId))
        {
            throw new ArgumentException("Message ID cannot be null or empty", nameof(msgId));
        }

        if (string.IsNullOrEmpty(methodName))
        {
            throw new ArgumentException("Method name cannot be null or empty", nameof(methodName));
        }

        var method = _controllerType.GetMethod(
            methodName,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        if (method == null)
        {
            throw new InvalidOperationException(
                $"Method '{methodName}' not found on controller type '{_controllerType.Name}'.");
        }

        var descriptor = new HandlerDescriptor(_controllerType, method);

        if (!_descriptors.TryAdd(msgId, descriptor))
        {
            throw new InvalidOperationException($"Handler already registered for message: {msgId}");
        }
    }

    /// <inheritdoc/>
    public void Add<TPacket>(string methodName) where TPacket : class
    {
        Add(typeof(TPacket).Name!, methodName);
    }
}
