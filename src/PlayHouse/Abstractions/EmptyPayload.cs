#nullable enable

namespace PlayHouse.Abstractions;

/// <summary>
/// Represents an empty payload with no data.
/// </summary>
/// <remarks>
/// This is a singleton implementation used for packets that don't carry payload data,
/// such as simple acknowledgments or control messages. Using a singleton reduces
/// allocations for these common cases.
/// </remarks>
public sealed class EmptyPayload : IPayload
{
    /// <summary>
    /// Gets the singleton instance of the empty payload.
    /// </summary>
    public static readonly EmptyPayload Instance = new();

    private EmptyPayload() { }

    /// <summary>
    /// Gets an empty read-only memory segment.
    /// </summary>
    public ReadOnlyMemory<byte> Data => ReadOnlyMemory<byte>.Empty;

    /// <summary>
    /// Gets the length of the payload, which is always 0.
    /// </summary>
    public int Length => 0;

    /// <summary>
    /// Disposes the payload. This is a no-op for the empty payload.
    /// </summary>
    public void Dispose() { }
}
