/**
 * A-02: Large Payload Tests
 *
 * Tests for handling large payloads with LZ4 compression.
 * Server returns 1MB payloads with LZ4 compression applied at transport layer.
 */

import { describe, test, expect, beforeEach, afterEach } from 'vitest';
import { BaseIntegrationTest } from '../helpers/BaseIntegrationTest.js';
import { Packet } from '../../../src/packet.js';
import { serializeLargePayloadRequest, parseBenchmarkReply } from '../helpers/TestMessages.js';

describe('A-02: Large Payload', () => {
    let testContext: BaseIntegrationTest;

    beforeEach(async () => {
        testContext = new BaseIntegrationTest();
        await testContext['beforeEach']();
        // Use longer timeout for large payloads
        testContext['connector']!.init({
            requestTimeoutMs: 30000,  // 30 seconds
            heartbeatIntervalMs: 10000
        });
    });

    afterEach(async () => {
        await testContext['afterEach']();
    });

    test('A-02-01: Can receive 1MB payload', async () => {
        // Given: Connected and authenticated
        await testContext['createStageAndConnect']();
        await testContext['authenticate']('large-payload-user-1');

        // When: Request 1MB payload
        const payload = serializeLargePayloadRequest({ sizeBytes: 1048576 });
        const largePayloadRequest = Packet.fromBytes('LargePayloadRequest', payload);
        const response = await testContext['connector']!.request(largePayloadRequest);

        // Then: Should receive 1MB response
        expect(response).toBeDefined();
        expect(response.msgId).toBe('BenchmarkReply');
        expect(response.payload.length).toBeGreaterThan(0);

        // Parse the response
        const reply = parseBenchmarkReply(response.payload);
        expect(reply.payload).toBeDefined();
        // Server returns 1MB (1048576 bytes)
        expect(reply.payload.length).toBe(1048576);
    });

    test('A-02-02: Large payload data integrity is maintained', async () => {
        // Given: Connected and authenticated
        await testContext['createStageAndConnect']();
        await testContext['authenticate']('large-payload-user-2');

        // When: Request large payload
        const payload = serializeLargePayloadRequest({ sizeBytes: 1048576 });
        const largePayloadRequest = Packet.fromBytes('LargePayloadRequest', payload);
        const response = await testContext['connector']!.request(largePayloadRequest);
        const reply = parseBenchmarkReply(response.payload);

        // Then: Data should have sequential byte pattern
        const data = reply.payload;
        // Server fills with sequential byte pattern: i % 256
        for (let i = 0; i < Math.min(1000, data.length); i++) {
            expect(data[i]).toBe(i % 256);
        }
    });

    test('A-02-03: Sequential large payload requests are handled', async () => {
        // Given: Connected and authenticated
        await testContext['createStageAndConnect']();
        await testContext['authenticate']('large-payload-user-3');

        // When: Send 3 sequential large payload requests
        const results: number[] = [];

        for (let i = 0; i < 3; i++) {
            const payload = serializeLargePayloadRequest({ sizeBytes: 1048576 });
            const request = Packet.fromBytes('LargePayloadRequest', payload);
            const response = await testContext['connector']!.request(request);
            const reply = parseBenchmarkReply(response.payload);
            results.push(reply.payload.length);
        }

        // Then: All should return 1MB
        expect(results).toHaveLength(3);
        results.forEach(size => {
            expect(size).toBe(1048576);
        });
    });

    test('A-02-04: Can send large request payload', async () => {
        // Given: Connected and authenticated
        await testContext['createStageAndConnect']();
        await testContext['authenticate']('large-payload-user-4');

        // When: Send large echo request (100KB string)
        const largeContent = 'A'.repeat(100000);
        const echoReply = await testContext['echo'](largeContent, 1);

        // Then: Should receive same large content back
        expect(echoReply.content).toBe(largeContent);
    });

    test('A-02-05: Parallel large payload requests are handled', { timeout: 30000 }, async () => {
        // Given: Connected and authenticated
        await testContext['createStageAndConnect']();
        await testContext['authenticate']('large-payload-user-5');

        // When: Send 3 parallel large payload requests (512KB each)
        const requests = Array.from({ length: 3 }, () => {
            const payload = serializeLargePayloadRequest({ sizeBytes: 1048576 });
            const request = Packet.fromBytes('LargePayloadRequest', payload);
            return testContext['connector']!.request(request);
        });

        const responses = await Promise.all(requests);

        // Then: All should complete successfully
        expect(responses).toHaveLength(3);
        responses.forEach(response => {
            const reply = parseBenchmarkReply(response.payload);
            // Server always returns 1MB regardless of request size
            expect(reply.payload.length).toBe(1048576);
        });
    });
});
