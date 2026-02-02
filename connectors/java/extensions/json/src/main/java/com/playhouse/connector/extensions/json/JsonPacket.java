package com.playhouse.connector.extensions.json;

import com.google.gson.Gson;
import com.google.gson.GsonBuilder;
import com.playhouse.connector.Packet;

import java.nio.charset.StandardCharsets;

/**
 * Factory methods for creating Packet instances from JSON-serializable objects.
 * <p>
 * This class provides utility methods to serialize Java objects into JSON and create
 * Packet instances with the serialized payload.
 * </p>
 *
 * <p>Example usage:</p>
 * <pre>{@code
 * // Create a packet from an object
 * ChatRequest request = new ChatRequest("Hello, World!");
 * Packet packet = JsonPacket.of(request);
 *
 * // Send the packet
 * connector.send(packet);
 * }</pre>
 */
public final class JsonPacket {

    private static final Gson GSON = new GsonBuilder().create();

    // Private constructor to prevent instantiation
    private JsonPacket() {
        throw new AssertionError("Cannot instantiate utility class");
    }

    /**
     * Creates a Packet from a JSON-serializable object.
     * <p>
     * The message ID is automatically derived from the simple class name of the object.
     * The object is serialized to JSON and stored as the packet payload.
     * </p>
     *
     * @param <T> The type of object to serialize
     * @param obj The object to serialize as JSON
     * @return A new Packet containing the JSON payload
     * @throws IllegalArgumentException If obj is null
     * @example
     * <pre>{@code
     * ChatMessage message = new ChatMessage("user123", "Hello!");
     * Packet packet = JsonPacket.of(message);
     * // Packet will have msgId = "ChatMessage" and JSON payload
     * }</pre>
     */
    public static <T> Packet of(T obj) {
        if (obj == null) {
            throw new IllegalArgumentException("Object cannot be null");
        }

        String msgId = obj.getClass().getSimpleName();
        String json = GSON.toJson(obj);
        byte[] payload = json.getBytes(StandardCharsets.UTF_8);

        return Packet.fromBytes(msgId, payload);
    }

    /**
     * Creates a Packet from a JSON-serializable object with a custom message ID.
     * <p>
     * This method allows you to specify a custom message ID instead of using the class name.
     * </p>
     *
     * @param <T>   The type of object to serialize
     * @param msgId The message ID for the packet
     * @param obj   The object to serialize as JSON
     * @return A new Packet containing the JSON payload with the specified message ID
     * @throws IllegalArgumentException If msgId or obj is null
     * @example
     * <pre>{@code
     * ChatMessage message = new ChatMessage("user123", "Hello!");
     * Packet packet = JsonPacket.of("CustomChatMsg", message);
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

        String json = GSON.toJson(obj);
        byte[] payload = json.getBytes(StandardCharsets.UTF_8);

        return Packet.fromBytes(msgId, payload);
    }

    /**
     * Gets the Gson instance used by this factory.
     * Useful for advanced configuration or custom serialization.
     *
     * @return The Gson instance
     */
    public static Gson getGson() {
        return GSON;
    }
}
