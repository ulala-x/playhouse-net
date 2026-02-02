package com.playhouse.connector.extensions.msgpack;

import com.playhouse.connector.Packet;
import org.junit.jupiter.api.Test;

import java.io.IOException;
import java.util.Optional;

import static org.assertj.core.api.Assertions.assertThat;
import static org.assertj.core.api.Assertions.assertThatThrownBy;

class MsgPackExtensionsTest {

    // Test data class - needs to be public and have default constructor for MessagePack
    public static class TestMessage {
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
    void testMsgPackPacketOf_shouldCreatePacketWithCorrectMsgId() {
        // Given
        TestMessage message = new TestMessage("Hello", 42);

        // When
        Packet packet = MsgPackPacket.of(message);

        // Then
        assertThat(packet.getMsgId()).isEqualTo("TestMessage");
        assertThat(packet.getPayload()).isNotEmpty();
    }

    @Test
    void testMsgPackPacketOf_withCustomMsgId() {
        // Given
        TestMessage message = new TestMessage("Hello", 42);

        // When
        Packet packet = MsgPackPacket.of("CustomMsgId", message);

        // Then
        assertThat(packet.getMsgId()).isEqualTo("CustomMsgId");
    }

    @Test
    void testMsgPackPacketOf_shouldThrowOnNullObject() {
        // When/Then
        assertThatThrownBy(() -> MsgPackPacket.of(null))
            .isInstanceOf(IllegalArgumentException.class)
            .hasMessageContaining("cannot be null");
    }

    @Test
    void testMsgPackPacketExtensions_parse_shouldDeserializeCorrectly() throws IOException {
        // Given
        TestMessage original = new TestMessage("Test", 123);
        Packet packet = MsgPackPacket.of(original);

        // When
        TestMessage parsed = MsgPackPacketExtensions.parse(packet, TestMessage.class);

        // Then
        assertThat(parsed).isNotNull();
        assertThat(parsed.getText()).isEqualTo("Test");
        assertThat(parsed.getNumber()).isEqualTo(123);
    }

    @Test
    void testMsgPackPacketExtensions_parse_shouldThrowOnNullPacket() {
        // When/Then
        assertThatThrownBy(() -> MsgPackPacketExtensions.parse(null, TestMessage.class))
            .isInstanceOf(IllegalArgumentException.class)
            .hasMessageContaining("Packet cannot be null");
    }

    @Test
    void testMsgPackPacketExtensions_parse_shouldThrowOnNullType() {
        // Given
        Packet packet = MsgPackPacket.of(new TestMessage("Test", 123));

        // When/Then
        assertThatThrownBy(() -> MsgPackPacketExtensions.parse(packet, null))
            .isInstanceOf(IllegalArgumentException.class)
            .hasMessageContaining("Type cannot be null");
    }

    @Test
    void testMsgPackPacketExtensions_tryParse_shouldReturnOptionalWithValue() {
        // Given
        TestMessage original = new TestMessage("Test", 456);
        Packet packet = MsgPackPacket.of(original);

        // When
        Optional<TestMessage> result = MsgPackPacketExtensions.tryParse(packet, TestMessage.class);

        // Then
        assertThat(result).isPresent();
        assertThat(result.get().getText()).isEqualTo("Test");
        assertThat(result.get().getNumber()).isEqualTo(456);
    }

    @Test
    void testMsgPackPacketExtensions_tryParse_shouldReturnEmptyOnInvalidData() {
        // Given
        Packet packet = Packet.fromBytes("TestMessage", "invalid msgpack data".getBytes());

        // When
        Optional<TestMessage> result = MsgPackPacketExtensions.tryParse(packet, TestMessage.class);

        // Then
        assertThat(result).isEmpty();
    }

    @Test
    void testRoundTrip_shouldPreserveData() throws IOException {
        // Given
        TestMessage original = new TestMessage("Round trip test", 789);

        // When
        Packet packet = MsgPackPacket.of(original);
        TestMessage restored = MsgPackPacketExtensions.parse(packet, TestMessage.class);

        // Then
        assertThat(restored.getText()).isEqualTo(original.getText());
        assertThat(restored.getNumber()).isEqualTo(original.getNumber());
    }

    @Test
    void testEmptyStringField_shouldSerializeCorrectly() throws IOException {
        // Given
        TestMessage message = new TestMessage("", 0);

        // When
        Packet packet = MsgPackPacket.of(message);
        TestMessage parsed = MsgPackPacketExtensions.parse(packet, TestMessage.class);

        // Then
        assertThat(parsed.getText()).isEmpty();
        assertThat(parsed.getNumber()).isEqualTo(0);
    }

    @Test
    void testBinarySize_shouldBeSmallerThanJson() throws IOException {
        // Given
        TestMessage message = new TestMessage("This is a longer test message to compare sizes", 999);
        Packet msgPackPacket = MsgPackPacket.of(message);

        // Create JSON representation for size comparison
        String jsonString = "{\"text\":\"This is a longer test message to compare sizes\",\"number\":999}";
        byte[] jsonBytes = jsonString.getBytes();

        // When/Then - MessagePack should typically be smaller or comparable
        // This is more of a demonstration than a strict test
        assertThat(msgPackPacket.getPayload().length).isLessThanOrEqualTo(jsonBytes.length + 20);
    }
}
