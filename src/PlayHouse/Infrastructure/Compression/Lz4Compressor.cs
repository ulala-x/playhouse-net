#nullable enable

using K4os.Compression.LZ4;

namespace PlayHouse.Infrastructure.Compression;

/// <summary>
/// LZ4 compression utilities for efficient payload compression.
/// Provides compression/decompression with configurable thresholds.
/// </summary>
public static class Lz4Compressor
{
    /// <summary>
    /// Minimum payload size to consider for compression. Default: 512 bytes.
    /// Payloads smaller than this are not worth compressing due to overhead.
    /// </summary>
    public const int CompressionThreshold = 512;

    /// <summary>
    /// Minimum compression ratio to use compressed data. Default: 0.9 (10% reduction).
    /// If compressed size is more than 90% of original, use uncompressed data.
    /// </summary>
    public const double MinCompressionRatio = 0.9;

    /// <summary>
    /// Default LZ4 compression level.
    /// </summary>
    public const LZ4Level DefaultCompressionLevel = LZ4Level.L00_FAST;

    /// <summary>
    /// Compresses data using LZ4 algorithm.
    /// </summary>
    /// <param name="data">The data to compress.</param>
    /// <param name="level">The compression level. Default: L00_FAST.</param>
    /// <returns>The compressed data.</returns>
    public static byte[] Compress(ReadOnlySpan<byte> data, LZ4Level level = DefaultCompressionLevel)
    {
        return LZ4Pickler.Pickle(data.ToArray(), level);
    }

    /// <summary>
    /// Compresses data using LZ4 algorithm if beneficial.
    /// Returns compressed data only if it achieves the minimum compression ratio.
    /// </summary>
    /// <param name="data">The data to compress.</param>
    /// <param name="level">The compression level. Default: L00_FAST.</param>
    /// <returns>
    /// A tuple containing:
    /// - compressed: The compressed data (or original if compression not beneficial)
    /// - wasCompressed: True if data was compressed; false otherwise
    /// </returns>
    public static (byte[] compressed, bool wasCompressed) TryCompress(
        ReadOnlySpan<byte> data,
        LZ4Level level = DefaultCompressionLevel)
    {
        if (!ShouldCompress(data.Length))
        {
            return (data.ToArray(), false);
        }

        var compressed = Compress(data, level);

        // Only use compressed if it's significantly smaller
        if (compressed.Length < data.Length * MinCompressionRatio)
        {
            return (compressed, true);
        }

        return (data.ToArray(), false);
    }

    /// <summary>
    /// Decompresses LZ4 compressed data.
    /// </summary>
    /// <param name="compressed">The compressed data.</param>
    /// <returns>The decompressed data.</returns>
    /// <exception cref="InvalidDataException">Thrown when decompression fails.</exception>
    public static byte[] Decompress(ReadOnlySpan<byte> compressed)
    {
        try
        {
            return LZ4Pickler.Unpickle(compressed.ToArray());
        }
        catch (Exception ex)
        {
            throw new InvalidDataException("Failed to decompress LZ4 data", ex);
        }
    }

    /// <summary>
    /// Decompresses LZ4 compressed data with known original size validation.
    /// </summary>
    /// <param name="compressed">The compressed data.</param>
    /// <param name="expectedOriginalSize">The expected size of the decompressed data.</param>
    /// <returns>The decompressed data.</returns>
    /// <exception cref="InvalidDataException">
    /// Thrown when decompression fails or size doesn't match.
    /// </exception>
    public static byte[] Decompress(ReadOnlySpan<byte> compressed, int expectedOriginalSize)
    {
        var decompressed = Decompress(compressed);

        if (decompressed.Length != expectedOriginalSize)
        {
            throw new InvalidDataException(
                $"Decompressed size mismatch: expected {expectedOriginalSize}, got {decompressed.Length}");
        }

        return decompressed;
    }

    /// <summary>
    /// Determines whether data of the specified length should be compressed.
    /// </summary>
    /// <param name="dataLength">The data length in bytes.</param>
    /// <returns>True if compression is recommended; otherwise, false.</returns>
    public static bool ShouldCompress(int dataLength)
    {
        return dataLength > CompressionThreshold;
    }

    /// <summary>
    /// Estimates the compressed size of data.
    /// This is a rough estimate based on typical compression ratios.
    /// </summary>
    /// <param name="originalSize">The original data size.</param>
    /// <param name="estimatedRatio">The estimated compression ratio (default: 0.5 for 50% compression).</param>
    /// <returns>The estimated compressed size in bytes.</returns>
    public static int EstimateCompressedSize(int originalSize, double estimatedRatio = 0.5)
    {
        return (int)(originalSize * estimatedRatio);
    }

    /// <summary>
    /// Compresses data into a pre-allocated destination buffer.
    /// </summary>
    /// <param name="source">The source data to compress.</param>
    /// <param name="destination">The destination buffer for compressed data.</param>
    /// <param name="level">The compression level.</param>
    /// <returns>The number of bytes written to the destination buffer.</returns>
    /// <exception cref="ArgumentException">
    /// Thrown when the destination buffer is too small.
    /// </exception>
    public static int CompressInto(
        ReadOnlySpan<byte> source,
        Span<byte> destination,
        LZ4Level level = DefaultCompressionLevel)
    {
        var maxCompressedSize = LZ4Codec.MaximumOutputSize(source.Length);
        if (destination.Length < maxCompressedSize)
        {
            throw new ArgumentException(
                $"Destination buffer too small: need at least {maxCompressedSize} bytes",
                nameof(destination));
        }

        return LZ4Codec.Encode(source, destination, level);
    }

    /// <summary>
    /// Decompresses data into a pre-allocated destination buffer.
    /// </summary>
    /// <param name="source">The compressed source data.</param>
    /// <param name="destination">The destination buffer for decompressed data.</param>
    /// <returns>The number of bytes written to the destination buffer.</returns>
    /// <exception cref="InvalidDataException">Thrown when decompression fails.</exception>
    public static int DecompressInto(ReadOnlySpan<byte> source, Span<byte> destination)
    {
        try
        {
            return LZ4Codec.Decode(source, destination);
        }
        catch (Exception ex)
        {
            throw new InvalidDataException("Failed to decompress LZ4 data", ex);
        }
    }
}
