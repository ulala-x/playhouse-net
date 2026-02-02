/**
 * PlayHouse Connector - Packet
 */

import type { IDisposable, IProtoDecoder, IProtoMessage } from './types.js';

/**
 * Packet class - represents a message packet
 *
 * A Packet contains a message ID, sequence number, stage ID, error code, and payload.
 * Use the factory methods (empty, fromBytes, fromProto) to create packets.
 */
export class Packet implements IDisposable {
    private _disposed = false;

    /**
     * Creates a new Packet instance
     * @internal Use factory methods instead
     */
    constructor(
        /** Message ID (type name) */
        public readonly msgId: string,
        /** Message sequence number (0 = push, >0 = request-response) */
        public readonly msgSeq: number,
        /** Stage ID (64-bit integer) */
        public readonly stageId: bigint,
        /** Error code (0 = success) */
        public readonly errorCode: number,
        /** Payload data */
        public readonly payload: Uint8Array
    ) {}

    /**
     * Creates an empty packet (no payload)
     * @param msgId Message ID
     */
    static empty(msgId: string): Packet {
        return new Packet(msgId, 0, 0n, 0, new Uint8Array(0));
    }

    /**
     * Creates a packet from raw bytes
     * @param msgId Message ID
     * @param bytes Payload bytes
     */
    static fromBytes(msgId: string, bytes: Uint8Array): Packet {
        return new Packet(msgId, 0, 0n, 0, bytes);
    }

    /**
     * Creates a packet from a protobuf message
     * @param proto Protobuf message with $type and encode()
     */
    static fromProto(proto: IProtoMessage): Packet {
        const msgId = proto.$type?.name;
        if (!msgId) {
            throw new Error('Protobuf message must have $type.name property');
        }

        const payload = proto.encode?.().finish();
        if (!payload) {
            throw new Error('Protobuf message must have encode() method');
        }

        return new Packet(msgId, 0, 0n, 0, payload);
    }

    /**
     * Parses the payload as a protobuf message
     * @param decoder Protobuf decoder with decode() method
     */
    parse<T>(decoder: IProtoDecoder<T>): T {
        return decoder.decode(this.payload);
    }

    /**
     * Whether this packet indicates an error
     */
    get hasError(): boolean {
        return this.errorCode !== 0;
    }

    /**
     * Whether this is a push message (not a response)
     */
    get isPush(): boolean {
        return this.msgSeq === 0;
    }

    /**
     * Whether this packet has been disposed
     */
    get isDisposed(): boolean {
        return this._disposed;
    }

    /**
     * Releases resources associated with this packet
     */
    dispose(): void {
        if (this._disposed) {
            return;
        }
        this._disposed = true;
        // Payload is typically a view into a larger buffer in production
        // For now, no explicit cleanup needed for Uint8Array
    }
}

/**
 * Internal parsed packet with full metadata
 * @internal
 */
export class ParsedPacket extends Packet {
    constructor(
        msgId: string,
        msgSeq: number,
        stageId: bigint,
        errorCode: number,
        payload: Uint8Array,
        /** Original (uncompressed) size, >0 means compressed */
        public readonly originalSize: number = 0
    ) {
        super(msgId, msgSeq, stageId, errorCode, payload);
    }
}
