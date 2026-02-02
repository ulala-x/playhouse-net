package com.playhouse.connector.extensions.json;

import com.google.gson.Gson;
import com.google.gson.GsonBuilder;
import com.google.gson.JsonSyntaxException;
import com.playhouse.connector.Packet;

import java.nio.charset.StandardCharsets;
import java.util.Optional;

/**
 * Extension methods for parsing JSON from Packet instances.
 * <p>
 * This class provides utility methods to deserialize Packet payloads into Java objects
 * using Google Gson JSON serialization.
 * </p>
 *
 * <p>Example usage:</p>
 * <pre>{@code
 * // Parse a packet into a typed object
 * ChatRequest request = JsonPacketExtensions.parse(packet, ChatRequest.class);
 *
 * // Try to parse with error handling
 * Optional<ChatRequest> maybeRequest = JsonPacketExtensions.tryParse(packet, ChatRequest.class);
 * maybeRequest.ifPresent(req -> {
 *     // Use request...
 * });
 * }</pre>
 */
public final class JsonPacketExtensions {

    private static final Gson GSON = new GsonBuilder().create();

    // Private constructor to prevent instantiation
    private JsonPacketExtensions() {
        throw new AssertionError("Cannot instantiate utility class");
    }

    /**
     * Parses the packet payload as JSON and deserializes it to the specified type.
     *
     * @param <T>    The type to deserialize to
     * @param packet The packet containing JSON payload
     * @param type   The class type to deserialize to
     * @return The deserialized object
     * @throws JsonSyntaxException If the JSON is malformed
     * @throws IllegalStateException If deserialization fails
     * @example
     * <pre>{@code
     * ChatRequest request = JsonPacketExtensions.parse(packet, ChatRequest.class);
     * System.out.println("Message: " + request.getMessage());
     * }</pre>
     */
    public static <T> T parse(Packet packet, Class<T> type) {
        if (packet == null) {
            throw new IllegalArgumentException("Packet cannot be null");
        }
        if (type == null) {
            throw new IllegalArgumentException("Type cannot be null");
        }

        String json = new String(packet.getPayload(), StandardCharsets.UTF_8);
        T result = GSON.fromJson(json, type);

        if (result == null) {
            throw new IllegalStateException("Failed to deserialize " + type.getName());
        }

        return result;
    }

    /**
     * Tries to parse the packet payload as JSON and deserialize it to the specified type.
     * Returns an Optional that is empty if parsing fails.
     *
     * @param <T>    The type to deserialize to
     * @param packet The packet containing JSON payload
     * @param type   The class type to deserialize to
     * @return Optional containing the deserialized object, or empty if parsing fails
     * @example
     * <pre>{@code
     * Optional<ChatRequest> maybeRequest = JsonPacketExtensions.tryParse(packet, ChatRequest.class);
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
     * Gets the Gson instance used by this extension.
     * Useful for advanced configuration or custom serialization.
     *
     * @return The Gson instance
     */
    public static Gson getGson() {
        return GSON;
    }
}
