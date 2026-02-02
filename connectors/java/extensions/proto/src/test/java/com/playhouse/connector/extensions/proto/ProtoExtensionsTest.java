package com.playhouse.connector.extensions.proto;

import com.google.protobuf.Any;
import com.google.protobuf.InvalidProtocolBufferException;
import com.google.protobuf.StringValue;
import com.google.protobuf.Int32Value;
import com.playhouse.connector.Packet;
import org.junit.jupiter.api.Test;

import java.util.Optional;

import static org.assertj.core.api.Assertions.assertThat;
import static org.assertj.core.api.Assertions.assertThatThrownBy;

/**
 * Tests for Protocol Buffer extensions.
 *
 * Note: These tests use StringValue and Int32Value from protobuf as simple MessageLite implementations
 * for testing purposes. In real usage, you would use your own generated proto classes.
 */
class ProtoExtensionsTest {

    @Test
    void testProtoPacketOf_shouldCreatePacketWithCorrectMsgId() {
        // Given
        StringValue message = StringValue.of("Hello, World!");

        // When
        Packet packet = ProtoPacket.of(message);

        // Then
        assertThat(packet.getMsgId()).isEqualTo("StringValue");
        assertThat(packet.getPayload()).isNotEmpty();
    }

    @Test
    void testProtoPacketOf_withCustomMsgId() {
        // Given
        StringValue message = StringValue.of("Hello, World!");

        // When
        Packet packet = ProtoPacket.of("CustomMsgId", message);

        // Then
        assertThat(packet.getMsgId()).isEqualTo("CustomMsgId");
    }

    @Test
    void testProtoPacketOf_shouldThrowOnNullMessage() {
        // When/Then
        assertThatThrownBy(() -> ProtoPacket.of(null))
            .isInstanceOf(IllegalArgumentException.class)
            .hasMessageContaining("cannot be null");
    }

    @Test
    void testProtoPacketOf_withNullMsgId_shouldThrow() {
        // Given
        StringValue message = StringValue.of("Test");

        // When/Then
        assertThatThrownBy(() -> ProtoPacket.of(null, message))
            .isInstanceOf(IllegalArgumentException.class)
            .hasMessageContaining("Message ID cannot be null");
    }

    @Test
    void testProtoPacketExtensions_parse_shouldDeserializeCorrectly() throws InvalidProtocolBufferException {
        // Given
        String originalText = "Test message";
        StringValue original = StringValue.of(originalText);
        Packet packet = ProtoPacket.of(original);

        // When
        StringValue parsed = ProtoPacketExtensions.parse(packet, StringValue.parser());

        // Then
        assertThat(parsed).isNotNull();
        assertThat(parsed.getValue()).isEqualTo(originalText);
    }

    @Test
    void testProtoPacketExtensions_parse_withDefaultInstance() throws InvalidProtocolBufferException {
        // Given
        String originalText = "Test with default instance";
        StringValue original = StringValue.of(originalText);
        Packet packet = ProtoPacket.of(original);

        // When
        StringValue parsed = ProtoPacketExtensions.parse(packet, StringValue.getDefaultInstance());

        // Then
        assertThat(parsed).isNotNull();
        assertThat(parsed.getValue()).isEqualTo(originalText);
    }

    @Test
    void testProtoPacketExtensions_parse_shouldThrowOnNullPacket() {
        // When/Then
        assertThatThrownBy(() -> ProtoPacketExtensions.parse(null, StringValue.parser()))
            .isInstanceOf(IllegalArgumentException.class)
            .hasMessageContaining("Packet cannot be null");
    }

    @Test
    void testProtoPacketExtensions_parse_shouldThrowOnNullParser() {
        // Given
        Packet packet = ProtoPacket.of(StringValue.of("Test"));

        // When/Then
        assertThatThrownBy(() -> ProtoPacketExtensions.parse(packet, (StringValue) null))
            .isInstanceOf(IllegalArgumentException.class)
            .hasMessageContaining("Default instance cannot be null");
    }

