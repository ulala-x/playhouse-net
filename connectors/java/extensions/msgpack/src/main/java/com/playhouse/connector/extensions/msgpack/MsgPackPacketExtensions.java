package com.playhouse.connector.extensions.msgpack;

import com.fasterxml.jackson.databind.ObjectMapper;
import com.playhouse.connector.Packet;
import org.msgpack.core.MessagePack;
import org.msgpack.core.MessageUnpacker;
import org.msgpack.jackson.dataformat.MessagePackFactory;

import java.io.IOException;
import java.util.Optional;

/**
 * Extension methods for parsing MessagePack from Packet instances.
 * <p>
 * This class provides utility methods to deserialize Packet payloads into Java objects
 * using MessagePack binary serialization format.
 * </p>
 *
 * <p>Example usage:</p>
 * <pre>{@code
 * // Parse a packet into a typed object
 * ChatRequest request = MsgPackPacketExtensions.parse(packet, ChatRequest.class);
 *
 * // Try to parse with error handling
 * Optional<ChatRequest> maybeRequest = MsgPackPacketExtensions.tryParse(packet, ChatRequest.class);
 * maybeRequest.ifPresent(req -> {
 *     // Use request...
 * });
 * }</pre>
 */
public final class MsgPackPacketExtensions {

    private static final ObjectMapper OBJECT_MAPPER = new ObjectMapper(new MessagePackFactory());

    // Private constructor to prevent instantiation
    private MsgPackPacketExtensions() {
        throw new AssertionError("Cannot instantiate utility class");
    }

    /**
     * Parses the packet payload as MessagePack and deserializes it to the specified type.
     *
     * @param <T>    The type to deserialize to
     * @param packet The packet containing MessagePack payload
     * @param type   The class type to deserialize to
     * @return The deserialized object
     * @throws IOException If deserialization fails
     * @throws IllegalStateException If the result is null
     * @example
     * <pre>{@code
     * ChatRequest request = MsgPackPacketExtensions.parse(packet, ChatRequest.class);
     * System.out.println("Message: " + request.getMessage());
     * }</pre>
     */
    public static <T> T parse(Packet packet, Class<T> type) throws IOException {
        if (packet == null) {
            throw new IllegalArgumentException("Packet cannot be null");
        }
        if (type == null) {
            throw new IllegalArgumentException("Type cannot be null");
        }

        byte[] payload = packet.getPayload();
        T result = OBJECT_MAPPER.readValue(payload, type);

        if (result == null) {
            throw new IllegalStateException("Failed to deserialize " + type.getName());
        }

        return result;
    }

    /**
     * Tries to parse the packet payload as MessagePack and deserialize it to the specified type.
     * Returns an Optional that is empty if parsing fails.
     *
     * @param <T>    The type to deserialize to
     * @param packet The packet containing MessagePack payload
     * @param type   The class type to deserialize to
     * @return Optional containing the deserialized object, or empty if parsing fails
     * @example
     * <pre>{@code
     * Optional<ChatRequest> maybeRequest = MsgPackPacketExtensions.tryParse(packet, ChatRequest.class);
     * if (maybeRequest.isPresent()) {
     *     ChatRequest request = maybeRequest.get();
     *     // Use request...
     * } else {
     *     System.err.println("Failed to parse packet");
     * }
     * }</pre>
     */
    public static <T> Optional<T> tryParse(Packet packet, Class<T> type) {
        try {
            return Optional.of(parse(packet, type));
        } catch (Exception e) {
            return Optional.empty();
        }
    }

    /**
     * Gets the ObjectMapper instance used by this extension.
     * Useful for advanced configuration or custom serialization.
     *
     * @return The ObjectMapper instance configured for MessagePack
     */
    public static ObjectMapper getObjectMapper() {
        return OBJECT_MAPPER;
    }
}
