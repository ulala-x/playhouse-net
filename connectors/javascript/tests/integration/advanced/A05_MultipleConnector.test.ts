/**
 * A-05: Multiple Connector Tests
 *
 * Tests for using multiple Connector instances simultaneously.
 * Verifies independent connections and communication.
 * Useful for multi-server connections or simulating multiple clients in tests.
 */

import { describe, test, expect, beforeEach, afterEach } from 'vitest';
import { Connector } from '../../../src/connector.js';
import { Packet } from '../../../src/packet.js';
import { TestServerClient, CreateStageResponse } from '../helpers/TestServerClient.js';

describe('A-05: Multiple Connectors', () => {
    const testServer = new TestServerClient();
    const connectors: Connector[] = [];
    const stages: CreateStageResponse[] = [];

    beforeEach(async () => {
        // Create 5 independent stages
        for (let i = 0; i < 5; i++) {
            stages.push(await testServer.createStage());
        }
    });

    afterEach(async () => {
        // Cleanup all connectors
        for (const connector of connectors) {
            if (connector.isConnected) {
                connector.disconnect();
                await delay(50);
            }
        }
        connectors.length = 0;
        stages.length = 0;
    });

    function createConnector(requestTimeoutMs = 10000): Connector {
        const connector = new Connector();
        connector.init({
            requestTimeoutMs,
            heartbeatIntervalMs: 10000
        });
        connectors.push(connector);
        return connector;
    }

    async function delay(ms: number): Promise<void> {
        return new Promise(resolve => setTimeout(resolve, ms));
    }

    function parsePayload(packet: Packet): any {
        try {
            if (packet.payload && packet.payload.length > 0) {
                return JSON.parse(new TextDecoder().decode(packet.payload));
            }
        } catch (e) {
            // If not JSON, return empty object
        }
        return {};
    }

    test('A-05-01: Multiple connectors can connect simultaneously', async () => {
        // Given: 5 connectors
        const testConnectors = Array.from({ length: 5 }, () => createConnector());

        // When: Connect all simultaneously
        const connectPromises = testConnectors.map((connector, index) =>
            connector.connect(testServer.wsUrl, stages[index].stageId, stages[index].stageType)
        );

        const results = await Promise.all(connectPromises);

        // Then: All connections should succeed
        expect(results.every(r => r === true)).toBe(true);
        expect(testConnectors.every(c => c.isConnected)).toBe(true);
    });

    test('A-05-02: Multiple connectors can authenticate independently', async () => {
        // Given: 3 connected connectors
        const testConnectors = Array.from({ length: 3 }, () => createConnector());

        for (let i = 0; i < testConnectors.length; i++) {
            await testConnectors[i].connect(
                testServer.wsUrl,
                stages[i].stageId,
                stages[i].stageType
            );
        }

        // When: Authenticate each with different user
        const authPromises = testConnectors.map(async (connector, index) => {
            const authRequest = Packet.empty('AuthenticateRequest');
            return await connector.authenticate(authRequest);
        });

        const authResults = await Promise.all(authPromises);

        // Then: All authentications should succeed
        expect(testConnectors.every(c => c.isAuthenticated)).toBe(true);
        authResults.forEach((result, index) => {
            expect(result).toBeDefined();
            expect(result.msgId).toBe('AuthenticateReply');
        });
    });

    test('A-05-03: Multiple connectors can send requests simultaneously', async () => {
        // Given: 3 connected and authenticated connectors
        const testConnectors = Array.from({ length: 3 }, () => createConnector());

        for (let i = 0; i < testConnectors.length; i++) {
            await testConnectors[i].connect(
                testServer.wsUrl,
                stages[i].stageId,
                stages[i].stageType
            );

            const authRequest = Packet.empty('AuthenticateRequest');
            await testConnectors[i].authenticate(authRequest);
        }

        // When: Send echo requests simultaneously
        const requestPromises = testConnectors.map(async (connector, index) => {
            const echoRequest = Packet.empty('EchoRequest');
            return await connector.request(echoRequest);
        });

        const responses = await Promise.all(requestPromises);

        // Then: All requests should complete
        expect(responses).toHaveLength(3);
        responses.forEach((response, index) => {
            expect(response.msgId).toBe('EchoReply');
        });
    });

    test('A-05-04: Disconnecting one connector does not affect others', async () => {
        // Given: 3 connected and authenticated connectors
        const testConnectors = Array.from({ length: 3 }, () => createConnector());

        for (let i = 0; i < testConnectors.length; i++) {
            await testConnectors[i].connect(
                testServer.wsUrl,
                stages[i].stageId,
                stages[i].stageType
            );

            const authRequest = Packet.empty('AuthenticateRequest');
            await testConnectors[i].authenticate(authRequest);
        }

        // When: Disconnect first connector
        testConnectors[0].disconnect();
        await delay(500);

        // Then: First is disconnected, others remain connected
        expect(testConnectors[0].isConnected).toBe(false);
        expect(testConnectors[1].isConnected).toBe(true);
        expect(testConnectors[2].isConnected).toBe(true);

        // Others can still make requests
        for (let i = 1; i < testConnectors.length; i++) {
            const echoRequest = Packet.empty('EchoRequest');
            const response = await testConnectors[i].request(echoRequest);
            expect(response.msgId).toBe('EchoReply');
        }
    });

    test('A-05-05: Multiple connectors can connect to same stage', async () => {
        // Given: Same stage for all connectors
        const sharedStage = stages[0];
        const testConnectors = Array.from({ length: 3 }, () => createConnector());

        // When: Connect all to same stage
        for (let i = 0; i < testConnectors.length; i++) {
            await testConnectors[i].connect(
                testServer.wsUrl,
                sharedStage.stageId,
                sharedStage.stageType
            );

            const authRequest = Packet.empty('AuthenticateRequest');
            await testConnectors[i].authenticate(authRequest);
        }

        // Then: All can make independent requests
        for (let i = 0; i < testConnectors.length; i++) {
            const echoRequest = Packet.empty('EchoRequest');
            const response = await testConnectors[i].request(echoRequest);
            expect(response.msgId).toBe('EchoReply');
        }
    });

    test('A-05-06: Stress test with many connectors', async () => {
        // Given: 10 connectors
        const connectorCount = 10;
        const testConnectors: Connector[] = [];
        const extraStages: CreateStageResponse[] = [];

        // Create additional stages
        for (let i = 0; i < connectorCount; i++) {
            extraStages.push(await testServer.createStage());
        }

        // When: Connect all simultaneously
        const connectPromises: Promise<boolean>[] = [];
        for (let i = 0; i < connectorCount; i++) {
            const connector = createConnector();
            testConnectors.push(connector);

            const stage = extraStages[i];
            connectPromises.push(
                connector.connect(testServer.wsUrl, stage.stageId, stage.stageType)
            );
        }

        const connectResults = await Promise.all(connectPromises);

        // Then: All connections should succeed
        expect(connectResults.every(r => r === true)).toBe(true);

        // Disconnect all
        testConnectors.forEach(c => c.disconnect());
        await delay(500);

        // All should be disconnected
        expect(testConnectors.every(c => !c.isConnected)).toBe(true);
    });

    test('A-05-07: Each connector\'s events are independent', async () => {
        // Given: 2 connectors with separate event handlers
        const connector1 = createConnector();
        const connector2 = createConnector();

        const connector1Received: string[] = [];
        const connector2Received: string[] = [];

        connector1.onReceive = (packet: Packet) => {
            if (packet.msgId === 'BroadcastNotify') {
                const notify = parsePayload(packet);
                connector1Received.push(notify.data || 'From Connector 1');
            }
        };

        connector2.onReceive = (packet: Packet) => {
            if (packet.msgId === 'BroadcastNotify') {
                const notify = parsePayload(packet);
                connector2Received.push(notify.data || 'From Connector 2');
            }
        };

        // Connect to different stages
        await connector1.connect(testServer.wsUrl, stages[0].stageId, stages[0].stageType);
        await connector2.connect(testServer.wsUrl, stages[1].stageId, stages[1].stageType);

        const auth1 = Packet.empty('AuthenticateRequest');
        await connector1.authenticate(auth1);

        const auth2 = Packet.empty('AuthenticateRequest');
        await connector2.authenticate(auth2);

        // When: Each sends broadcast request
        const broadcast1 = Packet.empty('BroadcastRequest');
        connector1.send(broadcast1);

        const broadcast2 = Packet.empty('BroadcastRequest');
        connector2.send(broadcast2);

        // Wait for push messages
        const deadline = Date.now() + 5000;
        while ((connector1Received.length === 0 || connector2Received.length === 0) && Date.now() < deadline) {
            connector1.mainThreadAction();
            connector2.mainThreadAction();
            await delay(10);
        }

        // Then: Each connector receives its own messages
        expect(connector1Received.length).toBeGreaterThan(0);
        expect(connector2Received.length).toBeGreaterThan(0);
    });
});
