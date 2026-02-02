/**
 * PlayHouse Connector - WebSocket Connection
 * Manages WebSocket connection, send/receive, and packet parsing
 */

import type { ConnectorConfig } from '../config.js';
import { ParsedPacket } from '../packet.js';
import { CONTENT_SIZE_HEADER, decodePacket, readContentSize } from './packet-codec.js';

/**
 * Connection state
 */
export type ConnectionState = 'disconnected' | 'connecting' | 'connected';

/**
 * Connection event handlers
 */
export interface ConnectionCallbacks {
    onConnect?: () => void;
    onPacket?: (packet: ParsedPacket) => void;
    onError?: (error: Error) => void;
    onDisconnect?: () => void;
}

/**
 * WebSocket connection wrapper
 * Handles binary WebSocket communication and packet framing
 */
export class WsConnection {
    private _ws: WebSocket | null = null;
    private _state: ConnectionState = 'disconnected';
    private _receiveChunks: Uint8Array[] = [];
    private _totalReceiveSize = 0;
    private _expectedPacketSize = -1;
    private _callbacks: ConnectionCallbacks = {};
    private _connectionTimeoutId: ReturnType<typeof setTimeout> | null = null;

    constructor(private readonly _config: Required<ConnectorConfig>) {}

    /**
     * Current connection state
     */
    get state(): ConnectionState {
        return this._state;
    }

    /**
     * Whether the connection is established
     */
    get isConnected(): boolean {
        return this._state === 'connected' && this._ws?.readyState === WebSocket.OPEN;
    }

    /**
     * Sets the event callbacks
     */
    setCallbacks(callbacks: ConnectionCallbacks): void {
        this._callbacks = callbacks;
    }

    /**
     * Connects to the WebSocket server
     * @param url WebSocket URL (ws:// or wss://)
     */
    async connect(url: string): Promise<void> {
        if (this._state !== 'disconnected') {
            throw new Error(`Cannot connect: current state is ${this._state}`);
        }

        // Validate URL
        if (!url.startsWith('ws://') && !url.startsWith('wss://')) {
            throw new Error(`Invalid WebSocket URL: ${url}. Must start with ws:// or wss://`);
        }

        this._state = 'connecting';

        return new Promise<void>((resolve, reject) => {
            try {
                this._ws = new WebSocket(url);
                this._ws.binaryType = 'arraybuffer';

                this._connectionTimeoutId = setTimeout(() => {
                    if (this._state === 'connecting') {
                        this._ws?.close();
                        this._state = 'disconnected';
                        this._connectionTimeoutId = null;
                        reject(new Error('Connection timeout'));
                    }
                }, this._config.connectionIdleTimeoutMs);

                this._ws.onopen = () => {
                    if (this._connectionTimeoutId) {
                        clearTimeout(this._connectionTimeoutId);
                        this._connectionTimeoutId = null;
                    }
                    this._state = 'connected';
                    this._callbacks.onConnect?.();
                    resolve();
                };

                this._ws.onerror = (_event) => {
                    if (this._connectionTimeoutId) {
                        clearTimeout(this._connectionTimeoutId);
                        this._connectionTimeoutId = null;
                    }
                    const error = new Error('WebSocket error');
                    if (this._state === 'connecting') {
                        this._state = 'disconnected';
                        reject(error);
                    } else {
                        this._callbacks.onError?.(error);
                    }
                };

                this._ws.onclose = () => {
                    if (this._connectionTimeoutId) {
                        clearTimeout(this._connectionTimeoutId);
                        this._connectionTimeoutId = null;
                    }
                    const wasConnected = this._state === 'connected';
                    this._state = 'disconnected';
                    this.resetReceiveBuffer();

                    if (wasConnected) {
                        this._callbacks.onDisconnect?.();
                    }
                };

                this._ws.onmessage = (event) => {
                    this.handleMessage(event);
                };
            } catch (error) {
                this._state = 'disconnected';
                reject(error);
            }
        });
    }

    /**
     * Disconnects from the server
     */
    disconnect(): void {
        // Clear connection timeout if still pending
        if (this._connectionTimeoutId) {
            clearTimeout(this._connectionTimeoutId);
            this._connectionTimeoutId = null;
        }

        if (this._ws) {
            // Remove handlers to avoid disconnect callback
            this._ws.onopen = null;
            this._ws.onclose = null;
            this._ws.onerror = null;
            this._ws.onmessage = null;

            if (this._ws.readyState === WebSocket.OPEN || this._ws.readyState === WebSocket.CONNECTING) {
                this._ws.close(1000, 'Client disconnect');
            }
            this._ws = null;
        }

        this._state = 'disconnected';
        this.resetReceiveBuffer();
    }

