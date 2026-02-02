package com.playhouse.connector;

import org.junit.jupiter.api.Test;

import java.nio.ByteBuffer;

import static org.assertj.core.api.Assertions.*;

/**
 * Packet 단위 테스트
 */
class PacketTest {

    @Test
    void testEmptyPacket() {
        // When
        Packet packet = Packet.empty("TestMessage");

        // Then
        assertThat(packet.getMsgId()).isEqualTo("TestMessage");
        assertThat(packet.getMsgSeq()).isEqualTo((short) 0);
        assertThat(packet.getStageId()).isEqualTo(0L);
        assertThat(packet.getErrorCode()).isEqualTo((short) 0);
        assertThat(packet.getOriginalSize()).isEqualTo(0);
        assertThat(packet.getPayload()).isEmpty();
        assertThat(packet.hasError()).isFalse();
        assertThat(packet.isCompressed()).isFalse();
    }

    @Test
    void testFromBytes() {
        // Given
        byte[] payload = {1, 2, 3, 4, 5};

        // When
        Packet packet = Packet.fromBytes("TestMessage", payload);

        // Then
        assertThat(packet.getMsgId()).isEqualTo("TestMessage");
        assertThat(packet.getPayload()).isEqualTo(payload);
    }

    @Test
    void testFromBytesWithNull() {
        // When
        Packet packet = Packet.fromBytes("TestMessage", null);

        // Then
        assertThat(packet.getPayload()).isEmpty();
    }

    @Test
    void testBuilder() {
        // Given
        byte[] payload = {10, 20, 30};

        // When
        Packet packet = Packet.builder("TestMessage")
            .msgSeq((short) 123)
            .stageId(456L)
            .errorCode((short) 789)
            .originalSize(100)
            .payload(payload)
            .build();

        // Then
        assertThat(packet.getMsgId()).isEqualTo("TestMessage");
        assertThat(packet.getMsgSeq()).isEqualTo((short) 123);
        assertThat(packet.getStageId()).isEqualTo(456L);
        assertThat(packet.getErrorCode()).isEqualTo((short) 789);
        assertThat(packet.getOriginalSize()).isEqualTo(100);
        assertThat(packet.getPayload()).isEqualTo(payload);
        assertThat(packet.hasError()).isTrue();
        assertThat(packet.isCompressed()).isTrue();
    }

    @Test
    void testBuilderWithDefaults() {
        // When
        Packet packet = Packet.builder("TestMessage").build();

        // Then
        assertThat(packet.getMsgId()).isEqualTo("TestMessage");
        assertThat(packet.getMsgSeq()).isEqualTo((short) 0);
        assertThat(packet.getStageId()).isEqualTo(0L);
        assertThat(packet.getErrorCode()).isEqualTo((short) 0);
        assertThat(packet.getOriginalSize()).isEqualTo(0);
        assertThat(packet.getPayload()).isEmpty();
    }

    @Test
    void testGetPayloadBuffer() {
        // Given
        byte[] payload = {1, 2, 3, 4};
        Packet packet = Packet.fromBytes("TestMessage", payload);

        // When
        ByteBuffer buffer = packet.getPayloadBuffer();

        // Then
        assertThat(buffer.remaining()).isEqualTo(4);
        assertThat(buffer.order()).isEqualTo(java.nio.ByteOrder.LITTLE_ENDIAN);
    }

    @Test
    void testHasError() {
        // Given
        Packet normalPacket = Packet.builder("Test").errorCode((short) 0).build();
        Packet errorPacket = Packet.builder("Test").errorCode((short) 1).build();

        // Then
        assertThat(normalPacket.hasError()).isFalse();
        assertThat(errorPacket.hasError()).isTrue();
    }

    @Test
    void testIsCompressed() {
        // Given
        Packet normalPacket = Packet.builder("Test").originalSize(0).build();
        Packet compressedPacket = Packet.builder("Test").originalSize(100).build();

        // Then
        assertThat(normalPacket.isCompressed()).isFalse();
        assertThat(compressedPacket.isCompressed()).isTrue();
    }

    @Test
    void testToString() {
        // Given
        Packet packet = Packet.builder("TestMessage")
            .msgSeq((short) 1)
            .stageId(100L)
            .payload(new byte[10])
            .build();

        // When
        String str = packet.toString();

        // Then
        assertThat(str).contains("TestMessage");
        assertThat(str).contains("msgSeq=1");
        assertThat(str).contains("stageId=100");
        assertThat(str).contains("payloadSize=10");
    }

    @Test
    void testClose() {
        // Given
        Packet packet = Packet.empty("Test");

        // When/Then - 예외가 발생하지 않아야 함
        assertThatCode(() -> packet.close()).doesNotThrowAnyException();
    }
}
