/**
 * Base Integration Test Class
 *
 * Provides common setup, teardown, and helper methods for integration tests.
 */

import { beforeEach, afterEach } from 'vitest';
import { Connector } from '../../../src/connector.js';
import { Packet } from '../../../src/packet.js';
import { TestServerClient, CreateStageResponse } from './TestServerClient.js';

/**
 * Protobuf message interface
 */
export interface IProtobufMessage {
    [key: string]: any;
}

/**
 * Base class for integration tests
 *
 * Provides:
 * - Connector lifecycle management
 * - Test server client
 * - Helper methods for common operations
 */
export class BaseIntegrationTest {
    protected testServer: TestServerClient;
    protected connector: Connector | null = null;
    protected stageInfo: CreateStageResponse | null = null;

    constructor() {
        this.testServer = new TestServerClient();
    }

    /**
     * Setup before each test
     */
    protected async beforeEach(): Promise<void> {
        this.connector = new Connector();
        this.connector.init({
            requestTimeoutMs: 5000,
            heartbeatIntervalMs: 10000
        });
    }

    /**
     * Cleanup after each test
     */
    protected async afterEach(): Promise<void> {
        if (this.connector) {
            if (this.connector.isConnected) {
                this.connector.disconnect();
                await this.delay(100);
            }
            this.connector = null;
        }
        this.stageInfo = null;
    }

    /**
     * Create a stage and connect to it
     * @param stageType Stage type to create
     * @returns Connection success
     */
    protected async createStageAndConnect(stageType: string = 'TestStage'): Promise<boolean> {
        this.stageInfo = await this.testServer.createStage(stageType);

        if (!this.connector) {
            throw new Error('Connector not initialized');
        }

        const wsUrl = `${this.testServer.wsUrl}`;
        await this.connector.connect(wsUrl, BigInt(this.stageInfo.stageId));
        return this.connector.isConnected;
    }

    /**
     * Authenticate with the test server
     * @param userId User ID for authentication
     * @param token Authentication token
     * @returns Authentication reply
     */
    protected async authenticate(userId: string, token: string = 'valid_token'): Promise<IProtobufMessage> {
        if (!this.connector) {
            throw new Error('Connector not initialized');
        }

        const authRequest = {
            userId,
            token
        };

        const requestPacket = Packet.create('AuthenticateRequest', authRequest);
        const responsePacket = await this.connector.authenticateAsync(requestPacket);

        return this.parsePayload(responsePacket);
    }

    /**
     * Send an echo request
     * @param content Content to echo
     * @param sequence Sequence number
     * @returns Echo reply
     */
    protected async echo(content: string, sequence: number = 1): Promise<IProtobufMessage> {
        if (!this.connector) {
            throw new Error('Connector not initialized');
        }

        const echoRequest = {
            content,
            sequence
        };

        const requestPacket = Packet.create('EchoRequest', echoRequest);
        const responsePacket = await this.connector.request(requestPacket);

        return this.parsePayload(responsePacket);
    }

    /**
     * Parse packet payload as a generic object
     * @param packet Packet to parse
     * @returns Parsed payload
     */
    protected parsePayload(packet: Packet): IProtobufMessage {
        // Simple JSON parsing for now
        // In production, use proper protobuf deserialization
        try {
            if (packet.payload && packet.payload.length > 0) {
                // For test purposes, we'll work with the raw payload
                // and assume the test server sends JSON-compatible data
                return JSON.parse(new TextDecoder().decode(packet.payload));
            }
        } catch (e) {
            // If not JSON, return empty object
        }
        return {};
    }

    /**
     * Wait for a condition to become true
     * @param condition Function that returns true when done
     * @param timeoutMs Maximum time to wait in milliseconds
     * @returns True if condition met, false if timeout
     */
    protected async waitForCondition(
        condition: () => boolean,
        timeoutMs: number = 5000
    ): Promise<boolean> {
        const startTime = Date.now();

        while (!condition()) {
            if (Date.now() - startTime > timeoutMs) {
                return false;
            }
            await this.delay(10);
        }

        return true;
    }

    /**
     * Wait for a condition with mainThreadAction calls
     * @param condition Function that returns true when done
     * @param timeoutMs Maximum time to wait in milliseconds
     * @returns True if condition met, false if timeout
     */
    protected async waitForConditionWithMainThread(
        condition: () => boolean,
        timeoutMs: number = 5000
    ): Promise<boolean> {
        const startTime = Date.now();

        while (!condition()) {
            if (Date.now() - startTime > timeoutMs) {
                return false;
            }
            this.connector?.mainThreadAction();
            await this.delay(10);
        }

        return true;
    }

    /**
     * Wait for a promise with mainThreadAction calls
     * @param promise Promise to wait for
     * @param timeoutMs Maximum time to wait in milliseconds
     * @returns Promise result
     */
    protected async waitWithMainThreadAction<T>(
        promise: Promise<T>,
        timeoutMs: number = 5000
    ): Promise<T> {
        const startTime = Date.now();
        let resolved = false;
        let result: T;
        let error: any;

        promise.then(
            (value) => {
                resolved = true;
                result = value;
            },
            (err) => {
                resolved = true;
                error = err;
            }
        );

        while (!resolved) {
            if (Date.now() - startTime > timeoutMs) {
                throw new Error(`Operation timed out after ${timeoutMs}ms`);
            }
            this.connector?.mainThreadAction();
            await this.delay(10);
        }

        if (error) {
            throw error;
        }

        return result!;
    }

    /**
     * Delay helper
     * @param ms Milliseconds to delay
     */
    protected async delay(ms: number): Promise<void> {
        return new Promise(resolve => setTimeout(resolve, ms));
    }
}