    /**
     * Sends binary data over the WebSocket
     */
    send(data: Uint8Array): void {
        if (!this.isConnected || !this._ws) {
            throw new Error('Not connected');
        }

        this._ws.send(data);
    }

    /**
     * Handles incoming WebSocket messages
     */
    private handleMessage(event: MessageEvent): void {
        if (!(event.data instanceof ArrayBuffer)) {
            console.warn('Received non-binary WebSocket message, ignoring');
            return;
        }

        const data = new Uint8Array(event.data);
        this.appendToReceiveBuffer(data);
        this.processReceiveBuffer();
    }

    /**
     * Appends data to the receive buffer (chunked approach for O(1) amortized)
     */
    private appendToReceiveBuffer(data: Uint8Array): void {
        this._receiveChunks.push(data);
        this._totalReceiveSize += data.length;
    }

    /**
     * Gets the merged receive buffer (merges chunks only when needed)
     */
    private getReceiveBuffer(): Uint8Array {
        if (this._receiveChunks.length === 0) {
            return new Uint8Array(0);
        }
        if (this._receiveChunks.length === 1) {
            return this._receiveChunks[0];
        }
        // Merge chunks
        const merged = new Uint8Array(this._totalReceiveSize);
        let offset = 0;
        for (const chunk of this._receiveChunks) {
            merged.set(chunk, offset);
            offset += chunk.length;
        }
        // Store merged result to avoid re-merging
        this._receiveChunks = [merged];
        return merged;
    }

    /**
     * Consumes bytes from the receive buffer
     */
    private consumeReceiveBuffer(bytes: number): void {
        if (bytes >= this._totalReceiveSize) {
            this._receiveChunks = [];
            this._totalReceiveSize = 0;
            return;
        }

        // Rebuild remaining data
        const remaining = this.getReceiveBuffer().subarray(bytes);
        this._receiveChunks = [remaining];
        this._totalReceiveSize = remaining.length;
    }

    /**
     * Processes accumulated data in the receive buffer
     */
    private processReceiveBuffer(): void {
        while (true) {
            // Read packet size header if not yet known
            if (this._expectedPacketSize === -1) {
                if (this._totalReceiveSize < CONTENT_SIZE_HEADER) {
                    break; // Need more data
                }

                const buffer = this.getReceiveBuffer();
                this._expectedPacketSize = readContentSize(buffer);

                if (
                    this._expectedPacketSize <= 0 ||
                    this._expectedPacketSize > 10 * 1024 * 1024
                ) {
                    console.error(`Invalid packet size: ${this._expectedPacketSize}`);
                    this.disconnect();
                    this._callbacks.onError?.(
                        new Error(`Invalid packet size: ${this._expectedPacketSize}`)
                    );
                    return;
                }

                // Consume the size header
                this.consumeReceiveBuffer(CONTENT_SIZE_HEADER);
            }

            // Check if we have the complete packet
            if (this._totalReceiveSize < this._expectedPacketSize) {
                break; // Need more data
            }

            // Extract and parse the packet
            try {
                const buffer = this.getReceiveBuffer();
                const packetData = buffer.subarray(0, this._expectedPacketSize);
                const packet = decodePacket(packetData);

                // Consume the packet from buffer
                this.consumeReceiveBuffer(this._expectedPacketSize);
                this._expectedPacketSize = -1;

                // Emit packet event
                this._callbacks.onPacket?.(packet);
            } catch (error) {
                console.error('Failed to parse packet:', error);
                // Skip this packet and continue
                this.consumeReceiveBuffer(this._expectedPacketSize);
                this._expectedPacketSize = -1;
                // Surface decode errors to onError callback
                this._callbacks.onError?.(
                    new Error(`Failed to parse packet: ${error instanceof Error ? error.message : String(error)}`)
                );
            }
        }
    }

    /**
     * Resets the receive buffer state
     */
    private resetReceiveBuffer(): void {
        this._receiveChunks = [];
        this._totalReceiveSize = 0;
        this._expectedPacketSize = -1;
    }
}
