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
    /// Gets an empty read-only span. Async block payloads don't carry byte data.
    /// </summary>
    public ReadOnlySpan<byte> DataSpan => ReadOnlySpan<byte>.Empty;

    public int Length => DataSpan.Length;

    /// <summary>
    /// Transfers ownership of the payload data to a new instance.
    /// For AsyncBlockPayload, this returns itself as it contains immutable references.
    /// </summary>
    public IPayload Move()
    {
        // AsyncBlockPayload는 콜백과 결과 참조만 담고 있으므로 자기 자신 반환
        return this;
    }

    /// <summary>
    /// Disposes the payload. This is a no-op for async block payloads.
    /// </summary>
    public void Dispose() { }
}
