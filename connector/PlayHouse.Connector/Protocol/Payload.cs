#nullable enable

using Google.Protobuf;

namespace PlayHouse.Connector.Protocol;

/// <summary>
/// 빈 페이로드
/// </summary>
public sealed class EmptyPayload : IPayload
{
    public static readonly EmptyPayload Instance = new();

    private EmptyPayload() { }

    public ReadOnlyMemory<byte> Data => ReadOnlyMemory<byte>.Empty;

    public void Dispose()
    {
        // Nothing to dispose
    }
}

/// <summary>
/// 바이트 배열 페이로드
/// </summary>
public sealed class BytePayload : IPayload
{
    private readonly byte[] _data;
    private bool _disposed;

    public BytePayload(byte[] data)
    {
        _data = data ?? throw new ArgumentNullException(nameof(data));
    }

    public BytePayload(ReadOnlySpan<byte> data)
    {
        _data = data.ToArray();
    }

    public ReadOnlyMemory<byte> Data => _data;

    public void Dispose()
    {
        _disposed = true;
    }
}

/// <summary>
/// Protobuf 메시지 페이로드
/// </summary>
public sealed class ProtoPayload : IPayload
{
    private readonly IMessage _proto;
    private byte[]? _cachedData;
    private bool _disposed;

    public ProtoPayload(IMessage proto)
    {
        _proto = proto ?? throw new ArgumentNullException(nameof(proto));
    }

    public ReadOnlyMemory<byte> Data
    {
        get
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(ProtoPayload));
            }

            _cachedData ??= _proto.ToByteArray();
            return _cachedData;
        }
    }

    public void Dispose()
    {
        _disposed = true;
        _cachedData = null;
    }
}
