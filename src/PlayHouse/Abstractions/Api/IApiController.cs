#nullable enable

namespace PlayHouse.Abstractions.Api;

/// <summary>
/// Delegate for API handler methods.
/// </summary>
/// <param name="packet">The incoming request packet.</param>
/// <param name="link">The sender context for replying and sending messages.</param>
/// <returns>A task representing the asynchronous operation.</returns>
public delegate Task ApiHandler(IPacket packet, IApiLink link);

/// <summary>
/// Interface for registering API handlers.
/// </summary>
/// <remarks>
/// Used to register message handlers during controller initialization.
/// Each handler is associated with a message ID and will be called when
/// a message with that ID is received.
///
/// Recommended: Use method name based registration (Add with methodName parameter)
/// for proper per-request controller instantiation with Scoped DI support.
/// </remarks>
public interface IHandlerRegister
{
    /// <summary>
    /// Registers a handler for a specific message ID.
    /// </summary>
    /// <param name="msgId">The message ID to handle.</param>
    /// <param name="handler">The handler function.</param>
    /// <remarks>
    /// Note: This method captures the controller instance. For per-request instantiation,
    /// use the methodName overload instead.
    /// </remarks>
    void Add(string msgId, ApiHandler handler);

    /// <summary>
    /// Registers a handler using a packet type as the message ID.
    /// </summary>
    /// <typeparam name="TPacket">The packet type (uses type name as msgId).</typeparam>
    /// <param name="handler">The handler function.</param>
    /// <remarks>
    /// Note: This method captures the controller instance. For per-request instantiation,
    /// use the methodName overload instead.
    /// </remarks>
    void Add<TPacket>(ApiHandler handler) where TPacket : class;

    /// <summary>
    /// Registers a handler by method name for per-request controller instantiation.
    /// </summary>
    /// <param name="msgId">The message ID to handle.</param>
    /// <param name="methodName">The name of the handler method in the controller.</param>
    /// <remarks>
    /// Recommended: This method enables proper per-request controller instantiation
    /// with Scoped dependency injection support.
    /// Example: register.Add("LoginReq", nameof(HandleLogin));
    /// </remarks>
    void Add(string msgId, string methodName);

    /// <summary>
    /// Registers a handler by method name using a packet type as the message ID.
    /// </summary>
    /// <typeparam name="TPacket">The packet type (uses type name as msgId).</typeparam>
    /// <param name="methodName">The name of the handler method in the controller.</param>
    /// <remarks>
    /// Recommended: This method enables proper per-request controller instantiation
    /// with Scoped dependency injection support.
    /// Example: register.Add&lt;LoginReq&gt;(nameof(HandleLogin));
    /// </remarks>
    void Add<TPacket>(string methodName) where TPacket : class;
}

/// <summary>
/// Base interface for API controllers.
/// </summary>
/// <remarks>
/// Controllers register their message handlers through the Handles method.
/// This is called during server initialization to build the handler registry.
///
/// Example usage:
/// <code>
/// public class MyController : IApiController
/// {
///     public void Handles(IHandlerRegister register)
///     {
///         register.Add("LoginReq", HandleLogin);
///         register.Add&lt;CreateRoomReq&gt;(HandleCreateRoom);
///     }
///
///     private async Task HandleLogin(IPacket packet, IApiLink link)
///     {
///         var req = packet.Parse&lt;LoginReq&gt;();
///         // ... handle login logic
///         link.Reply(new LoginRes { Success = true });
///     }
/// }
/// </code>
/// </remarks>
public interface IApiController
{
    /// <summary>
    /// Registers all handlers for this controller.
    /// </summary>
    /// <param name="register">The handler register to add handlers to.</param>
    void Handles(IHandlerRegister register);
}

