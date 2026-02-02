package com.playhouse.connector.extensions.json;

import com.playhouse.connector.Packet;
import org.junit.jupiter.api.Test;

import java.util.Optional;

import static org.assertj.core.api.Assertions.assertThat;
import static org.assertj.core.api.Assertions.assertThatThrownBy;

class JsonExtensionsTest {

    // Test data class
    static class TestMessage {
        private String text;
        private int number;

        public TestMessage() {
        }

        public TestMessage(String text, int number) {
            this.text = text;
            this.number = number;
        }

        public String getText() {
            return text;
        }

        public void setText(String text) {
            this.text = text;
        }

        public int getNumber() {
            return number;
        }

        public void setNumber(int number) {
            this.number = number;
        }
    }

    @Test
    void testJsonPacketOf_shouldCreatePacketWithCorrectMsgId() {
        // Given
        TestMessage message = new TestMessage("Hello", 42);

        // When
        Packet packet = JsonPacket.of(message);

        // Then
        assertThat(packet.getMsgId()).isEqualTo("TestMessage");
        assertThat(packet.getPayload()).isNotEmpty();
    }

    @Test
    void testJsonPacketOf_withCustomMsgId() {
        // Given
        TestMessage message = new TestMessage("Hello", 42);

        // When
        Packet packet = JsonPacket.of("CustomMsgId", message);

        // Then
        assertThat(packet.getMsgId()).isEqualTo("CustomMsgId");
    }

    @Test
    void testJsonPacketOf_shouldThrowOnNullObject() {
        // When/Then
        assertThatThrownBy(() -> JsonPacket.of(null))
            .isInstanceOf(IllegalArgumentException.class)
            .hasMessageContaining("cannot be null");
    }

    @Test
    void testJsonPacketExtensions_parse_shouldDeserializeCorrectly() {
        // Given
        TestMessage original = new TestMessage("Test", 123);
        Packet packet = JsonPacket.of(original);

        // When
        TestMessage parsed = JsonPacketExtensions.parse(packet, TestMessage.class);

        // Then
        assertThat(parsed).isNotNull();
        assertThat(parsed.getText()).isEqualTo("Test");
        assertThat(parsed.getNumber()).isEqualTo(123);
    }

    @Test
    void testJsonPacketExtensions_parse_shouldThrowOnNullPacket() {
        // When/Then
        assertThatThrownBy(() -> JsonPacketExtensions.parse(null, TestMessage.class))
            .isInstanceOf(IllegalArgumentException.class)
            .hasMessageContaining("Packet cannot be null");
    }

    @Test
    void testJsonPacketExtensions_parse_shouldThrowOnNullType() {
        // Given
        Packet packet = JsonPacket.of(new TestMessage("Test", 123));

        // When/Then
        assertThatThrownBy(() -> JsonPacketExtensions.parse(packet, null))
            .isInstanceOf(IllegalArgumentException.class)
            .hasMessageContaining("Type cannot be null");
    }

    @Test
    void testJsonPacketExtensions_tryParse_shouldReturnOptionalWithValue() {
        // Given
        TestMessage original = new TestMessage("Test", 456);
        Packet packet = JsonPacket.of(original);

        // When
        Optional<TestMessage> result = JsonPacketExtensions.tryParse(packet, TestMessage.class);

        // Then
        assertThat(result).isPresent();
        assertThat(result.get().getText()).isEqualTo("Test");
        assertThat(result.get().getNumber()).isEqualTo(456);
    }

    @Test
    void testJsonPacketExtensions_tryParse_shouldReturnEmptyOnInvalidJson() {
        // Given
        Packet packet = Packet.fromBytes("TestMessage", "invalid json".getBytes());

        // When
        Optional<TestMessage> result = JsonPacketExtensions.tryParse(packet, TestMessage.class);

        // Then
        assertThat(result).isEmpty();
    }

    @Test
    void testRoundTrip_shouldPreserveData() {
        // Given
        TestMessage original = new TestMessage("Round trip test", 789);

        // When
        Packet packet = JsonPacket.of(original);
        TestMessage restored = JsonPacketExtensions.parse(packet, TestMessage.class);

        // Then
        assertThat(restored.getText()).isEqualTo(original.getText());
        assertThat(restored.getNumber()).isEqualTo(original.getNumber());
    }

    @Test
    void testEmptyStringField_shouldSerializeCorrectly() {
        // Given
        TestMessage message = new TestMessage("", 0);

        // When
        Packet packet = JsonPacket.of(message);
        TestMessage parsed = JsonPacketExtensions.parse(packet, TestMessage.class);

        // Then
        assertThat(parsed.getText()).isEmpty();
        assertThat(parsed.getNumber()).isEqualTo(0);
    }
}
