package com.playhouse.connector.internal;

import com.playhouse.connector.Packet;
import org.slf4j.Logger;
import org.slf4j.LoggerFactory;

import java.nio.ByteBuffer;
import java.nio.ByteOrder;
import java.nio.charset.StandardCharsets;

/**
 * 패킷 인코딩/디코딩
 * <p>
 * Request Packet: ContentSize(4) + MsgIdLen(1) + MsgId(N) + MsgSeq(2) + StageId(8) + Payload(...)
 * Response Packet: ContentSize(4) + MsgIdLen(1) + MsgId(N) + MsgSeq(2) + StageId(8) + ErrorCode(2) + OriginalSize(4) + Payload(...)
 * <p>
 * Byte Order: Little-endian
 */
public final class PacketCodec {

    private static final Logger logger = LoggerFactory.getLogger(PacketCodec.class);

    /**
     * Maximum allowed packet size (16MB) to prevent memory DoS attacks.
     * This should be configurable if larger packets are needed.
     */
    public static final int MAX_PACKET_SIZE = 16 * 1024 * 1024;

    /**
     * Minimum header size: ContentSize(4) + MsgIdLen(1) + MsgId(1) + MsgSeq(2) + StageId(8)
     */
    private static final int MIN_HEADER_SIZE = 16;

    private PacketCodec() {
        // Utility class
    }

    /**
     * 요청 패킷 인코딩
     *
     * @param packet  패킷
     * @param msgSeq  메시지 시퀀스
     * @param stageId Stage ID
     * @return 인코딩된 ByteBuffer
     */
    public static ByteBuffer encodeRequest(Packet packet, short msgSeq, long stageId) {
        byte[] msgIdBytes = packet.getMsgId().getBytes(StandardCharsets.UTF_8);
        byte[] payload = packet.getPayload();

        // ContentSize = MsgIdLen(1) + MsgId(N) + MsgSeq(2) + StageId(8) + Payload(...)
        int contentSize = 1 + msgIdBytes.length + 2 + 8 + payload.length;
        int totalSize = 4 + contentSize;

        ByteBuffer buffer = ByteBuffer.allocate(totalSize).order(ByteOrder.LITTLE_ENDIAN);

        // Write
        buffer.putInt(contentSize);                      // ContentSize (4)
        buffer.put((byte) msgIdBytes.length);            // MsgIdLen (1)
        buffer.put(msgIdBytes);                          // MsgId (N)
        buffer.putShort(msgSeq);                         // MsgSeq (2)
        buffer.putLong(stageId);                         // StageId (8)
        buffer.put(payload);                             // Payload (...)

        buffer.flip();

        if (logger.isDebugEnabled()) {
            logger.debug("Encoded request packet: msgId={}, msgSeq={}, stageId={}, payloadSize={}",
                packet.getMsgId(), msgSeq, stageId, payload.length);
        }

        return buffer;
    }

    /**
     * 응답 패킷 디코딩
     *
     * @param buffer 수신된 데이터 버퍼
     * @return 디코딩된 패킷
     */
    public static Packet decodeResponse(ByteBuffer buffer) {
        buffer.order(ByteOrder.LITTLE_ENDIAN);

        // Read ContentSize
        int contentSize = buffer.getInt();

        // Read MsgIdLen
        int msgIdLen = buffer.get() & 0xFF;

        // Read MsgId
        byte[] msgIdBytes = new byte[msgIdLen];
        buffer.get(msgIdBytes);
        String msgId = new String(msgIdBytes, StandardCharsets.UTF_8);

        // Read MsgSeq
        short msgSeq = buffer.getShort();

        // Read StageId
        long stageId = buffer.getLong();

        // Read ErrorCode
        short errorCode = buffer.getShort();

        // Read OriginalSize
        int originalSize = buffer.getInt();

        // Read Payload
        int payloadSize = buffer.remaining();
        byte[] payload = new byte[payloadSize];
        buffer.get(payload);

        if (logger.isDebugEnabled()) {
            logger.debug("Decoded response packet: msgId={}, msgSeq={}, stageId={}, errorCode={}, originalSize={}, payloadSize={}",
                msgId, msgSeq, stageId, errorCode, originalSize, payloadSize);
        }

        return Packet.builder(msgId)
            .msgSeq(msgSeq)
            .stageId(stageId)
            .errorCode(errorCode)
            .originalSize(originalSize)
            .payload(payload)
            .build();
    }

    /**
     * 다음 패킷 크기 읽기 (ContentSize만 읽음)
     *
     * @param buffer 수신된 데이터 버퍼
     * @return 패킷 크기 (헤더 4바이트 포함), 데이터 부족 시 -1, 유효하지 않은 크기 시 -2
     */
    public static int peekPacketSize(ByteBuffer buffer) {
        if (buffer.remaining() < 4) {
            return -1;
        }

        int position = buffer.position();
        buffer.order(ByteOrder.LITTLE_ENDIAN);
        int contentSize = buffer.getInt(position);

        // Validate contentSize to prevent memory DoS and buffer issues
        if (contentSize < MIN_HEADER_SIZE - 4) {
            // contentSize should be at least minimum header size minus ContentSize(4) field
            logger.error("Invalid packet: contentSize {} is too small (min: {})", contentSize, MIN_HEADER_SIZE - 4);
            return -2;
        }

        if (contentSize > MAX_PACKET_SIZE) {
            logger.error("Invalid packet: contentSize {} exceeds maximum allowed size ({})", contentSize, MAX_PACKET_SIZE);
            return -2;
        }

        // Total size = ContentSize (header) + ContentSize (body)
        return 4 + contentSize;
    }
}
