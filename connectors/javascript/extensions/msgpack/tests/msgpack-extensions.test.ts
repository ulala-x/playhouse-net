import { describe, it, expect } from 'vitest';
import { Packet } from '@playhouse/connector';
import { encode } from '@msgpack/msgpack';
import { parseMsgPack, tryParseMsgPack, createMsgPackPacket } from '../src/msgpack-packet-extensions.js';

interface TestMessage {
    id: number;
    name: string;
    active: boolean;
}

interface ChatRequest {
    roomId: string;
    message: string;
    timestamp: number;
}

describe('MessagePack Packet Extensions', () => {
    describe('parseMsgPack', () => {
        it('should parse valid MessagePack payload', () => {
            const data: TestMessage = { id: 1, name: 'test', active: true };
            const payload = encode(data);
            const packet = Packet.fromBytes('TestMessage', payload);

            const result = parseMsgPack<TestMessage>(packet);

            expect(result).toEqual(data);
            expect(result.id).toBe(1);
            expect(result.name).toBe('test');
            expect(result.active).toBe(true);
        });

        it('should parse complex nested objects', () => {
            const data = {
                user: {
                    id: 123,
                    profile: {
                        name: 'Alice',
                        settings: {
                            theme: 'dark',
                            notifications: true
                        }
                    }
                },
                items: [1, 2, 3]
            };
            const payload = encode(data);
            const packet = Packet.fromBytes('ComplexMessage', payload);

            const result = parseMsgPack<typeof data>(packet);

            expect(result).toEqual(data);
            expect(result.user.profile.settings.theme).toBe('dark');
            expect(result.items).toEqual([1, 2, 3]);
        });

        it('should throw error for empty payload', () => {
            const packet = Packet.empty('EmptyMessage');

            expect(() => parseMsgPack<TestMessage>(packet)).toThrow('Cannot parse empty payload');
        });

        it('should throw error for invalid MessagePack', () => {
            const invalidData = new Uint8Array([0xFF, 0xFF, 0xFF, 0xFF]);
            const packet = Packet.fromBytes('InvalidMessage', invalidData);

            expect(() => parseMsgPack<TestMessage>(packet)).toThrow('Failed to parse MessagePack');
        });

        it('should handle unicode characters', () => {
            const data = { message: '안녕하세요 🎮', emoji: '🚀' };
            const payload = encode(data);
            const packet = Packet.fromBytes('UnicodeMessage', payload);

            const result = parseMsgPack<typeof data>(packet);

            expect(result.message).toBe('안녕하세요 🎮');
            expect(result.emoji).toBe('🚀');
        });

        it('should handle binary data', () => {
            const data = {
                id: 1,
                buffer: new Uint8Array([1, 2, 3, 4, 5])
            };
            const payload = encode(data);
            const packet = Packet.fromBytes('BinaryMessage', payload);

            const result = parseMsgPack<typeof data>(packet);

            expect(result.id).toBe(1);
            expect(result.buffer).toEqual(new Uint8Array([1, 2, 3, 4, 5]));
        });

        it('should handle large numbers', () => {
            const data = {
                small: 42,
                large: 9007199254740991, // MAX_SAFE_INTEGER
                negative: -9007199254740991,
                zero: 0
            };
            const payload = encode(data);
            const packet = Packet.fromBytes('NumberMessage', payload);

            const result = parseMsgPack<typeof data>(packet);

            expect(result.small).toBe(42);
            expect(result.large).toBe(9007199254740991);
            expect(result.negative).toBe(-9007199254740991);
            expect(result.zero).toBe(0);
        });
    });

    describe('tryParseMsgPack', () => {
        it('should return parsed object for valid MessagePack', () => {
            const data: ChatRequest = {
                roomId: 'room-123',
                message: 'Hello',
                timestamp: Date.now()
            };
            const payload = encode(data);
            const packet = Packet.fromBytes('ChatRequest', payload);

            const result = tryParseMsgPack<ChatRequest>(packet);

            expect(result).toBeDefined();
            expect(result?.roomId).toBe('room-123');
            expect(result?.message).toBe('Hello');
        });

        it('should return undefined for empty payload', () => {
            const packet = Packet.empty('EmptyMessage');

            const result = tryParseMsgPack<TestMessage>(packet);

            expect(result).toBeUndefined();
        });

        it('should return undefined for invalid MessagePack', () => {
            const invalidData = new Uint8Array([0xFF, 0xFF, 0xFF, 0xFF]);
            const packet = Packet.fromBytes('InvalidMessage', invalidData);

            const result = tryParseMsgPack<TestMessage>(packet);

            expect(result).toBeUndefined();
        });

        it('should not throw on parse errors', () => {
            const packet = Packet.fromBytes('BadMessage', new Uint8Array([0xFF, 0xFE]));

            expect(() => tryParseMsgPack<TestMessage>(packet)).not.toThrow();
            expect(tryParseMsgPack<TestMessage>(packet)).toBeUndefined();
        });
    });

    describe('createMsgPackPacket', () => {
        it('should create packet with MessagePack payload', () => {
            const data: TestMessage = { id: 42, name: 'Alice', active: true };

            const packet = createMsgPackPacket(data, 'TestMessage');

            expect(packet.msgId).toBe('TestMessage');
            expect(packet.payload.length).toBeGreaterThan(0);

            const parsed = parseMsgPack<TestMessage>(packet);
            expect(parsed).toEqual(data);
        });

        it('should use constructor name as default msgId', () => {
            class CustomMessage {
                constructor(public value: string) {}
            }
            const data = new CustomMessage('test');

            const packet = createMsgPackPacket(data);

            expect(packet.msgId).toBe('CustomMessage');
        });

        it('should throw error for plain objects without msgId', () => {
            const data = { key: 'value' };

            expect(() => createMsgPackPacket(data)).toThrow('Message ID must be provided');
        });

        it('should handle arrays', () => {
            const data = [1, 2, 3, 4, 5];

            const packet = createMsgPackPacket(data as any, 'NumberArray');

            expect(packet.msgId).toBe('NumberArray');
            const parsed = parseMsgPack<number[]>(packet);
            expect(parsed).toEqual(data);
        });

        it('should preserve data types', () => {
            const data = {
                str: 'text',
                num: 123,
                bool: true,
                nil: null,
                arr: [1, 2, 3],
                obj: { nested: 'value' }
            };

            const packet = createMsgPackPacket(data, 'ComplexData');
            const parsed = parseMsgPack<typeof data>(packet);

            expect(parsed.str).toBe('text');
            expect(parsed.num).toBe(123);
            expect(parsed.bool).toBe(true);
            expect(parsed.nil).toBeNull();
            expect(parsed.arr).toEqual([1, 2, 3]);
            expect(parsed.obj.nested).toBe('value');
        });

        it('should be more compact than JSON for certain data', () => {
            const data = {
                id: 12345,
                values: [1, 2, 3, 4, 5, 6, 7, 8, 9, 10]
            };

            const msgpackPacket = createMsgPackPacket(data, 'Data');
            const jsonPacket = Packet.create('Data', data);

            // MessagePack should generally be more compact for numeric data
            expect(msgpackPacket.payload.length).toBeLessThanOrEqual(jsonPacket.payload.length);
        });
    });

    describe('round-trip', () => {
        it('should correctly round-trip data', () => {
            const original: ChatRequest = {
                roomId: 'lobby-1',
                message: 'Test message',
                timestamp: 1234567890
            };

            const packet = createMsgPackPacket(original, 'ChatRequest');
            const parsed = parseMsgPack<ChatRequest>(packet);

            expect(parsed).toEqual(original);
        });

        it('should handle multiple round-trips', () => {
            let data = { counter: 0, label: 'start' };

            for (let i = 0; i < 10; i++) {
                const packet = createMsgPackPacket(data, 'CounterMessage');
                data = parseMsgPack<typeof data>(packet);
                data.counter++;
            }

            expect(data.counter).toBe(10);
            expect(data.label).toBe('start');
        });

        it('should handle Date objects as timestamps', () => {
            const data = {
                created: Date.now(),
                updated: Date.now() + 1000
            };

            const packet = createMsgPackPacket(data, 'TimestampData');
            const parsed = parseMsgPack<typeof data>(packet);

            expect(parsed.created).toBe(data.created);
            expect(parsed.updated).toBe(data.updated);
        });
    });
});
