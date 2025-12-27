#nullable enable

using System.Buffers;
using Google.Protobuf;
using Net.Zmq;

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
    /// Gets the payload data as a read-only span.
    /// </summary>
    ReadOnlySpan<byte> DataSpan { get; }

    /// <summary>
    /// Gets the length of the payload.
    /// </summary>
    int Length { get; }
}


public interface IMessagePayload : IPayload
{
    /// <summary>
    /// Gets the pre-created ZMQ Message for zero-copy send.
    /// </summary>
    Message Message { get; }

    /// <summary>
    /// Creates the ZMQ Message from MessagePool and serializes data into it.
    /// Should be called before Send() to avoid copying on send thread.
    /// </summary>
    void MakeMessage();
}

/// <summary>
/// Payload backed by ZMQ Message (zero-copy, owns message lifetime).
/// </summary>
public sealed class ZmqPayload(Message message) : IPayload
{
    private bool _disposed;

    /// <summary>
    /// Gets the underlying ZMQ Message.
    /// </summary>
    private Message Message { get; } = message;

    public ReadOnlySpan<byte> DataSpan => Message.Data;
    public int Length => DataSpan.Length;
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Message.Dispose();
    }
}

/// <summary>
/// Payload backed by ReadOnlyMemory (zero-copy, no allocation).
/// </summary>
// public sealed class MemoryPayload(ReadOnlyMemory<byte> data) : IMessagePayload
// {
//     public ReadOnlySpan<byte> DataSpan => data.Span;
//     public int Length => data.Length;
//     
//     private Message? _message;
//     public Message Message => _message!;
//     public void MakeMessage()
//     {
//         _message ??= MessagePool.Shared.Rent(data.Span);
//     }
//     public void Dispose()
//     {
//         _message?.Dispose();
//     }
// }
public sealed class MemoryPayload(ReadOnlyMemory<byte> data) : IPayload
{
    public ReadOnlySpan<byte> DataSpan => data.Span;
    public int Length => data.Length;
    public void Dispose()
    {
    }
}

/// <summary>
/// Payload implementation backed by a Protobuf message.
/// - S2C path: Uses ArrayPool via DataSpan (lazy serialization)
/// - S2S path: Uses MessagePool via MakeMessage() (direct serialization)
/// </summary>
public sealed class ProtoPayload : IMessagePayload
{
    private readonly IMessage _proto;
    private byte[]? _rentedBuffer;
    private int _actualSize = -1;
    private Message? _message;
    
    public ProtoPayload(IMessage proto)
    {
        _proto = proto;
    }

   /// <summary>
    /// S2C path: Lazy serialization to ArrayPool buffer.
    /// </summary>
    public ReadOnlySpan<byte> DataSpan
    {
        get
        {
            if (_rentedBuffer == null)
            {
                _rentedBuffer = ArrayPool<byte>.Shared.Rent(Length);
                _proto.WriteTo(_rentedBuffer.AsSpan(0, Length));
            }
            return _rentedBuffer.AsSpan(0, Length);
        }
    }

    public int Length
    {
        get
        {
            if (_actualSize < 0)
            {
                 _actualSize = _proto.CalculateSize();
            }
            return _actualSize;
        }
    }

    /// <summary>
    /// Gets the underlying Protobuf message.
    /// </summary>
    public IMessage GetProto() => _proto;

    public Message Message => _message!;

    /// <summary>
    /// S2S path: Direct serialization to MessagePool (no DataSpan copy).
    /// </summary>
    public void MakeMessage()
    {
        if (_message == null)
        {
            _message = MessagePool.Shared.Rent(DataSpan);
            //_message = MessagePool.Shared.Rent(Length);
            //_proto.WriteTo(_message.Data);
            //_message.SetActualDataSize(Length);
            //_proto.WriteTo(_message.Data.Slice(0, Length));
        }
    }

    public void Dispose()
    {
        if (_rentedBuffer != null)
        {
            ArrayPool<byte>.Shared.Return(_rentedBuffer);
            _rentedBuffer = null;
        }

        _message?.Dispose();
    }
}

/// <summary>
/// Singleton empty payload.
/// </summary>
public sealed class EmptyPayload : IPayload
{
    public static readonly EmptyPayload Instance = new();

    private EmptyPayload() { }

    public ReadOnlySpan<byte> DataSpan => ReadOnlySpan<byte>.Empty;
    public int Length => DataSpan.Length;
    public void Dispose() { }
}