    @Test
    void testProtoPacketExtensions_tryParse_shouldReturnOptionalWithValue() {
        // Given
        String originalText = "Test optional parse";
        StringValue original = StringValue.of(originalText);
        Packet packet = ProtoPacket.of(original);

        // When
        Optional<StringValue> result = ProtoPacketExtensions.tryParse(packet, StringValue.parser());

        // Then
        assertThat(result).isPresent();
        assertThat(result.get().getValue()).isEqualTo(originalText);
    }

    @Test
    void testProtoPacketExtensions_tryParse_withDefaultInstance() {
        // Given
        String originalText = "Test optional with default";
        StringValue original = StringValue.of(originalText);
        Packet packet = ProtoPacket.of(original);

        // When
        Optional<StringValue> result = ProtoPacketExtensions.tryParse(packet, StringValue.getDefaultInstance());

        // Then
        assertThat(result).isPresent();
        assertThat(result.get().getValue()).isEqualTo(originalText);
    }

    @Test
    void testProtoPacketExtensions_tryParse_shouldReturnEmptyOnInvalidData() {
        // Given - Create a packet with invalid protobuf data
        Packet packet = Packet.fromBytes("TestMessage", "invalid protobuf data".getBytes());

        // When
        Optional<StringValue> result = ProtoPacketExtensions.tryParse(packet, StringValue.parser());

        // Then
        assertThat(result).isEmpty();
    }

    @Test
    void testRoundTrip_shouldPreserveData() throws InvalidProtocolBufferException {
        // Given
        String originalText = "Round trip test with UTF-8: こんにちは 🎉";
        StringValue original = StringValue.of(originalText);

        // When
        Packet packet = ProtoPacket.of(original);
        StringValue restored = ProtoPacketExtensions.parse(packet, StringValue.parser());

        // Then
        assertThat(restored.getValue()).isEqualTo(originalText);
        assertThat(restored).isEqualTo(original);
    }

    @Test
    void testEmptyMessage_shouldSerializeCorrectly() throws InvalidProtocolBufferException {
        // Given
        StringValue message = StringValue.of("");

        // When
        Packet packet = ProtoPacket.of(message);
        StringValue parsed = ProtoPacketExtensions.parse(packet, StringValue.parser());

        // Then
        assertThat(parsed.getValue()).isEmpty();
    }

    @Test
    void testIntegerMessage_shouldSerializeCorrectly() throws InvalidProtocolBufferException {
        // Given
        Int32Value message = Int32Value.of(42);

        // When
        Packet packet = ProtoPacket.of(message);
        Int32Value parsed = ProtoPacketExtensions.parse(packet, Int32Value.parser());

        // Then
        assertThat(parsed.getValue()).isEqualTo(42);
    }

    @Test
    void testLargeString_shouldHandleCorrectly() throws InvalidProtocolBufferException {
        // Given - Create a large string (10KB)
        StringBuilder sb = new StringBuilder();
        for (int i = 0; i < 10000; i++) {
            sb.append("x");
        }
        String largeText = sb.toString();
        StringValue original = StringValue.of(largeText);

        // When
        Packet packet = ProtoPacket.of(original);
        StringValue restored = ProtoPacketExtensions.parse(packet, StringValue.parser());

        // Then
        assertThat(restored.getValue().length()).isEqualTo(largeText.length());
        assertThat(restored.getValue()).isEqualTo(largeText);
    }

    @Test
    void testAnyMessage_shouldWorkWithDifferentTypes() throws InvalidProtocolBufferException {
        // Given
        StringValue stringValue = StringValue.of("Test");
        Any anyMessage = Any.pack(stringValue);

        // When
        Packet packet = ProtoPacket.of(anyMessage);
        Any parsedAny = ProtoPacketExtensions.parse(packet, Any.parser());
        StringValue unpacked = parsedAny.unpack(StringValue.class);

        // Then
        assertThat(unpacked.getValue()).isEqualTo("Test");
    }
}
