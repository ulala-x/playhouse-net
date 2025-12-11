#nullable enable

using System.Buffers.Binary;
using System.Text;

namespace PlayHouse.Core.Messaging;

/// <summary>
/// 클라이언트 패킷 인코더.
/// </summary>
/// <remarks>
/// 서버에서 클라이언트로 전송하는 패킷을 인코딩합니다.
/// </remarks>
public static class PacketEncoder
{
    /// <summary>
    /// 응답 패킷을 인코딩합니다.
    /// </summary>
    /// <param name="msgId">메시지 ID.</param>
    /// <param name="msgSeq">메시지 시퀀스.</param>
    /// <param name="stageId">스테이지 ID.</param>
    /// <param name="errorCode">에러 코드.</param>
    /// <param name="payload">페이로드.</param>
    /// <returns>인코딩된 바이트 배열.</returns>
    public static byte[] EncodeResponse(
        string msgId,
        ushort msgSeq,
        long stageId,
        ushort errorCode,
        ReadOnlySpan<byte> payload)
    {
        var msgIdBytes = Encoding.UTF8.GetBytes(msgId);

        // Server Response Format:
        // Length(4) + MsgIdLen(1) + MsgId(N) + MsgSeq(2) + StageId(8) + ErrorCode(2) + OriginalSize(4) + Payload
        var contentSize = 1 + msgIdBytes.Length + 2 + 8 + 2 + 4 + payload.Length;
        var buffer = new byte[4 + contentSize];

        int offset = 0;

        // Length (4 bytes)
        BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(offset), contentSize);
        offset += 4;

        // MsgIdLen (1 byte)
        buffer[offset++] = (byte)msgIdBytes.Length;

        // MsgId
        msgIdBytes.CopyTo(buffer.AsSpan(offset));
        offset += msgIdBytes.Length;

        // MsgSeq (2 bytes)
        BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(offset), msgSeq);
        offset += 2;

        // StageId (8 bytes)
        BinaryPrimitives.WriteInt64LittleEndian(buffer.AsSpan(offset), stageId);
        offset += 8;

        // ErrorCode (2 bytes)
        BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(offset), errorCode);
        offset += 2;

        // OriginalSize (4 bytes) - 0 = no compression
        BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(offset), 0);
        offset += 4;

        // Payload
        payload.CopyTo(buffer.AsSpan(offset));

        return buffer;
    }

    /// <summary>
    /// 성공 응답 패킷을 인코딩합니다.
    /// </summary>
    public static byte[] EncodeSuccess(
        string msgId,
        ushort msgSeq,
        long stageId,
        ReadOnlySpan<byte> payload)
    {
        return EncodeResponse(msgId, msgSeq, stageId, 0, payload);
    }

    /// <summary>
    /// 에러 응답 패킷을 인코딩합니다.
    /// </summary>
    public static byte[] EncodeError(
        string msgId,
        ushort msgSeq,
        long stageId,
        ushort errorCode)
    {
        return EncodeResponse(msgId, msgSeq, stageId, errorCode, ReadOnlySpan<byte>.Empty);
    }

    /// <summary>
    /// 푸시 패킷을 인코딩합니다 (시퀀스 번호 0).
    /// </summary>
    public static byte[] EncodePush(
        string msgId,
        long stageId,
        ReadOnlySpan<byte> payload)
    {
        return EncodeResponse(msgId, 0, stageId, 0, payload);
    }
}
