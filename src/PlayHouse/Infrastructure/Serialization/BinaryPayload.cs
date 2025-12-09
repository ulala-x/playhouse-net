#nullable enable

using PlayHouse.Abstractions;

namespace PlayHouse.Infrastructure.Serialization;

/// <summary>
/// Simple byte array-based payload implementation.
/// Suitable for small payloads where copying overhead is acceptable.
/// </summary>
public sealed class BinaryPayload : IPayload
{
    private readonly byte[] _data;

    /// <summary>
    /// Initializes a new instance with the specified byte array.
    /// </summary>
    /// <param name="data">The byte array to wrap. If null, an empty array is used.</param>
    public BinaryPayload(byte[] data)
    {
        _data = data ?? Array.Empty<byte>();
    }

    /// <summary>
    /// Initializes a new instance by copying data from a span.
    /// </summary>
    /// <param name="data">The data to copy.</param>
    public BinaryPayload(ReadOnlySpan<byte> data)
    {
        _data = data.ToArray();
    }

    /// <inheritdoc />
    public ReadOnlyMemory<byte> Data => _data;

    /// <inheritdoc />
    public int Length => _data.Length;

    /// <inheritdoc />
    public void Dispose()
    {
        // No resources to dispose
    }
}
