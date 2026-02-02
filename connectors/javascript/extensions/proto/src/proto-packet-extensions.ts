/**
 * PlayHouse Connector - Protobuf Extension
 * Extension methods for parsing Protocol Buffers from Packet instances.
 */

import type { Type } from 'protobufjs';
import { Packet } from '@playhouse/connector';

/**
 * Interface for protobuf types with decode capability
 */
export interface ProtoType<T> {
    decode(reader: Uint8Array | import('protobufjs').Reader, length?: number): T;
}

/**
 * Parses the packet payload as Protobuf and deserializes it to the specified type.
 * @param packet The packet containing Protobuf payload
 * @param protoType The protobuf Type or decoder with decode() method
 * @returns The deserialized protobuf message
 * @throws Error if deserialization fails
 * @example
 * ```typescript
 * import { ChatRequest } from './generated/messages';
 * const request = parseProto(packet, ChatRequest);
 * ```
 */
export function parseProto<T>(packet: Packet, protoType: ProtoType<T>): T {
    try {
        // Empty payloads are valid for empty protobuf messages
        const result = protoType.decode(packet.payload);

        if (result === null || result === undefined) {
            throw new Error('Protobuf deserialization resulted in null or undefined');
        }

        return result;
    } catch (error) {
        if (error instanceof Error) {
            throw new Error(`Failed to parse Protobuf: ${error.message}`);
        }
        throw error;
    }
}

/**
 * Tries to parse the packet payload as Protobuf and deserialize it to the specified type.
 * @param packet The packet containing Protobuf payload
 * @param protoType The protobuf Type or decoder with decode() method
 * @returns The deserialized protobuf message, or undefined if parsing fails
 * @example
 * ```typescript
 * import { ChatRequest } from './generated/messages';
 * const request = tryParseProto(packet, ChatRequest);
 * if (request) {
 *     // Use request...
 * }
 * ```
 */
export function tryParseProto<T>(packet: Packet, protoType: ProtoType<T>): T | undefined {
    try {
        return parseProto<T>(packet, protoType);
    } catch {
        return undefined;
    }
}

/**
 * Creates a packet from a Protobuf message.
 * @param protoMessage The protobuf message to serialize
 * @param protoType The protobuf Type with encode() method
 * @param msgId Optional message ID (defaults to protoType name)
 * @returns A new Packet with Protobuf payload
 * @throws Error if encoding fails or type information is missing
 * @example
 * ```typescript
 * import { ChatRequest } from './generated/messages';
 * const message = ChatRequest.create({ roomId: 'room-1', message: 'Hello' });
 * const packet = createProtoPacket(message, ChatRequest, 'ChatRequest');
 * ```
 */
export function createProtoPacket<T extends Record<string, unknown>>(
    protoMessage: T,
    protoType: Type,
    msgId?: string
): Packet {
    const id = msgId ?? protoType.name;

    if (!id) {
        throw new Error('Message ID must be provided or protobuf type must have a name');
    }

    try {
        // Verify the message before encoding
        const errMsg = protoType.verify(protoMessage as Record<string, unknown>);
        if (errMsg) {
            throw new Error(`Protobuf verification failed: ${errMsg}`);
        }

        const message = protoType.create(protoMessage as Record<string, unknown>);
        const payload = protoType.encode(message).finish();

        return Packet.fromBytes(id, payload);
    } catch (error) {
        if (error instanceof Error) {
            throw new Error(`Failed to create Protobuf packet: ${error.message}`);
        }
        throw error;
    }
}

/**
 * Interface for protobuf.js Message instances
 */
export interface ProtoMessage {
    $type: Type;
    [key: string]: unknown;
}

/**
 * Creates a packet from a protobuf.js Message instance.
 * Uses the message's $type property for encoding.
 * @param protoMessage The protobuf message instance
 * @param msgId Optional message ID (defaults to $type.name)
 * @returns A new Packet with Protobuf payload
 * @throws Error if message lacks $type or encoding fails
 * @example
 * ```typescript
 * const message = ChatRequest.create({ roomId: 'room-1', message: 'Hello' });
 * const packet = createProtoPacketFromMessage(message);
 * ```
 */
export function createProtoPacketFromMessage(protoMessage: ProtoMessage, msgId?: string): Packet {
    const type = protoMessage.$type;
    if (!type) {
        throw new Error('Protobuf message must have $type property');
    }

    return createProtoPacket(protoMessage as Record<string, unknown>, type, msgId);
}
