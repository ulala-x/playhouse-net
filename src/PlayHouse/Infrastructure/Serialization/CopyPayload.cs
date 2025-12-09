#nullable enable

using PlayHouse.Abstractions;

namespace PlayHouse.Infrastructure.Serialization;

/// <summary>
/// Payload implementation that creates a defensive copy of the data.
/// Ensures the payload owns its data and is independent of the source.
/// </summary>
public sealed class CopyPayload : IPayload
{
    private readonly byte[] _data;

    /// <summary>
    /// Initializes a new instance by copying data from another payload.
    /// </summary>
    /// <param name="source">The source payload to copy from.</param>
    public CopyPayload(IPayload source)
    {
        _data = source.Data.ToArray();
    }

    /// <summary>
    /// Initializes a new instance by copying a byte array.
    /// </summary>
    /// <param name="data">The byte array to copy.</param>
    public CopyPayload(byte[] data)
    {
        _data = data;
    }

    /// <summary>
    /// Initializes a new instance by copying from ReadOnlyMemory.
    /// </summary>
    /// <param name="data">The data to copy.</param>
    public CopyPayload(ReadOnlyMemory<byte> data)
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
