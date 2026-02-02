/**
 * PlayHouse Connector - Configuration
 */

/**
 * Connector configuration options
 */
export interface ConnectorConfig {
    /**
     * Heartbeat interval in milliseconds
     * @default 10000 (10 seconds)
     */
    heartbeatIntervalMs?: number;

    /**
     * Heartbeat timeout in milliseconds
     * If no message is received within this time, connection is considered lost.
     * Set to 0 to disable.
     * @default 30000 (30 seconds)
     */
    heartbeatTimeoutMs?: number;

    /**
     * Request timeout in milliseconds
     * @default 30000 (30 seconds)
     */
    requestTimeoutMs?: number;

    /**
     * Connection idle timeout in milliseconds
     * @default 30000 (30 seconds)
     */
    connectionIdleTimeoutMs?: number;

    /**
     * Enable debug logging
     * @default false
     */
    debugMode?: boolean;
}

/**
 * Default configuration values
 */
export const DefaultConfig: Required<ConnectorConfig> = {
    heartbeatIntervalMs: 10000,
    heartbeatTimeoutMs: 30000,
    requestTimeoutMs: 30000,
    connectionIdleTimeoutMs: 30000,
    debugMode: false,
};

/**
 * Merges user config with default values
 */
export function mergeConfig(config?: ConnectorConfig): Required<ConnectorConfig> {
    return {
        ...DefaultConfig,
        ...config,
    };
}
