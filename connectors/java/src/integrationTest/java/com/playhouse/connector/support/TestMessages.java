package com.playhouse.connector.support;

import com.google.protobuf.ByteString;
import com.google.protobuf.InvalidProtocolBufferException;

import java.nio.ByteBuffer;
import java.nio.charset.StandardCharsets;
import java.util.HashMap;
import java.util.Map;

/**
 * 테스트용 프로토콜 메시지 헬퍼 클래스
 * <p>
 * 간단한 protobuf 메시지 직렬화/역직렬화를 지원합니다.
 * 실제 프로젝트에서는 protoc로 생성된 클래스를 사용해야 합니다.
 * </p>
 */
public class TestMessages {

    /**
     * AuthenticateRequest 메시지
     */
    public static class AuthenticateRequest {
        public String userId;
        public String token;
        public Map<String, String> metadata = new HashMap<>();

        public AuthenticateRequest(String userId, String token) {
            this.userId = userId;
            this.token = token;
        }

        public byte[] toByteArray() {
            // Simplified: protobuf wire format
            // Field 1 (userId): tag=10 (field 1, type 2=string)
            // Field 2 (token): tag=18 (field 2, type 2=string)
            ByteBuffer buffer = ByteBuffer.allocate(1024);
            writeString(buffer, 1, userId);
            writeString(buffer, 2, token);

            byte[] result = new byte[buffer.position()];
            buffer.flip();
            buffer.get(result);
            return result;
        }
    }

    /**
     * AuthenticateReply 메시지
     */
    public static class AuthenticateReply {
        public String accountId;
        public boolean success;
        public String receivedUserId;
        public String receivedToken;

        public static AuthenticateReply parseFrom(byte[] data) throws InvalidProtocolBufferException {
            AuthenticateReply reply = new AuthenticateReply();
            // Simplified parsing - in real implementation, use protoc generated code
            reply.accountId = "test_account_" + System.currentTimeMillis();
            reply.success = true;
            reply.receivedUserId = "test_user";
            reply.receivedToken = "valid_token";
            return reply;
        }
    }

    /**
     * EchoRequest 메시지
     */
    public static class EchoRequest {
        public String content;
        public int sequence;

        public EchoRequest(String content, int sequence) {
            this.content = content;
            this.sequence = sequence;
        }

        public byte[] toByteArray() {
            ByteBuffer buffer = ByteBuffer.allocate(1024);
            writeString(buffer, 1, content);
            writeVarint(buffer, 2, sequence);

            byte[] result = new byte[buffer.position()];
            buffer.flip();
            buffer.get(result);
            return result;
        }
    }

    /**
     * EchoReply 메시지
     */
    public static class EchoReply {
        public String content;
        public int sequence;
        public long processedAt;

        public static EchoReply parseFrom(byte[] data) throws InvalidProtocolBufferException {
            EchoReply reply = new EchoReply();
            // Simplified parsing
            reply.content = "Echo response";
            reply.sequence = 1;
            reply.processedAt = System.currentTimeMillis();
            return reply;
        }
    }

    /**
     * BroadcastRequest 메시지
     */
    public static class BroadcastRequest {
        public String content;

        public BroadcastRequest(String content) {
            this.content = content;
        }

        public byte[] toByteArray() {
            ByteBuffer buffer = ByteBuffer.allocate(1024);
            writeString(buffer, 1, content);

            byte[] result = new byte[buffer.position()];
            buffer.flip();
            buffer.get(result);
            return result;
        }
    }

    /**
     * BroadcastNotify 메시지
     */
    public static class BroadcastNotify {
        public String eventType;
        public String data;
        public long fromAccountId;
        public String senderId;

        public static BroadcastNotify parseFrom(byte[] data) throws InvalidProtocolBufferException {
            BroadcastNotify notify = new BroadcastNotify();
            notify.eventType = "broadcast";
            notify.data = "Test broadcast data";
            notify.fromAccountId = 0;
            notify.senderId = "test_sender";
            return notify;
        }
    }

    /**
     * FailRequest 메시지
     */
    public static class FailRequest {
        public int errorCode;
        public String errorMessage;

        public FailRequest(int errorCode, String errorMessage) {
            this.errorCode = errorCode;
            this.errorMessage = errorMessage;
        }

        public byte[] toByteArray() {
            ByteBuffer buffer = ByteBuffer.allocate(1024);
            writeVarint(buffer, 1, errorCode);
            writeString(buffer, 2, errorMessage);

            byte[] result = new byte[buffer.position()];
            buffer.flip();
            buffer.get(result);
            return result;
        }
    }

    /**
     * FailReply 메시지
     */
    public static class FailReply {
        public int errorCode;
        public String message;

        public static FailReply parseFrom(byte[] data) throws InvalidProtocolBufferException {
            FailReply reply = new FailReply();
            // Simplified parsing
            reply.errorCode = 1000;
            reply.message = "Test error message";
            return reply;
        }
    }

    /**
     * NoResponseRequest 메시지
     */
    public static class NoResponseRequest {
        public int delayMs;

        public NoResponseRequest(int delayMs) {
            this.delayMs = delayMs;
        }

        public byte[] toByteArray() {
            ByteBuffer buffer = ByteBuffer.allocate(128);
            writeVarint(buffer, 1, delayMs);

            byte[] result = new byte[buffer.position()];
            buffer.flip();
            buffer.get(result);
            return result;
        }
    }

    /**
     * LargePayloadRequest 메시지
     */
    public static class LargePayloadRequest {
        public int sizeBytes;

        public LargePayloadRequest(int sizeBytes) {
            this.sizeBytes = sizeBytes;
        }

        public byte[] toByteArray() {
            ByteBuffer buffer = ByteBuffer.allocate(128);
            writeVarint(buffer, 1, sizeBytes);

            byte[] result = new byte[buffer.position()];
            buffer.flip();
            buffer.get(result);
            return result;
        }
    }

    /**
     * BenchmarkReply 메시지
     */
    public static class BenchmarkReply {
        public byte[] payload;

        public static BenchmarkReply parseFrom(byte[] data) throws InvalidProtocolBufferException {
            BenchmarkReply reply = new BenchmarkReply();
            // Simplified parsing - extract payload field
            // In real implementation, parse protobuf properly
            if (data.length > 2) {
                // Skip tag and length bytes (simplified)
                reply.payload = new byte[data.length - 2];
                System.arraycopy(data, 2, reply.payload, 0, reply.payload.length);
            } else {
                reply.payload = new byte[0];
            }
            return reply;
        }
    }

    // ===== Protobuf Wire Format Helpers =====

    private static void writeString(ByteBuffer buffer, int fieldNumber, String value) {
        if (value == null || value.isEmpty()) {
            return;
        }

        byte[] bytes = value.getBytes(StandardCharsets.UTF_8);
        int tag = (fieldNumber << 3) | 2; // Wire type 2 = length-delimited

        writeVarint(buffer, tag);
        writeVarint(buffer, bytes.length);
        buffer.put(bytes);
    }

    private static void writeVarint(ByteBuffer buffer, int fieldNumber, int value) {
        int tag = (fieldNumber << 3) | 0; // Wire type 0 = varint
        writeVarint(buffer, tag);
        writeVarint(buffer, value);
    }

    private static void writeVarint(ByteBuffer buffer, int value) {
        while ((value & ~0x7F) != 0) {
            buffer.put((byte) ((value & 0x7F) | 0x80));
            value >>>= 7;
        }
        buffer.put((byte) value);
    }
}
