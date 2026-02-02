/**
 * C-03: Authentication Success Tests
 *
 * Tests for successful authentication scenarios.
 */

import { describe, test, expect, beforeEach, afterEach } from 'vitest';
import { BaseIntegrationTest } from '../helpers/BaseIntegrationTest.js';
import { Connector } from '../../../src/connector.js';
import { Packet } from '../../../src/packet.js';

describe('C-03: Authentication Success', () => {
    let testContext: BaseIntegrationTest;

    beforeEach(async () => {
        testContext = new BaseIntegrationTest();
        await testContext['beforeEach']();
    });

    afterEach(async () => {
        await testContext['afterEach']();
    });

    test('C-03-01: Authentication succeeds with valid token', async () => {
        // Given: Connected state
        await testContext['createStageAndConnect']();

        // When: Authenticate with valid token
        const authReply = await testContext['authenticate']('user123', 'valid_token');

        // Then: Authentication should succeed with correct response
        expect(authReply).toBeDefined();
        expect(testContext['connector']!.isAuthenticated).toBe(true);
    });

    test('C-03-02: Can authenticate using authenticateAsync', async () => {
        // Given: Connected state
        await testContext['createStageAndConnect']();

        const authRequest = {
            userId: 'testUser',
            token: 'valid_token'
        };

        // When: Call authenticateAsync
        const requestPacket = Packet.create('AuthenticateRequest', authRequest);
        const responsePacket = await testContext['connector']!.authenticate(requestPacket);

        // Then: Response packet should be returned correctly
        expect(responsePacket).toBeDefined();
        expect(responsePacket.msgId).toBe('AuthenticateReply');
    });

    test('C-03-03: Can authenticate with callback-based method', async () => {
        // Given: Connected state
        await testContext['createStageAndConnect']();

        const authRequest = {
            userId: 'callbackUser',
            token: 'valid_token'
        };

        let authReply: any;
        let callbackInvoked = false;

        // When: Authenticate with callback using requestWithCallback
        const requestPacket = Packet.create('AuthenticateRequest', authRequest);
        testContext['connector']!.requestWithCallback(requestPacket, (responsePacket) => {
            authReply = testContext['parsePayload'](responsePacket);
            callbackInvoked = true;
        });

        // Wait for callback with mainThreadAction
        const completed = await testContext['waitForConditionWithMainThread'](
            () => callbackInvoked,
            5000
        );

        // Then: Callback should be invoked with success response
        expect(completed).toBe(true);
        expect(callbackInvoked).toBe(true);
        expect(authReply).toBeDefined();
    });

    test('C-03-04: Can authenticate with metadata', async () => {
        // Given: Connected state
        await testContext['createStageAndConnect']();

        const authRequest = {
            userId: 'metaUser',
            token: 'valid_token',
            metadata: {
                client_version: '1.0.0',
                platform: 'javascript'
            }
        };

        // When: Authenticate with metadata
        const requestPacket = Packet.create('AuthenticateRequest', authRequest);
        const responsePacket = await testContext['connector']!.authenticate(requestPacket);

        // Then: Authentication should succeed even with metadata
        expect(responsePacket).toBeDefined();
        expect(testContext['connector']!.isAuthenticated).toBe(true);
    });

    test('C-03-05: Account ID is assigned after successful authentication', async () => {
        // Given: Connected state
        await testContext['createStageAndConnect']();

        // When: Authenticate successfully
        const authReply = await testContext['authenticate']('user_with_account_id');

        // Then: Account ID should be assigned
        expect(authReply.accountId).toBeDefined();
        expect(authReply.accountId).not.toBe('0');
    });

    test('C-03-06: Multiple users can authenticate simultaneously', async () => {
        // Given: 3 stages and connectors
        const stage1 = await testContext['testServer'].createTestStage();
        const stage2 = await testContext['testServer'].createTestStage();
        const stage3 = await testContext['testServer'].createTestStage();

        const connector1 = new Connector();
        const connector2 = new Connector();
        const connector3 = new Connector();

        try {
            connector1.init();
            connector2.init();
            connector3.init();

            const wsUrl = testContext['testServer'].wsUrl;

            // When: 3 connectors connect and authenticate
            await connector1.connect(wsUrl, stage1.stageId, stage1.stageType);
            await connector2.connect(wsUrl, stage2.stageId, stage2.stageType);
            await connector3.connect(wsUrl, stage3.stageId, stage3.stageType);

            const auth1Packet = Packet.create('AuthenticateRequest', { userId: 'user1', token: 'valid_token' });
            const auth2Packet = Packet.create('AuthenticateRequest', { userId: 'user2', token: 'valid_token' });
            const auth3Packet = Packet.create('AuthenticateRequest', { userId: 'user3', token: 'valid_token' });

            await Promise.all([
                connector1.authenticate(auth1Packet),
                connector2.authenticate(auth2Packet),
                connector3.authenticate(auth3Packet)
            ]);

            // Then: All authentications should succeed
            expect(connector1.isAuthenticated).toBe(true);
            expect(connector2.isAuthenticated).toBe(true);
            expect(connector3.isAuthenticated).toBe(true);
        } finally {
            connector1.disconnect();
            connector2.disconnect();
            connector3.disconnect();
        }
    });
});
