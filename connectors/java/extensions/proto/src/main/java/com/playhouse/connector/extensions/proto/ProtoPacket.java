package com.playhouse.connector.extensions.proto;

import com.google.protobuf.MessageLite;
import com.playhouse.connector.Packet;

/**
 * Factory methods for creating Packet instances from Protocol Buffer messages.
 * <p>
 * This class provides utility methods to serialize Protocol Buffer messages into binary format
 * and create Packet instances with the serialized payload.
 * </p>
 *
 * <p>Example usage:</p>
 * <pre>{@code
 * // Create a packet from a proto message
 * ChatRequest request = ChatRequest.newBuilder()
 *     .setMessage("Hello, World!")
 *     .build();
 * Packet packet = ProtoPacket.of(request);
 *
 * // Send the packet
 * connector.send(packet);
 * }</pre>
 */
public final class ProtoPacket {

    // Private constructor to prevent instantiation
    private ProtoPacket() {
        throw new AssertionError("Cannot instantiate utility class");
    }

    /**
     * Creates a Packet from a Protocol Buffer message.
     * <p>
     * The message ID is automatically derived from the simple class name of the message.
     * The message is serialized to Protocol Buffer binary format and stored as the packet payload.
     * </p>
     *
     * @param <T>     The Protocol Buffer message type
     * @param message The Protocol Buffer message to serialize
     * @return A new Packet containing the Protocol Buffer payload
     * @throws IllegalArgumentException If message is null
     * @example
     * <pre>{@code
     * ChatMessage message = ChatMessage.newBuilder()
     *     .setUserId("user123")
     *     .setText("Hello!")
     *     .build();
     * Packet packet = ProtoPacket.of(message);
     * // Packet will have msgId = "ChatMessage" and protobuf payload
     * }</pre>
     */
    public static <T extends MessageLite> Packet of(T message) {
        if (message == null) {
            throw new IllegalArgumentException("Message cannot be null");
        }

        String msgId = message.getClass().getSimpleName();
        byte[] payload = message.toByteArray();

        return Packet.fromBytes(msgId, payload);
    }

    /**
     * Creates a Packet from a Protocol Buffer message with a custom message ID.
     * <p>
     * This method allows you to specify a custom message ID instead of using the class name.
     * </p>
     *
     * @param <T>     The Protocol Buffer message type
     * @param msgId   The message ID for the packet
     * @param message The Protocol Buffer message to serialize
     * @return A new Packet containing the Protocol Buffer payload with the specified message ID
     * @throws IllegalArgumentException If msgId or message is null
     * @example
     * <pre>{@code
     * ChatMessage message = ChatMessage.newBuilder()
     *     .setUserId("user123")
     *     .setText("Hello!")
     *     .build();
     * Packet packet = ProtoPacket.of("CustomChatMsg", message);
     * // Packet will have msgId = "CustomChatMsg"
     * }</pre>
     */
    public static <T extends MessageLite> Packet of(String msgId, T message) {
        if (msgId == null || msgId.isEmpty()) {
            throw new IllegalArgumentException("Message ID cannot be null or empty");
        }
        if (message == null) {
            throw new IllegalArgumentException("Message cannot be null");
        }

        byte[] payload = message.toByteArray();
        return Packet.fromBytes(msgId, payload);
    }
}
