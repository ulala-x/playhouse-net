#nullable enable

using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using PlayHouse.Abstractions;

namespace PlayHouse.Core.Messaging;

/// <summary>
/// Processes messages within stage context using registered handlers.
/// </summary>
/// <remarks>
/// MessageHandler provides a pattern for routing messages to specific handlers
/// based on MsgId. This allows clean separation of message handling logic.
/// </remarks>
internal sealed class MessageHandler
{
    private readonly ConcurrentDictionary<string, Func<IPacket, Task>> _handlers = new();
    private readonly ILogger<MessageHandler> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="MessageHandler"/> class.
    /// </summary>
    /// <param name="logger">The logger instance.</param>
    public MessageHandler(ILogger<MessageHandler> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Registers a message handler for a specific message ID.
    /// </summary>
    /// <param name="msgId">The message identifier to handle.</param>
    /// <param name="handler">The handler function.</param>
    /// <returns>True if the handler was registered; false if a handler already exists for this MsgId.</returns>
    public bool RegisterHandler(string msgId, Func<IPacket, Task> handler)
    {
        if (_handlers.TryAdd(msgId, handler))
        {
            _logger.LogDebug("Registered handler for message ID: {MsgId}", msgId);
            return true;
        }

        _logger.LogWarning("Handler already registered for message ID: {MsgId}", msgId);
        return false;
    }

    /// <summary>
    /// Registers a synchronous message handler for a specific message ID.
    /// </summary>
    /// <param name="msgId">The message identifier to handle.</param>
    /// <param name="handler">The synchronous handler function.</param>
    /// <returns>True if the handler was registered; false if a handler already exists for this MsgId.</returns>
    public bool RegisterHandler(string msgId, Action<IPacket> handler)
    {
        return RegisterHandler(msgId, packet =>
        {
            handler(packet);
            return Task.CompletedTask;
        });
    }

    /// <summary>
    /// Unregisters a message handler.
    /// </summary>
    /// <param name="msgId">The message identifier.</param>
    /// <returns>True if the handler was removed; otherwise, false.</returns>
    public bool UnregisterHandler(string msgId)
    {
        if (_handlers.TryRemove(msgId, out _))
        {
            _logger.LogDebug("Unregistered handler for message ID: {MsgId}", msgId);
            return true;
        }

        _logger.LogWarning("No handler found to unregister for message ID: {MsgId}", msgId);
        return false;
    }

    /// <summary>
    /// Handles a message packet by routing it to the registered handler.
    /// </summary>
    /// <param name="packet">The packet to handle.</param>
    /// <returns>True if a handler was found and executed; otherwise, false.</returns>
    public async Task<bool> HandleAsync(IPacket packet)
    {
        if (_handlers.TryGetValue(packet.MsgId, out var handler))
        {
            try
            {
                _logger.LogTrace("Handling message: {MsgId}", packet.MsgId);
                await handler(packet);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error handling message {MsgId}", packet.MsgId);
                throw;
            }
        }

        _logger.LogWarning("No handler registered for message ID: {MsgId}", packet.MsgId);
        return false;
    }

    /// <summary>
    /// Checks if a handler is registered for the specified message ID.
    /// </summary>
    /// <param name="msgId">The message identifier.</param>
    /// <returns>True if a handler exists; otherwise, false.</returns>
    public bool HasHandler(string msgId)
    {
        return _handlers.ContainsKey(msgId);
    }

    /// <summary>
    /// Gets the number of registered handlers.
    /// </summary>
    public int HandlerCount => _handlers.Count;

    /// <summary>
    /// Clears all registered handlers.
    /// </summary>
    public void ClearHandlers()
    {
        var count = _handlers.Count;
        _handlers.Clear();
        _logger.LogInformation("Cleared {Count} message handlers", count);
    }
}

/// <summary>
/// Generic message handler that provides type-safe payload handling.
/// </summary>
/// <typeparam name="TPayload">The payload type.</typeparam>
internal sealed class MessageHandler<TPayload> where TPayload : IPayload
{
    private readonly ConcurrentDictionary<string, Func<TPayload, Task>> _handlers = new();
    private readonly ILogger _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="MessageHandler{TPayload}"/> class.
    /// </summary>
    /// <param name="logger">The logger instance.</param>
    public MessageHandler(ILogger logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Registers a typed message handler for a specific message ID.
    /// </summary>
    /// <param name="msgId">The message identifier to handle.</param>
    /// <param name="handler">The handler function.</param>
    /// <returns>True if the handler was registered; false if a handler already exists for this MsgId.</returns>
    public bool RegisterHandler(string msgId, Func<TPayload, Task> handler)
    {
        if (_handlers.TryAdd(msgId, handler))
        {
            _logger.LogDebug("Registered typed handler for message ID: {MsgId}", msgId);
            return true;
        }

        _logger.LogWarning("Typed handler already registered for message ID: {MsgId}", msgId);
        return false;
    }

    /// <summary>
    /// Handles a message packet with type-safe payload casting.
    /// </summary>
    /// <param name="packet">The packet to handle.</param>
    /// <returns>True if a handler was found and executed; otherwise, false.</returns>
    public async Task<bool> HandleAsync(IPacket packet)
    {
        if (!_handlers.TryGetValue(packet.MsgId, out var handler))
        {
            _logger.LogWarning("No typed handler registered for message ID: {MsgId}", packet.MsgId);
            return false;
        }

        if (packet.Payload is not TPayload typedPayload)
        {
            _logger.LogError("Payload type mismatch for message {MsgId}: expected {Expected}, got {Actual}",
                packet.MsgId, typeof(TPayload).Name, packet.Payload?.GetType().Name ?? "null");
            return false;
        }

        try
        {
            _logger.LogTrace("Handling typed message: {MsgId}", packet.MsgId);
            await handler(typedPayload);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling typed message {MsgId}", packet.MsgId);
            throw;
        }
    }
}
