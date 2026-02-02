/**
 * Test Server HTTP Client
 *
 * Provides utilities for interacting with the PlayHouse test server's HTTP API
 * to create stages for integration testing.
 */

/**
 * Stage creation response from test server
 */
export interface CreateStageResponse {
    success: boolean;
    stageId: number;
    stageType: string;
    replyPayloadId?: string;
}

/**
 * TestServerClient - HTTP API client for test server
 *
 * Environment variables:
 * - TEST_SERVER_HOST: Server hostname (default: localhost)
 * - TEST_SERVER_HTTP_PORT: HTTP port (default: 8080)
 * - TEST_SERVER_WS_PORT: WebSocket port (default: 38001)
 */
export class TestServerClient {
    public readonly host: string;
    public readonly httpPort: number;
    public readonly wsPort: number;
    private stageIdCounter = 1;

    constructor() {
        this.host = process.env.TEST_SERVER_HOST || 'localhost';
        this.httpPort = parseInt(process.env.TEST_SERVER_HTTP_PORT || '8080', 10);
        this.wsPort = parseInt(process.env.TEST_SERVER_WS_PORT || '38001', 10);
    }

    /**
     * Get the HTTP base URL
     */
    get httpBaseUrl(): string {
        return `http://${this.host}:${this.httpPort}`;
    }

    /**
     * Get the WebSocket URL
     */
    get wsUrl(): string {
        return `ws://${this.host}:${this.wsPort}`;
    }

    /**
     * Create a stage on the test server
     * @param stageType The type of stage to create (default: "TestStage")
     * @param maxPlayers Optional max players for the stage
     * @returns Stage information
     */
    async createStage(stageType: string = 'TestStage', maxPlayers?: number): Promise<CreateStageResponse> {
        const stageId = this.stageIdCounter++;

        const requestBody = {
            stageType,
            stageId,
            ...(maxPlayers !== undefined && { maxPlayers })
        };

        const response = await fetch(`${this.httpBaseUrl}/api/stages`, {
            method: 'POST',
            headers: {
                'Content-Type': 'application/json'
            },
            body: JSON.stringify(requestBody)
        });

        if (!response.ok) {
            throw new Error(`Failed to create stage: ${response.status} ${response.statusText}`);
        }

        const data = await response.json() as { success: boolean; stageId: number; replyPayloadId?: string };

        return {
            success: data.success,
            stageId: data.stageId,
            stageType,
            replyPayloadId: data.replyPayloadId
        };
    }

    /**
     * Create a test stage (convenience method)
     */
    async createTestStage(): Promise<CreateStageResponse> {
        return this.createStage('TestStage');
    }

    /**
     * Check if the test server is healthy
     */
    async checkHealth(): Promise<boolean> {
        try {
            const response = await fetch(`${this.httpBaseUrl}/api/health`, {
                method: 'GET'
            });
            return response.ok;
        } catch (error) {
            return false;
        }
    }
}
