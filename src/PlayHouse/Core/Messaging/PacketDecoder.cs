#nullable enable

using System.Buffers.Binary;
using System.Text;

namespace PlayHouse.Core.Messaging;

/// <summary>
/// 클라이언트 패킷 디코더.
/// </summary>
/// <remarks>
/// 클라이언트에서 서버로 전송되는 패킷을 디코딩합니다.
/// </remarks>
public static class PacketDecoder
{
    /// <summary>
    /// 패킷 디코딩 결과.
    /// </summary>
    public readonly struct DecodeResult
    {
        /// <summary>
        /// 디코딩 성공 여부.
        /// </summary>
        public bool Success { get; }

        /// <summary>
        /// 메시지 ID.
        /// </summary>
        public string MsgId { get; }

        /// <summary>
        /// 메시지 시퀀스.
        /// </summary>
        public ushort MsgSeq { get; }

        /// <summary>
        /// 스테이지 ID.
        /// </summary>
        public long StageId { get; }

        /// <summary>
        /// 페이로드.
        /// </summary>
        public byte[] Payload { get; }

        /// <summary>
        /// 에러 메시지 (실패 시).
        /// </summary>
        public string? Error { get; }

        /// <summary>
        /// 성공 결과를 생성합니다.
        /// </summary>
        public static DecodeResult Ok(string msgId, ushort msgSeq, long stageId, byte[] payload)
        {
            return new DecodeResult(true, msgId, msgSeq, stageId, payload, null);
        }

        /// <summary>
        /// 실패 결과를 생성합니다.
        /// </summary>
        public static DecodeResult Fail(string error)
        {
            return new DecodeResult(false, string.Empty, 0, 0, Array.Empty<byte>(), error);
        }

        private DecodeResult(bool success, string msgId, ushort msgSeq, long stageId, byte[] payload, string? error)
        {
            Success = success;
            MsgId = msgId;
            MsgSeq = msgSeq;
            StageId = stageId;
            Payload = payload;
            Error = error;
        }
    }

    /// <summary>
    /// 클라이언트 요청 패킷을 디코딩합니다.
    /// </summary>
    /// <param name="data">패킷 데이터 (길이 필드 제외).</param>
    /// <returns>디코딩 결과.</returns>
    public static DecodeResult DecodeRequest(ReadOnlySpan<byte> data)
    {
        try
        {
            if (data.Length < 11) // 최소 크기: MsgIdLen(1) + MsgSeq(2) + StageId(8)
            {
                return DecodeResult.Fail("Packet too small");
            }

            int offset = 0;

            // MsgIdLen (1 byte)
            var msgIdLen = data[offset++];
            if (data.Length < offset + msgIdLen + 10)
            {
                return DecodeResult.Fail("Invalid MsgId length");
            }

            // MsgId
            var msgId = Encoding.UTF8.GetString(data.Slice(offset, msgIdLen));
            offset += msgIdLen;

            // MsgSeq (2 bytes)
            var msgSeq = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(offset));
            offset += 2;

            // StageId (8 bytes)
            var stageId = BinaryPrimitives.ReadInt64LittleEndian(data.Slice(offset));
            offset += 8;

            // Payload
            var payload = data.Slice(offset).ToArray();

            return DecodeResult.Ok(msgId, msgSeq, stageId, payload);
        }
        catch (Exception ex)
        {
            return DecodeResult.Fail($"Decode error: {ex.Message}");
        }
    }

    /// <summary>
    /// 서버 응답 패킷을 디코딩합니다.
    /// </summary>
    public readonly struct ResponseDecodeResult
    {
        public bool Success { get; }
        public string MsgId { get; }
        public ushort MsgSeq { get; }
        public long StageId { get; }
        public ushort ErrorCode { get; }
        public int OriginalSize { get; }
        public byte[] Payload { get; }
        public string? Error { get; }

        public static ResponseDecodeResult Ok(
            string msgId, ushort msgSeq, long stageId,
            ushort errorCode, int originalSize, byte[] payload)
        {
            return new ResponseDecodeResult(true, msgId, msgSeq, stageId, errorCode, originalSize, payload, null);
        }

        public static ResponseDecodeResult Fail(string error)
        {
            return new ResponseDecodeResult(false, string.Empty, 0, 0, 0, 0, Array.Empty<byte>(), error);
        }

        private ResponseDecodeResult(
            bool success, string msgId, ushort msgSeq, long stageId,
            ushort errorCode, int originalSize, byte[] payload, string? error)
        {
            Success = success;
            MsgId = msgId;
            MsgSeq = msgSeq;
            StageId = stageId;
            ErrorCode = errorCode;
            OriginalSize = originalSize;
            Payload = payload;
            Error = error;
        }
    }

    /// <summary>
    /// 서버 응답 패킷을 디코딩합니다.
    /// </summary>
    /// <param name="data">패킷 데이터 (길이 필드 제외).</param>
    /// <returns>디코딩 결과.</returns>
    public static ResponseDecodeResult DecodeResponse(ReadOnlySpan<byte> data)
    {
        try
        {
            if (data.Length < 17) // 최소 크기
            {
                return ResponseDecodeResult.Fail("Response packet too small");
            }

            int offset = 0;

            // MsgIdLen (1 byte)
            var msgIdLen = data[offset++];
            if (data.Length < offset + msgIdLen + 16)
            {
                return ResponseDecodeResult.Fail("Invalid MsgId length");
            }

            // MsgId
            var msgId = Encoding.UTF8.GetString(data.Slice(offset, msgIdLen));
            offset += msgIdLen;

            // MsgSeq (2 bytes)
            var msgSeq = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(offset));
            offset += 2;

            // StageId (8 bytes)
            var stageId = BinaryPrimitives.ReadInt64LittleEndian(data.Slice(offset));
            offset += 8;

            // ErrorCode (2 bytes)
            var errorCode = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(offset));
            offset += 2;

            // OriginalSize (4 bytes)
            var originalSize = BinaryPrimitives.ReadInt32LittleEndian(data.Slice(offset));
            offset += 4;

            // Payload
            var payload = data.Slice(offset).ToArray();

            return ResponseDecodeResult.Ok(msgId, msgSeq, stageId, errorCode, originalSize, payload);
        }
        catch (Exception ex)
        {
            return ResponseDecodeResult.Fail($"Response decode error: {ex.Message}");
        }
    }
}
