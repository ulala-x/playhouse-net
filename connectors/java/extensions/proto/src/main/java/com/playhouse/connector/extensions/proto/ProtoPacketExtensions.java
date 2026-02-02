package com.playhouse.connector.extensions.proto;

import com.google.protobuf.InvalidProtocolBufferException;
import com.google.protobuf.MessageLite;
import com.google.protobuf.Parser;
import com.playhouse.connector.Packet;

import java.util.Optional;

/**
 * Extension methods for parsing Protocol Buffers from Packet instances.
 * <p>
 * This class provides utility methods to deserialize Packet payloads into Protocol Buffer objects
 * using Google Protocol Buffers binary serialization format.
 * </p>
 *
 * <p>Example usage:</p>
 * <pre>{@code
 * // Parse a packet into a proto message
 * ChatRequest request = ProtoPacketExtensions.parse(packet, ChatRequest.parser());
 *
 * // Try to parse with error handling
 * Optional<ChatRequest> maybeRequest = ProtoPacketExtensions.tryParse(packet, ChatRequest.parser());
 * maybeRequest.ifPresent(req -> {
 *     // Use request...
 * });
 * }</pre>
 */
public final class ProtoPacketExtensions {

    // Private constructor to prevent instantiation
    private ProtoPacketExtensions() {
        throw new AssertionError("Cannot instantiate utility class");
    }

    /**
     * Parses the packet payload as Protocol Buffers and deserializes it to the specified type.
     * <p>
     * This method uses the Protocol Buffer's parser to deserialize the binary payload.
     * </p>
     *
     * @param <T>    The Protocol Buffer message type to deserialize to
     * @param packet The packet containing Protocol Buffer payload
     * @param parser The Protocol Buffer parser for the message type
     * @return The deserialized Protocol Buffer message
     * @throws InvalidProtocolBufferException If deserialization fails
     * @throws IllegalArgumentException If packet or parser is null
     * @example
     * <pre>{@code
     * ChatRequest request = ProtoPacketExtensions.parse(packet, ChatRequest.parser());
     * System.out.println("Message: " + request.getMessage());
     * }</pre>
     */
    public static <T extends MessageLite> T parse(Packet packet, Parser<T> parser)
        throws InvalidProtocolBufferException {
        if (packet == null) {
            throw new IllegalArgumentException("Packet cannot be null");
        }
        if (parser == null) {
            throw new IllegalArgumentException("Parser cannot be null");
        }

        byte[] payload = packet.getPayload();
        return parser.parseFrom(payload);
    }

    /**
     * Tries to parse the packet payload as Protocol Buffers and deserialize it to the specified type.
     * Returns an Optional that is empty if parsing fails.
     *
     * @param <T>    The Protocol Buffer message type to deserialize to
     * @param packet The packet containing Protocol Buffer payload
     * @param parser The Protocol Buffer parser for the message type
     * @return Optional containing the deserialized message, or empty if parsing fails
     * @example
     * <pre>{@code
     * Optional<ChatRequest> maybeRequest = ProtoPacketExtensions.tryParse(packet, ChatRequest.parser());
     * if (maybeRequest.isPresent()) {
     *     ChatRequest request = maybeRequest.get();
     *     // Use request...
     * } else {
     *     System.err.println("Failed to parse packet");
     * }
     * }</pre>
     */
    public static <T extends MessageLite> Optional<T> tryParse(Packet packet, Parser<T> parser) {
        try {
            return Optional.of(parse(packet, parser));
        } catch (Exception e) {
            return Optional.empty();
        }
    }

    /**
     * Parses the packet payload using a default instance of the Protocol Buffer message.
     * <p>
     * This is a convenience method that extracts the parser from the default instance.
     * </p>
     *
     * @param <T>             The Protocol Buffer message type
     * @param packet          The packet containing Protocol Buffer payload
     * @param defaultInstance The default instance of the message type (used to get the parser)
     * @return The deserialized Protocol Buffer message
     * @throws InvalidProtocolBufferException If deserialization fails
     * @example
     * <pre>{@code
     * ChatRequest request = ProtoPacketExtensions.parse(packet, ChatRequest.getDefaultInstance());
     * }</pre>
     */
    @SuppressWarnings("unchecked")
    public static <T extends MessageLite> T parse(Packet packet, T defaultInstance)
        throws InvalidProtocolBufferException {
        if (defaultInstance == null) {
            throw new IllegalArgumentException("Default instance cannot be null");
        }
        Parser<T> parser = (Parser<T>) defaultInstance.getParserForType();
        return parse(packet, parser);
    }

    /**
     * Tries to parse the packet payload using a default instance of the Protocol Buffer message.
     *
     * @param <T>             The Protocol Buffer message type
     * @param packet          The packet containing Protocol Buffer payload
     * @param defaultInstance The default instance of the message type
     * @return Optional containing the deserialized message, or empty if parsing fails
     * @example
     * <pre>{@code
     * Optional<ChatRequest> maybeRequest = ProtoPacketExtensions.tryParse(packet, ChatRequest.getDefaultInstance());
     * }</pre>
     */
    public static <T extends MessageLite> Optional<T> tryParse(Packet packet, T defaultInstance) {
        try {
            return Optional.of(parse(packet, defaultInstance));
        } catch (Exception e) {
            return Optional.empty();
        }
    }
}
