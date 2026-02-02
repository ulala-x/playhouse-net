/**
 * PlayHouse Connector - JSON Extension
 * Extension methods for parsing JSON from Packet instances.
 */

import { Packet } from '@playhouse/connector';

/**
 * Parses the packet payload as JSON and deserializes it to the specified type.
 * @param packet The packet containing JSON payload
 * @returns The deserialized object
 * @throws Error if deserialization fails
 * @example
 * ```typescript
 * const request = parseJson<ChatRequest>(packet);
 * ```
 */
export function parseJson<T>(packet: Packet): T {
    if (packet.payload.length === 0) {
        throw new Error('Cannot parse empty payload as JSON');
    }

    try {
        const json = new TextDecoder().decode(packet.payload);
        const result = JSON.parse(json) as T;

        if (result === null || result === undefined) {
            throw new Error('JSON deserialization resulted in null or undefined');
        }

        return result;
    } catch (error) {
        if (error instanceof SyntaxError) {
            throw new Error(`Failed to parse JSON: ${error.message}`);
        }
        throw error;
    }
}

/**
 * Tries to parse the packet payload as JSON and deserialize it to the specified type.
 * @param packet The packet containing JSON payload
 * @returns The deserialized object, or undefined if parsing fails
 * @example
 * ```typescript
 * const request = tryParseJson<ChatRequest>(packet);
 * if (request) {
 *     // Use request...
 * }
 * ```
 */
export function tryParseJson<T>(packet: Packet): T | undefined {
    try {
        return parseJson<T>(packet);
    } catch {
        return undefined;
    }
}

/**
 * Creates a packet with JSON-serialized payload.
 * @param obj The object to serialize as JSON
 * @param msgId Optional message ID (defaults to constructor name)
 * @returns A new Packet with JSON payload
 * @example
 * ```typescript
 * const packet = createJsonPacket({ message: 'Hello' }, 'ChatMessage');
 * ```
 */
export function createJsonPacket<T extends object>(obj: T, msgId?: string): Packet {
    const id = msgId ?? obj.constructor.name;

    if (id === 'Object') {
        throw new Error('Message ID must be provided for plain objects or objects without constructor name');
    }

    const json = JSON.stringify(obj);
    const payload = new TextEncoder().encode(json);

    return Packet.fromBytes(id, payload);
}
