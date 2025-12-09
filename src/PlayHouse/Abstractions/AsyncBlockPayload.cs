#nullable enable

namespace PlayHouse.Abstractions;

/// <summary>
/// Internal payload type used for async block operations.
/// </summary>
/// <remarks>
/// This payload carries the post-callback and result from an async block operation
/// initiated via <see cref="IStageSender.AsyncBlock"/>. It is used internally by
/// the routing system to execute the post-callback in the stage's context.
/// </remarks>
internal sealed class AsyncBlockPayload : IPayload
{
    /// <summary>
    /// Gets the post-callback to execute in the stage context.
    /// </summary>
    public Func<object?, Task> PostCallback { get; }

    /// <summary>
    /// Gets the result from the async operation.
    /// </summary>
    public object? Result { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="AsyncBlockPayload"/> class.
    /// </summary>
    /// <param name="postCallback">The post-callback to execute.</param>
    /// <param name="result">The result from the async operation.</param>
    public AsyncBlockPayload(Func<object?, Task> postCallback, object? result)
    {
        PostCallback = postCallback;
        Result = result;
    }

    /// <summary>
    /// Gets an empty read-only memory segment. Async block payloads don't carry byte data.
    /// </summary>
    public ReadOnlyMemory<byte> Data => ReadOnlyMemory<byte>.Empty;

    /// <summary>
    /// Gets the length of the payload, which is always 0 for async block payloads.
    /// </summary>
    public int Length => 0;

    /// <summary>
    /// Disposes the payload. This is a no-op for async block payloads.
    /// </summary>
    public void Dispose() { }
}
