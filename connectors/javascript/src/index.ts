/**
 * PlayHouse JavaScript/TypeScript Connector
 *
 * A WebSocket-based connector for PlayHouse real-time game server framework.
 *
 * @packageDocumentation
 */

// Main API
export { Connector } from './connector.js';
export { Packet, ParsedPacket } from './packet.js';
export { ConnectorConfig, DefaultConfig } from './config.js';

// Types
export { ErrorCode, PacketConst, ConnectorError, isError, getErrorMessage } from './types.js';
export type { ErrorCodeType, IDisposable, IProtoDecoder, IProtoMessage } from './types.js';
