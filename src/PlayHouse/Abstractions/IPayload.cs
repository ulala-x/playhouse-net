#nullable enable

namespace PlayHouse.Abstractions;

/// <summary>
/// Represents the payload data of a packet.
/// </summary>
/// <remarks>
/// Implementations must be thread-safe for read operations and properly handle disposal
/// to prevent memory leaks. The payload data is immutable once created.
/// </remarks>
public interface IPayload : IDisposable
{
    /// <summary>
    /// Gets the payload data as a read-only memory segment.
    /// </summary>
    ReadOnlyMemory<byte> Data { get; }

    /// <summary>
    /// Gets the payload data as a read-only span.
    /// </summary>
    ReadOnlySpan<byte> DataSpan => Data.Span;
}
