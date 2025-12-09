#nullable enable

using System.Buffers;
using PlayHouse.Abstractions;

namespace PlayHouse.Infrastructure.Serialization;

/// <summary>
/// ArrayPool-based payload for high-performance scenarios.
/// Reduces GC pressure by renting buffers from the shared array pool.
/// Must be disposed to return the buffer to the pool.
/// </summary>
public sealed class PooledPayload : IPayload
{
    private readonly byte[] _rentedArray;
    private readonly int _length;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance by renting a buffer of the specified length.
    /// </summary>
    /// <param name="length">The required buffer length.</param>
    public PooledPayload(int length)
    {
        _rentedArray = ArrayPool<byte>.Shared.Rent(length);
        _length = length;
    }

    /// <summary>
    /// Initializes a new instance by renting a buffer and copying data.
    /// </summary>
    /// <param name="data">The data to copy into the rented buffer.</param>
    public PooledPayload(ReadOnlySpan<byte> data)
    {
        _length = data.Length;
        _rentedArray = ArrayPool<byte>.Shared.Rent(_length);
        data.CopyTo(_rentedArray);
    }

    /// <inheritdoc />
    public ReadOnlyMemory<byte> Data => new(_rentedArray, 0, _length);

    /// <summary>
    /// Gets a writable span over the payload data.
    /// Use with caution - modifications will affect the payload.
    /// </summary>
    /// <returns>A span over the valid portion of the rented buffer.</returns>
    public Span<byte> GetSpan() => new(_rentedArray, 0, _length);

    /// <inheritdoc />
    public int Length => _length;

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        ArrayPool<byte>.Shared.Return(_rentedArray);
    }
}
