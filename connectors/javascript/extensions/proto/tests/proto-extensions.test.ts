import { describe, it, expect } from 'vitest';
import { Packet } from '@playhouse/connector';
import protobuf from 'protobufjs';
import {
    parseProto,
    tryParseProto,
    createProtoPacket,
    createProtoPacketFromMessage
} from '../src/proto-packet-extensions.js';

// Define test message schemas
const TestMessageProto = `
syntax = "proto3";

message TestMessage {
    int32 id = 1;
    string name = 2;
    bool active = 3;
}

message ChatRequest {
    string roomId = 1;
    string message = 2;
    int64 timestamp = 3;
}

message ComplexMessage {
    message User {
        int32 id = 1;
        string name = 2;
    }
    User user = 1;
    repeated int32 items = 2;
}

message EmptyMessage {
}
`;

describe('Protobuf Packet Extensions', () => {
    let root: protobuf.Root;
    let TestMessage: protobuf.Type;
    let ChatRequest: protobuf.Type;
    let ComplexMessage: protobuf.Type;
    let EmptyMessage: protobuf.Type;

    // Setup protobuf types before tests
    beforeAll(async () => {
        root = protobuf.parse(TestMessageProto).root;
        TestMessage = root.lookupType('TestMessage');
        ChatRequest = root.lookupType('ChatRequest');
        ComplexMessage = root.lookupType('ComplexMessage');
        EmptyMessage = root.lookupType('EmptyMessage');
    });

    describe('parseProto', () => {
        it('should parse valid Protobuf payload', () => {
            const data = { id: 1, name: 'test', active: true };
            const message = TestMessage.create(data);
            const payload = TestMessage.encode(message).finish();
            const packet = Packet.fromBytes('TestMessage', payload);

            const result = parseProto(packet, TestMessage);

            expect(result).toMatchObject(data);
            expect((result as any).id).toBe(1);
            expect((result as any).name).toBe('test');
            expect((result as any).active).toBe(true);
        });

        it('should parse complex nested messages', () => {
            const data = {
                user: {
                    id: 123,
                    name: 'Alice'
                },
                items: [1, 2, 3]
            };
            const message = ComplexMessage.create(data);
            const payload = ComplexMessage.encode(message).finish();
            const packet = Packet.fromBytes('ComplexMessage', payload);

            const result = parseProto(packet, ComplexMessage);

            expect((result as any).user.id).toBe(123);
            expect((result as any).user.name).toBe('Alice');
            expect((result as any).items).toEqual([1, 2, 3]);
        });

        it('should handle zero-length payload as empty message', () => {
            const packet = Packet.empty('EmptyMessage');

            // Protobuf can decode empty payload to message with default values
            const result = parseProto(packet, TestMessage);

            expect(result).toBeDefined();
            expect((result as any).id).toBe(0);
            expect((result as any).name).toBe('');
            expect((result as any).active).toBe(false);
        });

        it('should throw error for invalid Protobuf', () => {
            const invalidData = new Uint8Array([0xFF, 0xFF, 0xFF, 0xFF]);
            const packet = Packet.fromBytes('InvalidMessage', invalidData);

            expect(() => parseProto(packet, TestMessage)).toThrow('Failed to parse Protobuf');
        });

        it('should handle empty messages', () => {
            const message = EmptyMessage.create({});
            const payload = EmptyMessage.encode(message).finish();
            const packet = Packet.fromBytes('EmptyMessage', payload);

            // Empty protobuf messages still have a valid encoding (though it may be zero-length)
            const result = parseProto(packet, EmptyMessage);

            expect(result).toBeDefined();
        });

        it('should handle messages with default values', () => {
            const data = { id: 0, name: '', active: false };
            const message = TestMessage.create(data);
            const payload = TestMessage.encode(message).finish();
            const packet = Packet.fromBytes('TestMessage', payload);

            const result = parseProto(packet, TestMessage);

            expect((result as any).id).toBe(0);
            expect((result as any).name).toBe('');
            expect((result as any).active).toBe(false);
        });
    });

    describe('tryParseProto', () => {
        it('should return parsed message for valid Protobuf', () => {
            const data = { roomId: 'room-123', message: 'Hello', timestamp: 1234567890 };
            const msg = ChatRequest.create(data);
            const payload = ChatRequest.encode(msg).finish();
            const packet = Packet.fromBytes('ChatRequest', payload);

            const result = tryParseProto(packet, ChatRequest);

            expect(result).toBeDefined();
            expect((result as any).roomId).toBe('room-123');
            expect((result as any).message).toBe('Hello');
        });

        it('should handle zero-length payload as empty message', () => {
            const packet = Packet.empty('EmptyMessage');

            const result = tryParseProto(packet, TestMessage);

            // Protobuf can decode empty payload to message with default values
            expect(result).toBeDefined();
            expect((result as any).id).toBe(0);
        });

        it('should return undefined for invalid Protobuf', () => {
            const invalidData = new Uint8Array([0xFF, 0xFF, 0xFF, 0xFF]);
            const packet = Packet.fromBytes('InvalidMessage', invalidData);

            const result = tryParseProto(packet, TestMessage);

            expect(result).toBeUndefined();
        });

        it('should not throw on parse errors', () => {
            const packet = Packet.fromBytes('BadMessage', new Uint8Array([0xFF, 0xFE]));

            expect(() => tryParseProto(packet, TestMessage)).not.toThrow();
            expect(tryParseProto(packet, TestMessage)).toBeUndefined();
        });
    });

    describe('createProtoPacket', () => {
        it('should create packet with Protobuf payload', () => {
            const data = { id: 42, name: 'Alice', active: true };

            const packet = createProtoPacket(data, TestMessage, 'TestMessage');

            expect(packet.msgId).toBe('TestMessage');
            expect(packet.payload.length).toBeGreaterThan(0);

            const parsed = parseProto(packet, TestMessage);
            expect(parsed).toMatchObject(data);
        });

        it('should use type name as default msgId', () => {
            const data = { id: 1, name: 'test', active: false };

            const packet = createProtoPacket(data, TestMessage);

            expect(packet.msgId).toBe('TestMessage');
        });

        it('should throw error for invalid message data', () => {
            const invalidData = { id: 'not a number', name: 123, active: 'not a boolean' };

            expect(() => createProtoPacket(invalidData, TestMessage)).toThrow('Protobuf verification failed');
        });

        it('should handle nested messages', () => {
            const data = {
                user: {
                    id: 999,
                    name: 'Bob'
                },
                items: [10, 20, 30]
            };

            const packet = createProtoPacket(data, ComplexMessage);
            const parsed = parseProto(packet, ComplexMessage);

            expect((parsed as any).user.id).toBe(999);
            expect((parsed as any).user.name).toBe('Bob');
            expect((parsed as any).items).toEqual([10, 20, 30]);
        });

        it('should handle empty messages', () => {
            const packet = createProtoPacket({}, EmptyMessage);

            expect(packet.msgId).toBe('EmptyMessage');
            // Empty messages may have zero-length payload
            expect(packet.payload.length).toBeGreaterThanOrEqual(0);

            const parsed = parseProto(packet, EmptyMessage);
            expect(parsed).toBeDefined();
        });
    });

    describe('createProtoPacketFromMessage', () => {
        it('should create packet from protobuf message instance', () => {
            const message = TestMessage.create({ id: 1, name: 'test', active: true });

            const packet = createProtoPacketFromMessage(message as any);

            expect(packet.msgId).toBe('TestMessage');
            expect(packet.payload.length).toBeGreaterThan(0);
        });

        it('should use custom msgId when provided', () => {
            const message = TestMessage.create({ id: 1, name: 'test', active: true });

            const packet = createProtoPacketFromMessage(message as any, 'CustomId');

            expect(packet.msgId).toBe('CustomId');
        });

        it('should throw error for message without $type', () => {
            const plainObject = { id: 1, name: 'test' };

            expect(() => createProtoPacketFromMessage(plainObject as any)).toThrow('must have $type property');
        });
    });

    describe('round-trip', () => {
        it('should correctly round-trip data', () => {
            const original = { roomId: 'lobby-1', message: 'Test message', timestamp: 1234567890 };

            const packet = createProtoPacket(original, ChatRequest, 'ChatRequest');
            const parsed = parseProto(packet, ChatRequest);

            expect((parsed as any).roomId).toBe(original.roomId);
            expect((parsed as any).message).toBe(original.message);
            // Protobuf int64 fields are returned as Long objects
            expect(Number((parsed as any).timestamp)).toBe(original.timestamp);
        });

        it('should handle multiple round-trips', () => {
            let data = { id: 0, name: 'start', active: true };

            for (let i = 0; i < 10; i++) {
                const packet = createProtoPacket(data, TestMessage, 'TestMessage');
                const parsed = parseProto(packet, TestMessage) as any;
                data = { id: parsed.id + 1, name: parsed.name, active: parsed.active };
            }

            expect(data.id).toBe(10);
            expect(data.name).toBe('start');
        });

        it('should preserve binary data integrity', () => {
            const data = { id: 12345, name: 'test-name', active: true };
            const packet1 = createProtoPacket(data, TestMessage);
            const packet2 = createProtoPacket(data, TestMessage);

            // Same data should produce same binary output
            expect(packet1.payload).toEqual(packet2.payload);
        });
    });

    describe('comparison with other formats', () => {
        it('should be more compact than JSON for structured data', () => {
            const data = {
                user: {
                    id: 123,
                    name: 'Alice'
                },
                items: [1, 2, 3, 4, 5, 6, 7, 8, 9, 10]
            };

            const protoPacket = createProtoPacket(data, ComplexMessage);
            const jsonPacket = Packet.create('ComplexMessage', data);

            // Protobuf should generally be more compact
            expect(protoPacket.payload.length).toBeLessThan(jsonPacket.payload.length);
        });
    });
});
