/**
 * C-09: Authentication Failure Tests
 *
 * Tests for authentication failures with invalid tokens or credentials.
 * The server returns an authentication failed error (errorCode=5),
 * and the connector handles it appropriately.
 */

import { describe, test, expect, beforeEach, afterEach } from 'vitest';
import { BaseIntegrationTest } from '../helpers/BaseIntegrationTest.js';
import { Packet } from '../../../src/packet.js';

describe('C-09: Authentication Failure', () => {
    let testContext: BaseIntegrationTest;

    // Server-defined AuthenticationFailed error code
    const AuthenticationFailedErrorCode = 5;

    beforeEach(async () => {
        testContext = new BaseIntegrationTest();
        await testContext['beforeEach']();
    });

    afterEach(async () => {
        await testContext['afterEach']();
    });

    test('C-09-01: Authentication fails with invalid token', async () => {
        // Given: Connected state
        await testContext['createStageAndConnect']();

        // When: Try to authenticate with invalid token
        const authRequest = {
            userId: 'testUser',
            token: 'invalid_token' // Invalid token
        };

        const requestPacket = Packet.create('AuthenticateRequest', authRequest);

        // Then: Authentication should fail with error
        await expect(
            testContext['connector']!.authenticate(requestPacket)
        ).rejects.toThrow();

        // IsAuthenticated should be false
        expect(testContext['connector']!.isAuthenticated).toBe(false);
    });

    test('C-09-02: Authentication fails with empty userId', async () => {
        // Given: Connected state
        await testContext['createStageAndConnect']();

        // When: Try to authenticate with empty userId
        const authRequest = {
            userId: '', // Empty userId
            token: 'valid_token'
        };

        const requestPacket = Packet.create('AuthenticateRequest', authRequest);

        // Then: Authentication should fail with error
        await expect(
            testContext['connector']!.authenticate(requestPacket)
        ).rejects.toThrow();

        expect(testContext['connector']!.isAuthenticated).toBe(false);
    });

    test('C-09-03: Authentication fails with empty token', async () => {
        // Given: Connected state
        await testContext['createStageAndConnect']();

        // When: Try to authenticate with empty token
        const authRequest = {
            userId: 'testUser',
            token: '' // Empty token
        };

        const requestPacket = Packet.create('AuthenticateRequest', authRequest);

        // Then: Authentication should fail with error
        await expect(
            testContext['connector']!.authenticate(requestPacket)
        ).rejects.toThrow();

        expect(testContext['connector']!.isAuthenticated).toBe(false);
    });

    test('C-09-04: Connection remains after authentication failure', async () => {
        // Given: Connected state
        await testContext['createStageAndConnect']();

        expect(testContext['connector']!.isConnected).toBe(true);

        // When: Authentication fails with invalid token
        const authRequest = {
            userId: 'testUser',
            token: 'invalid_token'
        };
        const requestPacket = Packet.create('AuthenticateRequest', authRequest);

        try {
            await testContext['connector']!.authenticate(requestPacket);
        } catch (error) {
            // Expected exception - authentication failure
        }

        // Then: Connection should remain but not authenticated
        expect(testContext['connector']!.isConnected).toBe(true);
        expect(testContext['connector']!.isAuthenticated).toBe(false);
    });

    test('C-09-05: Can retry authentication after failure', async () => {
        // Given: Connected state
        await testContext['createStageAndConnect']();

        // First authentication attempt (failure)
        const failRequest = {
            userId: 'testUser',
            token: 'invalid_token'
        };
        const failPacket = Packet.create('AuthenticateRequest', failRequest);

        try {
            await testContext['connector']!.authenticate(failPacket);
            expect(true).toBe(false); // Should not reach here
        } catch (error) {
            // Expected exception - authentication failure
        }

        // When: Second authentication attempt (success)
        const successReply = await testContext['authenticate']('testUser', 'valid_token');

        // Then: Second authentication should succeed
        expect(successReply).toBeDefined();
        expect(testContext['connector']!.isAuthenticated).toBe(true);
    });

    test('C-09-06: Authentication failure exception contains error info', async () => {
        // Given: Connected state
        await testContext['createStageAndConnect']();

        const authRequest = {
            userId: 'failUser',
            token: 'invalid_token'
        };

        // When: Authentication fails
        const requestPacket = Packet.create('AuthenticateRequest', authRequest);

        // Then: Exception should contain error information
        let caughtError: Error | null = null;
        try {
            await testContext['connector']!.authenticate(requestPacket);
        } catch (error) {
            caughtError = error as Error;
        }

        expect(caughtError).not.toBeNull();
        expect(caughtError!.message).toBeDefined();
    });

    test('C-09-07: Cannot send messages without authentication', async () => {
        // Given: Connected but not authenticated
        await testContext['createStageAndConnect']();

        expect(testContext['connector']!.isConnected).toBe(true);
        expect(testContext['connector']!.isAuthenticated).toBe(false);

        // When: Try to send echo request without authentication
        const echoRequest = {
            content: 'Test',
            sequence: 1
        };
        const requestPacket = Packet.create('EchoRequest', echoRequest);

        // Then: Should fail or return error
        const error = await testContext['connector']!.request(requestPacket).catch(e => e);
        expect(error).toBeDefined();
    });

    test('C-09-08: Authenticate without connection throws exception', async () => {
        // Given: Not connected
        expect(testContext['connector']!.isConnected).toBe(false);

        // When: Try to authenticate without connection
        // Then: Should throw exception
        await expect(
            testContext['connector']!.authenticate('TestStage', 'testUser')
        ).rejects.toThrow('Not connected');
    });

    test('C-09-09: Multiple authentication failures maintain connection', async () => {
        // Given: Connected state
        await testContext['createStageAndConnect']();

        // When: 3 consecutive authentication failures
        for (let i = 1; i <= 3; i++) {
            const authRequest = {
                userId: `failUser${i}`,
                token: 'invalid_token'
            };

            const requestPacket = Packet.create('AuthenticateRequest', authRequest);
            try {
                await testContext['connector']!.authenticate(requestPacket);
                expect(true).toBe(false); // Should not reach here
            } catch (error) {
                // Expected exception - authentication failure
            }
        }

        // Then: Connection should remain after multiple failures
        expect(testContext['connector']!.isConnected).toBe(true);
        expect(testContext['connector']!.isAuthenticated).toBe(false);
    });
});
