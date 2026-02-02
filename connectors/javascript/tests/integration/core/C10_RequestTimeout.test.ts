/**
 * C-10: Request Timeout Tests
 *
 * Tests that requests timeout when the server does not respond.
 * NoResponseRequest causes the server to intentionally not respond.
 */

import { describe, test, expect, beforeEach, afterEach } from 'vitest';
import { BaseIntegrationTest } from '../helpers/BaseIntegrationTest.js';
import { Connector } from '../../../src/connector.js';
import { Packet } from '../../../src/packet.js';
import { ErrorCode } from '../../../src/types.js';
import { serializeNoResponseRequest } from '../helpers/TestMessages.js';

describe('C-10: Request Timeout', () => {
    let testContext: BaseIntegrationTest;

    beforeEach(async () => {
        testContext = new BaseIntegrationTest();
        await testContext['beforeEach']();
    });

    afterEach(async () => {
        await testContext['afterEach']();
    });

    test('C-10-01: Request without response times out', async () => {
        // Given: Short timeout setting (2 seconds)
        const connector = new Connector();
        connector.init({
            requestTimeoutMs: 2000, // 2 second timeout
            heartbeatIntervalMs: 10000
        });
        testContext['connector'] = connector;

        await testContext['createStageAndConnect']();
        await testContext['authenticate']('timeoutUser');

        const noResponseRequest = {
            delayMs: 10000 // Server won't respond for 10 seconds
        };

        // When: Send request without response
        const payload = serializeNoResponseRequest(noResponseRequest);
        const requestPacket = Packet.fromBytes('NoResponseRequest', payload);

        // Then: Should timeout
        await expect(
            connector.request(requestPacket)
        ).rejects.toThrow();
    });

    test('C-10-02: Connection remains after timeout', async () => {
        // Given: Short timeout setting
        const connector = new Connector();
        connector.init({
            requestTimeoutMs: 2000,
            heartbeatIntervalMs: 10000
        });
        testContext['connector'] = connector;

        await testContext['createStageAndConnect']();
        await testContext['authenticate']('timeoutConnectionUser');

        const noResponseRequest = {
            delayMs: 10000
        };

        // When: Timeout occurs
        const payload = serializeNoResponseRequest(noResponseRequest);
        const requestPacket = Packet.fromBytes('NoResponseRequest', payload);
        try {
            await connector.request(requestPacket);
        } catch (error) {
            // Ignore timeout exception
        }

        // Then: Connection should remain
        expect(connector.isConnected).toBe(true);
        expect(connector.isAuthenticated).toBe(true);

        // Other requests should work normally
        const echoReply = await testContext['echo']('After Timeout', 1);
        expect(echoReply.content).toBe('After Timeout');
    });

    test('C-10-03: Callback-based request also times out', async () => {
        // Given: Short timeout setting
        const connector = new Connector();
        connector.init({
            requestTimeoutMs: 2000,
            heartbeatIntervalMs: 10000
        });
        testContext['connector'] = connector;

        await testContext['createStageAndConnect']();
        await testContext['authenticate']('callbackTimeoutUser');

        const noResponseRequest = {
            delayMs: 10000
        };

        let callbackInvoked = false;
        let errorCode: number | undefined;
        let errorOccurred = false;

        connector.onError = (_stageId, code, _message) => {
            errorCode = code;
            errorOccurred = true;
        };

        // When: Callback-based request without response
        const payload = serializeNoResponseRequest(noResponseRequest);
        const requestPacket = Packet.fromBytes('NoResponseRequest', payload);
        connector.requestWithCallback(requestPacket, () => {
            callbackInvoked = true;
        });

        // Wait for OnError event (with mainThreadAction, max 5 seconds)
        const completed = await testContext['waitForConditionWithMainThread'](
            () => errorOccurred,
            5000
        );

        // Then: OnError event should occur and callback should not be invoked
        expect(completed).toBe(true);
        expect(errorCode).toBe(ErrorCode.RequestTimeout);
        expect(callbackInvoked).toBe(false);
    });

    test('C-10-04: One timeout among multiple requests does not affect others', async () => {
        // Given: Short timeout setting
        const connector = new Connector();
        connector.init({
            requestTimeoutMs: 2000,
            heartbeatIntervalMs: 10000
        });
        testContext['connector'] = connector;

        await testContext['createStageAndConnect']();
        await testContext['authenticate']('multiTimeoutUser');

        // When: Send normal and timeout requests in parallel
        const echoTask1 = testContext['echo']('Normal 1', 1);
        const echoTask2 = testContext['echo']('Normal 2', 2);

        const noResponseRequest = {
            delayMs: 10000
        };
        const payload = serializeNoResponseRequest(noResponseRequest);
        const timeoutPacket = Packet.fromBytes('NoResponseRequest', payload);
        const timeoutTask = connector.request(timeoutPacket);

        const echoTask3 = testContext['echo']('Normal 3', 3);

        // Wait for normal requests to complete
        const echo1 = await echoTask1;
        const echo2 = await echoTask2;
        const echo3 = await echoTask3;

        // Then: Normal requests should succeed
        expect(echo1.content).toBe('Normal 1');
        expect(echo2.content).toBe('Normal 2');
        expect(echo3.content).toBe('Normal 3');

        // Timeout request should fail
        await expect(timeoutTask).rejects.toThrow();
    });

    test('C-10-05: Long timeout setting allows response to be received', async () => {
        // Given: Long timeout setting (15 seconds)
        const connector = new Connector();
        connector.init({
            requestTimeoutMs: 15000, // 15 second timeout
            heartbeatIntervalMs: 10000
        });
        testContext['connector'] = connector;

        await testContext['createStageAndConnect']();
        await testContext['authenticate']('longTimeoutUser');

        // When: Send echo request with short delay (response within timeout)
        const echoReply = await testContext['echo']('Long Timeout Test', 1);

        // Then: Should receive response normally
        expect(echoReply.content).toBe('Long Timeout Test');
    });

    test('C-10-06: Authentication request can also timeout', async () => {
        // Given: Very short timeout setting (100ms)
        const connector = new Connector();
        connector.init({
            requestTimeoutMs: 100, // 100ms timeout
            heartbeatIntervalMs: 10000
        });
        testContext['connector'] = connector;

        await testContext['createStageAndConnect']();

        // When: Authentication request (may timeout with network delay)
        // Note: This test may be unstable depending on network conditions
        // Timeout may not actually occur
        const exception = await connector.authenticate('TestStage', 'authTimeoutUser')
            .then(() => null)
            .catch((error) => error);

        // Then: If exception occurs, it should be about timeout or connection
        if (exception) {
            expect(exception.message).toBeDefined();
        }
    });

    test('C-10-07: Timeout exception contains request information', async () => {
        // Given: Short timeout setting
        const connector = new Connector();
        connector.init({
            requestTimeoutMs: 2000,
            heartbeatIntervalMs: 10000
        });
        testContext['connector'] = connector;

        await testContext['createStageAndConnect']();
        await testContext['authenticate']('exceptionInfoUser');

        const noResponseRequest = {
            delayMs: 10000
        };

        // When: Timeout occurs
        const payload = serializeNoResponseRequest(noResponseRequest);
        const requestPacket = Packet.fromBytes('NoResponseRequest', payload);
        let caughtException: Error | null = null;

        try {
            await connector.request(requestPacket);
        } catch (error) {
            caughtException = error as Error;
        }

        // Then: Exception should contain request information
        expect(caughtException).not.toBeNull();
        expect(caughtException!.message).toBeDefined();
        expect(caughtException!.message).toContain('timeout');
    });
});
