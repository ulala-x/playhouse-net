#nullable enable

using System;
using Google.Protobuf;

namespace PlayHouse.Connector.Protocol;

/// <summary>
/// 빈 페이로드
/// </summary>
public sealed class EmptyPayload : IPayload
{
    public static readonly EmptyPayload Instance = new();

    private EmptyPayload() { }

    public ReadOnlySpan<byte> DataSpan => ReadOnlySpan<byte>.Empty;

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

    public BytePayload(byte[] data)
    {
        _data = data ?? throw new ArgumentNullException(nameof(data));
    }

    public BytePayload(ReadOnlySpan<byte> data)
    {
        _data = data.ToArray();
    }

    public ReadOnlySpan<byte> DataSpan => _data;

    public void Dispose()
    {
        // Nothing to dispose for byte arrays
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

    public ReadOnlySpan<byte> DataSpan
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
