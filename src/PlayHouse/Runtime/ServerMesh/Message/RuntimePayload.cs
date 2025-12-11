#nullable enable

using Google.Protobuf;

namespace PlayHouse.Runtime.ServerMesh.Message;

/// <summary>
/// Payload wrapper for server-to-server message data.
/// </summary>
public interface IRuntimePayload : IDisposable
{
    /// <summary>
    /// Gets the payload data as ReadOnlySpan.
    /// </summary>
    ReadOnlySpan<byte> DataSpan { get; }

    /// <summary>
    /// Gets the payload data as ReadOnlyMemory.
    /// </summary>
    ReadOnlyMemory<byte> Data { get; }

    /// <summary>
    /// Gets the length of the payload.
    /// </summary>
    int Length { get; }
}

/// <summary>
/// Payload backed by a byte array.
/// </summary>
public sealed class ByteArrayPayload : IRuntimePayload
{
    private readonly byte[] _data;
    private bool _disposed;

    public ByteArrayPayload(byte[] data)
    {
        _data = data;
    }

    public ByteArrayPayload(ReadOnlySpan<byte> data)
    {
        _data = data.ToArray();
    }

    public ReadOnlySpan<byte> DataSpan => _data;
    public ReadOnlyMemory<byte> Data => _data;
    public int Length => _data.Length;

    public void Dispose()
    {
        _disposed = true;
    }
}

/// <summary>
/// Payload backed by a ByteString (Protobuf).
/// </summary>
public sealed class ByteStringPayload : IRuntimePayload
{
    private readonly ByteString _data;
    private bool _disposed;

    public ByteStringPayload(ByteString data)
    {
        _data = data;
    }

    public ReadOnlySpan<byte> DataSpan => _data.Span;
    public ReadOnlyMemory<byte> Data => _data.Memory;
    public int Length => _data.Length;

    public void Dispose()
    {
        _disposed = true;
    }
}

/// <summary>
/// Empty payload singleton.
/// </summary>
public sealed class EmptyRuntimePayload : IRuntimePayload
{
    public static readonly EmptyRuntimePayload Instance = new();

    private EmptyRuntimePayload() { }

    public ReadOnlySpan<byte> DataSpan => ReadOnlySpan<byte>.Empty;
    public ReadOnlyMemory<byte> Data => ReadOnlyMemory<byte>.Empty;
    public int Length => 0;

    public void Dispose() { }
}

/// <summary>
/// Payload backed by NetMQ frame data (zero-copy when possible).
/// </summary>
public sealed class FramePayload : IRuntimePayload
{
    private readonly byte[] _data;
    private bool _disposed;

    public FramePayload(byte[] frameData)
    {
        _data = frameData;
    }

    public ReadOnlySpan<byte> DataSpan => _data;
    public ReadOnlyMemory<byte> Data => _data;
    public int Length => _data.Length;

    public void Dispose()
    {
        _disposed = true;
    }
}
