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
 * - TEST_SERVER_HTTPS_PORT: HTTPS port (default: 8443)
 * - TEST_SERVER_WS_PORT: WebSocket port (default: 8080, same as HTTP)
 * - TEST_SERVER_WSS_PORT: WebSocket TLS port (default: 8443)
 * - TEST_SERVER_WS_PATH: WebSocket path (default: /ws)
 */
export class TestServerClient {
    public readonly host: string;
    public readonly httpPort: number;
    public readonly httpsPort: number;
    public readonly wsPort: number;
    public readonly wssPort: number;
    public readonly wsPath: string;
    // stageId must fit in UInt16 (1-65535)
    private static _stageIdCounter: number = 0;

    constructor() {
        this.host = process.env.TEST_SERVER_HOST || 'localhost';
        this.httpPort = parseInt(process.env.TEST_SERVER_HTTP_PORT || '8080', 10);
        this.httpsPort = parseInt(process.env.TEST_SERVER_HTTPS_PORT || '8443', 10);
        this.wsPort = parseInt(process.env.TEST_SERVER_WS_PORT || '8080', 10);
        this.wssPort = parseInt(process.env.TEST_SERVER_WSS_PORT || '8443', 10);
        this.wsPath = process.env.TEST_SERVER_WS_PATH || '/ws';
        if (TestServerClient._stageIdCounter == 0) {
            // Start with random offset within valid UInt16 range (1000-60000) to avoid conflicts
            TestServerClient._stageIdCounter = 1000 + Math.floor(Math.random() * 59000);
        }
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
        return `ws://${this.host}:${this.wsPort}${this.wsPath}`;
    }

    /**
     * Get the WebSocket TLS URL
     */
    get wssUrl(): string {
        return `wss://${this.host}:${this.wssPort}${this.wsPath}`;
    }

    /**
     * Create a stage on the test server
     * @param stageType The type of stage to create (default: "TestStage")
     * @param maxPlayers Optional max players for the stage
     * @returns Stage information
     */
    async createStage(stageType: string = 'TestStage', maxPlayers?: number): Promise<CreateStageResponse> {
        const maxAttempts = 5;
        let lastError: string | null = null;

        for (let attempt = 1; attempt <= maxAttempts; attempt++) {
            const stageId = TestServerClient.nextStageId();
            const requestBody = {
                stageType,
                stageId,
                ...(maxPlayers !== undefined && { maxPlayers })
            };

            let response: Response;
            try {
                response = await fetch(`${this.httpBaseUrl}/api/stages`, {
                    method: 'POST',
                    headers: {
                        'Content-Type': 'application/json'
                    },
                    body: JSON.stringify(requestBody)
                });
            } catch (error) {
                lastError = `Failed to create stage: ${error}`;
                continue;
            }

            if (!response.ok) {
                lastError = `Failed to create stage: ${response.status} ${response.statusText}: ${await response.text()}`;
                continue;
            }

            const data = await response.json() as { success: boolean; stageId: number; replyPayloadId?: string };
            if (data.success) {
                return {
                    success: data.success,
                    stageId: data.stageId,
                    stageType,
                    replyPayloadId: data.replyPayloadId
                };
            }

            lastError = `Stage already exists (stageId=${data.stageId}). Retrying...`;
        }

        throw new Error(lastError ?? 'Failed to create stage after retries');
    }

    private static nextStageId(): number {
        // Ensure stageId stays within UInt16 range and avoids 0
        const next = (TestServerClient._stageIdCounter % 65000) + 1;
        TestServerClient._stageIdCounter = next;
        return next;
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
