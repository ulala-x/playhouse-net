#nullable enable

namespace PlayHouse.Core.Protocols;

/// <summary>
/// Represents a message payload containing binary data.
/// Provides abstraction over different payload implementations (simple, pooled, compressed).
/// </summary>
public interface IPayload : IDisposable
{
    /// <summary>
    /// Gets the payload data as read-only memory.
    /// </summary>
    ReadOnlyMemory<byte> Data { get; }

    /// <summary>
    /// Gets the length of the payload in bytes.
    /// </summary>
    int Length { get; }
}
