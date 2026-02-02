package com.playhouse.connector.extensions.msgpack;

import com.fasterxml.jackson.databind.ObjectMapper;
import com.playhouse.connector.Packet;
import org.msgpack.jackson.dataformat.MessagePackFactory;

import java.io.IOException;

/**
 * Factory methods for creating Packet instances from MessagePack-serializable objects.
 * <p>
 * This class provides utility methods to serialize Java objects into MessagePack binary format
 * and create Packet instances with the serialized payload.
 * </p>
 *
 * <p>Example usage:</p>
 * <pre>{@code
 * // Create a packet from an object
 * ChatRequest request = new ChatRequest("Hello, World!");
 * Packet packet = MsgPackPacket.of(request);
 *
 * // Send the packet
 * connector.send(packet);
 * }</pre>
 */
public final class MsgPackPacket {

    private static final ObjectMapper OBJECT_MAPPER = new ObjectMapper(new MessagePackFactory());

    // Private constructor to prevent instantiation
    private MsgPackPacket() {
        throw new AssertionError("Cannot instantiate utility class");
    }

    /**
     * Creates a Packet from a MessagePack-serializable object.
     * <p>
     * The message ID is automatically derived from the simple class name of the object.
     * The object is serialized to MessagePack binary format and stored as the packet payload.
     * </p>
     *
     * @param <T> The type of object to serialize
     * @param obj The object to serialize as MessagePack
     * @return A new Packet containing the MessagePack payload
     * @throws IllegalArgumentException If obj is null
     * @throws RuntimeException If serialization fails
     * @example
     * <pre>{@code
     * ChatMessage message = new ChatMessage("user123", "Hello!");
     * Packet packet = MsgPackPacket.of(message);
     * // Packet will have msgId = "ChatMessage" and MessagePack payload
     * }</pre>
     */
    public static <T> Packet of(T obj) {
        if (obj == null) {
            throw new IllegalArgumentException("Object cannot be null");
        }

        String msgId = obj.getClass().getSimpleName();
        try {
            byte[] payload = OBJECT_MAPPER.writeValueAsBytes(obj);
            return Packet.fromBytes(msgId, payload);
        } catch (IOException e) {
            throw new RuntimeException("Failed to serialize object to MessagePack", e);
        }
    }

    /**
     * Creates a Packet from a MessagePack-serializable object with a custom message ID.
     * <p>
     * This method allows you to specify a custom message ID instead of using the class name.
     * </p>
     *
     * @param <T>   The type of object to serialize
     * @param msgId The message ID for the packet
     * @param obj   The object to serialize as MessagePack
     * @return A new Packet containing the MessagePack payload with the specified message ID
     * @throws IllegalArgumentException If msgId or obj is null
     * @throws RuntimeException If serialization fails
     * @example
     * <pre>{@code
     * ChatMessage message = new ChatMessage("user123", "Hello!");
     * Packet packet = MsgPackPacket.of("CustomChatMsg", message);
     * // Packet will have msgId = "CustomChatMsg"
     * }</pre>
     */
    public static <T> Packet of(String msgId, T obj) {
        if (msgId == null || msgId.isEmpty()) {
            throw new IllegalArgumentException("Message ID cannot be null or empty");
        }
        if (obj == null) {
            throw new IllegalArgumentException("Object cannot be null");
        }

        try {
            byte[] payload = OBJECT_MAPPER.writeValueAsBytes(obj);
            return Packet.fromBytes(msgId, payload);
        } catch (IOException e) {
            throw new RuntimeException("Failed to serialize object to MessagePack", e);
        }
    }

    /**
     * Gets the ObjectMapper instance used by this factory.
     * Useful for advanced configuration or custom serialization.
     *
     * @return The ObjectMapper instance configured for MessagePack
     */
    public static ObjectMapper getObjectMapper() {
        return OBJECT_MAPPER;
    }
}
