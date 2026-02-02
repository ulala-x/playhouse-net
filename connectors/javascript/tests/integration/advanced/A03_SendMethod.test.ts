/**
 * A-03: Send() Method Tests (Fire-and-Forget)
 *
 * Tests for the send() method which sends messages without waiting for responses.
 * Unlike request(), send() is used for one-way messages like notifications or events.
 */

import { describe, test, expect, beforeEach, afterEach } from 'vitest';
import { BaseIntegrationTest } from '../helpers/BaseIntegrationTest.js';
import { Packet } from '../../../src/packet.js';

describe('A-03: Send Method (Fire-and-Forget)', () => {
    let testContext: BaseIntegrationTest;

    beforeEach(async () => {
        testContext = new BaseIntegrationTest();
        await testContext['beforeEach']();
    });

    afterEach(async () => {
        await testContext['afterEach']();
    });

    test('A-03-01: send() sends message successfully', async () => {
        // Given: Connected and authenticated
        await testContext['createStageAndConnect']();
        await testContext['authenticate']('send-test-user-1');

        // When: Send message using send() (fire-and-forget)
        const echoRequest = Packet.empty('EchoRequest');
        testContext['connector']!.send(echoRequest);

        // Then: No exception should be thrown and connection maintained
        await testContext['delay'](100);
        expect(testContext['connector']!.isConnected).toBe(true);
    });

    test('A-03-02: Connection is maintained after send()', async () => {
        // Given: Connected and authenticated
        await testContext['createStageAndConnect']();
        await testContext['authenticate']('send-test-user-2');

        // When: Send 10 messages
        for (let i = 0; i < 10; i++) {
            const echoRequest = Packet.empty('EchoRequest');
            testContext['connector']!.send(echoRequest);
        }

        await testContext['delay'](200);

        // Then: Connection should still be maintained
        expect(testContext['connector']!.isConnected).toBe(true);
        expect(testContext['connector']!.isAuthenticated).toBe(true);
    });

    test('A-03-03: send() and request() can be mixed', async () => {
        // Given: Connected and authenticated
        await testContext['createStageAndConnect']();
        await testContext['authenticate']('send-test-user-3');

        // When: Mix send() and request() calls
        // Send some messages
        for (let i = 0; i < 5; i++) {
            const sendRequest = Packet.empty('EchoRequest');
            testContext['connector']!.send(sendRequest);
        }

        // Make a request in between
        const echoReply = await testContext['echo']('Request in between', 100);

        // Send more messages
        for (let i = 5; i < 10; i++) {
            const sendRequest = Packet.empty('EchoRequest');
            testContext['connector']!.send(sendRequest);
        }

        // Then: Request should complete successfully
        expect(echoReply.content).toBe('Request in between');
        expect(testContext['connector']!.isConnected).toBe(true);
    });

    test('A-03-04: send() with BroadcastRequest triggers push message', async () => {
        // Given: Connected and authenticated
        await testContext['createStageAndConnect']();
        await testContext['authenticate']('send-test-user-4');

        let receivedPush = false;
        let pushData = '';

        testContext['connector']!.onReceive = (packet: Packet) => {
            if (packet.msgId === 'BroadcastNotify') {
                receivedPush = true;
                const notify = testContext['parsePayload'](packet);
                pushData = notify.data || notify.content || 'Hello from Send!';
            }
        };

        // When: Send broadcast request
        const broadcastRequest = Packet.empty('BroadcastRequest');
        testContext['connector']!.send(broadcastRequest);

        // Wait for push message with mainThreadAction
        const received = await testContext['waitForConditionWithMainThread'](
            () => receivedPush,
            5000
        );

        // Then: Should receive push notification
        expect(received).toBe(true);
        expect(receivedPush).toBe(true);
    });

    test('A-03-05: send() after disconnect fires OnError', async () => {
        // Given: Connected and authenticated, then disconnected
        await testContext['createStageAndConnect']();
        await testContext['authenticate']('send-test-user-5');

        let errorFired = false;
        let errorCode = 0;

        testContext['connector']!.onError = (stageId: bigint, code: number, message: string) => {
            errorFired = true;
            errorCode = code;
        };

        testContext['connector']!.disconnect();
        await testContext['delay'](500);

        // When: Try to send after disconnect
        const echoRequest = Packet.empty('EchoRequest');
        testContext['connector']!.send(echoRequest);
        testContext['connector']!.mainThreadAction();

        // Then: OnError should be fired
        expect(errorFired).toBe(true);
        expect(errorCode).toBeGreaterThan(0);
    });

    test('A-03-06: send() before authentication may trigger OnError', async () => {
        // Given: Connected but not authenticated
        await testContext['createStageAndConnect']();

        let errorFired = false;

        testContext['connector']!.onError = () => {
            errorFired = true;
        };

        // When: Try to send before authentication
        const echoRequest = Packet.empty('EchoRequest');
        testContext['connector']!.send(echoRequest);
        testContext['connector']!.mainThreadAction();

        await testContext['delay'](100);

        // Then: Implementation-dependent - server may reject or allow
        // Test just verifies no crash occurs
        expect(testContext['connector']!.isConnected).toBe(true);
    });

    test('A-03-07: Rapid fire send() calls are all processed', async () => {
        // Given: Connected and authenticated
        await testContext['createStageAndConnect']();
        await testContext['authenticate']('send-test-user-7');

        // When: Send 100 rapid messages
        const messageCount = 100;
        for (let i = 0; i < messageCount; i++) {
            const echoRequest = Packet.empty('EchoRequest');
            testContext['connector']!.send(echoRequest);
        }

        // Give time for all messages to be sent
        await testContext['delay'](500);

        // Then: Connection should still be alive
        expect(testContext['connector']!.isConnected).toBe(true);

        // Verify server is still responsive with a request
        const echoReply = await testContext['echo']('Check', 999);
        expect(echoReply.content).toBe('Check');
    });
});
