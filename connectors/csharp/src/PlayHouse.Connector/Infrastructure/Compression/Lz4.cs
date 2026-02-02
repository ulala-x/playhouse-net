using System;
using K4os.Compression.LZ4;

namespace PlayHouse.Connector.Compression;

public class Lz4
{
    private const int MaxBufferSize = 1024 * 1024 * 2; // 2MB
    private byte[] _compressBuffer = new byte[1024 * 10];
    private byte[] _depressBuffer = new byte[1024 * 10];

    private void EnsureBufferSize(ref byte[] buffer, int requiredSize)
    {
        if (requiredSize > MaxBufferSize)
        {
            throw new ArgumentException($"Required buffer size ({requiredSize} bytes) exceeds the maximum allowed size ({MaxBufferSize} bytes).");
        }

        if (buffer.Length < requiredSize)
        {
            int newSize = Math.Min(requiredSize * 2, MaxBufferSize);
            buffer = new byte[newSize];
        }
    }

    public ReadOnlySpan<byte> Compress(ReadOnlySpan<byte> input)
    {
        int maxCompressedSize = LZ4Codec.MaximumOutputSize(input.Length);

        EnsureBufferSize(ref _compressBuffer, maxCompressedSize);

        int compressedSize = LZ4Codec.Encode(
            input,
            _compressBuffer.AsSpan(0, maxCompressedSize)
        );

        return _compressBuffer.AsSpan(0, compressedSize);
    }

    public ReadOnlySpan<byte> Decompress(ReadOnlySpan<byte> compressed, int originalSize)
    {
        EnsureBufferSize(ref _depressBuffer, originalSize);

        int decodedSize = LZ4Codec.Decode(
            compressed,
            _depressBuffer
        );

        if (decodedSize != originalSize)
        {
            throw new InvalidOperationException("Decompressed size does not match original size.");
        }

        return _depressBuffer.AsSpan(0, originalSize);
    }
}
