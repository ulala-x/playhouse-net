package com.playhouse.connector;

import java.nio.ByteBuffer;
import java.nio.ByteOrder;
import java.nio.charset.StandardCharsets;
import java.util.Arrays;

/**
 * PlayHouse 프로토콜 패킷
 * <p>
 * Request Packet 구조:
 * ContentSize(4) + MsgIdLen(1) + MsgId(N) + MsgSeq(2) + StageId(8) + Payload(...)
 * <p>
 * Response Packet 구조:
 * ContentSize(4) + MsgIdLen(1) + MsgId(N) + MsgSeq(2) + StageId(8) + ErrorCode(2) + OriginalSize(4) + Payload(...)
 */
public final class Packet implements AutoCloseable {

    private final String msgId;
    private final short msgSeq;
    private final long stageId;
    private final short errorCode;
    private final int originalSize;
    private final byte[] payload;

    /**
     * 패킷 생성자
     *
     * @param msgId        메시지 ID
     * @param msgSeq       메시지 시퀀스 (0 = Push, >0 = Request-Response)
     * @param stageId      Stage ID
     * @param errorCode    에러 코드 (응답 패킷에서만 사용)
     * @param originalSize 원본 크기 (압축된 경우)
     * @param payload      페이로드 데이터
     */
    private Packet(String msgId, short msgSeq, long stageId, short errorCode, int originalSize, byte[] payload) {
        this.msgId = msgId;
        this.msgSeq = msgSeq;
        this.stageId = stageId;
        this.errorCode = errorCode;
        this.originalSize = originalSize;
        this.payload = payload;
    }

    /**
     * 빈 패킷 생성
     *
     * @param msgId 메시지 ID
     * @return 빈 페이로드를 가진 패킷
     */
    public static Packet empty(String msgId) {
        return new Packet(msgId, (short) 0, 0L, (short) 0, 0, new byte[0]);
    }

    /**
     * 바이트 배열로부터 패킷 생성
     *
     * @param msgId 메시지 ID
     * @param bytes 페이로드 바이트 배열
     * @return 패킷 인스턴스
     */
    public static Packet fromBytes(String msgId, byte[] bytes) {
        if (bytes == null) {
            bytes = new byte[0];
        }
        return new Packet(msgId, (short) 0, 0L, (short) 0, 0, bytes);
    }

    /**
     * 패킷 빌더 생성
     *
     * @param msgId 메시지 ID
     * @return 패킷 빌더
     */
    public static Builder builder(String msgId) {
        return new Builder(msgId);
    }

    // Getters
    public String getMsgId() {
        return msgId;
    }

    public short getMsgSeq() {
        return msgSeq;
    }

    public long getStageId() {
        return stageId;
    }

    public short getErrorCode() {
        return errorCode;
    }

    public int getOriginalSize() {
        return originalSize;
    }

    public byte[] getPayload() {
        // 방어적 복사: 외부에서 배열을 수정할 수 없도록 복사본 반환
        return payload.clone();
    }

    /**
     * 페이로드를 ByteBuffer로 반환
     *
     * @return Little-endian ByteBuffer
     */
    public ByteBuffer getPayloadBuffer() {
        return ByteBuffer.wrap(payload).order(ByteOrder.LITTLE_ENDIAN);
    }

    /**
     * 에러가 있는지 확인
     *
     * @return 에러 코드가 0이 아니면 true
     */
    public boolean hasError() {
        return errorCode != 0;
    }

    /**
     * 압축되었는지 확인
     *
     * @return 원본 크기가 0보다 크면 true
     */
    public boolean isCompressed() {
        return originalSize > 0;
    }

    @Override
    public void close() {
        // 필요시 리소스 정리
    }

    @Override
    public String toString() {
        return String.format(
            "Packet{msgId='%s', msgSeq=%d, stageId=%d, errorCode=%d, originalSize=%d, payloadSize=%d}",
            msgId, msgSeq, stageId, errorCode, originalSize, payload.length
        );
    }

    /**
     * Packet Builder
     */
    public static final class Builder {
        private final String msgId;
        private short msgSeq = 0;
        private long stageId = 0L;
        private short errorCode = 0;
        private int originalSize = 0;
        private byte[] payload = new byte[0];

        private Builder(String msgId) {
            this.msgId = msgId;
        }

        public Builder msgSeq(short msgSeq) {
            this.msgSeq = msgSeq;
            return this;
        }

        public Builder stageId(long stageId) {
            this.stageId = stageId;
            return this;
        }

        public Builder errorCode(short errorCode) {
            this.errorCode = errorCode;
            return this;
        }

        public Builder originalSize(int originalSize) {
            this.originalSize = originalSize;
            return this;
        }

        public Builder payload(byte[] payload) {
            this.payload = payload != null ? payload : new byte[0];
            return this;
        }

        public Packet build() {
            return new Packet(msgId, msgSeq, stageId, errorCode, originalSize, payload);
        }
    }
}
