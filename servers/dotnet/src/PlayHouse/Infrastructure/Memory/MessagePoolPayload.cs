#nullable enable

using Microsoft.Extensions.ObjectPool;
using PlayHouse.Abstractions;

namespace PlayHouse.Infrastructure.Memory;

/// <summary>
/// MessagePool에서 대여한 버퍼를 관리하는 페이로드 구현체.
/// ObjectPool을 통해 객체 자체를 재사용하여 힙 할당을 최소화합니다.
/// </summary>
internal sealed class MessagePoolPayload : IPayload
{
    private static readonly ObjectPool<MessagePoolPayload> _pool = 
        new DefaultObjectPool<MessagePoolPayload>(new DefaultPooledObjectPolicy<MessagePoolPayload>());

    private byte[]? _buffer;
    private int _length;

    public ReadOnlySpan<byte> DataSpan => _buffer != null ? _buffer.AsSpan(0, _length) : ReadOnlySpan<byte>.Empty;
    public ReadOnlyMemory<byte> DataMemory => _buffer != null ? _buffer.AsMemory(0, _length) : ReadOnlyMemory<byte>.Empty;
    public int Length => _length;

    // 풀링을 위해 기본 생성자 노출
    public MessagePoolPayload() { }

    /// <summary>
    /// 풀에서 페이로드 객체를 가져와 초기화합니다.
    /// </summary>
    public static MessagePoolPayload Create(byte[] buffer, int length)
    {
        var payload = _pool.Get();
        payload._buffer = buffer;
        payload._length = length;
        return payload;
    }

    public IPayload Move()
    {
        if (_buffer == null) return EmptyPayload.Instance;

        // 새로운 그릇을 풀에서 꺼내고 알맹이(buffer)의 소유권을 넘김
        var moved = Create(_buffer, _length);
        _buffer = null; 
        return moved;
    }

    public void Dispose()
    {
        var buf = _buffer;
        if (buf != null)
        {
            _buffer = null;
            // 1. 알맹이(byte[])를 전역 MessagePool로 반환
            MessagePool.Return(buf);
            
            // 2. 그릇(객체 자신)을 객체 풀로 반환
            _pool.Return(this);
        }
    }
}