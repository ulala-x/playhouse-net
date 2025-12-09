#nullable enable

using Google.Protobuf;
using PlayHouse.Abstractions;

namespace PlayHouse.Tests.E2E.TestFixtures;

/// <summary>
/// Protobuf 메시지를 IPacket으로 래핑하는 구현체.
/// </summary>
/// <remarks>
/// MsgId는 Protobuf Descriptor.Name을 사용하여 자동 생성됩니다.
/// 예: ChatMessage → "ChatMessage", PlayerJoinedNotify → "PlayerJoinedNotify"
/// </remarks>
public class SimplePacket : IPacket
{
    private IMessage? _parsedMessage;

    /// <summary>
    /// Protobuf 메시지로 패킷 생성 (송신용)
    /// </summary>
    public SimplePacket(IMessage message, int stageId = 0)
    {
        MsgId = message.Descriptor.Name;  // "ChatMessage", "CreateStageRequest" 등
        Payload = new SimpleProtoPayload(message);
        _parsedMessage = message;
        MsgSeq = 0;  // Push 메시지
        StageId = stageId;
        ErrorCode = 0;
    }

    /// <summary>
    /// 수신된 데이터로 패킷 재구성 (역직렬화용)
    /// </summary>
    public SimplePacket(string msgId, IPayload payload, ushort msgSeq, int stageId = 0, ushort errorCode = 0)
    {
        MsgId = msgId;
        Payload = new CopyPayload(payload);
        MsgSeq = msgSeq;
        StageId = stageId;
        ErrorCode = errorCode;
    }

    public string MsgId { get; }
    public IPayload Payload { get; }
    public ushort MsgSeq { get; }
    public int StageId { get; }
    public ushort ErrorCode { get; }

    /// <summary>Request 여부 (MsgSeq > 0이면 응답 필요)</summary>
    public bool IsRequest => MsgSeq > 0;

    /// <summary>
    /// 타입 안전 파싱 - Protobuf 메시지로 역직렬화
    /// </summary>
    public T Parse<T>() where T : IMessage, new()
    {
        if (_parsedMessage == null)
        {
            var message = new T();
            _parsedMessage = message.Descriptor.Parser.ParseFrom(Payload.Data.Span);
        }
        return (T)_parsedMessage;
    }

    public void Dispose()
    {
        Payload?.Dispose();
    }
}

/// <summary>
/// Protobuf 메시지를 직렬화하여 Payload로 제공
/// </summary>
public sealed class SimpleProtoPayload : IPayload
{
    private readonly byte[] _data;

    public SimpleProtoPayload(IMessage message)
    {
        _data = message.ToByteArray();
    }

    public ReadOnlyMemory<byte> Data => _data;
    public int Length => _data.Length;

    public void Dispose() { }
}

/// <summary>
/// 기존 Payload를 복사하여 보관 (수신 시 사용)
/// </summary>
public sealed class CopyPayload : IPayload
{
    private readonly byte[] _data;

    public CopyPayload(IPayload source)
    {
        _data = source.Data.ToArray();
    }

    public CopyPayload(byte[] data)
    {
        _data = data;
    }

    public ReadOnlyMemory<byte> Data => _data;
    public int Length => _data.Length;

    public void Dispose() { }
}

/// <summary>
/// IPacket에서 Protobuf 메시지를 파싱하는 확장 메서드
/// </summary>
public static class SimplePacketExtension
{
    public static T Parse<T>(this IPacket packet) where T : IMessage, new()
    {
        if (packet is SimplePacket simplePacket)
        {
            return simplePacket.Parse<T>();
        }

        // 일반 IPacket인 경우 직접 파싱
        var message = new T();
        return (T)message.Descriptor.Parser.ParseFrom(packet.Payload.Data.Span);
    }
}
