/**
 * PlayHouse Connector - MessagePack Extension
 * Extension methods for parsing MessagePack from Packet instances.
 */

import { decode, encode } from '@msgpack/msgpack';
import { Packet } from '@playhouse/connector';

/**
 * Parses the packet payload as MessagePack and deserializes it to the specified type.
 * @param packet The packet containing MessagePack payload
 * @returns The deserialized object
 * @throws Error if deserialization fails
 * @example
 * ```typescript
 * const request = parseMsgPack<ChatRequest>(packet);
 * ```
 */
export function parseMsgPack<T>(packet: Packet): T {
    if (packet.payload.length === 0) {
        throw new Error('Cannot parse empty payload as MessagePack');
    }

    try {
        const result = decode(packet.payload) as T;

        if (result === null || result === undefined) {
            throw new Error('MessagePack deserialization resulted in null or undefined');
        }

        return result;
    } catch (error) {
        if (error instanceof Error) {
            throw new Error(`Failed to parse MessagePack: ${error.message}`);
        }
        throw error;
    }
}

/**
 * Tries to parse the packet payload as MessagePack and deserialize it to the specified type.
 * @param packet The packet containing MessagePack payload
 * @returns The deserialized object, or undefined if parsing fails
 * @example
 * ```typescript
 * const request = tryParseMsgPack<ChatRequest>(packet);
 * if (request) {
 *     // Use request...
 * }
 * ```
 */
export function tryParseMsgPack<T>(packet: Packet): T | undefined {
    try {
        return parseMsgPack<T>(packet);
    } catch {
        return undefined;
    }
}

/**
 * Creates a packet with MessagePack-serialized payload.
 * @param obj The object to serialize as MessagePack
 * @param msgId Optional message ID (defaults to constructor name)
 * @returns A new Packet with MessagePack payload
 * @example
 * ```typescript
 * const packet = createMsgPackPacket({ message: 'Hello' }, 'ChatMessage');
 * ```
 */
export function createMsgPackPacket<T extends object>(obj: T, msgId?: string): Packet {
    const id = msgId ?? obj.constructor.name;

    if (id === 'Object') {
        throw new Error('Message ID must be provided for plain objects or objects without constructor name');
    }

    const payload = encode(obj);

    return Packet.fromBytes(id, payload);
}
