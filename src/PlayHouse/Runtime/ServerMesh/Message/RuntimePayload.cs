#nullable enable

using Google.Protobuf;

namespace PlayHouse.Runtime.ServerMesh.Message;

/// <summary>
/// Payload wrapper for server-to-server message data.
/// </summary>
public interface IPayload : IDisposable
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
public sealed class ByteArrayPayload : IPayload
{
    private readonly byte[] _data;

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
        // Nothing to dispose for byte arrays
    }
}

/// <summary>
/// Payload backed by a ByteString (Protobuf).
/// </summary>
public sealed class ByteStringPayload : IPayload
{
    private readonly ByteString _data;

    public ByteStringPayload(ByteString data)
    {
        _data = data;
    }

    public ReadOnlySpan<byte> DataSpan => _data.Span;
    public ReadOnlyMemory<byte> Data => _data.Memory;
    public int Length => _data.Length;

    public void Dispose()
    {
        // Nothing to dispose for ByteString
    }
}

/// <summary>
/// Empty payload singleton.
/// </summary>
public sealed class EmptyPayload : IPayload
{
    public static readonly EmptyPayload Instance = new();

    private EmptyPayload() { }

    public ReadOnlySpan<byte> DataSpan => ReadOnlySpan<byte>.Empty;
    public ReadOnlyMemory<byte> Data => ReadOnlyMemory<byte>.Empty;
    public int Length => 0;

    public void Dispose() { }
}

/// <summary>
/// Payload backed by ZMQ frame data (zero-copy when possible).
/// </summary>
public sealed class FramePayload : IPayload
{
    private readonly byte[] _data;

    public FramePayload(byte[] frameData)
    {
        _data = frameData;
    }

    public ReadOnlySpan<byte> DataSpan => _data;
    public ReadOnlyMemory<byte> Data => _data;
    public int Length => _data.Length;

    public void Dispose()
    {
        // Nothing to dispose for frame data
    }
}

/// <summary>
/// Payload backed by ReadOnlyMemory (zero-copy, no allocation).
/// </summary>
public sealed class MemoryPayload : IPayload
{
    private readonly ReadOnlyMemory<byte> _data;

    public MemoryPayload(ReadOnlyMemory<byte> data)
    {
        _data = data;  // No copy, just reference
    }

    public ReadOnlySpan<byte> DataSpan => _data.Span;
    public ReadOnlyMemory<byte> Data => _data;
    public int Length => _data.Length;

    public void Dispose() { }
}

/// <summary>
/// Payload backed by ZMQ Message (zero-copy, owns message lifetime).
/// </summary>
public sealed class ZmqMessagePayload(Net.Zmq.Message message) : IPayload
{
    private bool _disposed;

    public ReadOnlySpan<byte> DataSpan => message.Data;
    public ReadOnlyMemory<byte> Data => message.Data.ToArray();
    public int Length => message.Size;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        message.Dispose();
    }
}
