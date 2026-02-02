/**
 * PlayHouse Connector - Main Connector Class
 *
 * The Connector class provides the primary API for connecting to
 * a PlayHouse game server and exchanging messages.
 */

import { ConnectorConfig, DefaultConfig, mergeConfig } from './config.js';
import { encodePacket } from './internal/packet-codec.js';
import { WsConnection } from './internal/ws-connection.js';
import { Packet, ParsedPacket } from './packet.js';
import { ErrorCode, PacketConst } from './types.js';

/**
 * Pending request tracking
 */
interface PendingRequest {
    msgSeq: number;
    request: Packet;
    stageId: bigint;
    resolve: (packet: Packet) => void;
    reject: (error: Error) => void;
    isAuthenticate: boolean;
    createdAt: number;
    timeoutId: ReturnType<typeof setTimeout>;
}

/**
 * Queued callback for main thread execution
 */
type QueuedAction = () => void;

/**
 * PlayHouse Connector
 *
 * Provides connectivity to PlayHouse game servers via WebSocket.
 *
 * @example
 * ```typescript
 * const connector = new Connector();
 * connector.init({ requestTimeoutMs: 10000 });
 *
 * connector.onConnect = () => console.log('Connected!');
 * connector.onReceive = (packet) => console.log('Push:', packet.msgId);
 * connector.onDisconnect = () => console.log('Disconnected');
 *
 * await connector.connect('ws://localhost:8080/ws');
 * await connector.authenticate('game', 'user123');
 *
 * const response = await connector.request(Packet.empty('Ping'));
 * console.log('Response:', response.msgId);
 *
 * connector.disconnect();
 * ```
 */
export class Connector {
    private _config: Required<ConnectorConfig> = { ...DefaultConfig };
    private _connection: WsConnection | null = null;
    private _pendingRequests: Map<number, PendingRequest> = new Map();
    private _actionQueue: QueuedAction[] = [];
    private _msgSeqCounter = 0;
    private _isAuthenticated = false;
    private _stageId: bigint = 0n;
    private _lastReceivedTime = 0;
    private _lastHeartbeatTime = 0;

    // Callback properties
    /** Called when connection is established */
    onConnect?: () => void;
    /** Called when a push message is received (not a response to a request) */
    onReceive?: (packet: Packet) => void;
    /** Called when an error occurs */
    onError?: (code: number, message: string) => void;
    /** Called when the connection is closed */
    onDisconnect?: () => void;

    /**
     * Initializes the connector with optional configuration
     * @param config Optional configuration overrides
     */
    init(config?: ConnectorConfig): void {
        this._config = mergeConfig(config);
    }

    /**
     * Gets the current configuration
     */
    get config(): Readonly<Required<ConnectorConfig>> {
        return this._config;
    }

    /**
     * Whether the connector is currently connected
     */
    get isConnected(): boolean {
        return this._connection?.isConnected ?? false;
    }

    /**
     * Whether the connector is authenticated
     */
    get isAuthenticated(): boolean {
        return this._isAuthenticated;
    }

    /**
     * Current stage ID
     */
    get stageId(): bigint {
        return this._stageId;
    }

    /**
     * Connects to a WebSocket server
     * @param url WebSocket URL (ws:// or wss://)
     * @param stageId Optional stage ID (default: 0n)
     */
    async connect(url: string, stageId: bigint = 0n): Promise<void> {
        if (this._connection?.isConnected) {
            throw new Error('Already connected. Call disconnect() first.');
        }

        this._stageId = stageId;
        this._isAuthenticated = false;
        this._lastReceivedTime = Date.now();
        this._lastHeartbeatTime = Date.now();

        this._connection = new WsConnection(this._config);
        this._connection.setCallbacks({
            onConnect: () => {
                this.queueAction(() => this.onConnect?.());
            },
            onPacket: (packet) => {
                this.handlePacket(packet);
            },
            onError: (error) => {
                this.queueAction(() =>
                    this.onError?.(ErrorCode.ConnectionFailed, error.message)
                );
            },
            onDisconnect: () => {
                this._isAuthenticated = false;
                this.clearPendingRequests();
                this.queueAction(() => this.onDisconnect?.());
            },
        });

        try {
            await this._connection.connect(url);
        } catch (error) {
            this._connection = null;
            throw error;
        }
    }

