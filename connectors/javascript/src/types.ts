/**
 * PlayHouse Connector - Common Type Definitions
 */

/**
 * Error codes returned by the connector
 */
export const ErrorCode = {
    /** Success - no error */
    Success: 0,
    /** Connection failed */
    ConnectionFailed: 1001,
    /** Connection timeout */
    ConnectionTimeout: 1002,
    /** Connection closed unexpectedly */
    ConnectionClosed: 1003,
    /** Request timeout */
    RequestTimeout: 2001,
    /** Invalid response received */
    InvalidResponse: 2002,
    /** Authentication failed */
    AuthenticationFailed: 3001,
    /** Disconnected before operation */
    Disconnected: 4001,
} as const;

export type ErrorCodeType = (typeof ErrorCode)[keyof typeof ErrorCode];

/**
 * Internal packet constants
 */
export const PacketConst = {
    /** Maximum message ID length in bytes */
    MsgIdLimit: 256,
    /** Maximum payload size (2MB) */
    MaxBodySize: 1024 * 1024 * 2,
    /** Minimum header size: MsgIdLen(1) + MsgSeq(2) + StageId(8) + ErrorCode(2) + OriginalSize(4) = 17 */
    MinHeaderSize: 17,
    /** Heartbeat message ID */
    HeartBeat: '@Heart@Beat@',
    /** Debug message ID */
    Debug: '@Debug@',
    /** Timeout message ID */
    Timeout: '@Timeout@',
} as const;

/**
 * Protobuf message interface (compatible with protobufjs)
 */
export interface IProtoMessage {
    /** Returns the message type name */
    $type?: { name: string };
    /** Encodes the message to bytes */
    encode?(): { finish(): Uint8Array };
}

/**
 * Protobuf decoder interface
 */
export interface IProtoDecoder<T> {
    decode(data: Uint8Array): T;
}

/**
 * Disposable interface for resource cleanup
 */
export interface IDisposable {
    dispose(): void;
}
