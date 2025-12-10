#nullable enable

namespace PlayHouse.Core.Api;

/// <summary>
/// Container class for service-related exceptions.
/// </summary>
public static class ServiceException
{
    /// <summary>
    /// Exception thrown when a message handler is not registered.
    /// </summary>
    public sealed class NotRegisterMethod : Exception
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="NotRegisterMethod"/> class.
        /// </summary>
        /// <param name="message">The error message.</param>
        public NotRegisterMethod(string message) : base(message)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="NotRegisterMethod"/> class.
        /// </summary>
        /// <param name="message">The error message.</param>
        /// <param name="innerException">The inner exception.</param>
        public NotRegisterMethod(string message, Exception innerException) : base(message, innerException)
        {
        }
    }

    /// <summary>
    /// Exception thrown when a controller instance is not registered in the service provider.
    /// </summary>
    public sealed class NotRegisterInstance : Exception
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="NotRegisterInstance"/> class.
        /// </summary>
        /// <param name="message">The error message.</param>
        public NotRegisterInstance(string message) : base(message)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="NotRegisterInstance"/> class.
        /// </summary>
        /// <param name="message">The error message.</param>
        /// <param name="innerException">The inner exception.</param>
        public NotRegisterInstance(string message, Exception innerException) : base(message, innerException)
        {
        }
    }

    /// <summary>
    /// Exception thrown when handler invocation fails.
    /// </summary>
    public sealed class HandlerInvocationFailed : Exception
    {
        /// <summary>
        /// Gets the message ID that failed to invoke.
        /// </summary>
        public string MsgId { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="HandlerInvocationFailed"/> class.
        /// </summary>
        /// <param name="msgId">The message ID.</param>
        /// <param name="message">The error message.</param>
        public HandlerInvocationFailed(string msgId, string message) : base(message)
        {
            MsgId = msgId;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="HandlerInvocationFailed"/> class.
        /// </summary>
        /// <param name="msgId">The message ID.</param>
        /// <param name="message">The error message.</param>
        /// <param name="innerException">The inner exception.</param>
        public HandlerInvocationFailed(string msgId, string message, Exception innerException) : base(message, innerException)
        {
            MsgId = msgId;
        }
    }
}
