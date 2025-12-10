#nullable enable

using PlayHouse.Abstractions.Api;

namespace PlayHouse.Core.Api.Reflection;

/// <summary>
/// Implementation of IHandlerRegister for registering API message handlers.
/// </summary>
internal sealed class HandlerRegister : IHandlerRegister
{
    private readonly Dictionary<string, ApiHandler> _handlers;

    /// <summary>
    /// Initializes a new instance of the <see cref="HandlerRegister"/> class.
    /// </summary>
    /// <param name="handlers">The dictionary to register handlers into.</param>
    public HandlerRegister(Dictionary<string, ApiHandler> handlers)
    {
        _handlers = handlers;
    }

    /// <inheritdoc/>
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

        if (!_handlers.TryAdd(msgId, handler))
        {
            throw new InvalidOperationException($"Handler already registered for message: {msgId}");
        }
    }

    /// <inheritdoc/>
    public void Add<TPacket>(ApiHandler handler) where TPacket : class
    {
        Add(typeof(TPacket).Name, handler);
    }
}