    /**
     * Disconnects from the server
     */
    disconnect(): void {
        this._isAuthenticated = false;
        this.clearPendingRequests();

        if (this._connection) {
            this._connection.disconnect();
            this._connection = null;
        }
    }

    /**
     * Sends a packet without expecting a response (fire-and-forget)
     * @param packet The packet to send
     */
    send(packet: Packet): void {
        if (!this.isConnected) {
            this.queueAction(() =>
                this.onError?.(ErrorCode.Disconnected, 'Not connected')
            );
            return;
        }

        const data = encodePacket(packet, 0, this._stageId);
        this._connection!.send(data);
    }

    /**
     * Sends a request and waits for a response
     * @param packet The request packet
     * @returns Promise that resolves with the response packet
     */
    async request(packet: Packet): Promise<Packet> {
        if (!this.isConnected) {
            throw new Error('Not connected');
        }

        const msgSeq = this.getNextMsgSeq();

        return new Promise<Packet>((resolve, reject) => {
            const timeoutId = setTimeout(() => {
                if (this._pendingRequests.has(msgSeq)) {
                    this._pendingRequests.delete(msgSeq);
                    reject(new Error(`Request timeout: ${packet.msgId}`));
                }
            }, this._config.requestTimeoutMs);

            const pending: PendingRequest = {
                msgSeq,
                request: packet,
                stageId: this._stageId,
                resolve,
                reject,
                isAuthenticate: false,
                createdAt: Date.now(),
                timeoutId,
            };

            this._pendingRequests.set(msgSeq, pending);

            try {
                const data = encodePacket(packet, msgSeq, this._stageId);
                this._connection!.send(data);
            } catch (error) {
                clearTimeout(timeoutId);
                this._pendingRequests.delete(msgSeq);
                reject(error);
            }
        });
    }

    /**
     * Authenticates with the server
     * @param serviceId Service identifier
     * @param accountId Account identifier
     * @param payload Optional additional authentication data
     * @returns Promise that resolves to true if authentication succeeded
     */
    async authenticate(
        serviceId: string,
        accountId: string,
        payload?: Uint8Array
    ): Promise<boolean> {
        if (!this.isConnected) {
            throw new Error('Not connected');
        }

        // Create authentication packet
        // The server expects a specific message format for authentication
        // Typically: AuthenticateReq with serviceId, accountId, payload fields
        // For now, we'll encode it as a simple packet with combined data
        const textEncoder = new TextEncoder();
        const serviceIdBytes = textEncoder.encode(serviceId);
        const accountIdBytes = textEncoder.encode(accountId);
        const payloadBytes = payload ?? new Uint8Array(0);

        // Simple format: serviceIdLen(2) + serviceId + accountIdLen(2) + accountId + payload
        const totalLen =
            2 +
            serviceIdBytes.length +
            2 +
            accountIdBytes.length +
            payloadBytes.length;
        const authPayload = new Uint8Array(totalLen);
        const view = new DataView(authPayload.buffer);
        let offset = 0;

        view.setUint16(offset, serviceIdBytes.length, true);
        offset += 2;
        authPayload.set(serviceIdBytes, offset);
        offset += serviceIdBytes.length;

        view.setUint16(offset, accountIdBytes.length, true);
        offset += 2;
        authPayload.set(accountIdBytes, offset);
        offset += accountIdBytes.length;

        authPayload.set(payloadBytes, offset);

        const authPacket = Packet.fromBytes('AuthenticateReq', authPayload);
        const msgSeq = this.getNextMsgSeq();

        return new Promise<boolean>((resolve, reject) => {
            const timeoutId = setTimeout(() => {
                if (this._pendingRequests.has(msgSeq)) {
                    this._pendingRequests.delete(msgSeq);
                    reject(new Error('Authentication timeout'));
                }
            }, this._config.requestTimeoutMs);

            const pending: PendingRequest = {
                msgSeq,
                request: authPacket,
                stageId: this._stageId,
                resolve: (response) => {
                    if (response.errorCode === 0) {
                        this._isAuthenticated = true;
                        resolve(true);
                    } else {
                        resolve(false);
                    }
                },
                reject,
                isAuthenticate: true,
                createdAt: Date.now(),
                timeoutId,
            };

            this._pendingRequests.set(msgSeq, pending);

            try {
                const data = encodePacket(authPacket, msgSeq, this._stageId);
                this._connection!.send(data);
            } catch (error) {
                clearTimeout(timeoutId);
                this._pendingRequests.delete(msgSeq);
                reject(error);
            }
        });
    }

