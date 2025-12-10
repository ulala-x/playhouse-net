#nullable enable

namespace PlayHouse.Connector.Protocol;

/// <summary>
/// 페이로드 인터페이스 - 직렬화된 데이터
/// </summary>
public interface IPayload : IDisposable
{
    /// <summary>
    /// 페이로드 데이터
    /// </summary>
    ReadOnlyMemory<byte> Data { get; }

    /// <summary>
    /// 페이로드 데이터 Span
    /// </summary>
    ReadOnlySpan<byte> DataSpan => Data.Span;
}
