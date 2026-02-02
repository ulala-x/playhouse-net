/**
 * C-05: Echo Request-Response Tests
 *
 * Tests for basic request-response pattern with Echo messages.
 */

import { describe, test, expect, beforeEach, afterEach } from 'vitest';
import { BaseIntegrationTest } from '../helpers/BaseIntegrationTest.js';
import { Packet } from '../../../src/packet.js';
import { serializeEchoRequest, parseEchoReply } from '../helpers/TestMessages.js';

describe('C-05: Echo Request-Response', () => {
    let testContext: BaseIntegrationTest;

    beforeEach(async () => {
        testContext = new BaseIntegrationTest();
        await testContext['beforeEach']();
        await testContext['createStageAndConnect']();
        await testContext['authenticate']('echoUser');
    });

    afterEach(async () => {
        await testContext['afterEach']();
    });

    test('C-05-01: Echo request-response works correctly', async () => {
        // Given: Authenticated connection
        const testContent = 'Hello PlayHouse!';
        const testSequence = 42;

        // When: Send echo request
        const echoReply = await testContext['echo'](testContent, testSequence);

        // Then: Response should match request
        expect(echoReply).toBeDefined();
        expect(echoReply.content).toBe(testContent);
        expect(echoReply.sequence).toBe(testSequence);
    });

    test('C-05-02: RequestAsync can send echo requests', async () => {
        // Given: Authenticated connection
        const echoRequest = {
            content: 'Async Echo Test',
            sequence: 1
        };

        // When: Call requestAsync
        const payload = serializeEchoRequest(echoRequest);
        const requestPacket = Packet.fromBytes('EchoRequest', payload);
        const responsePacket = await testContext['connector']!.request(requestPacket);

        // Then: Response should be correct
        expect(responsePacket).toBeDefined();
        expect(responsePacket.msgId).toBe('EchoReply');

        const echoReply = parseEchoReply(responsePacket.payload);
        expect(echoReply.content).toBe('Async Echo Test');
        expect(echoReply.sequence).toBe(1);
    });

    test('C-05-03: Request with callback receives echo reply', async () => {
        // Given: Authenticated connection
        const echoRequest = {
            content: 'Callback Echo Test',
            sequence: 100
        };

        let echoReply: any;
        let callbackInvoked = false;

        // When: Request with callback
        const payload = serializeEchoRequest(echoRequest);
        const requestPacket = Packet.fromBytes('EchoRequest', payload);
        testContext['connector']!.requestWithCallback(requestPacket, (responsePacket) => {
            echoReply = parseEchoReply(responsePacket.payload);
            callbackInvoked = true;
        });

        // Wait for callback
        const completed = await testContext['waitForConditionWithMainThread'](
            () => callbackInvoked,
            5000
        );

        // Then: Callback should be invoked with correct response
        expect(completed).toBe(true);
        expect(echoReply.content).toBe('Callback Echo Test');
        expect(echoReply.sequence).toBe(100);
    });

    test('C-05-04: Sequential echo requests all succeed', async () => {
        // When: Send 5 sequential echo requests
        const replies = [];
        for (let i = 1; i <= 5; i++) {
            const reply = await testContext['echo'](`Message ${i}`, i);
            replies.push(reply);
        }

        // Then: All responses should be correct in order
        expect(replies).toHaveLength(5);
        for (let i = 0; i < 5; i++) {
            expect(replies[i].content).toBe(`Message ${i + 1}`);
            expect(replies[i].sequence).toBe(i + 1);
        }
    });

    test('C-05-05: Parallel echo requests all succeed', async () => {
        // When: Send 10 parallel echo requests
        const tasks = [];
        for (let i = 1; i <= 10; i++) {
            tasks.push(testContext['echo'](`Parallel ${i}`, i));
        }

        const replies = await Promise.all(tasks);

        // Then: All responses should be correct
        expect(replies).toHaveLength(10);
        for (let i = 0; i < 10; i++) {
            const reply = replies.find(r => r.sequence === i + 1);
            expect(reply).toBeDefined();
            expect(reply!.content).toBe(`Parallel ${i + 1}`);
        }
    });

    test('C-05-06: Can echo empty string', async () => {
        // When: Echo empty string
        const echoReply = await testContext['echo']('', 999);

        // Then: Empty string should be echoed
        expect(echoReply.content).toBe('');
        expect(echoReply.sequence).toBe(999);
    });

    test('C-05-07: Can echo long string', async () => {
        // Given: 1KB string
        const longContent = 'A'.repeat(1024);

        // When: Echo long string
        const echoReply = await testContext['echo'](longContent, 1);

        // Then: Full string should be echoed
        expect(echoReply.content).toBe(longContent);
        expect(echoReply.content.length).toBe(1024);
    });

    test('C-05-08: Can echo unicode string', async () => {
        // Given: Unicode content
        const unicodeContent = '안녕하세요 🎮 こんにちは 你好';

        // When: Echo unicode string
        const echoReply = await testContext['echo'](unicodeContent, 1);

        // Then: Unicode string should be echoed correctly
        expect(echoReply.content).toBe(unicodeContent);
    });

    test('C-05-09: Response message type is correct', async () => {
        // Given: Echo request
        const echoRequest = {
            content: 'Type Check',
            sequence: 1
        };

        // When: Send echo request
        const payload = serializeEchoRequest(echoRequest);
        const requestPacket = Packet.fromBytes('EchoRequest', payload);
        const responsePacket = await testContext['connector']!.request(requestPacket);

        // Then: Response message type should be EchoReply
        expect(responsePacket.msgId).toBe('EchoReply');
    });
});