    /**
     * Processes pending callbacks on the main thread
     *
     * Call this periodically (e.g., in requestAnimationFrame) to process
     * callbacks on the main thread for proper UI updates.
     *
     * @example
     * ```typescript
     * function gameLoop() {
     *     connector.mainThreadAction();
     *     requestAnimationFrame(gameLoop);
     * }
     * requestAnimationFrame(gameLoop);
     * ```
     */
    mainThreadAction(): void {
        // Process queued actions
        while (this._actionQueue.length > 0) {
            const action = this._actionQueue.shift();
            try {
                action?.();
            } catch (error) {
                console.error('Error in queued action:', error);
            }
        }

        // Handle heartbeat and timeout checks
        if (this.isConnected && !this._config.debugMode) {
            this.sendHeartbeatIfNeeded();
            this.checkHeartbeatTimeout();
        }
    }

    /**
     * Handles incoming packets
     */
    private handlePacket(packet: ParsedPacket): void {
        this._lastReceivedTime = Date.now();

        // Handle heartbeat response
        if (packet.msgId === PacketConst.HeartBeat) {
            packet.dispose();
            return;
        }

        // Handle response (msgSeq > 0)
        if (packet.msgSeq > 0) {
            const pending = this._pendingRequests.get(packet.msgSeq);
            if (pending) {
                this._pendingRequests.delete(packet.msgSeq);
                clearTimeout(pending.timeoutId);

                if (packet.errorCode !== 0) {
                    pending.reject(
                        new Error(
                            `Request failed with error code ${packet.errorCode}: ${packet.msgId}`
                        )
                    );
                    packet.dispose();
                } else {
                    if (pending.isAuthenticate) {
                        this._isAuthenticated = true;
                    }
                    pending.resolve(packet);
                }
            } else {
                // No pending request found - could be timeout or duplicate
                if (this._config.debugMode) {
                    console.warn(`No pending request for msgSeq ${packet.msgSeq}`);
                }
                packet.dispose();
            }
            return;
        }

        // Handle push message (msgSeq == 0)
        this.queueAction(() => {
            try {
                this.onReceive?.(packet);
            } finally {
                packet.dispose();
            }
        });
    }

    /**
     * Sends heartbeat if needed
     */
    private sendHeartbeatIfNeeded(): void {
        if (this._config.heartbeatIntervalMs === 0) {
            return;
        }

        const now = Date.now();
        if (now - this._lastHeartbeatTime > this._config.heartbeatIntervalMs) {
            const heartbeat = Packet.empty(PacketConst.HeartBeat);
            try {
                const data = encodePacket(heartbeat, 0, this._stageId);
                this._connection?.send(data);
            } catch {
                // Ignore heartbeat send errors
            } finally {
                heartbeat.dispose();
            }
            this._lastHeartbeatTime = now;
        }
    }

    /**
     * Checks for heartbeat timeout
     */
    private checkHeartbeatTimeout(): void {
        if (this._config.heartbeatTimeoutMs === 0) {
            return;
        }

        const now = Date.now();
        if (now - this._lastReceivedTime > this._config.heartbeatTimeoutMs) {
            // Connection timeout
            this.disconnect();
            this.queueAction(() => this.onDisconnect?.());
        }
    }

    /**
     * Generates the next message sequence number
     */
    private getNextMsgSeq(): number {
        // msgSeq must be > 0 for request-response
        // Wrap around at 65535, skip 0
        this._msgSeqCounter = (this._msgSeqCounter % 65535) + 1;
        return this._msgSeqCounter;
    }

    /**
     * Clears all pending requests with timeout error
     */
    private clearPendingRequests(): void {
        for (const pending of this._pendingRequests.values()) {
            clearTimeout(pending.timeoutId);
            pending.reject(new Error('Connection closed'));
        }
        this._pendingRequests.clear();
    }

    /**
     * Queues an action to be executed in mainThreadAction
     */
    private queueAction(action: QueuedAction): void {
        this._actionQueue.push(action);
    }
}
