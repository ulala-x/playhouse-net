import { describe, it, expect } from 'vitest';
import { Packet } from '@playhouse/connector';
import { parseJson, tryParseJson, createJsonPacket } from '../src/json-packet-extensions.js';

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

describe('JSON Packet Extensions', () => {
    describe('parseJson', () => {
        it('should parse valid JSON payload', () => {
            const data: TestMessage = { id: 1, name: 'test', active: true };
            const json = JSON.stringify(data);
            const payload = new TextEncoder().encode(json);
            const packet = Packet.fromBytes('TestMessage', payload);

            const result = parseJson<TestMessage>(packet);

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
            const json = JSON.stringify(data);
            const payload = new TextEncoder().encode(json);
            const packet = Packet.fromBytes('ComplexMessage', payload);

            const result = parseJson<typeof data>(packet);

            expect(result).toEqual(data);
            expect(result.user.profile.settings.theme).toBe('dark');
            expect(result.items).toEqual([1, 2, 3]);
        });

        it('should throw error for empty payload', () => {
            const packet = Packet.empty('EmptyMessage');

            expect(() => parseJson<TestMessage>(packet)).toThrow('Cannot parse empty payload');
        });

        it('should throw error for invalid JSON', () => {
            const invalidJson = 'not valid json {';
            const payload = new TextEncoder().encode(invalidJson);
            const packet = Packet.fromBytes('InvalidMessage', payload);

            expect(() => parseJson<TestMessage>(packet)).toThrow('Failed to parse JSON');
        });

        it('should handle unicode characters', () => {
            const data = { message: '안녕하세요 🎮', emoji: '🚀' };
            const json = JSON.stringify(data);
            const payload = new TextEncoder().encode(json);
            const packet = Packet.fromBytes('UnicodeMessage', payload);

            const result = parseJson<typeof data>(packet);

            expect(result.message).toBe('안녕하세요 🎮');
            expect(result.emoji).toBe('🚀');
        });
    });

    describe('tryParseJson', () => {
        it('should return parsed object for valid JSON', () => {
            const data: ChatRequest = {
                roomId: 'room-123',
                message: 'Hello',
                timestamp: Date.now()
            };
            const json = JSON.stringify(data);
            const payload = new TextEncoder().encode(json);
            const packet = Packet.fromBytes('ChatRequest', payload);

            const result = tryParseJson<ChatRequest>(packet);

            expect(result).toBeDefined();
            expect(result?.roomId).toBe('room-123');
            expect(result?.message).toBe('Hello');
        });

        it('should return undefined for empty payload', () => {
            const packet = Packet.empty('EmptyMessage');

            const result = tryParseJson<TestMessage>(packet);

            expect(result).toBeUndefined();
        });

        it('should return undefined for invalid JSON', () => {
            const invalidJson = 'not valid json';
            const payload = new TextEncoder().encode(invalidJson);
            const packet = Packet.fromBytes('InvalidMessage', payload);

            const result = tryParseJson<TestMessage>(packet);

            expect(result).toBeUndefined();
        });

        it('should not throw on parse errors', () => {
            const packet = Packet.fromBytes('BadMessage', new Uint8Array([0xFF, 0xFE]));

            expect(() => tryParseJson<TestMessage>(packet)).not.toThrow();
            expect(tryParseJson<TestMessage>(packet)).toBeUndefined();
        });
    });

    describe('createJsonPacket', () => {
        it('should create packet with JSON payload', () => {
            const data: TestMessage = { id: 42, name: 'Alice', active: true };

            const packet = createJsonPacket(data, 'TestMessage');

            expect(packet.msgId).toBe('TestMessage');
            expect(packet.payload.length).toBeGreaterThan(0);

            const parsed = parseJson<TestMessage>(packet);
            expect(parsed).toEqual(data);
        });

        it('should use constructor name as default msgId', () => {
            class CustomMessage {
                constructor(public value: string) {}
            }
            const data = new CustomMessage('test');

            const packet = createJsonPacket(data);

            expect(packet.msgId).toBe('CustomMessage');
        });

        it('should throw error for plain objects without msgId', () => {
            const data = { key: 'value' };

            expect(() => createJsonPacket(data)).toThrow('Message ID must be provided');
        });

        it('should handle arrays', () => {
            const data = [1, 2, 3, 4, 5];

            const packet = createJsonPacket(data as any, 'NumberArray');

            expect(packet.msgId).toBe('NumberArray');
            const parsed = parseJson<number[]>(packet);
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

            const packet = createJsonPacket(data, 'ComplexData');
            const parsed = parseJson<typeof data>(packet);

            expect(parsed.str).toBe('text');
            expect(parsed.num).toBe(123);
            expect(parsed.bool).toBe(true);
            expect(parsed.nil).toBeNull();
            expect(parsed.arr).toEqual([1, 2, 3]);
            expect(parsed.obj.nested).toBe('value');
        });
    });

    describe('round-trip', () => {
        it('should correctly round-trip data', () => {
            const original: ChatRequest = {
                roomId: 'lobby-1',
                message: 'Test message',
                timestamp: 1234567890
            };

            const packet = createJsonPacket(original, 'ChatRequest');
            const parsed = parseJson<ChatRequest>(packet);

            expect(parsed).toEqual(original);
        });

        it('should handle multiple round-trips', () => {
            let data = { counter: 0, label: 'start' };

            for (let i = 0; i < 10; i++) {
                const packet = createJsonPacket(data, 'CounterMessage');
                data = parseJson<typeof data>(packet);
                data.counter++;
            }

            expect(data.counter).toBe(10);
            expect(data.label).toBe('start');
        });
    });
});
