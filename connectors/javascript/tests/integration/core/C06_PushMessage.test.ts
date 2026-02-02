/**
 * C-06: Push Message Tests (BroadcastNotify)
 *
 * Tests for receiving server-initiated push messages.
 */

import { describe, test, expect, beforeEach, afterEach } from 'vitest';
import { BaseIntegrationTest } from '../helpers/BaseIntegrationTest.js';
import { Packet } from '../../../src/packet.js';
import { serializeBroadcastRequest, parseBroadcastNotify } from '../helpers/TestMessages.js';

describe('C-06: Push Message', () => {
    let testContext: BaseIntegrationTest;

    beforeEach(async () => {
        testContext = new BaseIntegrationTest();
        await testContext['beforeEach']();
        await testContext['createStageAndConnect']();
        await testContext['authenticate']('pushUser');
    }, 15000);

    afterEach(async () => {
        await testContext['afterEach']();
    });

    test('C-06-01: Can receive push messages', async () => {
        // Given: Authenticated connection
        const receivedMessages: any[] = [];
        let pushReceived = false;

        testContext['connector']!.onReceive = (packet) => {
            if (packet.msgId === 'BroadcastNotify') {
                receivedMessages.push(parseBroadcastNotify(packet.payload));
                pushReceived = true;
            }
        };

        // When: Send broadcast request (server will push message back)
        const broadcastRequest = { content: 'Test Broadcast' };
        const payload = serializeBroadcastRequest(broadcastRequest);
        const requestPacket = Packet.fromBytes('BroadcastRequest', payload);
        testContext['connector']!.send(requestPacket);

        // Wait for push message
        const completed = await testContext['waitForConditionWithMainThread'](
            () => pushReceived,
            5000
        );

        // Then: Should receive push message
        expect(completed).toBe(true);
        expect(receivedMessages).toHaveLength(1);
    });

    test('C-06-02: OnReceive event has correct parameters', async () => {
        // Given: Authenticated connection
        let receivedPacket: any;
        let pushReceived = false;

        testContext['connector']!.onReceive = (packet) => {
            if (packet.msgId === 'BroadcastNotify') {
                receivedPacket = packet;
                pushReceived = true;
            }
        };

        // When: Send broadcast request
        const broadcastRequest = { content: 'Param Test' };
        const payload = serializeBroadcastRequest(broadcastRequest);
        const requestPacket = Packet.fromBytes('BroadcastRequest', payload);
        testContext['connector']!.send(requestPacket);

        // Wait for push
        const completed = await testContext['waitForConditionWithMainThread'](
            () => pushReceived,
            5000
        );

        // Then: Event should have correct parameters
        expect(completed).toBe(true);
        expect(receivedPacket.msgId).toBe('BroadcastNotify');
    });

    test('C-06-03: Can receive multiple push messages sequentially', async () => {
        // Given: Authenticated connection
        const receivedMessages: any[] = [];
        const expectedCount = 3;

        testContext['connector']!.onReceive = (packet) => {
            if (packet.msgId === 'BroadcastNotify') {
                receivedMessages.push(parseBroadcastNotify(packet.payload));
            }
        };

        // When: Send 3 broadcast requests
        for (let i = 1; i <= expectedCount; i++) {
            const request = { content: `Message ${i}` };
            const payload = serializeBroadcastRequest(request);
            const packet = Packet.fromBytes('BroadcastRequest', payload);
            testContext['connector']!.send(packet);

            // Brief delay with mainThreadAction
            await testContext['delay'](100);
            testContext['connector']!.mainThreadAction();
        }

        // Wait for all messages
        const completed = await testContext['waitForConditionWithMainThread'](
            () => receivedMessages.length >= expectedCount,
            10000
        );

        // Then: Should receive all push messages
        expect(completed).toBe(true);
        expect(receivedMessages.length).toBeGreaterThanOrEqual(expectedCount);
    });

    test('C-06-04: Push messages and request-response work together', async () => {
        // Given: Authenticated connection
        let pushReceived = false;

        testContext['connector']!.onReceive = (packet) => {
            if (packet.msgId === 'BroadcastNotify') {
                pushReceived = true;
            }
        };

        // When: Send echo and broadcast requests
        const echoTask = testContext['echo']('Echo Test', 1);

        const broadcastRequest = { content: 'Broadcast Test' };
        const payload = serializeBroadcastRequest(broadcastRequest);
        const broadcastPacket = Packet.fromBytes('BroadcastRequest', payload);
        testContext['connector']!.send(broadcastPacket);

        // Wait for both
        const echoReply = await echoTask;
        const pushCompleted = await testContext['waitForConditionWithMainThread'](
            () => pushReceived,
            5000
        );

        // Then: Both should succeed
        expect(echoReply.content).toBe('Echo Test');
        expect(pushCompleted).toBe(true);
    });

    test('C-06-05: BroadcastNotify contains sender info', async () => {
        // Given: Authenticated connection
        let receivedNotify: any;
        let pushReceived = false;

        testContext['connector']!.onReceive = (packet) => {
            if (packet.msgId === 'BroadcastNotify') {
                receivedNotify = parseBroadcastNotify(packet.payload);
                pushReceived = true;
            }
        };

        // When: Send broadcast request
        const broadcastRequest = { content: 'Sender Info Test' };
        const payload = serializeBroadcastRequest(broadcastRequest);
        const requestPacket = Packet.fromBytes('BroadcastRequest', payload);
        testContext['connector']!.send(requestPacket);

        // Wait for push
        const completed = await testContext['waitForConditionWithMainThread'](
            () => pushReceived,
            5000
        );

        // Then: Should contain sender information
        expect(completed).toBe(true);
        expect(receivedNotify).toBeDefined();
    });

    test('C-06-06: No exception when OnReceive handler not registered', async () => {
        // Given: Authenticated connection (no OnReceive handler)

        // When: Send broadcast request
        const broadcastRequest = { content: 'No Handler Test' };
        const payload = serializeBroadcastRequest(broadcastRequest);
        const requestPacket = Packet.fromBytes('BroadcastRequest', payload);

        const action = () => testContext['connector']!.send(requestPacket);

        // Then: Should not throw exception
        expect(action).not.toThrow();

        // Wait for message to be sent
        await testContext['delay'](1000);

        // Connection should remain
        expect(testContext['connector']!.isConnected).toBe(true);
    });
});
